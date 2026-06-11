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
