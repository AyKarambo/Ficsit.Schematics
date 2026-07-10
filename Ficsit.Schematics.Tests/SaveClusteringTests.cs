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
        Assert.Equal("Basic Iron Parts", outpost.Title); // iron-parts product line
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
        Assert.Contains(outposts, o => o.Title == "Basic Iron Parts");
        Assert.Contains(outposts, o => o.Title == "Copper Parts");
        // No machine spans the two sites.
        Assert.All(nodes, n => Assert.NotNull(n.Parent));
    }

    [Fact]
    public void Names_by_dominant_product_weighted_by_machine_count()
    {
        // One consolidated "Concrete ×10" node must outweigh two stray "Screw" nodes, so the
        // outpost is named for what it mostly makes — not the more numerous nodes.
        var concrete = Machine("Concrete", 0, 0);
        concrete.Max = "10";
        var nodes = new List<FactoryNode> { concrete, Machine("Screw", 10, 0), Machine("Screw", 20, 0) };

        var outpost = Assert.Single(SaveClustering.GroupByLocation(nodes, TestData.Database, radius: 80));
        Assert.Equal("Concrete", outpost.Title);
    }

    [Fact]
    public void A_two_node_mini_factory_becomes_an_outpost_but_lone_nodes_stay_loose()
    {
        // Post-consolidation, two different nodes side by side are a deliberate mini-factory
        // (a packager + its refinery); a single stray stays loose.
        var rod = Machine("Iron Rod", 0, 0);
        var screw = Machine("Screw", 10, 0);
        var lone = Machine("Wire", 9000, 9000);
        var nodes = new List<FactoryNode> { rod, screw, lone };

        var outposts = SaveClustering.GroupByLocation(nodes, TestData.Database, radius: 80);

        var outpost = Assert.Single(outposts);
        Assert.Same(outpost, rod.Parent);
        Assert.Same(outpost, screw.Parent);
        Assert.Null(lone.Parent); // lone machine stays loose
    }

    [Fact]
    public void Already_parented_nodes_are_left_alone()
    {
        var existing = new FactoryNode { Name = "Outpost", Kind = NodeKind.Outpost };
        var member = Machine("Iron Rod", 0, 0);
        member.Parent = existing;
        var nodes = new List<FactoryNode> { member, Machine("Wire", 10, 0), Machine("Wire", 20, 0) };

        var outposts = SaveClustering.GroupByLocation(nodes, TestData.Database, radius: 80);

        Assert.Single(outposts); // the two loose machines form their own outpost…
        Assert.Same(existing, member.Parent); // …but the already-grouped member is untouched
    }
}
