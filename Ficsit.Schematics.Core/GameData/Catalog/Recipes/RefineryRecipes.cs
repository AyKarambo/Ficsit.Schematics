namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Refinery.</summary>
public sealed class RefineryRecipes : RecipeModule
{
    protected override string Machine => "Refinery";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new( 19, "Pure Iron Ingot",          Batch: "12", Tier: "5-2", [In("Iron Ore", 7), In("Water", 4), Out("Iron Ingot", 13)], Alternate: true),
        new( 20, "Leached Iron ingot",       Batch: "6",  Tier: "7-5", [In("Iron Ore", 5), In("Sulfuric Acid", 1), Out("Iron Ingot", 10)], Alternate: true),
        new( 32, "Pure Copper Ingot",        Batch: "24", Tier: "5-2", [In("Copper Ore", 6), In("Water", 4), Out("Copper Ingot", 15)], Alternate: true),
        new( 34, "Leached Copper Ingot",     Batch: "12", Tier: "7-5", [In("Copper Ore", 9), In("Sulfuric Acid", 5), Out("Copper Ingot", 22)], Alternate: true),
        new( 40, "Coated Cable",             Batch: "8",  Tier: "5-2", [In("Wire", 5), In("Heavy Oil Residue", 2), Out("Cable", 9)], Alternate: true),
        new( 55, "Wet Concrete",             Batch: "3",  Tier: "5-2", [In("Limestone", 6), In("Water", 5), Out("Concrete", 4)], Alternate: true),
        new( 82, "Pure Quartz Crystal",      Batch: "8",  Tier: "5-2", [In("Raw Quartz", 9), In("Water", 5), Out("Quartz Crystal", 7)], Alternate: true),
        new( 84, "Steamed Copper Sheet",     Batch: "8",  Tier: "5-2", [In("Copper Ingot", 3), In("Water", 3), Out("Copper Sheet", 3)], Alternate: true),
        new( 86, "Polyester Fabric",         Batch: "2",  Tier: "5-2", [In("Polymer Resin", 1), In("Water", 1), Out("Fabric", 1)], Alternate: true),
        new(132, "Petroleum Coke",           Batch: "6",  Tier: "5-2", [In("Heavy Oil Residue", 4), Out("Petroleum Coke", 12)]),
        new(135, "Smokeless Powder",         Batch: "6",  Tier: "5-2", [In("Black Powder", 2), In("Heavy Oil Residue", 1), Out("Smokeless Powder", 2)]),
        new(136, "Residual Rubber",          Batch: "6",  Tier: "5-2", [In("Polymer Resin", 4), In("Water", 4), Out("Rubber", 2)]),
        new(137, "Recycled Rubber",          Batch: "12", Tier: "5-2", [In("Plastic", 6), In("Fuel", 6), Out("Rubber", 12)], Alternate: true),
        new(138, "Residual Plastic",         Batch: "6",  Tier: "5-2", [In("Polymer Resin", 6), In("Water", 2), Out("Plastic", 2)]),
        new(139, "Recycled Plastic",         Batch: "12", Tier: "5-2", [In("Rubber", 6), In("Fuel", 6), Out("Plastic", 12)], Alternate: true),
        new(145, "Fuel",                     Batch: "6",  Tier: "5-2", [In("Crude Oil", 6), Out("Fuel", 4), Out("Polymer Resin", 3)]),
        new(146, "Residual Fuel",            Batch: "6",  Tier: "5-2", [In("Heavy Oil Residue", 6), Out("Fuel", 4)]),
        new(149, "Plastic",                  Batch: "6",  Tier: "5-2", [In("Crude Oil", 3), Out("Plastic", 2), Out("Heavy Oil Residue", 1)]),
        new(150, "Rubber",                   Batch: "6",  Tier: "5-2", [In("Crude Oil", 3), Out("Rubber", 2), Out("Heavy Oil Residue", 2)]),
        new(151, "Heavy Oil Residue",        Batch: "6",  Tier: "5-2", [In("Crude Oil", 3), Out("Heavy Oil Residue", 4), Out("Polymer Resin", 2)], Alternate: true),
        new(152, "Polymer Resin",            Batch: "6",  Tier: "5-2", [In("Crude Oil", 6), Out("Polymer Resin", 13), Out("Heavy Oil Residue", 2)], Alternate: true),
        new(167, "Diluted Packaged Fuel",    Batch: "2",  Tier: "5-4", [In("Heavy Oil Residue", 1), In("Packaged Water", 2), Out("Packaged Fuel", 2)], Alternate: true, IgnoreInputMultiplier: true),
        new(173, "Ionized Fuel",             Batch: "24", Tier: "5-4", [In("Rocket Fuel", 16), In("Power Shard", 1), Out("Ionized Fuel", 16), Out("Compacted Coal", 2)]),
        new(177, "Liquid Biofuel",           Batch: "4",  Tier: "5-4", [In("Solid Biofuel", 6), In("Water", 3), Out("Liquid Biofuel", 4)]),
        new(184, "Turbofuel",                Batch: "16", Tier: "5-4", [In("Fuel", 6), In("Compacted Coal", 4), Out("Turbofuel", 5)]),
        new(187, "Turbo Heavy Fuel",         Batch: "8",  Tier: "5-4", [In("Heavy Oil Residue", 5), In("Compacted Coal", 4), Out("Turbofuel", 4)], Alternate: true),
        new(192, "Pure Caterium Ingot",      Batch: "5",  Tier: "5-5", [In("Caterium Ore", 2), In("Water", 2), Out("Caterium Ingot", 1)], Alternate: true),
        new(194, "Leached Caterium Ingot",   Batch: "10", Tier: "7-5", [In("Caterium Ore", 9), In("Sulfuric Acid", 5), Out("Caterium Ingot", 6)], Alternate: true),
        new(218, "Aluminum Scrap",           Batch: "1",  Tier: "7-1", [In("Alumina Solution", 4), In("Coal", 2), Out("Aluminum Scrap", 6), Out("Water", 2)]),
        new(219, "Electrode Aluminum Scrap", Batch: "4",  Tier: "7-1", [In("Alumina Solution", 12), In("Petroleum Coke", 4), Out("Aluminum Scrap", 20), Out("Water", 7)], Alternate: true),
        new(229, "Alumina Solution",         Batch: "6",  Tier: "7-1", [In("Bauxite", 12), In("Water", 18), Out("Alumina Solution", 12), Out("Silica", 5)]),
        new(231, "Sloppy Alumina",           Batch: "3",  Tier: "7-1", [In("Bauxite", 10), In("Water", 10), Out("Alumina Solution", 12)], Alternate: true),
        new(243, "Sulfuric Acid",            Batch: "6",  Tier: "7-5", [In("Sulfur", 5), In("Water", 5), Out("Sulfuric Acid", 5)]),
        new(277, "Quartz Purification",      Batch: "12", Tier: "8-5", [In("Raw Quartz", 24), In("Nitric Acid", 2), Out("Quartz Crystal", 15), Out("Dissolved Silica", 12)], Alternate: true),
    ];
}
