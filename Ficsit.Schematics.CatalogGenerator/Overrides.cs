using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.CatalogGenerator;

/// <summary>
/// Hand-authored modeling the Docs export cannot express. These tables are *inputs* to the
/// generator — reviewed by humans, and byte-stable across regenerations by construction.
/// Every entry carries the reason it exists.
/// </summary>
public static class Overrides
{
    /// <summary>
    /// Parts a pioneer gathers by hand (shown as manual inputs by the app). The export has
    /// no such flag: it mixes pick-ups, drops and craftables in the same descriptor tables.
    /// </summary>
    public static readonly IReadOnlySet<string> ManuallyGathered = new HashSet<string>(StringComparer.Ordinal)
    {
        "Leaves", "Mycelia", "Wood",
        "Blue Power Slug", "Purple Power Slug", "Yellow Power Slug",
        "Hatcher Remains", "Hog Remains", "Spitter Remains", "Stinger Remains",
        "Mercer Sphere", "Somersloop",
        "FICSMAS Gift", "FICSMAS Tree Branch", "FICSMAS Bow", "FICSMAS Actual Snow",
        "FICSMAS Ornament Bundle", "FICSMAS Wreath", "FICSMAS Wonder Star",
    };

    /// <summary>
    /// Tiers for parts no automated recipe produces (world pick-ups, creature drops,
    /// workshop-only items): the tier fixpoint has nothing to anchor them on.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Tier> PartTiers = new Dictionary<string, Tier>(StringComparer.Ordinal)
    {
        // Available from the start of the game.
        ["Leaves"] = "0-0", ["Mycelia"] = "0-0", ["Wood"] = "0-0",
        ["Blue Power Slug"] = "0-0", ["Purple Power Slug"] = "0-0", ["Yellow Power Slug"] = "0-0",
        ["Hatcher Remains"] = "0-0", ["Hog Remains"] = "0-0",
        ["Spitter Remains"] = "0-0", ["Stinger Remains"] = "0-0",
        ["Mercer Sphere"] = "0-0", ["Somersloop"] = "0-0",
        // FICSMAS world drops follow the event's anchor tier (the Gift Tree's).
        ["FICSMAS Tree Branch"] = "0-3", ["FICSMAS Bow"] = "0-3", ["FICSMAS Actual Snow"] = "0-3",
        // Workshop-only craftable, used as a Miner build cost.
        ["Portable Miner"] = "3-3",
    };

    /// <summary>
    /// Machine tiers the export cannot resolve (event or MAM unlocks carry no milestone).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Tier> MachineTiers = new Dictionary<string, Tier>(StringComparer.Ordinal)
    {
        // FICSMAS event unlock; the app anchors all FICSMAS content at 0-3.
        ["FICSMAS Gift Tree"] = "0-3",
        // MAM Sloop research unlock (no milestone in the export).
        ["Dimensional Depot Uploader"] = "9-1",
        // MAM Alien Technology research unlock (no milestone in the export).
        ["Alien Power Augmenter"] = "9-1",
        // MAM Caterium research (tech tier 3) since 1.1 — was milestone 6-1 before.
        ["Geothermal Generator"] = "3-1",
    };

    /// <summary>
    /// Resources whose extraction is gated by a milestone beyond the extractor itself.
    /// Scanner-unlock tiers in the export are unusable for this: the game re-adds ores
    /// to the scanner at arbitrary later milestones.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Tier> OreGates = new Dictionary<string, Tier>(StringComparer.Ordinal)
    {
        ["Coal"] = "3-1",     // Coal Power
        ["Bauxite"] = "7-1",  // Bauxite Refinement
        ["Uranium"] = "8-2",  // Nuclear Power
    };

    /// <summary>
    /// Recipes exempt from the global input-cost multiplier beyond the Packager's own
    /// (cost-neutral repackaging chains the reference data flags explicitly).
    /// </summary>
    public static readonly IReadOnlySet<string> IgnoreInputRecipes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Diluted Packaged Fuel",
    };

    /// <summary>
    /// App-side recipe display names where the export reuses one name for two recipes.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RecipeNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Both variants are called "Turbo Rifle Ammo"; the Manufacturer one uses packaged fuel.
        ["Recipe_CartridgeChaos_Packaged_C"] = "Packaged Turbo Rifle Ammo",
    };

    /// <summary>
    /// The Alien Power Augmenter's fueled boost (+30 % of grid power): the export only
    /// carries the passive base boost (mBaseBoostPercentage), not the fueled one.
    /// </summary>
    public static readonly Rational AugmenterFueledBoost = Rational.Parse("3/10");

    /// <summary>
    /// Curated recipe rows for machines whose behavior the export does not encode as recipes:
    /// placeholders for purity/grid-driven machines, the Alien Power Augmenter's fuel, the
    /// FICSMAS Gift Tree drop, and the Space Elevator phase goals (FGGamePhase data is not
    /// part of the Docs export). Tier defaults to the machine tier via the normal fixpoint
    /// when null.
    /// </summary>
    public static IReadOnlyList<RecipeRow> CuratedRecipes(string machine) => machine switch
    {
        "Geothermal Generator" =>
        [
            new(machine, "Geothermal Generator", Batch: 0, Tier: "3-1", [], IgnoreInputMultiplier: true),
        ],
        "Resource Well Pressurizer" =>
        [
            new(machine, "Resource Well Pressurizer", Batch: 1, Tier: "8-3", []),
        ],
        "Alien Power Augmenter" =>
        [
            // 1 Alien Power Matrix per 12 s sustains the fueled +30 % boost.
            new(machine, "Alien Power Augmenter", Batch: 12, Tier: "9-2",
                [new("Alien Power Matrix", -1)], IgnoreInputMultiplier: true),
        ],
        "FICSMAS Gift Tree" =>
        [
            // Batch time mirrors the export's mTimeToProduceItem (4 s).
            new(machine, "FICSMAS Gift", Batch: 4, Tier: "0-3",
                [new("FICSMAS Gift", 1)], Ficsmas: true),
        ],
        "Space Elevator" =>
        [
            new(machine, "Space Elevator Phase 1", Batch: 60, Tier: "2-1",
                [new("Smart Plating", -50)], SpaceElevatorMultiplier: "True"),
            new(machine, "Space Elevator Phase 2", Batch: 60, Tier: "4-3",
                [new("Smart Plating", -1000), new("Versatile Framework", -1000), new("Automated Wiring", -100)], SpaceElevatorMultiplier: "True"),
            new(machine, "Space Elevator Phase 3", Batch: 60, Tier: "6-1",
                [new("Versatile Framework", -2500), new("Modular Engine", -500), new("Adaptive Control Unit", -100)], SpaceElevatorMultiplier: "True"),
            new(machine, "Space Elevator Phase 4", Batch: 60, Tier: "8-5",
                [new("Assembly Director System", -500), new("Magnetic Field Generator", -500), new("Nuclear Pasta", -100), new("Thermal Propulsion Rocket", -250)], SpaceElevatorMultiplier: "True"),
            new(machine, "Space Elevator Phase 5", Batch: 60, Tier: "9-4",
                [new("Nuclear Pasta", -1000), new("Biochemical Sculptor", -1000), new("AI Expansion Server", -256), new("Ballistic Warp Drive", -200)], SpaceElevatorMultiplier: "True"),
        ],
        _ => [],
    };

    /// <summary>
    /// Old catalog name → official export name, for anything the app renames when adopting
    /// official names. Populated from the generator's verify report; consumed by the
    /// NameAliases emitter so legacy .sfmd saves keep loading.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // The game renamed the part (and its recipes) from singular to plural.
        ["Screw"] = "Screws",
        ["Cast Screw"] = "Cast Screws",
        ["Steel Screw"] = "Steel Screws",
        // The export fixed the capitalization of this alternate's name.
        ["Leached Iron ingot"] = "Leached Iron Ingot",
    };
}
