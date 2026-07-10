namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>One-off machines: gift tree, space elevator, sink, depot uploader.</summary>
public sealed class SpecialMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(1, "FICSMAS Gift Tree"),

        // Place exactly one (defaultMax 1).
        Machine(5, "Space Elevator")
            .WithFamily(6, defaultMax: "1"),

        // The sink's throughput is set by the belt mark feeding it (a capacity family).
        Machine(8, "AWESOME Sink")
            .WithFamily(4, showPpm: true, capacities:
            [
                Belt(Mark.Mk1, 60, isDefault: true),
                Belt(Mark.Mk2, 120, color: 13775920),
                Belt(Mark.Mk3, 270, color: 16311334),
                Belt(Mark.Mk4, 480, color: 8433977),
                Belt(Mark.Mk5, 780, color: 2504952),
                Belt(Mark.Mk6, 1200, color: 12330744),
            ]),

        // Uploads to the Dimensional Depot at a selectable rate.
        Machine(30, "Dimensional Depot Uploader")
            .WithFamily(3, showPpm: true, capacities:
            [
                Upload(15, isDefault: true),
                Upload(30, color: 13775920),
                Upload(60, color: 16311334),
                Upload(120, color: 8433977),
                Upload(240, color: 12330744),
            ]),
    ];
}
