namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Geothermal Generator.</summary>
public sealed class GeothermalGeneratorRecipes : RecipeModule
{
    protected override string Machine => "Geothermal Generator";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(215, "Geothermal Generator", Batch: 0, Tier: "6-1", [], IgnoreInputMultiplier: true),
    ];
}
