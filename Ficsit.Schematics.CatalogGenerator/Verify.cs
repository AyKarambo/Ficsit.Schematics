using Ficsit.Schematics.CatalogGenerator.Derivation;
using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.GameData.Catalog;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.CatalogGenerator;

/// <summary>
/// Diffs the derived <see cref="CatalogModel"/> against the catalog currently compiled into
/// Core (<see cref="GameDataCatalog.BuildDocument"/>). Every difference is printed — each is
/// either a real game-data change to accept, a derivation bug to fix, or a rename to record
/// in <see cref="Overrides.LegacyNames"/>.
/// </summary>
public static class Verify
{
    public static int Run(CatalogModel model, IReadOnlyList<string> problems)
    {
        var diffs = Collect(model, problems, GameDataCatalog.BuildDocument());
        foreach (var diff in diffs) Console.WriteLine(diff);
        Console.WriteLine($"--- {diffs.Count} difference(s).");
        return diffs.Count == 0 ? 0 : 3;
    }

    /// <summary>Every difference between the derived model and <paramref name="document"/> —
    /// empty when the compiled catalog matches the export (the oracle test asserts this).</summary>
    public static List<string> Collect(
        CatalogModel model, IReadOnlyList<string> problems, GameDataDocument document)
    {
        var diffs = new List<string>();
        diffs.AddRange(problems.Select(p => $"problem: {p}"));

        CompareParts(model, document, diffs);
        CompareRecipes(model, document, diffs);
        CompareMachines(model, document, diffs);
        return diffs;
    }

    private static void CompareParts(CatalogModel model, GameDataDocument document, List<string> diffs)
    {
        var derived = model.Parts.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var current = document.Parts.ToDictionary(p => p.Name, StringComparer.Ordinal);

        foreach (var name in current.Keys.Except(derived.Keys)) diffs.Add($"part missing from derivation: {name}");
        foreach (var name in derived.Keys.Except(current.Keys)) diffs.Add($"part new in derivation: {name}");

        foreach (var (name, part) in derived)
        {
            if (!current.TryGetValue(name, out var reference)) continue;
            if (part.Tier != reference.Tier) diffs.Add($"part {name}: tier {reference.Tier} -> {part.Tier}");
            if (part.SinkPoints != reference.SinkPoints) diffs.Add($"part {name}: sink {reference.SinkPoints} -> {part.SinkPoints}");
            if (part.Fluid != reference.Fluid) diffs.Add($"part {name}: fluid {reference.Fluid} -> {part.Fluid}");
            if (part.ManuallyGathered != reference.IsManuallyGathered) diffs.Add($"part {name}: manual {reference.IsManuallyGathered} -> {part.ManuallyGathered}");
        }
    }

    private static void CompareRecipes(CatalogModel model, GameDataDocument document, List<string> diffs)
    {
        var derived = new Dictionary<string, RecipeRow>(StringComparer.Ordinal);
        foreach (var row in model.AllRecipes)
            if (!derived.TryAdd(row.Name, row))
                diffs.Add($"recipe duplicate name in derivation: {row.Name} ({derived[row.Name].Machine} vs {row.Machine})");
        var current = document.Recipes.ToDictionary(r => r.Name, StringComparer.Ordinal);

        foreach (var name in current.Keys.Except(derived.Keys)) diffs.Add($"recipe missing from derivation: {name} ({current[name].Machine})");
        foreach (var name in derived.Keys.Except(current.Keys)) diffs.Add($"recipe new in derivation: {name} ({derived[name].Machine})");

        foreach (var (name, recipe) in derived)
        {
            if (!current.TryGetValue(name, out var reference)) continue;
            if (recipe.Machine != reference.Machine) diffs.Add($"recipe {name}: machine {reference.Machine} -> {recipe.Machine}");
            if (recipe.Batch != reference.BatchTime) diffs.Add($"recipe {name}: batch {reference.BatchTime} -> {recipe.Batch}");
            if (recipe.Tier != reference.Tier) diffs.Add($"recipe {name}: tier {reference.Tier} -> {recipe.Tier}");
            if (recipe.Alternate != reference.Alternate) diffs.Add($"recipe {name}: alternate {reference.Alternate} -> {recipe.Alternate}");
            if (recipe.Ficsmas != reference.Ficsmas) diffs.Add($"recipe {name}: ficsmas {reference.Ficsmas} -> {recipe.Ficsmas}");
            if (!NullableEquals(recipe.AveragePower, reference.AveragePower)) diffs.Add($"recipe {name}: avgPower {reference.AveragePower} -> {recipe.AveragePower}");
            if (!NullableEquals(recipe.MinPower, reference.MinPower)) diffs.Add($"recipe {name}: minPower {reference.MinPower} -> {recipe.MinPower}");
            if (recipe.IgnoreInputMultiplier != reference.IgnoreInputMultiplier) diffs.Add($"recipe {name}: ignoreInput {reference.IgnoreInputMultiplier} -> {recipe.IgnoreInputMultiplier}");
            if ((recipe.SpaceElevatorMultiplier ?? "") != (reference.SpaceElevatorMultiplier ?? "")) diffs.Add($"recipe {name}: elevator '{reference.SpaceElevatorMultiplier}' -> '{recipe.SpaceElevatorMultiplier}'");

            var derivedParts = recipe.Parts.OrderBy(p => p.Part, StringComparer.Ordinal).Select(p => $"{p.Part}={p.Amount}");
            var referenceParts = reference.Parts.OrderBy(p => p.Part, StringComparer.Ordinal).Select(p => $"{p.Part}={p.Amount}");
            if (!derivedParts.SequenceEqual(referenceParts))
                diffs.Add($"recipe {name}: parts [{string.Join(", ", referenceParts)}] -> [{string.Join(", ", derivedParts)}]");
        }
    }

    private static void CompareMachines(CatalogModel model, GameDataDocument document, List<string> diffs)
    {
        var derived = model.MachineStats.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var current = document.Machines.ToDictionary(m => m.Name, StringComparer.Ordinal);

        foreach (var name in current.Keys.Except(derived.Keys)) diffs.Add($"machine missing from derivation: {name}");
        foreach (var name in derived.Keys.Except(current.Keys)) diffs.Add($"machine new in derivation: {name}");

        foreach (var (name, stat) in derived)
        {
            if (!current.TryGetValue(name, out var reference)) continue;
            if (stat.Tier != reference.Tier) diffs.Add($"machine {name}: tier {reference.Tier} -> {stat.Tier}");
            if (!NullableEquals(stat.Power, reference.AveragePower)) diffs.Add($"machine {name}: power {reference.AveragePower} -> {stat.Power}");
            if (!NullableEquals(stat.MinPower, reference.MinPower)) diffs.Add($"machine {name}: minPower {reference.MinPower} -> {stat.MinPower}");
            if (!NullableEquals(stat.BasePower, reference.BasePower)) diffs.Add($"machine {name}: basePower {reference.BasePower} -> {stat.BasePower}");
            if (!NullableEquals(stat.BasePowerBoost, reference.BasePowerBoost)) diffs.Add($"machine {name}: baseBoost {reference.BasePowerBoost} -> {stat.BasePowerBoost}");
            if (!NullableEquals(stat.FueledBasePowerBoost, reference.FueledBasePowerBoost)) diffs.Add($"machine {name}: fueledBoost {reference.FueledBasePowerBoost} -> {stat.FueledBasePowerBoost}");
            if (!NullableEquals(stat.OverclockExp, reference.OverclockPowerExponent)) diffs.Add($"machine {name}: overclockExp {reference.OverclockPowerExponent} -> {stat.OverclockExp}");
            if (stat.Sloops != reference.MaxProductionShards) diffs.Add($"machine {name}: sloops {reference.MaxProductionShards} -> {stat.Sloops}");
            if (!NullableEquals(stat.SloopMultiplier, reference.ProductionShardMultiplier)) diffs.Add($"machine {name}: sloopMult {reference.ProductionShardMultiplier} -> {stat.SloopMultiplier}");
            if (!NullableEquals(stat.SloopPowerExp, reference.ProductionShardPowerExponent)) diffs.Add($"machine {name}: sloopExp {reference.ProductionShardPowerExponent} -> {stat.SloopPowerExp}");

            var derivedCost = (stat.Cost ?? []).OrderBy(c => c.Part, StringComparer.Ordinal).Select(c => $"{c.Part}={c.Amount}");
            var referenceCost = reference.Cost.OrderBy(c => c.Part, StringComparer.Ordinal).Select(c => $"{c.Part}={c.Amount}");
            if (!derivedCost.SequenceEqual(referenceCost))
                diffs.Add($"machine {name}: cost [{string.Join(", ", referenceCost)}] -> [{string.Join(", ", derivedCost)}]");
        }

        // Family variant throughputs (Miner marks) against the derived extraction rates.
        foreach (var family in document.MultiMachines)
            foreach (var variant in family.Machines)
                if (variant.PartsRatio is { } ratio
                    && derived.TryGetValue(variant.Name, out var stat)
                    && !NullableEquals(stat.Throughput, ratio))
                    diffs.Add($"variant {variant.Name}: throughput {ratio} -> {stat.Throughput}");
    }

    private static bool NullableEquals(Rational? a, Rational? b)
        => a.HasValue == b.HasValue && (!a.HasValue || a.Value == b!.Value);
}
