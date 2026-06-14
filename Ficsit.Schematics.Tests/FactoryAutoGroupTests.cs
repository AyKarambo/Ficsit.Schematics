using Ficsit.Schematics.Core.Model;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class FactoryAutoGroupTests
{
    private static FactoryGraph Graph(params NodeConnection[] connections)
    {
        var graph = new FactoryGraph();
        graph.Connections.AddRange(connections);
        return graph;
    }

    [Fact]
    public void Groups_the_chain_feeding_a_shared_intermediate()
    {
        var ore = new FactoryNode { Name = "ore" };
        var ingot = new FactoryNode { Name = "ingot" };
        var plate = new FactoryNode { Name = "plate" };
        var rod = new FactoryNode { Name = "rod" };
        var graph = Graph(
            new NodeConnection { From = ore, To = ingot, Part = "Ore" },
            new NodeConnection { From = ingot, To = plate, Part = "Ingot" },
            new NodeConnection { From = ingot, To = rod, Part = "Ingot" });

        var groups = FactoryAutoGroup.KeyIntermediateGroups([ore, ingot, plate, rod], graph);

        // "Ingot" feeds 2 recipes → key intermediate; its sub-chain is ore + ingot.
        var group = Assert.Single(groups);
        Assert.Equal("Ingot", group.Part);
        Assert.Contains(ore, group.Nodes);
        Assert.Contains(ingot, group.Nodes);
        Assert.DoesNotContain(plate, group.Nodes);
        Assert.DoesNotContain(rod, group.Nodes);
    }

    [Fact]
    public void No_groups_in_a_pure_linear_chain()
    {
        var a = new FactoryNode { Name = "a" };
        var b = new FactoryNode { Name = "b" };
        var c = new FactoryNode { Name = "c" };
        var graph = Graph(
            new NodeConnection { From = a, To = b, Part = "x" },
            new NodeConnection { From = b, To = c, Part = "y" });

        Assert.Empty(FactoryAutoGroup.KeyIntermediateGroups([a, b, c], graph));
    }

    [Fact]
    public void Shared_upstream_stays_out_of_the_groups()
    {
        // u feeds both intermediates; s1/s2 feed exactly one each.
        var u = new FactoryNode { Name = "u" };
        var k1 = new FactoryNode { Name = "k1" };
        var k2 = new FactoryNode { Name = "k2" };
        var s1 = new FactoryNode { Name = "s1" };
        var s2 = new FactoryNode { Name = "s2" };
        var a1 = new FactoryNode { Name = "a1" };
        var a2 = new FactoryNode { Name = "a2" };
        var b1 = new FactoryNode { Name = "b1" };
        var b2 = new FactoryNode { Name = "b2" };
        var graph = Graph(
            new NodeConnection { From = u, To = k1, Part = "U" },
            new NodeConnection { From = u, To = k2, Part = "U" },
            new NodeConnection { From = s1, To = k1, Part = "S1" },
            new NodeConnection { From = s2, To = k2, Part = "S2" },
            new NodeConnection { From = k1, To = a1, Part = "P1" },
            new NodeConnection { From = k1, To = a2, Part = "P1" },
            new NodeConnection { From = k2, To = b1, Part = "P2" },
            new NodeConnection { From = k2, To = b2, Part = "P2" });

        var groups = FactoryAutoGroup.KeyIntermediateGroups([u, k1, k2, s1, s2, a1, a2, b1, b2], graph);

        Assert.Equal(2, groups.Count);                              // P1 and P2 sub-chains
        foreach (var group in groups) Assert.DoesNotContain(u, group.Nodes);
    }
}
