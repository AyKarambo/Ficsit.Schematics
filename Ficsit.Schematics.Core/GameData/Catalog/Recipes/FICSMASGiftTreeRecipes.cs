namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the FICSMAS Gift Tree.</summary>
public sealed class FICSMASGiftTreeRecipes : RecipeModule
{
    protected override string Machine => "FICSMAS Gift Tree";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(316, "FICSMAS Gift", Batch: 4, Tier: "0-3", [Out("FICSMAS Gift", 1)], Ficsmas: true),
    ];
}
