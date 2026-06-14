namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>Power generators. Most scale power linearly with clock (overclockExp 1);
/// the Geothermal is purity-driven and the Alien Power Augmenter is a flat grid boost.</summary>
public sealed class GeneratorMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(6,  "Biomass Burner", "0-6", power: 30, overclockExp: 1,
                cost: [C("Iron Plate", 15), C("Iron Rod", 15), C("Wire", 25)]),
        Machine(11, "Coal-Powered Generator", "3-1", power: 75, overclockExp: 1,
                cost: [C("Reinforced Iron Plate", 20), C("Rotor", 10), C("Cable", 30)]),
        Machine(19, "Fuel-Powered Generator", "5-5", power: 250, overclockExp: 1,
                cost: [C("Motor", 15), C("Encased Industrial Beam", 15), C("Copper Sheet", 30), C("Rubber", 50), C("Quickwire", 50)]),
        Machine(23, "Nuclear Power Plant", "8-2", power: 2500, overclockExp: 1,
                cost: [C("Supercomputer", 10), C("Heavy Modular Frame", 25), C("Alclad Aluminum Sheet", 100), C("Cable", 200), C("Concrete", 250)]),

        // Geothermal output depends on the purity of the node it sits on (a power-ratio family).
        Machine(21, "Geothermal Generator", "6-1", power: 200, minPower: 100,
                cost: [C("Motor", 10), C("High-Speed Connector", 25), C("Modular Frame", 25), C("Copper Sheet", 50), C("Wire", 250)])
            .WithFamily(5, defaultMax: "1", capacities:
            [
                Cap("Impure", color: 13775920, powerRatio: R("1/2")),
                Cap("Normal", isDefault: true),
                Cap("Pure", color: 8433977, powerRatio: 2),
            ]),

        // Boosts grid power rather than generating from a fuel recipe.
        Machine(28, "Alien Power Augmenter", "9-1", basePower: 500, basePowerBoost: R("1/10"),
                fueledBasePowerBoost: R("3/10"),
                cost: [C("Somersloop", 10), C("SAM Fluctuator", 50), C("Cable", 100), C("Encased Industrial Beam", 50), C("Motor", 25), C("Computer", 10)]),
    ];
}
