namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Biomass Burner.</summary>
public sealed class BiomassBurnerRecipes : RecipeModule
{
    protected override string Machine => "Biomass Burner";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(21, "Leaves Biomass Burner", "1/2", "0-6", [In("Leaves", 1)], IgnoreInputMultiplier: true),
        new(28, "Mycelia Biomass Burner", "2/3", "0-6", [In("Mycelia", 1)], IgnoreInputMultiplier: true),
        new(29, "Wood Biomass Burner", "10/3", "0-6", [In("Wood", 1)], IgnoreInputMultiplier: true),
        new(51, "Biomass Burner", "6", "0-6", [In("Biomass", 1)], IgnoreInputMultiplier: true),
        new(98, "Solid Biofuel Biomass Burner", "15", "2-2", [In("Solid Biofuel", 1)], IgnoreInputMultiplier: true),
        new(169, "Packaged Liquid Biofuel Biomass Burner", "25", "5-4", [In("Packaged Liquid Biofuel", 1)], IgnoreInputMultiplier: true),
    ];
}
