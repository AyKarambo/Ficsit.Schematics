namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Particle Accelerator.</summary>
public sealed class ParticleAcceleratorRecipes : RecipeModule
{
    protected override string Machine => "Particle Accelerator";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(275, "Nuclear Pasta",               Batch: "120", Tier: "8-5", [In("Copper Powder", 200), In("Pressure Conversion Cube", 1), Out("Nuclear Pasta", 1)], AveragePower: "-1000", MinPower: "-1500"),
        new(279, "Instant Plutonium Cell",      Batch: "120", Tier: "8-5", [In("Non-Fissile Uranium", 150), In("Aluminum Casing", 20), Out("Encased Plutonium Cell", 20)], Alternate: true, AveragePower: "-500", MinPower: "-750"),
        new(284, "Plutonium Pellet",            Batch: "60",  Tier: "8-5", [In("Non-Fissile Uranium", 100), In("Uranium Waste", 25), Out("Plutonium Pellet", 30)], AveragePower: "-500", MinPower: "-750"),
        new(287, "Diamonds",                    Batch: "2",   Tier: "9-1", [In("Coal", 20), Out("Diamonds", 1)], AveragePower: "-500", MinPower: "-750"),
        new(288, "Cloudy Diamonds",             Batch: "3",   Tier: "9-1", [In("Coal", 12), In("Limestone", 24), Out("Diamonds", 1)], Alternate: true, AveragePower: "-500", MinPower: "-750"),
        new(289, "Oil-Based Diamonds",          Batch: "3",   Tier: "9-1", [In("Crude Oil", 10), Out("Diamonds", 2)], Alternate: true, AveragePower: "-500", MinPower: "-750"),
        new(290, "Petroleum Diamonds",          Batch: "2",   Tier: "9-1", [In("Petroleum Coke", 24), Out("Diamonds", 1)], Alternate: true, AveragePower: "-500", MinPower: "-750"),
        new(292, "Turbo Diamonds",              Batch: "3",   Tier: "9-1", [In("Coal", 30), In("Packaged Turbofuel", 2), Out("Diamonds", 3)], Alternate: true, AveragePower: "-500", MinPower: "-750"),
        new(301, "Dark Matter Crystal",         Batch: "2",   Tier: "9-2", [In("Diamonds", 1), In("Dark Matter Residue", 5), Out("Dark Matter Crystal", 1)], AveragePower: "-1000", MinPower: "-1500"),
        new(302, "Dark Matter Crystallization", Batch: "3",   Tier: "9-2", [In("Dark Matter Residue", 10), Out("Dark Matter Crystal", 1)], Alternate: true, AveragePower: "-1000", MinPower: "-1500"),
        new(303, "Dark Matter Trap",            Batch: "2",   Tier: "9-2", [In("Time Crystal", 1), In("Dark Matter Residue", 5), Out("Dark Matter Crystal", 2)], Alternate: true, AveragePower: "-1000", MinPower: "-1500"),
        new(313, "Ficsonium",                   Batch: "6",   Tier: "9-5", [In("Plutonium Waste", 1), In("Singularity Cell", 1), In("Dark Matter Residue", 20), Out("Ficsonium", 1)], AveragePower: "-1000", MinPower: "-1500"),
    ];
}
