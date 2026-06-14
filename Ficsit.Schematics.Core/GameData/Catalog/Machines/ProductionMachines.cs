namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>General production machines. <c>sloops</c> = somersloop slots for output boosting.</summary>
public sealed class ProductionMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(2,  "Constructor", "0-3", power: "-4", sloops: 1, sloopMultiplier: "1", sloopPowerExp: "2",
                cost: [C("Reinforced Iron Plate", 2), C("Cable", 8)]),
        Machine(7,  "Assembler", "2-1", power: "-15", sloops: 2, sloopMultiplier: "1/2", sloopPowerExp: "2",
                cost: [C("Reinforced Iron Plate", 8), C("Rotor", 4), C("Cable", 10)]),
        Machine(15, "Refinery", "5-2", power: "-30", sloops: 2, sloopMultiplier: "1/2", sloopPowerExp: "2",
                cost: [C("Motor", 10), C("Encased Industrial Beam", 10), C("Steel Pipe", 30), C("Copper Sheet", 20)]),
        Machine(17, "Packager", "5-4", power: "-10",
                cost: [C("Steel Beam", 20), C("Rubber", 10), C("Plastic", 10)]),
        Machine(20, "Manufacturer", "6-1", power: "-55", sloops: 4, sloopMultiplier: "1/4", sloopPowerExp: "2",
                cost: [C("Motor", 10), C("Modular Frame", 20), C("Plastic", 50), C("Cable", 50)]),
        Machine(22, "Blender", "7-5", power: "-75", sloops: 4, sloopMultiplier: "1/4", sloopPowerExp: "2",
                cost: [C("Computer", 10), C("Heavy Modular Frame", 10), C("Motor", 20), C("Aluminum Casing", 50)]),

        // Particle Accelerator / Converter / Quantum Encoder have variable, recipe-driven power
        // (no fixed AveragePower), so only the overclock curve is set.
        Machine(27, "Particle Accelerator", "8-5", sloops: 4, sloopMultiplier: "1/4", sloopPowerExp: "2",
                cost: [C("Turbo Motor", 10), C("Supercomputer", 10), C("Fused Modular Frame", 25), C("Cooling System", 50), C("Quickwire", 500)]),
        Machine(29, "Converter", "9-1", sloops: 2, sloopMultiplier: "1/2", sloopPowerExp: "2",
                cost: [C("Fused Modular Frame", 10), C("Cooling System", 25), C("Radio Control Unit", 50), C("SAM Fluctuator", 100)]),
        Machine(31, "Quantum Encoder", "9-2", sloops: 4, sloopMultiplier: "1/4", sloopPowerExp: "2",
                cost: [C("Turbo Motor", 20), C("Supercomputer", 20), C("Cooling System", 50), C("Time Crystal", 50), C("Ficsite Trigon", 100)]),
    ];
}
