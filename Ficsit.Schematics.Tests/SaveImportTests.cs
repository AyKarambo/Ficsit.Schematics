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
    public void Truck_route_bridges_machines_across_stations_as_a_tagged_connection()
    {
        const string smelter = "L:PersistentLevel.Build_SmelterMk1_C_1";
        const string constructor = "L:PersistentLevel.Build_ConstructorMk1_C_2";
        const string stationA = "L:PersistentLevel.Build_TruckStation_C_3";
        const string stationB = "L:PersistentLevel.Build_TruckStation_C_4";
        var world = new SaveWorld
        {
            Buildings =
            [
                new SaveBuilding { ClassName = "Build_SmelterMk1_C", Instance = smelter },
                new SaveBuilding { ClassName = "Build_ConstructorMk1_C", Instance = constructor, X = 90_000 },
            ],
            // The smelter belt-feeds station A; station B belt-feeds the constructor. Nothing
            // links the stations by belt — only the truck circuit bridges them.
            ComponentLinks = new Dictionary<string, string>
            {
                [smelter + ".Output0"] = stationA + ".Input0",
                [stationB + ".Output0"] = constructor + ".Input0",
            },
            VehicleRoutes = [new SaveVehicleRoute(LogisticsKind.Truck, [stationA, stationB])],
        };

        var (nodes, connections) = SaveImport.Build(world, TestData.Database);

        Assert.Equal(2, nodes.Count); // the stations themselves are not nodes
        var link = Assert.Single(connections);
        Assert.Equal("Iron Ingot", link.Part);
        Assert.Equal(LogisticsKind.Truck, link.Logistics);
    }

    [Fact]
    public void Train_route_bridges_machines_across_freight_platforms_as_a_tagged_connection()
    {
        const string miner = "L:PersistentLevel.Build_MinerMk1_C_1";
        const string smelter = "L:PersistentLevel.Build_SmelterMk1_C_2";
        const string dockA = "L:PersistentLevel.Build_TrainDockingStation_C_3";
        const string dockB = "L:PersistentLevel.Build_TrainDockingStationLiquid_C_4";
        var world = new SaveWorld
        {
            Buildings =
            [
                new SaveBuilding { ClassName = "Build_MinerMk1_C", Instance = miner },
                new SaveBuilding { ClassName = "Build_SmelterMk1_C", Instance = smelter, X = 90_000 },
            ],
            ResourceNodes = [Node("node1", "Iron Ore", "Normal", 0, 0)],
            // The miner belt-feeds platform A; platform B belt-feeds the smelter. Only the
            // train route (from a timetable over both platforms' stations) bridges them.
            ComponentLinks = new Dictionary<string, string>
            {
                [miner + ".Output0"] = dockA + ".Input0",
                [dockB + ".Output0"] = smelter + ".Input0",
            },
            VehicleRoutes = [new SaveVehicleRoute(LogisticsKind.Train, [dockA, dockB])],
        };

        var (nodes, connections) = SaveImport.Build(world, TestData.Database);

        Assert.Equal(2, nodes.Count); // the platforms themselves are not nodes
        var link = Assert.Single(connections);
        Assert.Equal("Iron Ore", link.Part);
        Assert.Equal(LogisticsKind.Train, link.Logistics);
    }

    [Fact]
    public void Belt_connections_stay_plain_when_a_route_retraces_them()
    {
        const string smelter = "L:PersistentLevel.Build_SmelterMk1_C_1";
        const string constructor = "L:PersistentLevel.Build_ConstructorMk1_C_2";
        const string stationA = "L:PersistentLevel.Build_TruckStation_C_3";
        const string stationB = "L:PersistentLevel.Build_TruckStation_C_4";
        var world = new SaveWorld
        {
            Buildings =
            [
                new SaveBuilding { ClassName = "Build_SmelterMk1_C", Instance = smelter },
                new SaveBuilding { ClassName = "Build_ConstructorMk1_C", Instance = constructor, X = 100 },
            ],
            // A direct belt already carries the part; the truck circuit exists but adds nothing.
            ComponentLinks = new Dictionary<string, string>
            {
                [smelter + ".Output0"] = constructor + ".Input0",
                [stationB + ".Output1"] = stationA + ".Input1",
            },
            VehicleRoutes = [new SaveVehicleRoute(LogisticsKind.Truck, [stationA, stationB])],
        };

        var (_, connections) = SaveImport.Build(world, TestData.Database);

        var link = Assert.Single(connections);
        Assert.Equal(LogisticsKind.None, link.Logistics); // belt wins; no duplicate truck edge
    }

    [Fact]
    public void A_per_actor_recipe_stem_beats_the_order_correlation()
    {
        // When the scan attributed mCurrentRecipe to the actor itself, that wins outright —
        // even when the ordered stems would say otherwise.
        var world = new SaveWorld
        {
            Buildings =
            [
                new SaveBuilding { ClassName = "Build_ConstructorMk1_C", RecipeStem = "Cable" },
                new SaveBuilding { ClassName = "Build_ConstructorMk1_C", X = 100, RecipeStem = "Wire" },
            ],
            RecipeStems = ["Wire", "Cable"], // opposite order — must be ignored
        };

        var nodes = SaveImport.BuildNodes(world, TestData.Database);

        Assert.Equal(2, nodes.Count);
        Assert.Equal("Cable", nodes[0].Name);
        Assert.Equal("Wire", nodes[1].Name);
    }

    [Fact]
    public void A_machine_without_its_own_recipe_falls_back_to_first_recipe_not_the_queue()
    {
        // With per-actor recipes present, an unconfigured machine must NOT steal a stem from
        // the ordered list (that would shift someone else's recipe onto it).
        var world = new SaveWorld
        {
            Buildings =
            [
                new SaveBuilding { ClassName = "Build_ConstructorMk1_C", RecipeStem = "Cable" },
                new SaveBuilding { ClassName = "Build_ConstructorMk1_C", X = 100 }, // no recipe set
            ],
            RecipeStems = ["Cable"],
        };

        var nodes = SaveImport.BuildNodes(world, TestData.Database);

        Assert.Equal("Cable", nodes[0].Name);
        Assert.NotEqual("Cable", nodes[1].Name); // best-effort default, not a stolen stem
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
