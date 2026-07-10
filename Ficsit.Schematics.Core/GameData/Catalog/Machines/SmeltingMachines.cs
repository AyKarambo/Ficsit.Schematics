namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>Smelting machines (one ingredient in, ingots out).</summary>
public sealed class SmeltingMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(0,  "Smelter"),
        Machine(12, "Foundry"),
    ];
}
