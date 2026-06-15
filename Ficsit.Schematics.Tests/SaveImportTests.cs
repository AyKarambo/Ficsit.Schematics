using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class SaveImportTests
{
    private static ResourceNodeInfo Node(string instance, string part, string purity, double xCm, double yCm)
        => new() { Instance = instance, Kind = ResourceNodeKind.Node, Part = part, Purity = purity, X = xCm, Y = yCm };

    [Fact]
    public void Extractor_snaps_to_its_resource_node_with_recipe_purity_and_mark()
    {
        var world = new SaveWorld
        {
            Buildings = [new SaveBuilding { ClassName = "Build_MinerMk2_C", X = 100_000, Y = 200_000 }],
            ResourceNodes = [Node("node1", "Iron Ore", "Pure", 100_000, 200_000)],
        };

        var node = Assert.Single(SaveImport.BuildNodes(world, TestData.Database));
        Assert.Equal("Iron Ore", node.Name);            // recipe from the node
        Assert.Equal("node1", node.ResourceNodeId);     // snapped
        Assert.Equal("Miner Mk.2", node.MachineVariant); // mark from the class
        Assert.Equal("Pure", node.Capacity);            // purity from the node
        Assert.Equal(1000, node.X, 3);                  // world cm → canvas units (cm / 100)
        Assert.Equal(2000, node.Y, 3);
    }

    [Fact]
    public void Two_extractors_do_not_share_one_node()
    {
        var world = new SaveWorld
        {
            Buildings =
            [
                new SaveBuilding { ClassName = "Build_MinerMk1_C", X = 100_000, Y = 200_000 },
                new SaveBuilding { ClassName = "Build_MinerMk1_C", X = 100_010, Y = 200_010 },
            ],
            ResourceNodes = [Node("only", "Iron Ore", "Normal", 100_000, 200_000)],
        };

        // Only one node, so only one extractor snaps; the second has no free node nearby → dropped.
        var node = Assert.Single(SaveImport.BuildNodes(world, TestData.Database));
        Assert.Equal("only", node.ResourceNodeId);
    }

    [Fact]
    public void Production_machine_is_placed_with_a_recipe_as_one_machine()
    {
        var world = new SaveWorld
        {
            Buildings = [new SaveBuilding { ClassName = "Build_ConstructorMk1_C", X = 50_000, Y = -25_000 }],
        };

        var node = Assert.Single(SaveImport.BuildNodes(world, TestData.Database));
        Assert.True(TestData.Database.RecipesByName.TryGetValue(node.Name, out var recipe));
        Assert.Equal("Constructor", recipe!.Machine);
        Assert.Null(node.ResourceNodeId);
        Assert.Equal("1", node.Max);                    // one physical machine
        Assert.Equal(500, node.X, 3);
        Assert.Equal(-250, node.Y, 3);
    }

    [Fact]
    public void Clock_and_sloops_carry_through_to_the_placed_node()
    {
        var world = new SaveWorld
        {
            Buildings =
            [
                new SaveBuilding
                {
                    ClassName = "Build_ConstructorMk1_C", X = 0, Y = 0,
                    ClockSpeed = new Rational(3, 2), Somersloops = 1,
                },
            ],
        };

        var node = Assert.Single(SaveImport.BuildNodes(world, TestData.Database));
        Assert.Equal(new Rational(3, 2), node.ClockSpeed);
        Assert.Equal(1, node.Somersloops);
    }

    [Fact]
    public void Production_recipes_correlate_to_machines_in_order()
    {
        // The k-th machine of a type takes the k-th mCurrentRecipe of that type — so a Packager
        // that packages Water shows Water, not the placeholder first recipe (issue #8 follow-up).
        var world = new SaveWorld
        {
            Buildings =
            [
                new SaveBuilding { ClassName = "Build_Packager_C", X = 0, Y = 0 },
                new SaveBuilding { ClassName = "Build_Packager_C", X = 100, Y = 0 },
            ],
            RecipeStems = ["PackagedWater", "PackagedFuel"],
        };

        var nodes = SaveImport.BuildNodes(world, TestData.Database);
        Assert.Equal(2, nodes.Count);
        Assert.Equal("Packaged Water", nodes[0].Name);
        Assert.Equal("Packaged Fuel", nodes[1].Name);
    }

    [Fact]
    public void Recipe_stems_only_feed_their_own_machine_type()
    {
        // A Constructor recipe in the save must not be consumed by a Packager (per-type queues).
        var world = new SaveWorld
        {
            Buildings = [new SaveBuilding { ClassName = "Build_Packager_C", X = 0, Y = 0 }],
            RecipeStems = ["IronPlate", "PackagedWater"],
        };

        var node = Assert.Single(SaveImport.BuildNodes(world, TestData.Database));
        Assert.Equal("Packaged Water", node.Name); // the Iron Plate (Constructor) stem is ignored here
    }

    [Fact]
    public void Generator_buildings_import_as_unified_generator_nodes()
    {
        // A generator imports as one node that accepts any fuel — its fuel comes from connections
        // (added later), not a guessed recipe (issue #10).
        var world = new SaveWorld
        {
            Buildings = [new SaveBuilding { ClassName = "Build_GeneratorFuel_C", X = 1000, Y = 2000 }],
        };

        var node = Assert.Single(SaveImport.BuildNodes(world, TestData.Database));
        Assert.Equal(NodeKind.Generator, node.Kind);
        Assert.Equal("Fuel-Powered Generator", node.Name);
        Assert.Null(node.ResourceNodeId);
        Assert.Equal(10, node.X, 3);
    }

    [Fact]
    public void Unmodelled_classes_are_skipped()
    {
        var world = new SaveWorld
        {
            Buildings =
            [
                new SaveBuilding { ClassName = "Build_ConveyorBeltMk5_C", X = 0, Y = 0 },
                new SaveBuilding { ClassName = "Build_Wall_8x4_C", X = 0, Y = 0 },
            ],
        };

        Assert.Empty(SaveImport.BuildNodes(world, TestData.Database));
    }
}
