using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class SaveClusteringTests
{
    private static FactoryNode Machine(string recipe, double x, double y)
        => new() { Name = recipe, Kind = NodeKind.Recipe, X = x, Y = y };

    [Fact]
    public void A_dense_cluster_becomes_one_named_outpost()
    {
        var nodes = new List<FactoryNode>
        {
            Machine("Iron Rod", 0, 0),
            Machine("Iron Rod", 10, 0),
            Machine("Iron Plate", 20, 10),
        };

        var outposts = SaveClustering.GroupByLocation(nodes, TestData.Database, radius: 80);

        var outpost = Assert.Single(outposts);
        Assert.Equal(NodeKind.Outpost, outpost.Kind);
        Assert.Equal("Iron Rod", outpost.Title); // dominant output (2 of 3)
        Assert.All(nodes, n => Assert.Same(outpost, n.Parent));
    }

    [Fact]
    public void Distant_groups_become_separate_outposts()
    {
        var nodes = new List<FactoryNode>
        {
            Machine("Iron Rod", 0, 0), Machine("Iron Rod", 10, 0), Machine("Iron Rod", 20, 0),
            Machine("Wire", 5000, 5000), Machine("Wire", 5010, 5000), Machine("Wire", 5020, 5000),
        };

        var outposts = SaveClustering.GroupByLocation(nodes, TestData.Database, radius: 80);

        Assert.Equal(2, outposts.Count);
        Assert.Contains(outposts, o => o.Title == "Iron Rod");
        Assert.Contains(outposts, o => o.Title == "Wire");
        // No machine spans the two sites.
        Assert.All(nodes, n => Assert.NotNull(n.Parent));
    }

    [Fact]
    public void Sparse_machines_below_the_minimum_stay_loose()
    {
        var nodes = new List<FactoryNode>
        {
            Machine("Iron Rod", 0, 0),
            Machine("Iron Rod", 10, 0), // only two together — below the 3-machine minimum
            Machine("Wire", 9000, 9000), // lone machine
        };

        var outposts = SaveClustering.GroupByLocation(nodes, TestData.Database, radius: 80);

        Assert.Empty(outposts);
        Assert.All(nodes, n => Assert.Null(n.Parent));
    }

    [Fact]
    public void Already_parented_nodes_are_left_alone()
    {
        var existing = new FactoryNode { Name = "Outpost", Kind = NodeKind.Outpost };
        var member = Machine("Iron Rod", 0, 0);
        member.Parent = existing;
        var nodes = new List<FactoryNode> { member, Machine("Wire", 10, 0), Machine("Wire", 20, 0) };

        var outposts = SaveClustering.GroupByLocation(nodes, TestData.Database, radius: 80);

        Assert.Empty(outposts); // only 2 loose machines remain → below the minimum
        Assert.Same(existing, member.Parent);
    }
}
