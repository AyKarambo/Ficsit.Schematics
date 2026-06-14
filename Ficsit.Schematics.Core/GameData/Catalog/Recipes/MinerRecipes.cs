namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Miner.</summary>
public sealed class MinerRecipes : RecipeModule
{
    protected override string Machine => "Miner";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new( 0, "Iron Ore",     Batch: "60", Tier: "0-5", [Out("Iron Ore", 1)]),
        new( 1, "Limestone",    Batch: "60", Tier: "0-5", [Out("Limestone", 1)]),
        new( 2, "Copper Ore",   Batch: "60", Tier: "0-5", [Out("Copper Ore", 1)]),
        new( 3, "Caterium Ore", Batch: "60", Tier: "0-5", [Out("Caterium Ore", 1)]),
        new( 4, "Sulfur",       Batch: "60", Tier: "0-5", [Out("Sulfur", 1)]),
        new( 5, "Raw Quartz",   Batch: "60", Tier: "0-5", [Out("Raw Quartz", 1)]),
        new( 6, "SAM",          Batch: "60", Tier: "0-5", [Out("SAM", 1)]),
        new( 7, "Coal",         Batch: "60", Tier: "3-1", [Out("Coal", 1)]),
        new(12, "Bauxite",      Batch: "60", Tier: "7-1", [Out("Bauxite", 1)]),
        new(13, "Uranium",      Batch: "60", Tier: "8-2", [Out("Uranium", 1)]),
    ];
}
