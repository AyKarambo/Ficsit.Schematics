namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>Resource-extraction machines, with their mark/purity families.</summary>
public sealed class ExtractorMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        // The Miner is a "multi-machine": the planner treats it as one building, but you
        // pick a MARK (Mk.1/2/3 → throughput) and the node's PURITY (×½ / ×1 / ×2). Each
        // mark is its own machine; recipes target the family name "Miner".
        Family(0, "Miner", showPpm: true, autoRound: false, defaultMax: "60",
            marks:
            [
                Mark(4,  "Miner Mk.1", "0-5", throughput: "60", isDefault: true,
                     power: "-5",  cost: [C("Portable Miner", 1), C("Iron Plate", 10), C("Concrete", 10)]),
                Mark(14, "Miner Mk.2", "4-3", throughput: "120",
                     power: "-15", cost: [C("Portable Miner", 2), C("Encased Industrial Beam", 10), C("Steel Pipe", 20), C("Modular Frame", 10)]),
                Mark(26, "Miner Mk.3", "8-4", throughput: "240",
                     power: "-45", cost: [C("Portable Miner", 3), C("Steel Pipe", 50), C("Supercomputer", 5), C("Fused Modular Frame", 10), C("Turbo Motor", 3)]),
            ],
            capacities: Purity),

        // Single machines that still snap to nodes of three purities → capacity-only families.
        Machine(16, "Oil Extractor", "5-2", power: "-40",
                cost: [C("Motor", 15), C("Encased Industrial Beam", 20), C("Cable", 60)])
            .WithFamily(1, showPpm: true, autoRound: false, defaultMax: "120", capacities: Purity),

        Machine(24, "Resource Well Extractor", "8-3", overclockExp: null,
                cost: [C("Steel Beam", 10), C("Aluminum Casing", 10)])
            .WithFamily(2, showPpm: true, defaultMax: "60", capacities: Purity),

        // Plain extractors (no marks, no purity choice).
        Machine(10, "Water Extractor", "3-1", power: "-20",
                cost: [C("Copper Sheet", 20), C("Reinforced Iron Plate", 10), C("Rotor", 10)]),

        Machine(25, "Resource Well Pressurizer", "8-3", power: "-150",
                cost: [C("Radio Control Unit", 10), C("Heavy Modular Frame", 25), C("Motor", 50), C("Alclad Aluminum Sheet", 50), C("Rubber", 100)]),
    ];
}
