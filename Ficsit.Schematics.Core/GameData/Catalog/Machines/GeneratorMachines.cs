namespace Ficsit.Schematics.Core.GameData.Catalog.Machines;

/// <summary>Power generators. Most scale power linearly with clock; the Geothermal is
/// purity-driven and the Alien Power Augmenter is a flat grid boost.</summary>
public sealed class GeneratorMachines : MachineModule
{
    protected override IReadOnlyList<MachineGroup> Groups =>
    [
        Machine(6,  "Biomass Burner"),
        Machine(11, "Coal-Powered Generator"),
        Machine(19, "Fuel-Powered Generator"),
        Machine(23, "Nuclear Power Plant"),

        // Geothermal output depends on the purity of the node it sits on (a power-ratio family).
        Machine(21, "Geothermal Generator")
            .WithFamily(5, defaultMax: "1", capacities:
            [
                Cap(Purity.Impure, power: R("1/2")),
                Cap(Purity.Normal),
                Cap(Purity.Pure, power: 2),
            ]),

        // Boosts grid power rather than generating from a fuel recipe.
        Machine(28, "Alien Power Augmenter"),
    ];
}
