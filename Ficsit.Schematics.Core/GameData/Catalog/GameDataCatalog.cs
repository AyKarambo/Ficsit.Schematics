namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// Assembles the game database from the catalog modules in this assembly.
/// Machines, parts and recipes are authored as grouped C# tables
/// (<see cref="MachineModule"/>, <see cref="PartModule"/>, <see cref="RecipeModule"/>)
/// and discovered via reflection — there is no runtime data file. Each entry's
/// sort key restores the canonical game order.
/// </summary>
public static class GameDataCatalog
{
    public static GameDataDocument BuildDocument()
    {
        var machineModules = Discover<MachineModule>().ToList();

        var document = new GameDataDocument();
        document.Machines.AddRange(Ordered(machineModules.SelectMany(m => m.Machines)));
        document.MultiMachines.AddRange(Ordered(machineModules.SelectMany(m => m.Families)));
        document.Parts.AddRange(Ordered(Discover<PartModule>().SelectMany(m => m.Build())));
        document.Recipes.AddRange(Ordered(Discover<RecipeModule>().SelectMany(m => m.Build())));
        return document;
    }

    public static GameDatabase BuildDatabase() => new(BuildDocument());

    /// <summary>Restores canonical game order from the per-entry sort keys.</summary>
    private static IEnumerable<TDefinition> Ordered<TDefinition>(
        IEnumerable<(int Sort, TDefinition Definition)> items)
        => items.OrderBy(item => item.Sort).Select(item => item.Definition);

    private static IEnumerable<T> Discover<T>() where T : class
        => typeof(GameDataCatalog).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(T).IsAssignableFrom(type))
            .Select(type => (T)Activator.CreateInstance(type)!);
}
