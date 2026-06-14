namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>Smelting machines (one ingredient in, ingots out).</summary>
public sealed class SmeltingMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(0,  "Smelter", "0-2", power: -4, overclockExp: StandardOverclock,
                sloops: 1, sloopMultiplier: 1, sloopPowerExp: 2,
                cost: [C("Iron Rod", 5), C("Wire", 8)]),
        Machine(12, "Foundry", "3-3", power: -16, overclockExp: StandardOverclock,
                sloops: 2, sloopMultiplier: R("1/2"), sloopPowerExp: 2,
                cost: [C("Modular Frame", 10), C("Rotor", 10), C("Concrete", 20)]),
    ];
}
