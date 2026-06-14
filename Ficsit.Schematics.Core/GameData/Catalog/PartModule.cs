namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A group of parts authored as a readable table (one <see cref="Part"/> per row).
/// Discovered via reflection by <see cref="GameDataCatalog"/>; each row's
/// <see cref="Part.Sort"/> places it in the canonical game-data order.
/// </summary>
public abstract class PartModule
{
    protected abstract IReadOnlyList<Part> Parts { get; }

    public IEnumerable<(int Sort, PartDefinition Definition)> Build()
        => Parts.Select(p => (p.Sort, p.ToDefinition()));
}
