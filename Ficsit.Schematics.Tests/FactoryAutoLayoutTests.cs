using Ficsit.Schematics.Core.Model;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class FactoryAutoLayoutTests
{
    private static FactoryGraph Graph(params NodeConnection[] connections)
    {
        var graph = new FactoryGraph();
        graph.Connections.AddRange(connections);
        return graph;
    }

    [Fact]
    public void Arranges_a_chain_into_left_to_right_columns()
    {
        var a = new FactoryNode { Name = "A", X = 999, Y = 999 };
        var b = new FactoryNode { Name = "B", X = 0, Y = 0 };
        var c = new FactoryNode { Name = "C", X = 50, Y = 50 };
        var graph = Graph(
            new NodeConnection { From = a, To = b, Part = "p1" },
            new NodeConnection { From = b, To = c, Part = "p2" });

        var pos = FactoryAutoLayout.Arrange([a, b, c], graph, 100, 200);

        // Producers sit one column left of their consumers; origin is the top-left.
        Assert.Equal(100, pos[a].X);
        Assert.Equal(100 + FactoryAutoLayout.ColumnGap, pos[b].X);
        Assert.Equal(100 + 2 * FactoryAutoLayout.ColumnGap, pos[c].X);
        Assert.Equal(200, pos[a].Y);
        Assert.Equal(200, pos[b].Y);
        Assert.Equal(200, pos[c].Y);
    }

    [Fact]
    public void Stacks_sibling_consumers_in_one_column()
    {
        // One source feeding two sinks — P and Q share the next column, stacked.
        var s = new FactoryNode { Name = "S" };
        var p = new FactoryNode { Name = "P", Y = 0 };
        var q = new FactoryNode { Name = "Q", Y = 100 };
        var graph = Graph(
            new NodeConnection { From = s, To = p, Part = "p1" },
            new NodeConnection { From = s, To = q, Part = "p2" });

        var pos = FactoryAutoLayout.Arrange([s, p, q], graph, 0, 0);

        Assert.True(pos[s].X < pos[p].X);
        Assert.Equal(pos[p].X, pos[q].X);
        Assert.Equal(FactoryAutoLayout.RowGap, System.Math.Abs(pos[p].Y - pos[q].Y), 3);
    }

    [Fact]
    public void Places_a_feeder_next_to_what_it_feeds()
    {
        // R feeds only the deep node C (a main chain A→B→C). "Next to what it feeds"
        // means R lands in the column just left of C, not far out at column 0.
        var a = new FactoryNode { Name = "A" };
        var b = new FactoryNode { Name = "B" };
        var c = new FactoryNode { Name = "C" };
        var r = new FactoryNode { Name = "R" };
        var graph = Graph(
            new NodeConnection { From = a, To = b, Part = "p1" },
            new NodeConnection { From = b, To = c, Part = "p2" },
            new NodeConnection { From = r, To = c, Part = "p3" });

        var pos = FactoryAutoLayout.Arrange([a, b, c, r], graph, 0, 0);

        Assert.Equal(pos[b].X, pos[r].X);        // R rides alongside B, one column left of C
        Assert.True(pos[r].X < pos[c].X);
        Assert.True(pos[a].X < pos[r].X);
    }

    [Fact]
    public void Reorders_a_column_to_reduce_crossings()
    {
        // A feeds Y; A and B both feed X. A good layout puts Y above X so A's wires
        // don't cross B's. (Single component via the shared consumer X.)
        var a = new FactoryNode { Name = "A", Y = 0 };
        var b = new FactoryNode { Name = "B", Y = 100 };
        var x = new FactoryNode { Name = "X", Y = 0 };
        var y = new FactoryNode { Name = "Y", Y = 100 };
        var graph = Graph(
            new NodeConnection { From = a, To = y, Part = "p1" },
            new NodeConnection { From = a, To = x, Part = "p2" },
            new NodeConnection { From = b, To = x, Part = "p3" });

        var pos = FactoryAutoLayout.Arrange([a, b, x, y], graph, 0, 0);

        Assert.True(pos[a].Y < pos[b].Y, "sources keep their order");
        Assert.True(pos[y].Y < pos[x].Y, "Y (fed only by A) sits above X (fed by A and B)");
        Assert.Equal(pos[a].X, pos[b].X);
        Assert.Equal(pos[x].X, pos[y].X);
        Assert.True(pos[y].X > pos[a].X);
    }

    [Fact]
    public void Separates_independent_islands_into_bands()
    {
        // Two unrelated chains — they must not interleave; one sits entirely above
        // the other with a gap between the bands.
        var a = new FactoryNode { Name = "A" };
        var b = new FactoryNode { Name = "B" };
        var x = new FactoryNode { Name = "X" };
        var y = new FactoryNode { Name = "Y" };
        var graph = Graph(
            new NodeConnection { From = a, To = b, Part = "p1" },
            new NodeConnection { From = x, To = y, Part = "p2" });

        var pos = FactoryAutoLayout.Arrange([a, b, x, y], graph, 0, 0);

        var firstBandBottom = System.Math.Max(pos[a].Y, pos[b].Y);
        var secondBandTop = System.Math.Min(pos[x].Y, pos[y].Y);
        Assert.True(secondBandTop - firstBandBottom >= FactoryAutoLayout.RowGap,
            "independent islands are separated by a band gap");
        Assert.True(pos[b].X > pos[a].X);
        Assert.True(pos[y].X > pos[x].X);
    }

    [Fact]
    public void Tolerates_a_recycle_loop_without_hanging()
    {
        // A↔B cycle plus a downstream sink C — must terminate and place everyone
        // compactly (no node thrown to a far-off column).
        var a = new FactoryNode { Name = "A" };
        var b = new FactoryNode { Name = "B" };
        var c = new FactoryNode { Name = "C" };
        var graph = Graph(
            new NodeConnection { From = a, To = b, Part = "p1" },
            new NodeConnection { From = b, To = a, Part = "p2" },
            new NodeConnection { From = b, To = c, Part = "p3" });

        var pos = FactoryAutoLayout.Arrange([a, b, c], graph, 0, 0);

        Assert.Equal(3, pos.Count);
        Assert.True(pos[c].X >= pos[b].X);
        // Compact: the cycle does not stretch the layout past its node count.
        Assert.True(pos[c].X <= 3 * FactoryAutoLayout.ColumnGap);
    }
}
