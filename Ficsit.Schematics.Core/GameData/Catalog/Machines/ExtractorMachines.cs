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
                Variant(4,  Mark.Mk1, isDefault: true),
                Variant(14, Mark.Mk2),
                Variant(26, Mark.Mk3),
            ],
            capacities: Purities),

        // Single machines that still snap to nodes of three purities → capacity-only families.
        Machine(16, "Oil Extractor")
            .WithFamily(1, showPpm: true, autoRound: false, defaultMax: "120", capacities: Purities),

        Machine(24, "Resource Well Extractor")
            .WithFamily(2, showPpm: true, defaultMax: "60", capacities: Purities),

        // Plain extractors (no marks, no purity choice).
        Machine(10, "Water Extractor"),

        Machine(25, "Resource Well Pressurizer"),
    ];
}
