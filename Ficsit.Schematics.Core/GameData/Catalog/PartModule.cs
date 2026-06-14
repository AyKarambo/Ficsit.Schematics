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

/// <summary>One part row. Optional columns default to the common case (solid, no sink value).</summary>
public sealed record Part(
    int Sort,
    string Name,
    string Tier,
    int SinkPoints = 0,
    bool Fluid = false,
    bool ManuallyGathered = false)
{
    public PartDefinition ToDefinition() => new()
    {
        Name = Name,
        Tier = Tier,
        SinkPoints = SinkPoints,
        Fluid = Fluid,
        IsManuallyGathered = ManuallyGathered,
    };
}
