namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>One part row. Optional columns default to the common case (solid, no sink value).</summary>
public sealed record Part(
    int Sort,
    string Name,
    Tier Tier,
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
