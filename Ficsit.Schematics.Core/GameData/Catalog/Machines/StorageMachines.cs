namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>Storage containers and fluid buffers (no power, no overclock).</summary>
public sealed class StorageMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(3,  "Storage Container", "0-5", overclockExp: null,
                cost: [C("Iron Plate", 10), C("Iron Rod", 10)]),
        Machine(9,  "Fluid Buffer", "3-1", overclockExp: null,
                cost: [C("Copper Sheet", 10), C("Modular Frame", 5)]),
        Machine(13, "Industrial Storage Container", "4-2", overclockExp: null,
                cost: [C("Steel Beam", 20), C("Steel Pipe", 20)]),
        Machine(18, "Industrial Fluid Buffer", "5-5", overclockExp: null,
                cost: [C("Encased Industrial Beam", 5), C("Copper Sheet", 10), C("Plastic", 25)]),
    ];
}
