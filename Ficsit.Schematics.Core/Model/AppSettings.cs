namespace Ficsit.Schematics.Core.Model;

/// <summary>
/// Application settings, mirroring the reference app's settings.json so behavior
/// and defaults match. Persisted as a single document.
/// </summary>
public sealed class AppSettings
{
    public int Id { get; set; } = 1;

    public double WindowX { get; set; }
    public double WindowY { get; set; }
    public double WindowWidth { get; set; } = 1400;
    public double WindowHeight { get; set; } = 900;

    public int UiScale { get; set; } = 14;
    public bool DarkMode { get; set; } = true;

    public string LanguageId { get; set; } = "en-US";
    public string LanguageName { get; set; } = "English (US)";

    public bool UseBuildingGrid { get; set; }
    public string BuildingGridX { get; set; } = "100";
    public string BuildingGridY { get; set; } = "100";
    public bool UseConnectionGrid { get; set; }
    public string ConnectionGridX { get; set; } = "20";
    public string ConnectionGridY { get; set; } = "20";

    /// <summary>Connection style: Curves | Direct | 2D.</summary>
    public string Path { get; set; } = "Curves";

    public int DragSensitivity { get; set; } = 25;

    public bool Autosave { get; set; } = true;
    public int AutosaveIntervalMinutes { get; set; } = 5;
    public int MaxBackups { get; set; } = 100;

    public bool FlagInvalidValues { get; set; } = true;
    public bool FlagNonMatchingValues { get; set; }

    public Dictionary<string, NumberFormatSetting> Numbers { get; set; } = DefaultNumbers();

    public List<MachineDefaultSetting> MachineDefaults { get; set; } = [];

    public static Dictionary<string, NumberFormatSetting> DefaultNumbers() => new()
    {
        ["value"] = new() { DisplayType = "Decimal", DecimalPlaces = 2 },
        ["valueToolTip"] = new() { DisplayType = "Fraction" },
        ["maxToolTip"] = new() { DisplayType = "Decimal", DecimalPlaces = 2 },
        ["clockSpeedToolTip"] = new() { DisplayType = "Decimal", DecimalPlaces = 4, RoundingType = "Up" },
        ["connection"] = new() { DisplayType = "Decimal", DecimalPlaces = 2 },
        ["connectionToolTip"] = new() { DisplayType = "Fraction" },
        ["summaryPanel"] = new() { DisplayType = "Decimal", DecimalPlaces = 2 },
        ["summaryPanelTooltip"] = new() { DisplayType = "Fraction" },
    };
}
