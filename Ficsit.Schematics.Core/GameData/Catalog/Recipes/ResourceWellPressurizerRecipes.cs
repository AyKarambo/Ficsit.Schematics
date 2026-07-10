namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Resource Well Pressurizer.</summary>
public sealed class ResourceWellPressurizerRecipes : RecipeModule
{
    protected override string Machine => "Resource Well Pressurizer";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(265, "Resource Well Pressurizer", Batch: 1, Tier: "8-3", []),
    ];
}
