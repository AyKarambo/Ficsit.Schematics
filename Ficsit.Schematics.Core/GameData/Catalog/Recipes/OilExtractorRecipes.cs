namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Oil Extractor.</summary>
public sealed class OilExtractorRecipes : RecipeModule
{
    protected override string Machine => "Oil Extractor";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(10, "Crude Oil", Batch: "1", Tier: "5-2", [Out("Crude Oil", 2)]),
    ];
}
