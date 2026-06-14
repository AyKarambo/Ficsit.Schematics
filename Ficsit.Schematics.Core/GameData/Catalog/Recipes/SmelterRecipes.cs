namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Smelter.</summary>
public sealed class SmelterRecipes : RecipeModule
{
    protected override string Machine => "Smelter";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(16, "Iron Ingot", "2", "0-0", [In("Iron Ore", 1), Out("Iron Ingot", 1)]),
        new(30, "Copper Ingot", "2", "0-2", [In("Copper Ore", 1), Out("Copper Ingot", 1)]),
        new(191, "Caterium Ingot", "4", "5-5", [In("Caterium Ore", 3), Out("Caterium Ingot", 1)]),
        new(224, "Pure Aluminum Ingot", "2", "7-1", [In("Aluminum Scrap", 2), Out("Aluminum Ingot", 1)], Alternate: true),
        new(317, "Blue FICSMAS Ornament", "12", "0-3", [In("FICSMAS Gift", 1), Out("Blue FICSMAS Ornament", 2)], Ficsmas: true),
        new(319, "Red FICSMAS Ornament", "12", "0-3", [In("FICSMAS Gift", 1), Out("Red FICSMAS Ornament", 1)], Ficsmas: true),
    ];
}
