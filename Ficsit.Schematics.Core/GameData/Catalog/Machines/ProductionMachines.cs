namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>General production machines (stats, somersloop slots and costs come from MachineStats).</summary>
public sealed class ProductionMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(2,  "Constructor"),
        Machine(7,  "Assembler"),
        Machine(15, "Refinery"),
        Machine(17, "Packager"),
        Machine(20, "Manufacturer"),
        Machine(22, "Blender"),

        // Particle Accelerator / Converter / Quantum Encoder have variable, recipe-driven
        // power (no fixed AveragePower in their stats), so only the overclock curve applies.
        Machine(27, "Particle Accelerator"),
        Machine(29, "Converter"),
        Machine(31, "Quantum Encoder"),
    ];
}
