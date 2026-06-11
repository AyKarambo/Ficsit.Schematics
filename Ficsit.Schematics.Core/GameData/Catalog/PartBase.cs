namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// A part contributed to the game catalog. Every sealed subclass is discovered
/// via reflection by <see cref="GameDataCatalog"/> — adding a class is all it
/// takes to add a part to the app.
/// </summary>
public abstract class PartBase
{
    /// <summary>Position in the canonical game-data ordering (drives list/chooser order).</summary>
    public abstract int SortIndex { get; }

    public abstract string PartName { get; }

    /// <summary>Unlock tier, e.g. "0-2" (tier 0, milestone 2).</summary>
    public abstract string Tier { get; }

    public virtual int SinkPoints => 0;

    public virtual bool Fluid => false;

    public PartDefinition ToDefinition() => new()
    {
        Name = PartName,
        Tier = Tier,
        SinkPoints = SinkPoints,
        Fluid = Fluid,
    };
}
