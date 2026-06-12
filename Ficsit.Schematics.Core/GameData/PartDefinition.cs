namespace Ficsit.Schematics.Core.GameData;

public sealed class PartDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public int SinkPoints { get; set; }
    public bool Fluid { get; set; }

    /// <summary>
    /// True for parts that must be collected by hand in-game (Leaves, Wood,
    /// Mycelia, alien remains, power slugs, FICSMAS drops, etc.).  The planner
    /// treats these as unavailable raw inputs unless the user explicitly
    /// provisions them.
    /// </summary>
    public bool IsManuallyGathered { get; set; }
}
