namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>One-off machines: gift tree, space elevator, sink, depot uploader.</summary>
public sealed class SpecialMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(1, "FICSMAS Gift Tree", "0-3", overclockExp: null,
                cost: [C("FICSMAS Gift", 50), C("FICSMAS Tree Branch", 100), C("Red FICSMAS Ornament", 30), C("Blue FICSMAS Ornament", 30)]),

        // Place exactly one (defaultMax 1).
        Machine(5, "Space Elevator", "0-6", overclockExp: null,
                cost: [C("Concrete", 500), C("Iron Plate", 250), C("Iron Rod", 400), C("Wire", 1500)])
            .WithFamily(6, defaultMax: "1"),

        // The sink's throughput is set by the belt mark feeding it (a capacity family).
        Machine(8, "AWESOME Sink", "2-4", power: "-30", overclockExp: null,
                cost: [C("Reinforced Iron Plate", 15), C("Cable", 30), C("Concrete", 45)])
            .WithFamily(4, showPpm: true, capacities:
            [
                Cap("Mk.1 Belt", "60", isDefault: true),
                Cap("Mk.2 Belt", "120", color: 13775920),
                Cap("Mk.3 Belt", "270", color: 16311334),
                Cap("Mk.4 Belt", "480", color: 8433977),
                Cap("Mk.5 Belt", "780", color: 2504952),
                Cap("Mk.6 Belt", "1200", color: 12330744),
            ]),

        // Uploads to the Dimensional Depot at a selectable rate.
        Machine(30, "Dimensional Depot Uploader", "9-1", overclockExp: null,
                cost: [C("Mercer Sphere", 1), C("SAM Fluctuator", 10), C("Modular Frame", 10), C("Wire", 100)])
            .WithFamily(3, showPpm: true, capacities:
            [
                Cap("15/min", "15", isDefault: true),
                Cap("30/min", "30", color: 13775920),
                Cap("60/min", "60", color: 16311334),
                Cap("120/min", "120", color: 8433977),
                Cap("240/min", "240", color: 12330744),
            ]),
    ];
}
