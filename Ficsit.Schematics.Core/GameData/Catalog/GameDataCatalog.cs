namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// Assembles the game database from the catalog classes in this assembly.
/// Machines, parts and recipes are plain C# classes (one per file under
/// Catalog/) found via reflection — there is no runtime data file. Order is
/// restored from each entry's SortIndex so lists keep the canonical game order.
/// </summary>
public static class GameDataCatalog
{
    public static GameDataDocument BuildDocument()
    {
        var document = new GameDataDocument();
        document.Machines.AddRange(Discover<MachineBase>()
            .OrderBy(m => m.SortIndex).Select(m => m.ToDefinition()));
        document.MultiMachines.AddRange(Discover<MultiMachineBase>()
            .OrderBy(m => m.SortIndex).Select(m => m.ToDefinition()));
        document.Parts.AddRange(Discover<PartBase>()
            .OrderBy(p => p.SortIndex).Select(p => p.ToDefinition()));
        document.Recipes.AddRange(Discover<RecipeBase>()
            .OrderBy(r => r.SortIndex).Select(r => r.ToDefinition()));
        return document;
    }

    public static GameDatabase BuildDatabase() => new(BuildDocument());

    private static IEnumerable<T> Discover<T>() where T : class
        => typeof(GameDataCatalog).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(T).IsAssignableFrom(type))
            .Select(type => (T)Activator.CreateInstance(type)!);
}
