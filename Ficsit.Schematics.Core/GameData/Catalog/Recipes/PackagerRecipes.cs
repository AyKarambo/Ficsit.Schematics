namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Packager.</summary>
public sealed class PackagerRecipes : RecipeModule
{
    protected override string Machine => "Packager";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(156, "Unpackage Fuel",              Batch: 2, Tier: "5-4", [In("Packaged Fuel", 2), Out("Fuel", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(157, "Unpackage Heavy Oil Residue", Batch: 6, Tier: "5-4", [In("Packaged Heavy Oil Residue", 2), Out("Heavy Oil Residue", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(158, "Unpackage Oil",               Batch: 2, Tier: "5-4", [In("Packaged Oil", 2), Out("Crude Oil", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(159, "Unpackage Water",             Batch: 1, Tier: "5-4", [In("Packaged Water", 2), Out("Water", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(162, "Packaged Water",              Batch: 2, Tier: "5-4", [In("Water", 2), In("Empty Canister", 2), Out("Packaged Water", 2)], IgnoreInputMultiplier: true),
        new(164, "Packaged Heavy Oil Residue",  Batch: 4, Tier: "5-4", [In("Heavy Oil Residue", 2), In("Empty Canister", 2), Out("Packaged Heavy Oil Residue", 2)], IgnoreInputMultiplier: true),
        new(165, "Packaged Oil",                Batch: 4, Tier: "5-4", [In("Crude Oil", 2), In("Empty Canister", 2), Out("Packaged Oil", 2)], IgnoreInputMultiplier: true),
        new(166, "Packaged Fuel",               Batch: 3, Tier: "5-4", [In("Fuel", 2), In("Empty Canister", 2), Out("Packaged Fuel", 2)], IgnoreInputMultiplier: true),
        new(168, "Packaged Liquid Biofuel",     Batch: 3, Tier: "5-4", [In("Liquid Biofuel", 2), In("Empty Canister", 2), Out("Packaged Liquid Biofuel", 2)], IgnoreInputMultiplier: true),
        new(170, "Packaged Turbofuel",          Batch: 6, Tier: "5-4", [In("Turbofuel", 2), In("Empty Canister", 2), Out("Packaged Turbofuel", 2)], IgnoreInputMultiplier: true),
        new(171, "Packaged Rocket Fuel",        Batch: 1, Tier: "5-4", [In("Rocket Fuel", 2), In("Empty Fluid Tank", 1), Out("Packaged Rocket Fuel", 1)], IgnoreInputMultiplier: true),
        new(172, "Packaged Ionized Fuel",       Batch: 3, Tier: "5-4", [In("Ionized Fuel", 4), In("Empty Fluid Tank", 2), Out("Packaged Ionized Fuel", 2)], IgnoreInputMultiplier: true),
        new(174, "Unpackage Ionized Fuel",      Batch: 3, Tier: "5-4", [In("Packaged Ionized Fuel", 2), Out("Ionized Fuel", 4), Out("Empty Fluid Tank", 2)], IgnoreInputMultiplier: true),
        new(178, "Unpackage Liquid Biofuel",    Batch: 2, Tier: "5-4", [In("Packaged Liquid Biofuel", 2), Out("Liquid Biofuel", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(180, "Unpackage Rocket Fuel",       Batch: 1, Tier: "5-4", [In("Packaged Rocket Fuel", 1), Out("Rocket Fuel", 2), Out("Empty Fluid Tank", 1)], IgnoreInputMultiplier: true),
        new(185, "Unpackage Turbofuel",         Batch: 6, Tier: "5-4", [In("Packaged Turbofuel", 2), Out("Turbofuel", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(225, "Packaged Alumina Solution",   Batch: 1, Tier: "7-1", [In("Alumina Solution", 2), In("Empty Canister", 2), Out("Packaged Alumina Solution", 2)], IgnoreInputMultiplier: true),
        new(230, "Unpackage Alumina Solution",  Batch: 1, Tier: "7-1", [In("Packaged Alumina Solution", 2), Out("Alumina Solution", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(233, "Packaged Sulfuric Acid",      Batch: 3, Tier: "7-5", [In("Sulfuric Acid", 2), In("Empty Canister", 2), Out("Packaged Sulfuric Acid", 2)], IgnoreInputMultiplier: true),
        new(244, "Unpackage Sulfuric Acid",     Batch: 1, Tier: "7-5", [In("Packaged Sulfuric Acid", 1), Out("Sulfuric Acid", 1), Out("Empty Canister", 1)], IgnoreInputMultiplier: true),
        new(255, "Packaged Nitrogen Gas",       Batch: 1, Tier: "8-3", [In("Nitrogen Gas", 4), In("Empty Fluid Tank", 1), Out("Packaged Nitrogen Gas", 1)], IgnoreInputMultiplier: true),
        new(262, "Unpackage Nitrogen Gas",      Batch: 1, Tier: "8-3", [In("Packaged Nitrogen Gas", 1), Out("Nitrogen Gas", 4), Out("Empty Fluid Tank", 1)], IgnoreInputMultiplier: true),
        new(271, "Packaged Nitric Acid",        Batch: 2, Tier: "8-5", [In("Nitric Acid", 1), In("Empty Fluid Tank", 1), Out("Packaged Nitric Acid", 1)], IgnoreInputMultiplier: true),
        new(281, "Unpackage Nitric Acid",       Batch: 3, Tier: "8-5", [In("Packaged Nitric Acid", 1), Out("Nitric Acid", 1), Out("Empty Fluid Tank", 1)], IgnoreInputMultiplier: true),
    ];
}
