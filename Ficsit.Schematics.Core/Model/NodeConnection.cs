namespace Ficsit.Schematics.Core.Model;

/// <summary>A directed part flow between two nodes (producer output → consumer input).</summary>
public sealed class NodeConnection
{
    public required FactoryNode From { get; set; }
    public required FactoryNode To { get; set; }

    /// <summary>The part carried. Specialty nodes adopt whatever part is connected.</summary>
    public required string Part { get; set; }

    /// <summary>How the part travels: belt/pipe, or a save-imported vehicle route.</summary>
    public LogisticsKind Logistics { get; set; }

    /// <summary>Optional user-placed waypoints, in canvas coordinates.</summary>
    public List<(double X, double Y)> Waypoints { get; } = [];
}
