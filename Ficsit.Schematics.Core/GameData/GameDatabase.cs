using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

/// <summary>
/// Loaded game data with lookup indices. Immutable after construction; one
/// instance is shared application-wide. Built from the C# catalog classes —
/// see <see cref="Catalog.GameDataCatalog"/>.
/// </summary>
public sealed class GameDatabase
{
    public GameDataDocument Document { get; }

    public IReadOnlyDictionary<string, MachineDefinition> MachinesByName { get; }
    public IReadOnlyDictionary<string, MultiMachineDefinition> MultiMachinesByName { get; }
    public IReadOnlyDictionary<string, PartDefinition> PartsByName { get; }
    public IReadOnlyDictionary<string, RecipeDefinition> RecipesByName { get; }

    /// <summary>Recipes grouped by produced part, in data order.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RecipeDefinition>> RecipesByOutput { get; }

    /// <summary>
    /// Machines that are unified fuel generators: they produce power and burn one of several
    /// fuels (Fuel-Powered / Coal-Powered / Nuclear / Biomass Burner). Each is modeled as a
    /// single node (<see cref="Model.NodeKind.Generator"/>) that accepts any of its fuels — the
    /// fuel is the recipe's first input, and the connected fuel selects the active recipe. The
    /// fuel-less Geothermal Generator (purity-driven) is excluded.
    /// </summary>
    public IReadOnlySet<string> GeneratorMachines { get; }

    /// <summary>
    /// The highest belt throughput (parts/min) present in the catalog — derived from the
    /// Belt-mark capacities on machines such as the AWESOME Sink.  A future Mk.7 belt added
    /// to the catalog will automatically raise this threshold.
    /// </summary>
    public Rational MaxBeltThroughput { get; }

    /// <summary>
    /// The highest pipe throughput (fluids/min) for a single pipeline.  Satisfactory ships
    /// Mk.1 (300/min) and Mk.2 (600/min) pipes; pipes are not yet modeled as Belt-style
    /// capacity entries in the catalog, so this constant reflects the Mk.2 value.
    /// </summary>
    public Rational MaxPipeThroughput { get; } = new Rational(600);

    public GameDatabase(GameDataDocument document)
    {
        Document = document;
        MachinesByName = document.Machines.ToDictionary(m => m.Name);
        MultiMachinesByName = document.MultiMachines.ToDictionary(m => m.Name);
        PartsByName = document.Parts.ToDictionary(p => p.Name);
        RecipesByName = document.Recipes.ToDictionary(r => r.Name);

        var byOutput = new Dictionary<string, IReadOnlyList<RecipeDefinition>>();
        foreach (var group in document.Recipes
                     .SelectMany(r => r.Outputs.Select(o => (o.Part, Recipe: r)))
                     .GroupBy(x => x.Part))
            byOutput[group.Key] = group.Select(x => x.Recipe).ToList();
        RecipesByOutput = byOutput;

        // Derive MaxBeltThroughput from Belt-mark capacities in the catalog.
        // Belt capacities are named "Mk.N Belt" and carry a parts-per-minute ratio.
        var beltMax = document.MultiMachines
            .SelectMany(mm => mm.Capacities)
            .Where(c => c.Name.EndsWith(" Belt", StringComparison.Ordinal) && c.PartsRatio.HasValue)
            .Select(c => c.PartsRatio!.Value)
            .DefaultIfEmpty(new Rational(1200))
            .Max();
        MaxBeltThroughput = beltMax;

        // A unified fuel generator: produces power and has recipes that burn a fuel (≥1 input).
        var fuelMachines = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in document.Recipes)
            if (recipe.Inputs.Any()
                && MachinesByName.TryGetValue(recipe.Machine, out var machine)
                && machine.AveragePowerValue.IsPositive)
                fuelMachines.Add(recipe.Machine);
        GeneratorMachines = fuelMachines;
    }

    /// <summary>
    /// The multi-machine family a recipe's machine belongs to, when any
    /// ("Miner Mk.1" recipes belong to family "Miner"; most machines have none).
    /// </summary>
    public MultiMachineDefinition? MultiMachineFor(string machineName)
    {
        if (MultiMachinesByName.TryGetValue(machineName, out var direct))
            return direct;
        return Document.MultiMachines.FirstOrDefault(
            mm => mm.Machines.Any(v => v.Name == machineName));
    }

    internal static Rational ParseOrZero(string? text)
        => text is not null && Rational.TryParse(text, out var value) ? value : Rational.Zero;
}
