using Ficsit.Schematics.Core.GameData;
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
/// </summary>
public static class FactoryPlanner
{
    private static readonly Rational SinkPenaltyEliminate = new(1_000_000);
    private static readonly Rational SinkPenaltyAllowed = new(1, 1_000_000);
    private static readonly Rational TieBreakEpsilon = new(1, 1_000_000_000);

    public static PlanResult Plan(GameDatabase data, PlanRequest request, IReadOnlyList<ResourceNodeInfo>? mapNodes = null)
    {
        if (request.Targets.Count == 0)
            return new PlanResult { Status = PlanStatus.Infeasible };

        var weights = ScarcityWeights.Build(mapNodes);
        var exclusiveParts = request.Provisions.Where(p => p.Exclusive).Select(p => p.Part).ToHashSet();
        var recipes = CollectCandidateRecipes(data, request, exclusiveParts, out var parts);

        var provisionByPart = request.Provisions
            .GroupBy(p => p.Part)
            .ToDictionary(
                g => g.Key,
                g => (Cap: g.Aggregate(Rational.Zero, (s, p) => s + p.MaxPerMinute),
                      Exclusive: g.Any(p => p.Exclusive)));
        foreach (var part in provisionByPart.Keys) parts.Add(part);
        var targetByPart = request.Targets
            .GroupBy(t => t.Part)
            .ToDictionary(g => g.Key, g => g.Aggregate(Rational.Zero, (s, t) => s + t.Rate));
        foreach (var part in targetByPart.Keys) parts.Add(part);

        var partList = parts.OrderBy(p => p, StringComparer.Ordinal).ToList();
        var partRow = partList.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);

        // Targets scale together (bundle variable t) when maximizing, or when an
        // exclusive provision may bottleneck a fixed-rate request (t capped at 1).
        var scaledTargetMode = !request.MaximizeFromProvisions && exclusiveParts.Count > 0;
        var hasBundle = request.MaximizeFromProvisions || scaledTargetMode;

        // ---- columns ----------------------------------------------------
        var costs = new List<Rational>();
        var columnKinds = new List<(char Kind, string Name)>(); // r=recipe s=supply k=sink t=bundle l=slack
        var columns = new List<(int Row, Rational Coefficient)[]>();

        foreach (var recipe in recipes)
        {
            columns.Add(recipe.Parts
                .Where(p => partRow.ContainsKey(p.Part))
                .Select(p => (partRow[p.Part], recipe.RatePerMinute(p.Part)))
                .ToArray());
            columnKinds.Add(('r', recipe.Name));
            costs.Add(RecipeCost(data, recipe, request.Bias));
        }

        // External supplies: extractable raws (always — Converter recipes can
        // also produce ores, so "has no producer" is not a safe test), true
        // leaf parts, and everything the user offered. Banned raws get none.
        var producible = new HashSet<string>(recipes.SelectMany(r => r.Outputs.Select(o => o.Part)));
        var capRows = new List<(string Part, Rational Cap)>();
        foreach (var part in partList)
        {
            var provided = provisionByPart.TryGetValue(part, out var provision);
            var isExternal = weights.ContainsKey(part) || !producible.Contains(part);
            if (request.BannedResources.Contains(part)) continue;
            if (!provided && (!isExternal || request.MaximizeFromProvisions)) continue;

            columns.Add([(partRow[part], Rational.One)]);
            columnKinds.Add(('s', part));
            costs.Add(SupplyCost(part, provided, request.Bias, weights));
            if (provided) capRows.Add((part, provision.Cap));
        }

        // Sinks for solid byproducts.
        var sinkPenalty = request.Byproducts == ByproductMode.Eliminate
            ? SinkPenaltyEliminate
            : SinkPenaltyAllowed;
        foreach (var part in partList)
        {
            if (data.PartsByName.TryGetValue(part, out var def) && def.Fluid) continue;
            columns.Add([(partRow[part], -Rational.One)]);
            columnKinds.Add(('k', part));
            costs.Add(sinkPenalty);
        }

        var bundleColumn = -1;
        if (hasBundle)
        {
            bundleColumn = costs.Count;
            columns.Add(targetByPart.Select(t => (partRow[t.Key], -t.Value)).ToArray());
            columnKinds.Add(('t', "bundle"));
            costs.Add(Rational.Zero); // bias cost; stage 1 overrides
        }

        // Slack columns for the cap rows (and for "t ≤ 1" in scaled target mode).
        var capRowBase = partList.Count;
        var bundleCapRow = -1;
        var rowCount = partList.Count + capRows.Count + (scaledTargetMode ? 1 : 0);
        for (var i = 0; i < capRows.Count; i++)
        {
            columns.Add([(capRowBase + i, Rational.One)]);
            columnKinds.Add(('l', capRows[i].Part));
            costs.Add(Rational.Zero);
        }
        if (scaledTargetMode)
        {
            bundleCapRow = capRowBase + capRows.Count;
            columns.Add([(bundleCapRow, Rational.One)]);
            columnKinds.Add(('l', "bundle"));
            costs.Add(Rational.Zero);
        }

        // ---- dense system (optionally with a pinned bundle value) --------
        var n = costs.Count;
        Rational[][] Assemble(Rational? pinnedBundle, out Rational[] b)
        {
            var extraRow = pinnedBundle is not null ? 1 : 0;
            var a = new Rational[rowCount + extraRow][];
            b = new Rational[rowCount + extraRow];
            for (var i = 0; i < a.Length; i++)
            {
                a[i] = new Rational[n];
                Array.Fill(a[i], Rational.Zero);
                b[i] = Rational.Zero;
            }
            for (var j = 0; j < n; j++)
            {
                foreach (var (row, coefficient) in columns[j])
                    a[row][j] = coefficient;
                if (columnKinds[j].Kind == 's')
                {
                    var capIndex = capRows.FindIndex(c => c.Part == columnKinds[j].Name);
                    if (capIndex >= 0) a[capRowBase + capIndex][j] = Rational.One;
                }
                if (columnKinds[j].Kind == 't' && bundleCapRow >= 0)
                    a[bundleCapRow][j] = Rational.One;
            }
            foreach (var (part, rate) in targetByPart)
                b[partRow[part]] = hasBundle ? Rational.Zero : rate;
            for (var i = 0; i < capRows.Count; i++)
                b[capRowBase + i] = capRows[i].Cap;
            if (bundleCapRow >= 0)
                b[bundleCapRow] = Rational.One;
            if (pinnedBundle is not null)
            {
                a[rowCount][bundleColumn] = Rational.One;
                b[rowCount] = pinnedBundle.Value;
            }
            return a;
        }

        SimplexSolution solution;
        var achieved = Rational.One;
        if (hasBundle)
        {
            // Stage 1: how much of the bundle is achievable at all?
            var stage1Costs = new Rational[n];
            Array.Fill(stage1Costs, Rational.Zero);
            stage1Costs[bundleColumn] = -Rational.One;
            var a1 = Assemble(null, out var b1);
            var stage1 = RationalSimplex.Minimize(a1, b1, stage1Costs);
            if (stage1.Status != PlanStatus.Optimal)
                return new PlanResult { Status = stage1.Status };
            achieved = stage1.Values[bundleColumn];
            if (!achieved.IsPositive)
                return new PlanResult { Status = PlanStatus.Infeasible };

            // Stage 2: best plan among those achieving it.
            var a2 = Assemble(achieved, out var b2);
            solution = RationalSimplex.Minimize(a2, b2, [.. costs]);
        }
        else
        {
            var a = Assemble(null, out var b);
            solution = RationalSimplex.Minimize(a, b, [.. costs]);
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
                    result.Recipes.Add(new PlannedRecipe(name, value));
                    result.TotalMachines += value;
                    result.TotalPowerMW += value * PowerPerMachine(data, data.RecipesByName[name]);
                    break;
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

    /// <summary>Space Elevator phases as ready-made target bundles (ratios = part amounts).</summary>
    public static IReadOnlyList<(string Phase, IReadOnlyList<PlanTarget> Bundle)> SpaceElevatorPhases(GameDatabase data)
        => data.Document.Recipes
            .Where(r => r.Machine == "Space Elevator" && r.Inputs.Any())
            .Select(r => (r.Name, (IReadOnlyList<PlanTarget>)r.Inputs
                .Select(i => new PlanTarget(i.Part, i.AmountValue.Abs()))
                .ToList()))
            .ToList();

    // ------------------------------------------------------------- internals

    /// <summary>Backward closure from the targets over eligible recipes,
    /// also pulling in producers of byproducts so loops can close. Recipes
    /// producing an exclusive provision are excluded — that supply is fixed.</summary>
    private static List<RecipeDefinition> CollectCandidateRecipes(
        GameDatabase data, PlanRequest request, HashSet<string> exclusiveParts, out HashSet<string> parts)
    {
        var eligible = data.Document.Recipes
            .Where(r => r.Inputs.Any() && r.Outputs.Any()
                && r.Machine != "Space Elevator"
                && !r.Ficsmas
                && (request.UseAlternateRecipes || !r.Alternate)
                && !r.Outputs.Any(o => exclusiveParts.Contains(o.Part)))
            .ToList();
        var producers = new Dictionary<string, List<RecipeDefinition>>();
        foreach (var recipe in eligible)
            foreach (var output in recipe.Outputs)
            {
                if (!producers.TryGetValue(output.Part, out var list))
                    producers[output.Part] = list = [];
                list.Add(recipe);
            }

        parts = [];
        var included = new HashSet<string>();
        var result = new List<RecipeDefinition>();
        var queue = new Queue<string>(request.Targets.Select(t => t.Part));
        while (queue.Count > 0)
        {
            var part = queue.Dequeue();
            if (!parts.Add(part)) continue;
            if (!producers.TryGetValue(part, out var producingRecipes)) continue;
            foreach (var recipe in producingRecipes)
            {
                if (!included.Add(recipe.Name)) continue;
                result.Add(recipe);
                foreach (var p in recipe.Parts) queue.Enqueue(p.Part);
            }
        }
        return result;
    }

    private static Rational RecipeCost(GameDatabase data, RecipeDefinition recipe, PlanBias bias) => bias switch
    {
        PlanBias.Machines => Rational.One,
        PlanBias.Power => PowerPerMachine(data, recipe) + TieBreakEpsilon,
        _ => Rational.Zero,
    };

    private static Rational SupplyCost(string part, bool provided, PlanBias bias, Dictionary<string, Rational> weights)
    {
        if (provided) return Rational.Zero; // "use what I already have" is free
        var weight = ScarcityWeights.WeightFor(weights, part);
        return bias == PlanBias.Resources ? weight : weight * TieBreakEpsilon;
    }

    /// <summary>Average MW one machine of this recipe consumes (0 for generators).</summary>
    public static Rational PowerPerMachine(GameDatabase data, RecipeDefinition recipe)
    {
        var recipeOverride = GameDatabase.ParseOrZero(recipe.AveragePower);
        if (!recipeOverride.IsZero) return recipeOverride.Abs();

        if (!data.MachinesByName.TryGetValue(recipe.Machine, out var machine))
        {
            var family = data.MultiMachineFor(recipe.Machine);
            var variant = family?.Machines.FirstOrDefault(v => v.Default) ?? family?.Machines.FirstOrDefault();
            if (variant is null || !data.MachinesByName.TryGetValue(variant.Name, out machine))
                return Rational.Zero;
        }
        var power = machine.AveragePowerValue;
        return power.IsNegative ? power.Abs() : Rational.Zero;
    }
}
