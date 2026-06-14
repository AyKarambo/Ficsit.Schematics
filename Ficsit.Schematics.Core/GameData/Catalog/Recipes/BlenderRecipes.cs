namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Blender.</summary>
public sealed class BlenderRecipes : RecipeModule
{
    protected override string Machine => "Blender";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(106, "Distilled Silica", "6", "8-5", [In("Dissolved Silica", 12), In("Limestone", 5), In("Water", 10), Out("Silica", 27), Out("Water", 8)], Alternate: true),
        new(148, "Diluted Fuel", "6", "7-5", [In("Heavy Oil Residue", 5), In("Water", 10), Out("Fuel", 10)], Alternate: true),
        new(182, "Rocket Fuel", "6", "8-5", [In("Turbofuel", 6), In("Nitric Acid", 1), Out("Rocket Fuel", 10), Out("Compacted Coal", 1)]),
        new(183, "Nitro Rocket Fuel", "12/5", "8-3", [In("Fuel", 4), In("Nitrogen Gas", 3), In("Sulfur", 4), In("Coal", 2), Out("Rocket Fuel", 6), Out("Compacted Coal", 1)], Alternate: true),
        new(188, "Turbo Blend Fuel", "8", "7-5", [In("Fuel", 2), In("Heavy Oil Residue", 4), In("Sulfur", 3), In("Petroleum Coke", 3), Out("Turbofuel", 6)], Alternate: true),
        new(220, "Instant Scrap", "6", "7-5", [In("Bauxite", 15), In("Coal", 10), In("Sulfuric Acid", 5), In("Water", 6), Out("Aluminum Scrap", 30), Out("Water", 5)], Alternate: true),
        new(222, "Turbo Rifle Ammo", "12", "7-5", [In("Rifle Ammo", 25), In("Aluminum Casing", 3), In("Turbofuel", 3), Out("Turbo Rifle Ammo", 50)]),
        new(234, "Battery", "3", "7-5", [In("Sulfuric Acid", "5/2"), In("Alumina Solution", 2), In("Aluminum Casing", 1), Out("Battery", 1), Out("Water", "3/2")]),
        new(246, "Encased Uranium Cell", "12", "8-2", [In("Uranium", 10), In("Concrete", 3), In("Sulfuric Acid", 8), Out("Encased Uranium Cell", 5), Out("Sulfuric Acid", 2)]),
        new(258, "Cooling System", "10", "8-3", [In("Heat Sink", 2), In("Rubber", 2), In("Water", 5), In("Nitrogen Gas", 25), Out("Cooling System", 1)]),
        new(259, "Cooling Device", "24", "8-3", [In("Heat Sink", 4), In("Motor", 1), In("Nitrogen Gas", 24), Out("Cooling System", 2)], Alternate: true),
        new(260, "Fused Modular Frame", "40", "8-3", [In("Heavy Modular Frame", 1), In("Aluminum Casing", 50), In("Nitrogen Gas", 25), Out("Fused Modular Frame", 1)]),
        new(261, "Heat-Fused Frame", "20", "8-5", [In("Heavy Modular Frame", 1), In("Aluminum Ingot", 50), In("Nitric Acid", 8), In("Fuel", 10), Out("Fused Modular Frame", 1)], Alternate: true),
        new(280, "Nitric Acid", "6", "8-5", [In("Nitrogen Gas", 12), In("Water", 3), In("Iron Plate", 1), Out("Nitric Acid", 3)]),
        new(282, "Non-Fissile Uranium", "24", "8-5", [In("Uranium Waste", 15), In("Silica", 10), In("Nitric Acid", 6), In("Sulfuric Acid", 6), Out("Non-Fissile Uranium", 20), Out("Water", 6)]),
        new(283, "Fertile Uranium", "12", "8-5", [In("Uranium", 5), In("Uranium Waste", 5), In("Nitric Acid", 3), In("Sulfuric Acid", 5), Out("Non-Fissile Uranium", 20), Out("Water", 8)], Alternate: true),
        new(299, "Biochemical Sculptor", "120", "9-1", [In("Assembly Director System", 1), In("Ficsite Trigon", 80), In("Water", 20), Out("Biochemical Sculptor", 4)]),
    ];
}
