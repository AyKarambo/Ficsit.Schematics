namespace Ficsit.Schematics.Core.GameData;

public sealed class PartDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public int SinkPoints { get; set; }
    public bool Fluid { get; set; }
}
