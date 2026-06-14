namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Constructor.</summary>
public sealed class ConstructorRecipes : RecipeModule
{
    protected override string Machine => "Constructor";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(22, "Iron Rod", "4", "0-0", [In("Iron Ingot", 1), Out("Iron Rod", 1)]),
        new(23, "Steel Rod", "5", "3-3", [In("Steel Ingot", 1), Out("Iron Rod", 4)], Alternate: true),
        new(24, "Aluminum Rod", "8", "7-1", [In("Aluminum Ingot", 1), Out("Iron Rod", 7)], Alternate: true),
        new(25, "Iron Plate", "6", "0-0", [In("Iron Ingot", 3), Out("Iron Plate", 2)]),
        new(35, "Wire", "4", "0-2", [In("Copper Ingot", 1), Out("Wire", 2)]),
        new(36, "Iron Wire", "24", "0-3", [In("Iron Ingot", 5), Out("Wire", 9)], Alternate: true),
        new(37, "Caterium Wire", "4", "5-5", [In("Caterium Ingot", 1), Out("Wire", 8)], Alternate: true),
        new(39, "Cable", "2", "0-2", [In("Wire", 2), Out("Cable", 1)]),
        new(43, "Screw", "6", "0-3", [In("Iron Rod", 1), Out("Screw", 4)]),
        new(44, "Cast Screw", "24", "0-3", [In("Iron Ingot", 5), Out("Screw", 20)], Alternate: true),
        new(45, "Steel Screw", "12", "3-3", [In("Steel Beam", 1), Out("Screw", 52)], Alternate: true),
        new(46, "Iron Rebar", "4", "0-3", [In("Iron Rod", 1), Out("Iron Rebar", 1)]),
        new(47, "Biomass (Alien Protein)", "4", "0-3", [In("Alien Protein", 1), Out("Biomass", 100)]),
        new(48, "Biomass (Mycelia)", "4", "0-3", [In("Mycelia", 1), Out("Biomass", 10)]),
        new(49, "Biomass (Leaves)", "5", "0-6", [In("Leaves", 10), Out("Biomass", 5)]),
        new(50, "Biomass (Wood)", "4", "0-6", [In("Wood", 4), Out("Biomass", 20)]),
        new(52, "Concrete", "4", "0-3", [In("Limestone", 3), Out("Concrete", 1)]),
        new(60, "Alien DNA Capsule", "6", "0-3", [In("Alien Protein", 1), Out("Alien DNA Capsule", 1)]),
        new(61, "Hatcher Protein", "3", "0-3", [In("Hatcher Remains", 1), Out("Alien Protein", 1)]),
        new(62, "Hog Protein", "3", "0-3", [In("Hog Remains", 1), Out("Alien Protein", 1)]),
        new(63, "Spitter Protein", "3", "0-3", [In("Spitter Remains", 1), Out("Alien Protein", 1)]),
        new(64, "Stinger Protein", "3", "0-3", [In("Stinger Remains", 1), Out("Alien Protein", 1)]),
        new(65, "Power Shard (1)", "8", "0-3", [In("Blue Power Slug", 1), Out("Power Shard", 1)]),
        new(66, "Power Shard (2)", "12", "0-3", [In("Yellow Power Slug", 1), Out("Power Shard", 2)]),
        new(67, "Power Shard (5)", "24", "0-3", [In("Purple Power Slug", 1), Out("Power Shard", 5)]),
        new(78, "Silica", "8", "0-5", [In("Raw Quartz", 3), Out("Silica", 5)]),
        new(80, "Quartz Crystal", "8", "0-5", [In("Raw Quartz", 5), Out("Quartz Crystal", 3)]),
        new(83, "Copper Sheet", "6", "2-1", [In("Copper Ingot", 2), Out("Copper Sheet", 1)]),
        new(97, "Solid Biofuel", "4", "2-2", [In("Biomass", 8), Out("Solid Biofuel", 4)]),
        new(101, "Biocoal", "8", "3-1", [In("Biomass", 5), Out("Coal", 6)], Alternate: true),
        new(102, "Charcoal", "4", "3-1", [In("Wood", 1), Out("Coal", 10)], Alternate: true),
        new(111, "Steel Pipe", "6", "3-3", [In("Steel Ingot", 3), Out("Steel Pipe", 2)]),
        new(112, "Iron Pipe", "12", "3-3", [In("Iron Ingot", 20), Out("Steel Pipe", 5)], Alternate: true),
        new(115, "Steel Beam", "4", "3-3", [In("Steel Ingot", 4), Out("Steel Beam", 1)]),
        new(117, "Aluminum Beam", "8", "7-1", [In("Aluminum Ingot", 3), Out("Steel Beam", 3)], Alternate: true),
        new(155, "Empty Canister", "4", "5-4", [In("Plastic", 2), Out("Empty Canister", 4)]),
        new(161, "Steel Canister", "6", "5-4", [In("Steel Ingot", 4), Out("Empty Canister", 4)], Alternate: true),
        new(163, "Empty Fluid Tank", "1", "8-3", [In("Aluminum Ingot", 1), Out("Empty Fluid Tank", 1)]),
        new(189, "Quickwire", "5", "5-5", [In("Caterium Ingot", 1), Out("Quickwire", 5)]),
        new(227, "Aluminum Casing", "2", "7-1", [In("Aluminum Ingot", 3), Out("Aluminum Casing", 2)]),
        new(270, "Copper Powder", "6", "8-5", [In("Copper Ingot", 30), Out("Copper Powder", 5)]),
        new(286, "Reanimated SAM", "2", "9-1", [In("SAM", 4), Out("Reanimated SAM", 1)]),
        new(294, "Ficsite Trigon", "6", "9-1", [In("Ficsite Ingot", 1), Out("Ficsite Trigon", 3)]),
        new(318, "FICSMAS Tree Branch", "6", "0-3", [In("FICSMAS Gift", 1), Out("FICSMAS Tree Branch", 1)], Ficsmas: true),
        new(320, "FICSMAS Bow", "12", "0-3", [In("FICSMAS Gift", 2), Out("FICSMAS Bow", 1)], Ficsmas: true),
        new(321, "FICSMAS Actual Snow", "12", "0-3", [In("FICSMAS Gift", 5), Out("FICSMAS Actual Snow", 2)], Ficsmas: true),
        new(322, "Candy Cane", "12", "0-3", [In("FICSMAS Gift", 3), Out("Candy Cane", 1)], Ficsmas: true),
        new(323, "Snowball", "12", "0-3", [In("FICSMAS Actual Snow", 3), Out("Snowball", 1)], Ficsmas: true),
    ];
}
