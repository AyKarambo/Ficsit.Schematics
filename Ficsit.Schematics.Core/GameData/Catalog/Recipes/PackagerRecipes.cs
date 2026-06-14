namespace Ficsit.Schematics.Core.GameData.Catalog.Recipes;

/// <summary>Recipes that run on the Packager.</summary>
public sealed class PackagerRecipes : RecipeModule
{
    protected override string Machine => "Packager";

    protected override IReadOnlyList<Recipe> Recipes =>
    [
        new(156, "Unpackage Fuel", "2", "5-4", [In("Packaged Fuel", 2), Out("Fuel", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(157, "Unpackage Heavy Oil Residue", "6", "5-4", [In("Packaged Heavy Oil Residue", 2), Out("Heavy Oil Residue", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(158, "Unpackage Oil", "2", "5-4", [In("Packaged Oil", 2), Out("Crude Oil", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(159, "Unpackage Water", "1", "5-4", [In("Packaged Water", 2), Out("Water", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(162, "Packaged Water", "2", "5-4", [In("Water", 2), In("Empty Canister", 2), Out("Packaged Water", 2)], IgnoreInputMultiplier: true),
        new(164, "Packaged Heavy Oil Residue", "4", "5-4", [In("Heavy Oil Residue", 2), In("Empty Canister", 2), Out("Packaged Heavy Oil Residue", 2)], IgnoreInputMultiplier: true),
        new(165, "Packaged Oil", "4", "5-4", [In("Crude Oil", 2), In("Empty Canister", 2), Out("Packaged Oil", 2)], IgnoreInputMultiplier: true),
        new(166, "Packaged Fuel", "3", "5-4", [In("Fuel", 2), In("Empty Canister", 2), Out("Packaged Fuel", 2)], IgnoreInputMultiplier: true),
        new(168, "Packaged Liquid Biofuel", "3", "5-4", [In("Liquid Biofuel", 2), In("Empty Canister", 2), Out("Packaged Liquid Biofuel", 2)], IgnoreInputMultiplier: true),
        new(170, "Packaged Turbofuel", "6", "5-4", [In("Turbofuel", 2), In("Empty Canister", 2), Out("Packaged Turbofuel", 2)], IgnoreInputMultiplier: true),
        new(171, "Packaged Rocket Fuel", "1", "5-4", [In("Rocket Fuel", 2), In("Empty Fluid Tank", 1), Out("Packaged Rocket Fuel", 1)], IgnoreInputMultiplier: true),
        new(172, "Packaged Ionized Fuel", "3", "5-4", [In("Ionized Fuel", 4), In("Empty Fluid Tank", 2), Out("Packaged Ionized Fuel", 2)], IgnoreInputMultiplier: true),
        new(174, "Unpackage Ionized Fuel", "3", "5-4", [In("Packaged Ionized Fuel", 2), Out("Ionized Fuel", 4), Out("Empty Fluid Tank", 2)], IgnoreInputMultiplier: true),
        new(178, "Unpackage Liquid Biofuel", "2", "5-4", [In("Packaged Liquid Biofuel", 2), Out("Liquid Biofuel", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(180, "Unpackage Rocket Fuel", "1", "5-4", [In("Packaged Rocket Fuel", 1), Out("Rocket Fuel", 2), Out("Empty Fluid Tank", 1)], IgnoreInputMultiplier: true),
        new(185, "Unpackage Turbofuel", "6", "5-4", [In("Packaged Turbofuel", 2), Out("Turbofuel", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(225, "Packaged Alumina Solution", "1", "7-1", [In("Alumina Solution", 2), In("Empty Canister", 2), Out("Packaged Alumina Solution", 2)], IgnoreInputMultiplier: true),
        new(230, "Unpackage Alumina Solution", "1", "7-1", [In("Packaged Alumina Solution", 2), Out("Alumina Solution", 2), Out("Empty Canister", 2)], IgnoreInputMultiplier: true),
        new(233, "Packaged Sulfuric Acid", "3", "7-5", [In("Sulfuric Acid", 2), In("Empty Canister", 2), Out("Packaged Sulfuric Acid", 2)], IgnoreInputMultiplier: true),
        new(244, "Unpackage Sulfuric Acid", "1", "7-5", [In("Packaged Sulfuric Acid", 1), Out("Sulfuric Acid", 1), Out("Empty Canister", 1)], IgnoreInputMultiplier: true),
        new(255, "Packaged Nitrogen Gas", "1", "8-3", [In("Nitrogen Gas", 4), In("Empty Fluid Tank", 1), Out("Packaged Nitrogen Gas", 1)], IgnoreInputMultiplier: true),
        new(262, "Unpackage Nitrogen Gas", "1", "8-3", [In("Packaged Nitrogen Gas", 1), Out("Nitrogen Gas", 4), Out("Empty Fluid Tank", 1)], IgnoreInputMultiplier: true),
        new(271, "Packaged Nitric Acid", "2", "8-5", [In("Nitric Acid", 1), In("Empty Fluid Tank", 1), Out("Packaged Nitric Acid", 1)], IgnoreInputMultiplier: true),
        new(281, "Unpackage Nitric Acid", "3", "8-5", [In("Packaged Nitric Acid", 1), Out("Nitric Acid", 1), Out("Empty Fluid Tank", 1)], IgnoreInputMultiplier: true),
    ];
}
