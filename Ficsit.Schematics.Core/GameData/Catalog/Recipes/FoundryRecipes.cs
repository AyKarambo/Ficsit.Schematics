namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Foundry.</summary>
public sealed class FoundryRecipes : RecipeModule
{
    protected override string Machine => "Foundry";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new( 17, "Basic Iron Ingot",        Batch: 12, Tier: "3-3", [In("Iron Ore", 5), In("Limestone", 8), Out("Iron Ingot", 10)], Alternate: true),
        new( 18, "Iron Alloy Ingot",        Batch: 12, Tier: "3-3", [In("Iron Ore", 8), In("Copper Ore", 2), Out("Iron Ingot", 15)], Alternate: true),
        new( 26, "Steel Cast Plate",        Batch: 4,  Tier: "3-3", [In("Iron Ingot", 1), In("Steel Ingot", 1), Out("Iron Plate", 3)], Alternate: true),
        new( 31, "Copper Alloy Ingot",      Batch: 6,  Tier: "3-3", [In("Copper Ore", 5), In("Iron Ore", 5), Out("Copper Ingot", 10)], Alternate: true),
        new( 33, "Tempered Copper Ingot",   Batch: 12, Tier: "5-2", [In("Copper Ore", 5), In("Petroleum Coke", 8), Out("Copper Ingot", 12)], Alternate: true),
        new( 81, "Fused Quartz Crystal",    Batch: 20, Tier: "3-3", [In("Raw Quartz", 25), In("Coal", 12), Out("Quartz Crystal", 18)], Alternate: true),
        new(107, "Steel Ingot",             Batch: 4,  Tier: "3-3", [In("Iron Ore", 3), In("Coal", 3), Out("Steel Ingot", 3)]),
        new(108, "Solid Steel Ingot",       Batch: 3,  Tier: "3-3", [In("Iron Ingot", 2), In("Coal", 2), Out("Steel Ingot", 3)], Alternate: true),
        new(109, "Coke Steel Ingot",        Batch: 12, Tier: "5-2", [In("Iron Ore", 15), In("Petroleum Coke", 15), Out("Steel Ingot", 20)], Alternate: true),
        new(110, "Compacted Steel Ingot",   Batch: 24, Tier: "5-4", [In("Iron Ore", 2), In("Compacted Coal", 1), Out("Steel Ingot", 4)], Alternate: true),
        new(113, "Molded Steel Pipe",       Batch: 6,  Tier: "3-3", [In("Steel Ingot", 5), In("Concrete", 3), Out("Steel Pipe", 5)], Alternate: true),
        new(116, "Molded Beam",             Batch: 12, Tier: "3-3", [In("Steel Ingot", 24), In("Concrete", 16), Out("Steel Beam", 9)], Alternate: true),
        new(193, "Tempered Caterium Ingot", Batch: 8,  Tier: "5-5", [In("Caterium Ore", 6), In("Petroleum Coke", 2), Out("Caterium Ingot", 3)], Alternate: true),
        new(223, "Aluminum Ingot",          Batch: 4,  Tier: "7-1", [In("Aluminum Scrap", 6), In("Silica", 5), Out("Aluminum Ingot", 4)]),
        new(327, "Iron FICSMAS Ornament",   Batch: 12, Tier: "3-3", [In("Blue FICSMAS Ornament", 3), In("Iron Ingot", 3), Out("Iron FICSMAS Ornament", 1)], Ficsmas: true),
        new(328, "Copper FICSMAS Ornament", Batch: 12, Tier: "3-3", [In("Red FICSMAS Ornament", 2), In("Copper Ingot", 2), Out("Copper FICSMAS Ornament", 1)], Ficsmas: true),
    ];
}
