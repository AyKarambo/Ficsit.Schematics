using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.CatalogGenerator.Derivation;

/// <summary>
/// Derives the full <see cref="CatalogModel"/> from the Docs export: machine stats,
/// recipe rows per module, the tier fixpoint, and the part list.
/// </summary>
public sealed class CatalogDerivation
{
    private readonly DocsExport _export;
    private readonly ItemIndex _items;
    private readonly SchematicIndex _schematics;
    private readonly List<string> _problems = [];

    /// <summary>Build classes of machines whose power draw varies per recipe.</summary>
    private static readonly HashSet<string> VariablePowerMachines = new(StringComparer.Ordinal)
    {
        "Build_HadronCollider_C", "Build_Converter_C", "Build_QuantumEncoder_C",
    };

    public CatalogDerivation(DocsExport export)
    {
        _export = export;
        _items = new ItemIndex(export);
        _schematics = new SchematicIndex(export);
    }

    /// <summary>Anything the derivation could not resolve; verify mode prints these.</summary>
    public IReadOnlyList<string> Problems => _problems;

    public CatalogModel Derive()
    {
        var stats = MachineTable.All.Select(DeriveMachineStats).ToList();
        var machineTiers = MachineTiersForRecipes(stats);

        var modules = new Dictionary<string, List<RecipeRow>>(StringComparer.Ordinal);
        foreach (var machine in MachineTable.All)
        {
            var rows = machine.Kind switch
            {
                ModuleKind.Recipes => DeriveMachineRecipes(machine),
                ModuleKind.Extractor => DeriveExtractorRecipes(machine),
                ModuleKind.FuelGenerator => DeriveFuelRecipes(machine),
                ModuleKind.Curated => Overrides.CuratedRecipes(machine.RecipeMachine).ToList(),
                _ => [],
            };
            if (rows.Count == 0) continue;
            if (!modules.TryGetValue(machine.RecipeMachine, out var existing))
                modules[machine.RecipeMachine] = existing = [];
            existing.AddRange(rows);
        }

        ResolveTiers(modules, machineTiers, out var partTiers);
        var parts = AssembleParts(modules, stats, partTiers);

        // Deterministic order inside every module: progression, then name.
        var ordered = modules.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<RecipeRow>)kv.Value
                .OrderBy(r => r.Tier)
                .ThenBy(r => r.Alternate)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .ToList(),
            StringComparer.Ordinal);

        return new CatalogModel
        {
            Parts = parts,
            RecipesByModule = ordered,
            MachineStats = stats.OrderBy(s => s.Name, StringComparer.Ordinal).ToList(),
        };
    }

    // ------------------------------------------------------------------ machine stats

    private MachineStatRow DeriveMachineStats(ModeledMachine machine)
    {
        var entry = _export.ByClassName.GetValueOrDefault(machine.BuildClass);
        if (entry is null)
        {
            _problems.Add($"machine: no export entry for {machine.BuildClass}");
            return new MachineStatRow(machine.Name, Tier: "0-0");
        }

        var tier = MachineTier(machine);
        var cost = BuildCost(machine);
        var powerConsumption = Number(entry, "mPowerConsumption") ?? Rational.Zero;
        var canOverclock = entry.String("mCanChangePotential") == "True";
        var overclockExp = canOverclock ? Number(entry, "mPowerConsumptionExponent") : null;

        // Fuel-burning and nuclear generators: positive power, linear overclock curve.
        if (Number(entry, "mPowerProduction") is { IsPositive: true } production)
            return new MachineStatRow(machine.Name, tier, Power: production, OverclockExp: Rational.One, Cost: cost);

        // The Geothermal's output is purity-driven: the export carries the variable-production
        // factor (= average on a normal node); minimum is half of it.
        if (Number(entry, "mVariablePowerProductionFactor") is { IsPositive: true } geoFactor)
            return new MachineStatRow(machine.Name, tier, Power: geoFactor, MinPower: geoFactor / 2, Cost: cost);

        // The Alien Power Augmenter boosts the grid instead of producing.
        if (Number(entry, "mBasePowerProduction") is { IsPositive: true } basePower)
            return new MachineStatRow(machine.Name, tier,
                BasePower: basePower,
                BasePowerBoost: Number(entry, "mBaseBoostPercentage"),
                FueledBasePowerBoost: Overrides.AugmenterFueledBoost,
                Cost: cost);

        // Everything else consumes power (stored negative); zero draw is omitted.
        var sloops = 0;
        Rational? sloopMultiplier = null, sloopPowerExp = null;
        if (entry.String("mCanChangeProductionBoost") == "True")
        {
            sloops = entry.String("mOverrideProductionShardSlotSize") == "True"
                ? int.Parse(entry.Require("mProductionShardSlotSize"))
                : 1; // the class default when not overridden
            if (sloops > 0)
            {
                sloopMultiplier = Number(entry, "mProductionShardBoostMultiplier");
                sloopPowerExp = Number(entry, "mProductionBoostPowerConsumptionExponent");
            }
        }

        // The clock curve only matters where power scales with it: machines that draw power,
        // or whose recipes do (variable-power machines).
        if (!powerConsumption.IsPositive && !VariablePowerMachines.Contains(machine.BuildClass))
            overclockExp = null;

        var throughput = ExtractionPerMinute(entry);
        return new MachineStatRow(machine.Name, tier,
            Power: powerConsumption.IsPositive ? -powerConsumption : null,
            OverclockExp: overclockExp,
            Sloops: sloops,
            SloopMultiplier: sloops > 0 ? sloopMultiplier : null,
            SloopPowerExp: sloops > 0 ? sloopPowerExp : null,
            Cost: cost,
            Throughput: throughput);
    }

    /// <summary>Solid extraction rate in items/min (Miner marks), when the entry extracts solids.</summary>
    private static Rational? ExtractionPerMinute(DocsEntry entry)
    {
        if (entry.String("mAllowedResourceForms") is not "(RF_SOLID)") return null;
        var cycle = Number(entry, "mExtractCycleTime");
        var perCycle = Number(entry, "mItemsPerCycle");
        if (cycle is not { IsPositive: true } || perCycle is null) return null;
        return perCycle.Value * 60 / cycle.Value;
    }

    private Tier MachineTier(ModeledMachine machine)
    {
        if (Overrides.MachineTiers.TryGetValue(machine.Name, out var over)) return over;
        var recipeClass = BuildingRecipeClass(machine);
        if (recipeClass is not null && _schematics.RecipeTier(recipeClass) is { } tier) return tier;
        _problems.Add($"machine: no unlock tier for {machine.Name} ({machine.BuildClass}) — add an override");
        return "0-0";
    }

    private string? BuildingRecipeClass(ModeledMachine machine)
        => BuildingRecipe(machine)?.ClassName;

    private DocsEntry? BuildingRecipe(ModeledMachine machine)
    {
        // The build recipe produces the building's descriptor: Build_X_C → Desc_X_C.
        var descriptor = "Desc_" + machine.BuildClass["Build_".Length..];
        return _export.Group("FGRecipe")?.Entries.FirstOrDefault(
            r => UeText.ItemAmounts(r.String("mProduct")).Any(p => p.ClassName == descriptor));
    }

    private IReadOnlyList<(string Part, long Amount)> BuildCost(ModeledMachine machine)
    {
        var recipe = BuildingRecipe(machine);
        if (recipe is null)
        {
            _problems.Add($"machine: no build recipe for {machine.BuildClass}");
            return [];
        }
        return UeText.ItemAmounts(recipe.String("mIngredients"))
            .Select(i => (_items.Require(i.ClassName).DisplayName, i.Amount))
            .ToList();
    }

    // ------------------------------------------------------------------ FGRecipe modules

    private List<RecipeRow> DeriveMachineRecipes(ModeledMachine machine)
    {
        var rows = new List<RecipeRow>();
        foreach (var recipe in _export.Group("FGRecipe")?.Entries ?? [])
        {
            var producers = UeText.ClassNames(recipe.String("mProducedIn"));
            if (!producers.Contains(machine.BuildClass)) continue;

            var parts = new List<AmountRow>();
            foreach (var (cls, amount) in UeText.ItemAmounts(recipe.String("mIngredients")))
                parts.Add(new AmountRow(_items.Require(cls).DisplayName, -ItemAmount(cls, amount)));
            foreach (var (cls, amount) in UeText.ItemAmounts(recipe.String("mProduct")))
                parts.Add(new AmountRow(_items.Require(cls).DisplayName, ItemAmount(cls, amount)));

            Rational? averagePower = null, minPower = null;
            if (VariablePowerMachines.Contains(machine.BuildClass))
            {
                var constant = Number(recipe, "mVariablePowerConsumptionConstant") ?? Rational.Zero;
                var factor = Number(recipe, "mVariablePowerConsumptionFactor") ?? Rational.Zero;
                averagePower = -(constant + factor / 2);
                minPower = -(constant + factor);
            }

            // Alternates are named "Alternate: X" in the export (though not all carry the
            // prefix — the class name is the reliable marker); the app stores a flag instead.
            var name = recipe.Require("mDisplayName");
            var alternate = name.StartsWith("Alternate: ", StringComparison.Ordinal)
                || recipe.ClassName.StartsWith("Recipe_Alternate", StringComparison.Ordinal);
            if (name.StartsWith("Alternate: ", StringComparison.Ordinal)) name = name["Alternate: ".Length..];
            if (Overrides.RecipeNames.TryGetValue(recipe.ClassName, out var renamed)) name = renamed;

            rows.Add(new RecipeRow(
                machine.RecipeMachine,
                name,
                Batch: Number(recipe, "mManufactoringDuration") ?? Rational.Zero,
                Tier: "0-0", // resolved by the tier fixpoint
                parts,
                Alternate: alternate,
                Ficsmas: _schematics.IsFicsmas(recipe.ClassName),
                AveragePower: averagePower,
                MinPower: minPower,
                // Packaging is cost-neutral: exempt from the global input-cost multiplier.
                IgnoreInputMultiplier: machine.Name == "Packager" || Overrides.IgnoreInputRecipes.Contains(name))
            {
                UnlockTier = _schematics.RecipeTier(recipe.ClassName),
            });
        }
        return rows;
    }

    /// <summary>Amounts are per item; fluids and gases are stored in cm³ and modeled in m³.</summary>
    private Rational ItemAmount(string itemClass, long rawAmount)
        => _items.Require(itemClass).Fluid ? new Rational(rawAmount) / 1000 : new Rational(rawAmount);

    // ------------------------------------------------------------------ extractors

    private List<RecipeRow> DeriveExtractorRecipes(ModeledMachine machine)
    {
        // Missing export entry: DeriveMachineStats already reported the problem for this
        // machine — derive nothing so verify mode prints it instead of crashing here.
        if (!_export.ByClassName.TryGetValue(machine.BuildClass, out var entry)) return [];
        var rows = new List<RecipeRow>();

        // Extraction is available once the machine exists and any gating milestone is passed;
        // scanner-unlock tiers are unusable (the game re-adds ores at arbitrary milestones).
        var machineTier = MachineTier(machine);

        if (machine.RecipeMachine == "Miner")
        {
            // One row per solid resource; the family variant ratio carries the actual rate,
            // so the row itself is normalized to 1/min.
            foreach (var resource in _items.All.Where(i => i.IsResource && !i.Fluid)
                         .OrderBy(i => i.DisplayName, StringComparer.Ordinal))
                rows.Add(new RecipeRow(machine.RecipeMachine, resource.DisplayName, Batch: 60, Tier: "0-0",
                    [new AmountRow(resource.DisplayName, 1)])
                {
                    UnlockTier = GatedTier(machineTier, resource.DisplayName),
                });
            return rows;
        }

        // Fluid extractors: one row per allowed resource at the machine's rate (per minute).
        var cycle = Number(entry, "mExtractCycleTime") ?? Rational.One;
        var perCycle = Number(entry, "mItemsPerCycle") ?? Rational.Zero;
        var perMinute = perCycle / 1000 * 60 / cycle;
        foreach (var resourceClass in UeText.ClassNames(entry.String("mAllowedResources")))
        {
            var resource = _items.Require(resourceClass);
            var name = machine.Name switch
            {
                // Curated display names for well outputs (the app's naming, not the game's).
                "Resource Well Extractor" when resource.DisplayName == "Water" => "Well Water",
                "Resource Well Extractor" when resource.DisplayName == "Crude Oil" => "Oil Well",
                _ => resource.DisplayName,
            };
            rows.Add(new RecipeRow(machine.RecipeMachine, name, Batch: 60, Tier: "0-0",
                [new AmountRow(resource.DisplayName, perMinute)])
            {
                UnlockTier = GatedTier(machineTier, resource.DisplayName),
            });
        }
        return rows;
    }

    private static Tier GatedTier(Tier machineTier, string resource)
        => Overrides.OreGates.TryGetValue(resource, out var gate) && gate.CompareTo(machineTier) > 0
            ? gate
            : machineTier;

    // ------------------------------------------------------------------ fuel generators

    private List<RecipeRow> DeriveFuelRecipes(ModeledMachine machine)
    {
        // Same graceful path as DeriveExtractorRecipes: the problem is already recorded.
        if (!_export.ByClassName.TryGetValue(machine.BuildClass, out var entry)) return [];
        var power = Number(entry, "mPowerProduction") ?? Rational.One;
        var fuelLoad = Number(entry, "mFuelLoadAmount") ?? Rational.One;
        var supplementalRatio = Number(entry, "mSupplementalToPowerRatio") ?? Rational.Zero;
        var rows = new List<RecipeRow>();

        foreach (var fuel in entry.Objects("mFuel"))
        {
            var fuelItem = _items.Find(fuel.String("mFuelClass") ?? "");
            if (fuelItem is null) continue;

            // Burn time: total energy in the load divided by the generator's output.
            // mEnergyValue is per item (per cm³ for fluids), the load is in items/cm³.
            var batch = fuelItem.EnergyMJ * fuelLoad / power;
            var parts = new List<AmountRow>
            {
                new(fuelItem.DisplayName, -(fuelItem.Fluid ? fuelLoad / 1000 : fuelLoad)),
            };

            if (_items.Find(fuel.String("mSupplementalResourceClass") ?? "") is { } supplemental)
            {
                // Supplemental (water) flow tracks output power: ratio is cm³ per MJ.
                var amount = power * supplementalRatio * batch / 1000;
                parts.Add(new AmountRow(supplemental.DisplayName, -amount));
            }

            if (_items.Find(fuel.String("mByproduct") ?? "") is { } byproduct)
                parts.Add(new AmountRow(byproduct.DisplayName,
                    Rational.Parse(fuel.String("mByproductAmount") ?? "0")));

            rows.Add(new RecipeRow(machine.RecipeMachine, FuelRecipeName(machine.Name, fuelItem.DisplayName),
                Batch: batch, Tier: "0-0", parts, IgnoreInputMultiplier: true));
        }
        return rows;
    }

    /// <summary>The app's naming scheme for generator fuel recipes.</summary>
    private static string FuelRecipeName(string machine, string fuel) => machine switch
    {
        "Biomass Burner" => fuel == "Biomass" ? "Biomass Burner" : $"{fuel} Biomass Burner",
        "Nuclear Power Plant" => $"{fuel.Replace(" Fuel Rod", "")} Nuclear Power Plant",
        _ => $"{fuel} Generator",
    };

    // ------------------------------------------------------------------ tiers

    /// <summary>Machine tier as seen by recipes: families resolve to their lowest mark.</summary>
    private static Dictionary<string, Tier> MachineTiersForRecipes(List<MachineStatRow> stats)
    {
        var byName = stats.ToDictionary(s => s.Name, s => s.Tier, StringComparer.Ordinal);
        var result = new Dictionary<string, Tier>(StringComparer.Ordinal);
        foreach (var machine in MachineTable.All)
        {
            var tier = byName[machine.Name];
            if (!result.TryGetValue(machine.RecipeMachine, out var existing) || tier.CompareTo(existing) < 0)
                result[machine.RecipeMachine] = tier;
        }
        return result;
    }

    /// <summary>
    /// The tier fixpoint: recipes unlocked by a milestone keep that tier; every other recipe
    /// becomes available at max(machine tier, its ingredients' tiers); a part becomes
    /// available at the min tier of the recipes producing it. Iterated to a fixed point.
    /// </summary>
    private void ResolveTiers(
        Dictionary<string, List<RecipeRow>> modules,
        Dictionary<string, Tier> machineTiers,
        out Dictionary<string, Tier> partTiers)
    {
        // Fixed part tiers: hand overrides plus milestone-gated raw resources (an alternate
        // recipe producing an ore does not make the ore available earlier).
        var pinned = new HashSet<string>(Overrides.PartTiers.Keys, StringComparer.Ordinal);
        partTiers = new Dictionary<string, Tier>(Overrides.PartTiers, StringComparer.Ordinal);
        foreach (var (ore, gate) in Overrides.OreGates)
        {
            partTiers[ore] = gate;
            pinned.Add(ore);
        }

        var recipeTiers = new Dictionary<RecipeRow, Tier>(ReferenceEqualityComparer.Instance);
        var all = modules.Values.SelectMany(r => r).ToList();
        foreach (var row in all)
            if (row.UnlockTier is { } preset)
                recipeTiers[row] = preset;

        // A part's availability comes from recipes that primarily produce it: byproducts
        // don't count (Rocket Fuel's Compacted Coal), unpackaging doesn't count (it would
        // form a cycle with packaging), and alternates only count for parts that have no
        // standard primary producer at all (e.g. Compacted Coal, Turbofuel).
        static string? PrimaryOutput(RecipeRow row)
            => row.Name.StartsWith("Unpackage ", StringComparison.Ordinal)
                ? null
                : row.Parts.FirstOrDefault(p => p.Amount.IsPositive)?.Part;

        var standardProduced = new HashSet<string>(
            all.Where(r => !r.Alternate).Select(PrimaryOutput).OfType<string>(),
            StringComparer.Ordinal);

        // Parts nothing primarily produces (byproduct-only, e.g. Dissolved Silica) take
        // their availability from whatever recipe emits them.
        var primaryProduced = new HashSet<string>(
            all.Select(PrimaryOutput).OfType<string>(), StringComparer.Ordinal);

        for (var iteration = 0; iteration < 64; iteration++)
        {
            var changed = false;

            foreach (var row in all)
            {
                if (row.UnlockTier is not null) continue;
                var tier = machineTiers[row.Machine];
                var resolvable = true;
                foreach (var part in row.Parts.Where(p => p.Amount.IsNegative))
                {
                    if (!partTiers.TryGetValue(part.Part, out var partTier)) { resolvable = false; break; }
                    if (partTier.CompareTo(tier) > 0) tier = partTier;
                }
                if (!resolvable) continue;
                if (!recipeTiers.TryGetValue(row, out var existing) || tier.CompareTo(existing) != 0)
                {
                    recipeTiers[row] = tier;
                    changed = true;
                }
            }

            foreach (var row in all)
            {
                if (!recipeTiers.TryGetValue(row, out var recipeTier)) continue;
                var primary = PrimaryOutput(row);
                foreach (var output in row.Parts.Where(p => p.Amount.IsPositive))
                {
                    var counts = output.Part == primary
                        ? !row.Alternate || !standardProduced.Contains(output.Part)
                        : !primaryProduced.Contains(output.Part);
                    if (!counts || pinned.Contains(output.Part)) continue;
                    if (!partTiers.TryGetValue(output.Part, out var existing) || recipeTier.CompareTo(existing) < 0)
                    {
                        partTiers[output.Part] = recipeTier;
                        changed = true;
                    }
                }
            }

            if (!changed) break;
        }

        if (Environment.GetEnvironmentVariable("CG_DEBUG_TIERS") == "1")
        {
            var resolved = partTiers;
            var unresolvedParts = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var row in all.Where(r => !recipeTiers.ContainsKey(r)))
            {
                var blocked = row.Parts.Where(p => p.Amount.IsNegative && !resolved.ContainsKey(p.Part)).Select(p => p.Part).ToList();
                unresolvedParts.UnionWith(blocked);
                Console.Error.WriteLine($"debug: '{row.Name}' ({row.Machine}) blocked on [{string.Join(", ", blocked)}] alternate={row.Alternate}");
            }
            foreach (var part in unresolvedParts)
            {
                var producers = all.Where(r => r.Parts.Any(p => p.Amount.IsPositive && p.Part == part))
                    .Select(r => $"{r.Name} (alt={r.Alternate}, resolved={recipeTiers.ContainsKey(r)})");
                Console.Error.WriteLine($"debug: part '{part}' std={standardProduced.Contains(part)} producers: {string.Join("; ", producers)}");
            }
        }

        foreach (var (module, rows) in modules.ToList())
            modules[module] = rows.Select(row =>
            {
                if (recipeTiers.TryGetValue(row, out var tier)) return row with { Tier = tier };
                _problems.Add($"tier: unresolved for recipe '{row.Name}' ({row.Machine})");
                return row;
            }).ToList();
    }

    // ------------------------------------------------------------------ parts

    private List<PartRow> AssembleParts(
        Dictionary<string, List<RecipeRow>> modules,
        List<MachineStatRow> stats,
        Dictionary<string, Tier> partTiers)
    {
        var referenced = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in modules.Values.SelectMany(r => r))
            foreach (var part in row.Parts)
                referenced.Add(part.Part);
        foreach (var stat in stats)
            foreach (var (part, _) in stat.Cost ?? [])
                referenced.Add(part);

        var infoByName = new Dictionary<string, ItemInfo>(StringComparer.Ordinal);
        foreach (var item in _items.All)
            infoByName.TryAdd(item.DisplayName, item);

        var parts = new List<PartRow>();
        foreach (var name in referenced)
        {
            if (!infoByName.TryGetValue(name, out var info))
            {
                _problems.Add($"part: no descriptor for '{name}'");
                continue;
            }
            if (!partTiers.TryGetValue(name, out var tier))
            {
                _problems.Add($"part: unresolved tier for '{name}' — add an override");
                tier = "0-0";
            }
            parts.Add(new PartRow(
                name,
                tier,
                SinkPoints: info.Fluid ? 0 : info.SinkPoints,
                Fluid: info.Fluid,
                ManuallyGathered: Overrides.ManuallyGathered.Contains(name)));
        }

        // Deterministic canonical order: progression, sinkable before non-sinkable, cheap first.
        return parts
            .OrderBy(p => p.Tier)
            .ThenBy(p => p.SinkPoints == 0)
            .ThenBy(p => p.SinkPoints)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static Rational? Number(DocsEntry entry, string property)
        => entry.String(property) is { } text && Rational.TryParse(text, out var value) ? value : null;
}
