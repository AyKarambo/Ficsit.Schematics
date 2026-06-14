namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Nuclear Power Plant.</summary>
public sealed class NuclearPowerPlantRecipes : RecipeModule
{
    protected override string Machine => "Nuclear Power Plant";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(254, "Uranium Nuclear Power Plant", "300", "8-2", [In("Uranium Fuel Rod", 1), In("Water", 1200), Out("Uranium Waste", 50)], IgnoreInputMultiplier: true),
        new(285, "Plutonium Nuclear Power Plant", "600", "8-5", [In("Plutonium Fuel Rod", 1), In("Water", 2400), Out("Plutonium Waste", 10)], IgnoreInputMultiplier: true),
        new(315, "Ficsonium Nuclear Power Plant", "60", "9-5", [In("Ficsonium Fuel Rod", 1), In("Water", 240)], IgnoreInputMultiplier: true),
    ];
}
