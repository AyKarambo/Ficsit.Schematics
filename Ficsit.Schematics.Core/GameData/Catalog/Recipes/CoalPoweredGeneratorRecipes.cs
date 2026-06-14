namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Coal-Powered Generator.</summary>
public sealed class CoalPoweredGeneratorRecipes : RecipeModule
{
    protected override string Machine => "Coal-Powered Generator";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(105, "Coal Generator", "4", "3-1", [In("Coal", 1), In("Water", 3)], IgnoreInputMultiplier: true),
        new(133, "Petroleum Coke Generator", "12/5", "5-2", [In("Petroleum Coke", 1), In("Water", "9/5")], IgnoreInputMultiplier: true),
        new(153, "Compacted Coal Generator", "42/5", "5-4", [In("Compacted Coal", 1), In("Water", "63/10")], IgnoreInputMultiplier: true),
    ];
}
