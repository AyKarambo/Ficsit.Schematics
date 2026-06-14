namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Water Extractor.</summary>
public sealed class WaterExtractorRecipes : RecipeModule
{
    protected override string Machine => "Water Extractor";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(8, "Water", Batch: "60", Tier: "3-1", [Out("Water", 120)]),
    ];
}
