namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Space Elevator.</summary>
public sealed class SpaceElevatorRecipes : RecipeModule
{
    protected override string Machine => "Space Elevator";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new( 95, "Space Elevator Phase 1", Batch: "60", Tier: "2-1", [In("Smart Plating", 50)], SpaceElevatorMultiplier: "True"),
        new(127, "Space Elevator Phase 2", Batch: "60", Tier: "4-3", [In("Smart Plating", 1000), In("Versatile Framework", 1000), In("Automated Wiring", 100)], SpaceElevatorMultiplier: "True"),
        new(214, "Space Elevator Phase 3", Batch: "60", Tier: "6-1", [In("Versatile Framework", 2500), In("Modular Engine", 500), In("Adaptive Control Unit", 100)], SpaceElevatorMultiplier: "True"),
        new(276, "Space Elevator Phase 4", Batch: "60", Tier: "8-5", [In("Assembly Director System", 500), In("Magnetic Field Generator", 500), In("Nuclear Pasta", 100), In("Thermal Propulsion Rocket", 250)], SpaceElevatorMultiplier: "True"),
        new(312, "Space Elevator Phase 5", Batch: "60", Tier: "9-4", [In("Nuclear Pasta", 1000), In("Biochemical Sculptor", 1000), In("AI Expansion Server", 256), In("Ballistic Warp Drive", 200)], SpaceElevatorMultiplier: "True"),
    ];
}
