namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Fuel-Powered Generator.</summary>
public sealed class FuelPoweredGeneratorRecipes : RecipeModule
{
    protected override string Machine => "Fuel-Powered Generator";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(147, "Fuel Generator", "3", "5-5", [In("Fuel", 1)], IgnoreInputMultiplier: true),
        new(175, "Ionized Fuel Generator", "20", "5-5", [In("Ionized Fuel", 1)], IgnoreInputMultiplier: true),
        new(179, "Liquid Biofuel Generator", "3", "5-5", [In("Liquid Biofuel", 1)], IgnoreInputMultiplier: true),
        new(181, "Rocket Fuel Generator", "72/5", "5-5", [In("Rocket Fuel", 1)], IgnoreInputMultiplier: true),
        new(186, "Turbofuel Generator", "8", "5-5", [In("Turbofuel", 1)], IgnoreInputMultiplier: true),
    ];
}
