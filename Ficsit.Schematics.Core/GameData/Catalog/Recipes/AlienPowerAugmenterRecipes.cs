namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Alien Power Augmenter.</summary>
public sealed class AlienPowerAugmenterRecipes : RecipeModule
{
    protected override string Machine => "Alien Power Augmenter";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(300, "Alien Power Augmenter", "12", "9-2", [In("Alien Power Matrix", 1)], IgnoreInputMultiplier: true),
    ];
}
