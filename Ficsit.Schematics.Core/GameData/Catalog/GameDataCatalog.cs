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
        var machines = Discover<MachineBase>().OrderBy(m => m.SortIndex).ToList();

        var document = new GameDataDocument();
        // ToIndexedMachineDefinitions() yields (sortKey, def) pairs — one per standalone
        // machine, or one per variant for merged family classes (each with its original
        // SortIndex so Miner Mk.1/2/3 are interleaved at their canonical positions).
        document.Machines.AddRange(
            machines.SelectMany(m => m.ToIndexedMachineDefinitions())
                    .OrderBy(x => x.SortIndex)
                    .Select(x => x.Definition));
        // Family definitions come from merged-machine classes that override
        // ToFamilyDefinition().  Sorted by FamilySortIndex (canonical multi-machine order).
        document.MultiMachines.AddRange(
            machines
                .Select(m => (FamilySortIndex: GetFamilySortIndex(m), Def: m.ToFamilyDefinition()))
                .Where(x => x.Def is not null)
                .OrderBy(x => x.FamilySortIndex)
                .Select(x => x.Def!));
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

    /// <summary>
    /// Returns the FamilySortIndex for a merged-family machine class, or
    /// <see cref="int.MaxValue"/> for standalone machines.
    /// </summary>
    private static int GetFamilySortIndex(MachineBase m) => m switch
    {
        ExtractorBase e => e.FamilySortIndex,
        GeneratorBase g => g.FamilySortIndex,
        StorageBase s => s.FamilySortIndex,
        SpecialBase sp => sp.FamilySortIndex,
        _ => int.MaxValue,
    };
}
