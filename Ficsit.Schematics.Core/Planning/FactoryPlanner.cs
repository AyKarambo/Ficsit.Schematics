using System.Collections.Concurrent;
using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// Synthesizes a factory for the requested outputs as an exact linear program:
/// one variable per candidate recipe (machine count), per external supply and
/// per sinkable byproduct, with a balance row per part. The bias picks the
/// objective; bans simply remove supplies; byproduct handling is priced.
/// Recipe cycles (e.g. recycled plastic/rubber loops) fall out naturally —
/// this is what makes zero-waste oil chains plannable at all.
///
/// Exclusive provisions ("this is all the X I have, don't build more") remove
/// the part's producer recipes and, in target mode, turn the solve into a
/// two-stage program: first maximize the achievable fraction of the targets,
/// then optimize the bias at that fraction.
///
/// Somersloops (when a budget is given) are layered on top: a first solve picks
/// machine counts, <see cref="SomersloopAllocator"/> chooses which sloopable recipes
/// to amplify within the budget, and a second solve runs with those recipes' output
/// boosted so the whole chain rebalances around the extra throughput.
/// </summary>
public static class FactoryPlanner
{
    private static readonly Rational SinkPenaltyEliminate = new(1_000_000);
    private static readonly Rational SinkPenaltyAllowed = new(1, 1_000_000);

    /// <summary>Virtual "part" standing for electrical power (MW), so a power target flows
    /// through the same balance-row machinery as any part. Never a real catalog part — it is
    /// produced only by generator recipes and can be neither supplied nor sunk.</summary>
    public const string PowerPart = "#Power";

    public static PlanResult Plan(
        GameDatabase data, PlanRequest request, IReadOnlyList<ResourceNodeInfo>? mapNodes = null,
        IProgress<PlanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (request.Targets.Count == 0)
            return new PlanResult { Status = PlanStatus.Infeasible };

        progress?.Report(new PlanProgress("Collecting recipes"));
        var weights = ScarcityWeights.Build(mapNodes);
        // The user's resource-preference budget biases the baseline scarcity weights:
        // a multiplier < 1 makes a raw cheaper (the plan reaches for it), > 1 dearer.
        foreach (var (part, multiplier) in request.WeightMultipliers)
            if (weights.TryGetValue(part, out var weight))
                weights[part] = weight * multiplier;
        var exclusiveParts = request.Provisions.Where(p => p.Exclusive).Select(p => p.Part).ToHashSet();
        var recipes = CollectCandidateRecipes(data, request, exclusiveParts, out var parts);

        // Build provision map imperatively (no LINQ)
        var provisionByPart = new Dictionary<string, (Rational Cap, bool Exclusive)>();
        foreach (var provision in request.Provisions)
        {
            if (provisionByPart.TryGetValue(provision.Part, out var existing))
            {
                provisionByPart[provision.Part] = (
                    existing.Cap + provision.MaxPerMinute,
                    existing.Exclusive || provision.Exclusive
                );
            }
            else
            {
                provisionByPart[provision.Part] = (provision.MaxPerMinute, provision.Exclusive);
            }
            parts.Add(provision.Part);
        }

        // Build target map imperatively (no LINQ)
        var targetByPart = new Dictionary<string, Rational>();
        foreach (var target in request.Targets)
        {
            if (targetByPart.TryGetValue(target.Part, out var existing))
                targetByPart[target.Part] = existing + target.Rate;
            else
                targetByPart[target.Part] = target.Rate;
            parts.Add(target.Part);
        }

        // Sort parts and build row index (imperative)
        var partList = new List<string>(parts);
        partList.Sort(StringComparer.Ordinal);
        var partRow = new Dictionary<string, int>();
        for (var i = 0; i < partList.Count; i++)
            partRow[partList[i]] = i;

        // Targets scale together (bundle variable t) when maximizing, or when an
        // exclusive provision may bottleneck a fixed-rate request (t capped at 1).
        var scaledTargetMode = !request.MaximizeFromProvisions && exclusiveParts.Count > 0;
        var hasBundle = request.MaximizeFromProvisions || scaledTargetMode;

        var result = RunSolve(data, request, weights, recipes, partList, partRow,
            provisionByPart, targetByPart, scaledTargetMode, hasBundle, sloopLevels: null, progress, cancellationToken);

        // Somersloop pass: choose which recipes to amplify within the budget, then
        // re-solve with their output boosted so the chain rebalances. The choice uses
        // the first solve's counts; the re-solve only reduces them, so it never exceeds
        // the budget. Skipped under the power objective (slooping costs power).
        if (result.Status == PlanStatus.Optimal && request.SomersloopBudget > 0 && request.Bias != PlanBias.Power)
        {
            var levels = SomersloopAllocator.Choose(data, result.Recipes, weights, request.Bias, request.SomersloopBudget);
            if (levels.Count > 0)
            {
                progress?.Report(new PlanProgress("Optimizing Somersloops"));
                var slooped = RunSolve(data, request, weights, recipes, partList, partRow,
                    provisionByPart, targetByPart, scaledTargetMode, hasBundle, levels, progress, cancellationToken);
                if (slooped.Status == PlanStatus.Optimal) result = slooped;
            }
        }

        // Clock-fit pass: after the final result (post-Somersloop), apply a uniform
        // overclock factor to squeeze the plan into a machine-count or power budget.
        if (result.Status == PlanStatus.Optimal
            && request.FitMode != FitMode.None
            && request.FitBudget.IsPositive)
        {
            result = ApplyClockFit(data, result, request.FitMode, request.FitBudget);
        }

        return result;
    }

    /// <summary>
    /// One LP solve over the prepared recipe/part system. <paramref name="sloopLevels"/>
    /// (recipe → per-machine sloops) amplifies those recipes' output coefficients; null is
    /// the plain solve. Reads back into a fresh <see cref="PlanResult"/>.
    /// </summary>
    private static PlanResult RunSolve(
        GameDatabase data, PlanRequest request, Dictionary<string, Rational> weights,
        List<RecipeDefinition> recipes, List<string> partList, Dictionary<string, int> partRow,
        Dictionary<string, (Rational Cap, bool Exclusive)> provisionByPart,
        Dictionary<string, Rational> targetByPart, bool scaledTargetMode, bool hasBundle,
        Dictionary<string, int>? sloopLevels, IProgress<PlanProgress>? progress, CancellationToken cancellationToken)
    {
        // ---- columns ----------------------------------------------------
        var costs = new List<Rational>();
        var columnKinds = new List<(char Kind, string Name)>(); // r=recipe s=supply k=sink t=bundle l=slack
        var columns = new List<(int Row, Rational Coefficient)[]>();

        foreach (var recipe in recipes)
        {
            // A slooped recipe's outputs are amplified; inputs (negative) are untouched, so
            // the re-solve naturally rebalances upstream around the boosted output.
            var outFactor = Rational.One;
            if (sloopLevels is not null && sloopLevels.TryGetValue(recipe.Name, out var sloops) && sloops > 0
                && ResolveMachine(data, recipe) is { } sloopMachine)
                outFactor = Amplification.OutputFactor(sloopMachine, sloops);

            // Build column for this recipe imperatively
            var columnEntries = new List<(int Row, Rational Coefficient)>();
            foreach (var part in recipe.Parts)
            {
                if (partRow.TryGetValue(part.Part, out var row))
                {
                    var coefficient = recipe.RatePerMinute(part.Part);
                    if (coefficient.IsPositive && outFactor != Rational.One) coefficient *= outFactor;
                    columnEntries.Add((row, coefficient));
                }
            }
            // Generators contribute to the virtual #Power balance when MW is targeted.
            var generated = PowerGeneratedPerMachine(data, recipe);
            if (generated.IsPositive && partRow.TryGetValue(PowerPart, out var powerRow))
                columnEntries.Add((powerRow, generated));
            columns.Add(columnEntries.ToArray());
            columnKinds.Add(('r', recipe.Name));
            costs.Add(RecipeCost(data, recipe, request.Bias));
        }

        // External supplies: extractable raws (always — Converter recipes can
        // also produce ores, so "has no producer" is not a safe test), true
        // leaf parts, and everything the user offered. Banned raws get none.
        var producible = new HashSet<string>();
        foreach (var recipe in recipes)
        {
            foreach (var output in recipe.Outputs)
            {
                producible.Add(output.Part);
            }
            // #Power is "producible" by generators, never an external supply.
            if (PowerGeneratedPerMachine(data, recipe).IsPositive) producible.Add(PowerPart);
        }
        var capRowBase = partList.Count;
        var capRows = new List<(string Part, Rational Cap)>();
        foreach (var part in partList)
        {
            var provided = provisionByPart.TryGetValue(part, out var provision);
            var isExternal = weights.ContainsKey(part) || !producible.Contains(part);
            // A provision is a deliberate user supply: it overrides the ban so a
            // banned-but-provisioned part still gets its capped supply column.
            if (request.BannedResources.Contains(part) && !provided) continue;
            if (!provided && (!isExternal || request.MaximizeFromProvisions)) continue;

            // Capped supplies carry their cap-row entry inline so the column
            // list fully describes the sparse matrix.
            if (provided)
            {
                columns.Add([(partRow[part], Rational.One), (capRowBase + capRows.Count, Rational.One)]);
                capRows.Add((part, provision.Cap));
            }
            else
            {
                columns.Add([(partRow[part], Rational.One)]);
            }
            columnKinds.Add(('s', part));
            costs.Add(SupplyCost(part, provided, request.Bias, weights));
        }

        // Sinks for solid byproducts.
        var sinkPenalty = request.Byproducts == ByproductMode.Eliminate
            ? SinkPenaltyEliminate
            : SinkPenaltyAllowed;
        foreach (var part in partList)
        {
            if (part == PowerPart) continue; // power cannot be sunk
            if (data.PartsByName.TryGetValue(part, out var def) && def.Fluid) continue;
            columns.Add([(partRow[part], -Rational.One)]);
            columnKinds.Add(('k', part));
            costs.Add(sinkPenalty);
        }

        var bundleCapRow = scaledTargetMode ? capRowBase + capRows.Count : -1;
        var rowCount = partList.Count + capRows.Count + (scaledTargetMode ? 1 : 0);

        var bundleColumn = -1;
        if (hasBundle)
        {
            bundleColumn = costs.Count;
            var bundleEntries = new (int Row, Rational Coefficient)[targetByPart.Count + (bundleCapRow >= 0 ? 1 : 0)];
            var e = 0;
            foreach (var (part, rate) in targetByPart)
                bundleEntries[e++] = (partRow[part], -rate);
            if (bundleCapRow >= 0)
                bundleEntries[e] = (bundleCapRow, Rational.One); // t ≤ 1
            columns.Add(bundleEntries);
            columnKinds.Add(('t', "bundle"));
            costs.Add(Rational.Zero); // bias cost; stage 1 overrides
        }

        // Slack columns for the cap rows (and for "t ≤ 1" in scaled target mode).
        for (var i = 0; i < capRows.Count; i++)
        {
            columns.Add([(capRowBase + i, Rational.One)]);
            columnKinds.Add(('l', capRows[i].Part));
            costs.Add(Rational.Zero);
        }
        if (scaledTargetMode)
        {
            columns.Add([(bundleCapRow, Rational.One)]);
            columnKinds.Add(('l', "bundle"));
            costs.Add(Rational.Zero);
        }

        // ---- sparse system + revised simplex (warm-started stage 2) ------
        var n = costs.Count;
        var b = new Rational[rowCount];
        Array.Fill(b, Rational.Zero);
        if (!hasBundle)
            foreach (var (part, rate) in targetByPart)
                b[partRow[part]] = rate;
        for (var i = 0; i < capRows.Count; i++)
            b[capRowBase + i] = capRows[i].Cap;
        if (bundleCapRow >= 0)
            b[bundleCapRow] = Rational.One;

        var costArray = new Rational[n];
        for (var j = 0; j < n; j++) costArray[j] = costs[j];

        var baseMatrix = SparseMatrix.FromColumns(rowCount, columns);
        RevisedSolveResult solution;
        var achieved = Rational.One;
        if (hasBundle)
        {
            // Stage 1: how much of the bundle is achievable at all?
            progress?.Report(new PlanProgress("Maximizing achievable output"));
            var stage1Costs = new Rational[n];
            Array.Fill(stage1Costs, Rational.Zero);
            stage1Costs[bundleColumn] = -Rational.One;
            var stage1 = RevisedSimplexSolver.Minimize(baseMatrix, b, stage1Costs, cancellationToken);
            if (stage1.Status != PlanStatus.Optimal)
                return new PlanResult { Status = stage1.Status };
            achieved = stage1.Values[bundleColumn];
            if (!achieved.IsPositive)
                return new PlanResult { Status = PlanStatus.Infeasible };

            // Stage 2: pin the bundle with one extra row and warm-start from
            // the stage-1 basis (block-extended B⁻¹) instead of a cold restart.
            var bundleEntriesOld = columns[bundleColumn];
            var pinnedEntries = new (int Row, Rational Coefficient)[bundleEntriesOld.Length + 1];
            Array.Copy(bundleEntriesOld, pinnedEntries, bundleEntriesOld.Length);
            pinnedEntries[^1] = (rowCount, Rational.One);
            columns[bundleColumn] = pinnedEntries;

            var extendedMatrix = SparseMatrix.FromColumns(rowCount + 1, columns);
            var bExtended = new Rational[rowCount + 1];
            Array.Copy(b, bExtended, rowCount);
            bExtended[rowCount] = achieved;

            progress?.Report(new PlanProgress("Solving"));
            solution = stage1.Snapshot is { } snapshot
                ? RevisedSimplexSolver.MinimizeWarm(extendedMatrix, bExtended, costArray, snapshot, bundleColumn, cancellationToken)
                : RevisedSimplexSolver.Minimize(extendedMatrix, bExtended, costArray, cancellationToken);
        }
        else
        {
            progress?.Report(new PlanProgress("Solving"));
            solution = RevisedSimplexSolver.Minimize(baseMatrix, b, costArray, cancellationToken);
        }
        if (solution.Status != PlanStatus.Optimal)
            return new PlanResult { Status = solution.Status };

        // ---- read back ---------------------------------------------------
        var result = new PlanResult
        {
            Status = PlanStatus.Optimal,
            BundleMultiplier = request.MaximizeFromProvisions ? achieved : Rational.One,
            AchievedFraction = scaledTargetMode ? achieved : Rational.One,
        };

        for (var j = 0; j < n; j++)
        {
            var value = solution.Values[j];
            if (!value.IsPositive) continue;
            var (kind, name) = columnKinds[j];
            switch (kind)
            {
                case 'r':
                {
                    var sloopLevel = sloopLevels is not null && sloopLevels.TryGetValue(name, out var lvl) ? lvl : 0;
                    result.Recipes.Add(new PlannedRecipe(name, value, sloopLevel));
                    result.TotalMachines += value;
                    var recipeDef = data.RecipesByName[name];
                    var basePower = PowerPerMachine(data, recipeDef);
                    var generatedMw = PowerGeneratedPerMachine(data, recipeDef);
                    if (generatedMw.IsPositive) result.PowerGeneratedMW += value * generatedMw;
                    if (sloopLevel > 0 && ResolveMachine(data, recipeDef) is { } poweredMachine)
                    {
                        // Power scales by the (≈ quadratic) sloop factor on the reduced count.
                        var powerFactor = Amplification.PowerFactor(poweredMachine, sloopLevel);
                        result.TotalPowerMW += FromMegawatts(value.ToDouble() * basePower.ToDouble() * powerFactor);
                        result.SomersloopsUsed += (int)value.Ceiling() * sloopLevel;
                    }
                    else
                    {
                        result.TotalPowerMW += value * basePower;
                    }
                    break;
                }
                case 's':
                    result.Supplies[name] = value;
                    break;
                case 'k':
                    result.Sinks[name] = value;
                    break;
            }
        }
        foreach (var (part, rate) in targetByPart)
            result.Outputs[part] = hasBundle ? rate * achieved : rate;

        // Tight provisions = the bottlenecks capping the output.
        foreach (var (part, provision) in provisionByPart)
            if (result.Supplies.TryGetValue(part, out var used) && used == provision.Cap)
                result.Bottlenecks.Add(part);

        return result;
    }

    /// <summary>
    /// Computes and applies a single uniform clock factor that scales the solved plan
    /// into the requested budget.
    ///
    /// <b>Machine budget:</b> exact — <c>factor = TotalMachines / budget</c>.
    ///
    /// <b>Power budget approximation (v1):</b> at clock factor <c>f</c> and machine
    /// fraction <c>1/f</c> per recipe, the new power for recipe i is
    /// <c>(machines_i / f) × basePower_i × f^exp_i = machines_i × basePower_i × f^(exp_i − 1)</c>.
    /// So total new power = ∑ machines_i × basePower_i × f^(exp_i − 1).
    /// For v1 we use a power-weighted mean exponent <c>eff_exp</c> across all recipes
    /// (exact when every machine has the same exponent — the standard case where all
    /// production machines share exp ≈ 1.32), giving
    /// <c>budget = TotalPower × f^(eff_exp − 1)</c>  →  <c>f = (budget / TotalPower) ^ (1 / (eff_exp − 1))</c>.
    /// The approximation error is zero for uniform-exponent plans and negligible for
    /// mixed ones (generator exponents differ, but generators contribute zero planner
    /// power because the planner only plans consumers).
    ///
    /// In both cases the factor is clamped to (0.01, 2.5]; if the budget cannot be
    /// met at 250 % the closest achievable result is reported (plan is still Optimal).
    /// </summary>
    private static PlanResult ApplyClockFit(GameDatabase data, PlanResult result, FitMode mode, Rational budget)
    {
        // Compute the raw (unclamped) factor.
        double rawFactor;
        if (mode == FitMode.Machines)
        {
            // factor = TotalMachines / budget  →  new_machines = TotalMachines / factor = budget
            rawFactor = result.TotalMachines.ToDouble() / budget.ToDouble();
        }
        else // FitMode.Power
        {
            if (result.TotalPowerMW.IsZero)
                return result; // no power-consuming machines; nothing to fit

            // Compute power-weighted mean exponent for the approximation.
            // Only recipes with positive base power contribute.
            var totalWeightedExp = 0.0;
            var totalPowerBase = 0.0;
            foreach (var planned in result.Recipes)
            {
                if (!data.RecipesByName.TryGetValue(planned.Recipe, out var recipeDef)) continue;
                var basePower = PowerPerMachine(data, recipeDef).ToDouble();
                if (basePower <= 0) continue;
                var exp = (ResolveMachine(data, recipeDef)?.OverclockPowerExponentValue ?? Rational.One).ToDouble();
                var contribution = planned.Machines.ToDouble() * basePower;
                totalWeightedExp += contribution * exp;
                totalPowerBase += contribution;
            }
            if (totalPowerBase <= 0)
                return result;

            var effExp = totalWeightedExp / totalPowerBase;
            var expMinusOne = effExp - 1.0;
            if (Math.Abs(expMinusOne) < 1e-9)
            {
                // exp ≈ 1: power scales linearly with machines, treat as machine budget at equivalent rate.
                rawFactor = result.TotalPowerMW.ToDouble() / budget.ToDouble();
            }
            else
            {
                // f = (budget / TotalPower)^(1 / (eff_exp - 1))
                rawFactor = Math.Pow(budget.ToDouble() / result.TotalPowerMW.ToDouble(), 1.0 / expMinusOne);
            }
        }

        if (rawFactor <= 0 || double.IsNaN(rawFactor) || double.IsInfinity(rawFactor))
            return result; // degenerate: leave plan unchanged

        // Clamp to (0.01, 2.5] using the exact FactoryNode bounds.
        var minFactor = FactoryNode.MinClockSpeed;   // 0.01
        var maxFactor = FactoryNode.MaxClockSpeed;   // 2.5
        Rational factor;
        if (rawFactor <= minFactor.ToDouble())
            factor = minFactor; // underclocking hits floor
        else if (rawFactor >= maxFactor.ToDouble())
            factor = maxFactor; // overclocking hits ceiling
        else
            factor = FromMegawatts(rawFactor); // reuse double→Rational helper (3 decimal places)

        // Apply factor: divide machine counts and recompute power.
        var newRecipes = new List<PlannedRecipe>(result.Recipes.Count);
        var newTotalMachines = Rational.Zero;
        var newTotalPowerMW = Rational.Zero;

        foreach (var planned in result.Recipes)
        {
            var newMachines = planned.Machines / factor;
            newRecipes.Add(planned with { Machines = newMachines });
            newTotalMachines += newMachines;

            if (!data.RecipesByName.TryGetValue(planned.Recipe, out var recipeDef)) continue;
            var basePower = PowerPerMachine(data, recipeDef);
            if (basePower.IsZero) continue;
            var machine = ResolveMachine(data, recipeDef);
            var exp = machine?.OverclockPowerExponentValue ?? Rational.One;
            // new_power_i = new_machines_i × basePower × factor^exp
            var clockPow = factor.Pow(exp);
            newTotalPowerMW += FromMegawatts(newMachines.ToDouble() * basePower.ToDouble() * clockPow);
        }

        // Build updated result (copy everything from original, replace what changed).
        var fitted = new PlanResult
        {
            Status = result.Status,
            TotalMachines = newTotalMachines,
            TotalPowerMW = newTotalPowerMW,
            BundleMultiplier = result.BundleMultiplier,
            AchievedFraction = result.AchievedFraction,
            SomersloopsUsed = result.SomersloopsUsed,
            ClockFactor = factor,
        };
        fitted.Recipes.AddRange(newRecipes);
        foreach (var (k, v) in result.Supplies) fitted.Supplies[k] = v;
        foreach (var (k, v) in result.Sinks) fitted.Sinks[k] = v;
        foreach (var (k, v) in result.Outputs) fitted.Outputs[k] = v;
        fitted.Bottlenecks.AddRange(result.Bottlenecks);
        return fitted;
    }

    /// <summary>Space Elevator phases as ready-made target bundles (ratios = part amounts).</summary>
    public static IReadOnlyList<(string Phase, IReadOnlyList<PlanTarget> Bundle)> SpaceElevatorPhases(GameDatabase data)
        => data.Document.Recipes
            .Where(r => r.Machine == "Space Elevator" && r.Inputs.Any())
            .Select(r => (r.Name, (IReadOnlyList<PlanTarget>)r.Inputs
                .Select(i => new PlanTarget(i.Part, i.AmountValue.Abs()))
                .ToList()))
            .ToList();

    /// <summary>Machines that pull a part straight out of the ground/water.</summary>
    private static readonly HashSet<string> ExtractorMachines =
        ["Miner", "Oil Extractor", "Water Extractor", "Resource Well Extractor", "Resource Well Pressurizer"];

    /// <summary>
    /// Parts that some extraction recipe outputs (ores, Crude Oil, Water,
    /// Nitrogen Gas, SAM, …), plus anything the scarcity model already prices as
    /// a raw. This is the "extractable raw" notion the ore-conversion filter and
    /// the supply loop share.
    /// </summary>
    public static HashSet<string> ExtractableRaws(GameDatabase data)
    {
        var raws = new HashSet<string>(ScarcityWeights.Build(null).Keys);
        foreach (var recipe in data.Document.Recipes)
        {
            if (!ExtractorMachines.Contains(recipe.Machine)) continue;
            foreach (var output in recipe.Outputs)
                raws.Add(output.Part);
        }
        return raws;
    }

    /// <summary>
    /// Converter recipes that synthesize an extractable raw from SAM ("ore
    /// conversion"). Off by default — they otherwise dominate efficient plans.
    /// </summary>
    public static IEnumerable<string> OreConversionRecipes(GameDatabase data)
    {
        var raws = ExtractableRaws(data);
        foreach (var recipe in data.Document.Recipes)
        {
            if (recipe.Machine != "Converter") continue;
            foreach (var output in recipe.Outputs)
            {
                if (raws.Contains(output.Part))
                {
                    yield return recipe.Name;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Recipe names unlocked above the given progression tier (phase). The
    /// Auto-Plan "available up to tier" cap maps these onto DisabledRecipes so the
    /// planner never reaches for a machine the player hasn't built yet (e.g. the
    /// Blender). Alternates are filtered by their own tier like any other recipe.
    /// </summary>
    public static IEnumerable<string> RecipesAboveTier(GameDatabase data, int maxPhase)
    {
        foreach (var recipe in data.Document.Recipes)
            if (recipe.Tier.Phase > maxPhase)
                yield return recipe.Name;
    }

    // ------------------------------------------------------------- internals

    /// <summary>Backward closure from the targets over eligible recipes,
    /// also pulling in producers of byproducts so loops can close. Recipes
    /// producing an exclusive provision are excluded — that supply is fixed.</summary>
    private static List<RecipeDefinition> CollectCandidateRecipes(
        GameDatabase data, PlanRequest request, HashSet<string> exclusiveParts, out HashSet<string> parts)
    {
        // Filter eligible recipes (imperative loop, no LINQ in hot path)
        var eligible = new List<RecipeDefinition>();
        foreach (var recipe in data.Document.Recipes)
        {
            if (!recipe.Inputs.Any()) continue;
            // A recipe with no part output is eligible only if it generates power
            // (a fuel generator), so a MW target can reach it.
            if (!recipe.Outputs.Any() && !PowerGeneratedPerMachine(data, recipe).IsPositive) continue;
            if (recipe.Machine == "Space Elevator") continue;
            if (recipe.Ficsmas) continue;
            if (request.DisabledRecipes.Contains(recipe.Name)) continue;

            var hasExclusiveOutput = false;
            foreach (var output in recipe.Outputs)
            {
                if (exclusiveParts.Contains(output.Part))
                {
                    hasExclusiveOutput = true;
                    break;
                }
            }
            if (hasExclusiveOutput) continue;

            eligible.Add(recipe);
        }

        // Build producer map (imperative)
        var producers = new Dictionary<string, List<RecipeDefinition>>();
        foreach (var recipe in eligible)
        {
            foreach (var output in recipe.Outputs)
            {
                if (!producers.TryGetValue(output.Part, out var list))
                    producers[output.Part] = list = [];
                list.Add(recipe);
            }
            // Generators "produce" the virtual #Power part so a MW target's backward
            // closure pulls them (and their fuel/water) in.
            if (PowerGeneratedPerMachine(data, recipe).IsPositive)
            {
                if (!producers.TryGetValue(PowerPart, out var powerList))
                    producers[PowerPart] = powerList = [];
                powerList.Add(recipe);
            }
        }

        // Level-synchronous parallel BFS: each depth level is expanded as one
        // parallel pass into thread-local buffers (no shared queue, no cache
        // line bouncing), merged once at the level barrier. Deduplication is
        // lock-free via ConcurrentDictionary.TryAdd. Small frontiers run
        // sequentially — for them the parallel machinery costs more than the
        // work itself.
        const int parallelThreshold = 32;
        var seenParts = new ConcurrentDictionary<string, byte>();
        var included = new ConcurrentDictionary<string, byte>();
        var collected = new ConcurrentDictionary<string, RecipeDefinition>();

        var frontier = new List<string>();
        void Seed(string part)
        {
            if (seenParts.TryAdd(part, 0)) frontier.Add(part);
        }
        foreach (var target in request.Targets) Seed(target.Part);
        foreach (var provision in request.Provisions) Seed(provision.Part);

        void Expand(string part, List<string> buffer)
        {
            if (!producers.TryGetValue(part, out var producingRecipes)) return;
            foreach (var recipe in producingRecipes)
            {
                if (!included.TryAdd(recipe.Name, 0)) continue;
                collected[recipe.Name] = recipe;
                foreach (var reference in recipe.Parts)
                    if (seenParts.TryAdd(reference.Part, 0))
                        buffer.Add(reference.Part);
            }
        }

        while (frontier.Count > 0)
        {
            var nextLevel = new List<string>();
            if (frontier.Count < parallelThreshold)
            {
                foreach (var part in frontier) Expand(part, nextLevel);
            }
            else
            {
                Parallel.ForEach(
                    frontier,
                    static () => new List<string>(),
                    (part, _, local) =>
                    {
                        Expand(part, local);
                        return local;
                    },
                    local =>
                    {
                        if (local.Count == 0) return;
                        lock (nextLevel) nextLevel.AddRange(local);
                    });
            }
            frontier = nextLevel;
        }

        parts = new HashSet<string>(seenParts.Keys);

        // Deterministic column order regardless of traversal interleaving:
        // canonical game-data order (same plans on every run).
        var dataOrder = new Dictionary<string, int>(data.Document.Recipes.Count);
        for (var i = 0; i < data.Document.Recipes.Count; i++)
            dataOrder[data.Document.Recipes[i].Name] = i;
        var result = new List<RecipeDefinition>(collected.Count);
        foreach (var recipe in collected.Values) result.Add(recipe);
        result.Sort((x, y) => dataOrder[x.Name].CompareTo(dataOrder[y.Name]));
        return result;
    }

    private static Rational RecipeCost(GameDatabase data, RecipeDefinition recipe, PlanBias bias) => bias switch
    {
        PlanBias.Machines => Rational.One,
        PlanBias.Power => PowerPerMachine(data, recipe),
        _ => Rational.Zero,
    };

    private static Rational SupplyCost(string part, bool provided, PlanBias bias, Dictionary<string, Rational> weights)
    {
        // Tie-break epsilons would be mathematically harmless but poison every
        // reduced-cost fraction with huge denominators — exact arithmetic pays
        // for them dearly. Bland's deterministic rule already makes results
        // reproducible among equal-cost optima.
        if (provided) return Rational.Zero; // "use what I already have" is free
        return bias == PlanBias.Resources ? ScarcityWeights.WeightFor(weights, part) : Rational.Zero;
    }

    /// <summary>Resolves the machine a recipe runs on — the named machine, or the default
    /// (else first) variant of its multi-machine family.</summary>
    public static MachineDefinition? ResolveMachine(GameDatabase data, RecipeDefinition recipe)
    {
        if (data.MachinesByName.TryGetValue(recipe.Machine, out var machine)) return machine;
        var family = data.MultiMachineFor(recipe.Machine);
        var variant = family?.Machines.FirstOrDefault(v => v.Default) ?? family?.Machines.FirstOrDefault();
        return variant is not null && data.MachinesByName.TryGetValue(variant.Name, out machine) ? machine : null;
    }

    /// <summary>Average MW one machine of this recipe consumes (0 for generators).</summary>
    public static Rational PowerPerMachine(GameDatabase data, RecipeDefinition recipe)
    {
        var recipeOverride = recipe.AveragePower ?? Rational.Zero;
        if (!recipeOverride.IsZero) return recipeOverride.Abs();

        var machine = ResolveMachine(data, recipe);
        if (machine is null) return Rational.Zero;
        var power = machine.AveragePowerValue;
        return power.IsNegative ? power.Abs() : Rational.Zero;
    }

    /// <summary>MW one machine of this recipe generates (positive) — non-zero only for fuel
    /// generators (Coal/Fuel/Nuclear/Biomass). 0 for everything that consumes or is neutral.</summary>
    public static Rational PowerGeneratedPerMachine(GameDatabase data, RecipeDefinition recipe)
    {
        var recipeOverride = recipe.AveragePower ?? Rational.Zero;
        if (recipeOverride.IsPositive) return recipeOverride;
        if (recipeOverride.IsNegative) return Rational.Zero;
        var machine = ResolveMachine(data, recipe);
        return machine is { } m && m.AveragePowerValue.IsPositive ? m.AveragePowerValue : Rational.Zero;
    }

    /// <summary>Builds a Rational from a megawatt double (3 decimals) — used only for the
    /// already-approximate slooped-power accounting, matching the live solver's precision.</summary>
    private static Rational FromMegawatts(double megawatts)
        => new((long)Math.Round(megawatts * 1000.0), 1000);
}
