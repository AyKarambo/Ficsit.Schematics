using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class SaveLayoutTests
{
    private static FactoryNode Machine(string recipe, double x, double y, FactoryNode? parent = null)
        => new() { Name = recipe, Kind = NodeKind.Recipe, X = x, Y = y, Parent = parent };

    private static FactoryNode Outpost(double x, double y)
        => new() { Name = "Outpost", Kind = NodeKind.Outpost, X = x, Y = y };

    private static NodeConnection Wire(FactoryNode from, FactoryNode to, string part)
        => new() { From = from, To = to, Part = part };

    [Fact]
    public void Members_spread_out_with_producers_left_of_consumers()
    {
        // Three machines within game-metres of each other — heaped on the canvas before layout.
        var outpost = Outpost(100, 200);
        var rod = Machine("Iron Rod", 100, 200, outpost);
        var screw = Machine("Screw", 104, 201, outpost);
        var plate = Machine("Iron Plate", 108, 202, outpost);
        var nodes = new List<FactoryNode> { rod, screw, plate };
        var wires = new List<NodeConnection> { Wire(rod, screw, "Iron Rod") };

        SaveLayout.ArrangeOutposts(nodes, wires, [outpost]);

        Assert.True(rod.X < screw.X); // feeder sits left of what it feeds
        var positions = nodes.Select(n => (n.X, n.Y)).ToHashSet();
        Assert.Equal(nodes.Count, positions.Count); // nobody stacked on anybody
        Assert.Equal((100, 200), (outpost.X, outpost.Y)); // the box keeps its world anchor
    }

    [Fact]
    public void Snapped_extractors_and_loose_machines_keep_their_world_positions()
    {
        var outpost = Outpost(0, 0);
        var miner = Machine("Iron Ore", 0, 0, outpost);
        miner.ResourceNodeId = "Node_123";
        var smelter = Machine("Iron Ingot", 4, 1, outpost);
        var rod = Machine("Iron Rod", 8, 2, outpost);
        var loose = Machine("Wire", 9000, 9000);
        var nodes = new List<FactoryNode> { miner, smelter, rod, loose };
        var wires = new List<NodeConnection>
        {
            Wire(miner, smelter, "Iron Ore"),
            Wire(smelter, rod, "Iron Ingot"),
        };

        SaveLayout.ArrangeOutposts(nodes, wires, [outpost]);

        Assert.Equal((0, 0), (miner.X, miner.Y)); // pinned to its resource node
        Assert.Equal((9000, 9000), (loose.X, loose.Y)); // not in an outpost — real world spot
        Assert.True(smelter.X < rod.X); // the rest is still layered
        Assert.NotEqual((smelter.X, smelter.Y), (rod.X, rod.Y));
    }

    [Fact]
    public void Each_outpost_lays_out_independently_at_its_own_anchor()
    {
        var west = Outpost(0, 0);
        var east = Outpost(5000, 5000);
        var w1 = Machine("Iron Rod", 0, 0, west);
        var w2 = Machine("Screw", 3, 1, west);
        var e1 = Machine("Wire", 5000, 5000, east);
        var e2 = Machine("Cable", 5003, 5001, east);
        var nodes = new List<FactoryNode> { w1, w2, e1, e2 };
        var wires = new List<NodeConnection> { Wire(w1, w2, "Iron Rod"), Wire(e1, e2, "Wire") };

        SaveLayout.ArrangeOutposts(nodes, wires, [west, east]);

        // Members stay near their own outpost's anchor, not pulled to a common origin.
        Assert.All(new[] { w1, w2 }, n => Assert.True(n.X < 2000 && n.Y < 2000));
        Assert.All(new[] { e1, e2 }, n => Assert.True(n.X >= 5000 && n.Y >= 5000));
    }

    [Fact]
    public void A_single_member_outpost_is_left_untouched()
    {
        var outpost = Outpost(10, 10);
        var only = Machine("Concrete", 12, 11, outpost);

        SaveLayout.ArrangeOutposts([only], [], [outpost]);

        Assert.Equal((12, 11), (only.X, only.Y));
    }
}
