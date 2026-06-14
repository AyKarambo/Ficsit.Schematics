namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Resource Well Extractor.</summary>
public sealed class ResourceWellExtractorRecipes : RecipeModule
{
    protected override string Machine => "Resource Well Extractor";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(9, "Well Water", "1", "8-3", [Out("Water", 1)]),
        new(11, "Oil Well", "1", "8-3", [Out("Crude Oil", 1)]),
        new(14, "Nitrogen Gas", "1", "8-3", [Out("Nitrogen Gas", 1)]),
    ];
}
