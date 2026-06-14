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

    /// <summary>Colour each wire by the part it carries, so belts are traceable. Default on.</summary>
    public bool WireColorByPart { get; set; } = true;

    /// <summary>Hovering/selecting one machine fades everything but its connections. Default on.</summary>
    public bool FocusHighlight { get; set; } = true;

    /// <summary>Render the world map (with imported resource nodes) behind the canvas.</summary>
    public bool ShowMap { get; set; }

    public int DragSensitivity { get; set; } = 25;

    public bool Autosave { get; set; } = true;
    public int AutosaveIntervalMinutes { get; set; } = 5;
    public int MaxBackups { get; set; } = 100;

    public bool FlagInvalidValues { get; set; } = true;
    public bool FlagNonMatchingValues { get; set; }

    public Dictionary<string, NumberFormatSetting> Numbers { get; set; } = DefaultNumbers();

    public List<MachineDefaultSetting> MachineDefaults { get; set; } = [];

    /// <summary>
    /// Auto-planner: exclude manually gathered parts (Leaves, Wood, alien
    /// remains, …) as raw inputs. Default on — plans should not invent free
    /// hand-collected resources.
    /// </summary>
    public bool PlannerExcludeManualParts { get; set; } = true;

    /// <summary>
    /// Auto-planner: allow Converter ore-from-SAM conversion recipes. Default off
    /// — they otherwise dominate "efficient" plans.
    /// </summary>
    public bool PlannerAllowOreConversion { get; set; }

    /// <summary>
    /// Auto-planner: recipes (by name) the user has disabled. The *disabled* set
    /// is stored so recipes added by future data updates default to enabled.
    /// </summary>
    public List<string> PlannerDisabledRecipes { get; set; } = [];

    /// <summary>
    /// Auto-planner: the user's resource-preference budget — a slider position per
    /// extractable raw (see <see cref="Planning.ScarcityWeights.WeightedResources"/>).
    /// Empty means neutral: every raw equal, i.e. the built-in scarcity defaults
    /// untouched. Only the relative split matters, so the values need not sum to any
    /// particular total. This is the global default the Auto-Plan panel restores to.
    /// </summary>
    public Dictionary<string, int> PlannerResourcePreferences { get; set; } = [];

    /// <summary>
    /// Auto-planner: only use recipes unlocked up to this progression tier (phase),
    /// so plans don't reach for machines the player hasn't built. 99 = no cap (all
    /// tiers). The Auto-Plan picker writes this; it maps onto DisabledRecipes via
    /// <see cref="Planning.FactoryPlanner.RecipesAboveTier"/>.
    /// </summary>
    public int PlannerMaxTierPhase { get; set; } = 99;

    /// <summary>
    /// Auto-planner: skip the draft review and build straight onto the canvas when a
    /// plan completes. Default off — plans are reviewed before they touch the canvas.
    /// </summary>
    public bool PlannerAutoApply { get; set; }

    /// <summary>
    /// Auto-planner: collapse each key-intermediate sub-chain into a named outpost when
    /// a plan is applied, so a dense plan reads as a handful of blocks. Default on.
    /// </summary>
    public bool PlannerAutoCollapse { get; set; } = true;

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
