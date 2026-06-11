namespace Ficsit.Schematics.Core.Model;

/// <summary>The complete saved document: a root graph plus canvas/calculation settings.</summary>
public sealed class FactoryDocument
{
    public FactoryGraph Root { get; set; } = new();

    public string Language { get; set; } = "en-US";
    public string Solver { get; set; } = "Basic";

    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }

    public bool UseBuildingGrid { get; set; }
    public string BuildingGridX { get; set; } = "100";
    public string BuildingGridY { get; set; } = "100";
    public bool UseConnectionGrid { get; set; }
    public string ConnectionGridX { get; set; } = "20";
    public string ConnectionGridY { get; set; } = "20";

    /// <summary>Connection rendering style: "Curves", "Direct" or "2D".</summary>
    public string Path { get; set; } = "Curves";

    public string SpaceElevatorMultiplier { get; set; } = "1";
    public string InputMultiplier { get; set; } = "1";
    public string PowerMultiplier { get; set; } = "1";
}
