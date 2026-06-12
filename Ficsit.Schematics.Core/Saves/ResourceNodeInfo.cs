namespace Ficsit.Schematics.Core.Saves;

/// <summary>One resource node read from a Satisfactory save file.</summary>
public sealed class ResourceNodeInfo
{
    public int Id { get; set; }

    /// <summary>Stable per-save actor instance path, e.g. "Persistent_Level:PersistentLevel.BP_ResourceNode620".</summary>
    public string Instance { get; set; } = string.Empty;

    public ResourceNodeKind Kind { get; set; }

    /// <summary>Game part name ("Iron Ore", "Crude Oil", …) or "Geyser".</summary>
    public string Part { get; set; } = string.Empty;

    /// <summary>"Impure" | "Normal" | "Pure" (Normal when the save carries no purity).</summary>
    public string Purity { get; set; } = "Normal";

    /// <summary>World position in centimeters (Unreal coordinates).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}
