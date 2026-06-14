namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Resource Well Extractor.</summary>
public sealed class ResourceWellExtractorRecipes : RecipeModule
{
    protected override string Machine => "Resource Well Extractor";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new( 9, "Well Water",   Batch: 1, Tier: "8-3", [Out("Water", 1)]),
        new(11, "Oil Well",     Batch: 1, Tier: "8-3", [Out("Crude Oil", 1)]),
        new(14, "Nitrogen Gas", Batch: 1, Tier: "8-3", [Out("Nitrogen Gas", 1)]),
    ];
}
