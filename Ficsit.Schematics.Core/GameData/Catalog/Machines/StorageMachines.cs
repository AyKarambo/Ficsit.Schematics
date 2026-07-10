namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>Storage containers and fluid buffers (no power, no overclock).</summary>
public sealed class StorageMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(3,  "Storage Container"),
        Machine(9,  "Fluid Buffer"),
        Machine(13, "Industrial Storage Container"),
        Machine(18, "Industrial Fluid Buffer"),
    ];
}
