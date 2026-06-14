namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Manufacturer.</summary>
public sealed class ManufacturerRecipes : RecipeModule
{
    protected override string Machine => "Manufacturer";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(96, "Plastic Smart Plating", "24", "6-1", [In("Reinforced Iron Plate", 1), In("Rotor", 1), In("Plastic", 3), Out("Smart Plating", 2)], Alternate: true),
        new(121, "Flexible Framework", "16", "6-1", [In("Modular Frame", 1), In("Steel Beam", 6), In("Rubber", 8), Out("Versatile Framework", 2)], Alternate: true),
        new(128, "Automated Speed Wiring", "32", "6-1", [In("Stator", 2), In("Wire", 40), In("High-Speed Connector", 1), Out("Automated Wiring", 4)], Alternate: true),
        new(130, "Rigor Motor", "48", "6-1", [In("Rotor", 3), In("Stator", 3), In("Crystal Oscillator", 1), Out("Motor", 6)], Alternate: true),
        new(198, "Explosive Rebar", "12", "6-1", [In("Iron Rebar", 2), In("Smokeless Powder", 2), In("Steel Pipe", 2), Out("Explosive Rebar", 1)]),
        new(199, "Gas Filter", "8", "6-1", [In("Fabric", 2), In("Coal", 4), In("Iron Plate", 2), Out("Gas Filter", 1)]),
        new(202, "Crystal Oscillator", "120", "6-1", [In("Quartz Crystal", 36), In("Cable", 28), In("Reinforced Iron Plate", 5), Out("Crystal Oscillator", 2)]),
        new(203, "Insulated Crystal Oscillator", "32", "6-1", [In("Quartz Crystal", 10), In("Rubber", 7), In("AI Limiter", 1), Out("Crystal Oscillator", 1)], Alternate: true),
        new(204, "High-Speed Connector", "16", "6-1", [In("Quickwire", 56), In("Cable", 10), In("Circuit Board", 1), Out("High-Speed Connector", 1)]),
        new(205, "Silicon High-Speed Connector", "40", "6-1", [In("Quickwire", 60), In("Silica", 25), In("Circuit Board", 2), Out("High-Speed Connector", 2)], Alternate: true),
        new(206, "Computer", "24", "6-1", [In("Circuit Board", 4), In("Cable", 8), In("Plastic", 16), Out("Computer", 1)]),
        new(207, "Caterium Computer", "16", "6-1", [In("Circuit Board", 4), In("Quickwire", 14), In("Rubber", 6), Out("Computer", 1)], Alternate: true),
        new(209, "Modular Engine", "60", "6-1", [In("Motor", 2), In("Rubber", 15), In("Smart Plating", 2), Out("Modular Engine", 1)]),
        new(210, "Heavy Modular Frame", "30", "6-1", [In("Modular Frame", 5), In("Steel Pipe", 20), In("Encased Industrial Beam", 5), In("Screw", 120), Out("Heavy Modular Frame", 1)]),
        new(211, "Heavy Encased Frame", "64", "6-1", [In("Modular Frame", 8), In("Encased Industrial Beam", 10), In("Steel Pipe", 36), In("Concrete", 22), Out("Heavy Modular Frame", 3)], Alternate: true),
        new(212, "Heavy Flexible Frame", "16", "6-1", [In("Modular Frame", 5), In("Encased Industrial Beam", 3), In("Rubber", 20), In("Screw", 104), Out("Heavy Modular Frame", 1)], Alternate: true),
        new(213, "Adaptive Control Unit", "60", "6-1", [In("Automated Wiring", 5), In("Circuit Board", 5), In("Heavy Modular Frame", 1), In("Computer", 2), Out("Adaptive Control Unit", 1)]),
        new(221, "Packaged Turbo Rifle Ammo", "12", "7-1", [In("Rifle Ammo", 25), In("Aluminum Casing", 3), In("Packaged Turbofuel", 3), Out("Turbo Rifle Ammo", 50)]),
        new(232, "Iodine-Infused Filter", "16", "7-4", [In("Gas Filter", 1), In("Quickwire", 8), In("Aluminum Casing", 1), Out("Iodine-Infused Filter", 1)]),
        new(235, "Classic Battery", "8", "7-5", [In("Sulfur", 6), In("Alclad Aluminum Sheet", 7), In("Plastic", 8), In("Wire", 12), Out("Battery", 4)], Alternate: true),
        new(236, "Radio Control Unit", "48", "7-5", [In("Aluminum Casing", 32), In("Crystal Oscillator", 1), In("Computer", 2), Out("Radio Control Unit", 2)]),
        new(237, "Radio Control System", "40", "7-5", [In("Crystal Oscillator", 1), In("Circuit Board", 10), In("Aluminum Casing", 60), In("Rubber", 30), Out("Radio Control Unit", 3)], Alternate: true),
        new(238, "Radio Connection Unit", "16", "8-3", [In("Heat Sink", 4), In("High-Speed Connector", 2), In("Quartz Crystal", 12), Out("Radio Control Unit", 1)], Alternate: true),
        new(239, "Supercomputer", "32", "7-5", [In("Computer", 4), In("AI Limiter", 2), In("High-Speed Connector", 3), In("Plastic", 28), Out("Supercomputer", 1)]),
        new(240, "Super-State Computer", "25", "8-2", [In("Computer", 3), In("Electromagnetic Control Rod", 1), In("Battery", 10), In("Wire", 25), Out("Supercomputer", 1)], Alternate: true),
        new(247, "Infused Uranium Cell", "12", "8-2", [In("Uranium", 5), In("Silica", 3), In("Sulfur", 5), In("Quickwire", 15), Out("Encased Uranium Cell", 4)], Alternate: true),
        new(251, "Nuke Nobelisk", "120", "8-2", [In("Nobelisk", 5), In("Encased Uranium Cell", 20), In("Smokeless Powder", 10), In("AI Limiter", 6), Out("Nuke Nobelisk", 1)]),
        new(252, "Uranium Fuel Rod", "150", "8-2", [In("Encased Uranium Cell", 50), In("Encased Industrial Beam", 3), In("Electromagnetic Control Rod", 5), Out("Uranium Fuel Rod", 1)]),
        new(253, "Uranium Fuel Unit", "300", "8-2", [In("Encased Uranium Cell", 100), In("Electromagnetic Control Rod", 10), In("Crystal Oscillator", 3), In("Rotor", 10), Out("Uranium Fuel Rod", 3)], Alternate: true),
        new(266, "Turbo Motor", "32", "8-4", [In("Cooling System", 4), In("Radio Control Unit", 2), In("Motor", 4), In("Rubber", 24), Out("Turbo Motor", 1)]),
        new(267, "Turbo Electric Motor", "64", "8-4", [In("Motor", 7), In("Radio Control Unit", 9), In("Electromagnetic Control Rod", 5), In("Rotor", 7), Out("Turbo Motor", 3)], Alternate: true),
        new(268, "Turbo Pressure Motor", "32", "8-5", [In("Motor", 4), In("Pressure Conversion Cube", 1), In("Packaged Nitrogen Gas", 24), In("Stator", 8), Out("Turbo Motor", 2)], Alternate: true),
        new(269, "Thermal Propulsion Rocket", "120", "8-4", [In("Modular Engine", 5), In("Turbo Motor", 2), In("Cooling System", 6), In("Fused Modular Frame", 2), Out("Thermal Propulsion Rocket", 2)]),
        new(272, "Plutonium Fuel Rod", "240", "8-5", [In("Encased Plutonium Cell", 30), In("Steel Beam", 18), In("Electromagnetic Control Rod", 6), In("Heat Sink", 10), Out("Plutonium Fuel Rod", 1)]),
        new(298, "SAM Fluctuator", "6", "9-1", [In("Reanimated SAM", 6), In("Wire", 5), In("Steel Pipe", 3), Out("SAM Fluctuator", 1)]),
        new(310, "Singularity Cell", "60", "9-4", [In("Nuclear Pasta", 1), In("Dark Matter Crystal", 20), In("Iron Plate", 100), In("Concrete", 200), Out("Singularity Cell", 10)]),
        new(311, "Ballistic Warp Drive", "60", "9-4", [In("Thermal Propulsion Rocket", 1), In("Singularity Cell", 5), In("Superposition Oscillator", 2), In("Dark Matter Crystal", 40), Out("Ballistic Warp Drive", 1)]),
    ];
}
