using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Saves;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class SaveConsolidationTests
{
    private static FactoryNode N(string recipe) => new() { Name = recipe, Kind = NodeKind.Recipe, Max = "1" };
    private static NodeConnection C(FactoryNode f, FactoryNode t, string part) => new() { From = f, To = t, Part = part };

    [Fact]
    public void Parallel_machines_merge_into_one_counted_node()
    {
        var source = N("Iron Ingot");
        var c1 = N("Iron Plate");
        var c2 = N("Iron Plate");
        var sink = N("Reinforced Iron Plate");
        var nodes = new[] { source, c1, c2, sink };
        var conns = new[]
        {
            C(source, c1, "Iron Ingot"), C(source, c2, "Iron Ingot"),
            C(c1, sink, "Iron Plate"), C(c2, sink, "Iron Plate"),
        };

        var (cNodes, cConns) = SaveConsolidation.Consolidate(nodes, conns);

        // c1 + c2 (same recipe, same source, same sink) → one "Iron Plate" x2; source/sink kept.
        Assert.Equal(3, cNodes.Count);
        var merged = Assert.Single(cNodes, n => n.Name == "Iron Plate");
        Assert.Equal("2", merged.Max);
        // The 4 connections collapse to source→merged and merged→sink.
        Assert.Equal(2, cConns.Count);
        Assert.Contains(cConns, x => x.From.Name == "Iron Ingot" && x.To == merged && x.Part == "Iron Ingot");
        Assert.Contains(cConns, x => x.From == merged && x.To.Name == "Reinforced Iron Plate" && x.Part == "Iron Plate");
    }

    [Fact]
    public void Same_recipe_with_different_neighbours_does_not_merge()
    {
        var s1 = N("Iron Ingot");
        var s2 = N("Iron Ingot");
        var a = N("Iron Plate"); // fed by s1
        var b = N("Iron Plate"); // fed by s2 — different source, so a separate line
        var nodes = new[] { s1, s2, a, b };
        var conns = new[] { C(s1, a, "Iron Ingot"), C(s2, b, "Iron Ingot") };

        var (cNodes, _) = SaveConsolidation.Consolidate(nodes, conns);

        Assert.Equal(2, cNodes.Count(n => n.Name == "Iron Plate")); // not merged
    }

    [Fact]
    public void Isolated_machines_are_left_individual()
    {
        // Two same-recipe machines with no connections (e.g. vehicle-fed) must stay separate.
        var a = N("Iron Plate");
        var b = N("Iron Plate");

        var (cNodes, _) = SaveConsolidation.Consolidate(new[] { a, b }, []);

        Assert.Equal(2, cNodes.Count);
    }

    [Fact]
    public void Different_clocks_do_not_merge()
    {
        // A machine at 150% is not the same as one at 100%, even behind the same manifold.
        var source = N("Iron Ingot");
        var fast = N("Iron Plate");
        fast.ClockSpeed = new Core.Numerics.Rational(3, 2);
        var slow = N("Iron Plate");
        var sink = N("Reinforced Iron Plate");
        var nodes = new[] { source, fast, slow, sink };
        var conns = new[]
        {
            C(source, fast, "Iron Ingot"), C(source, slow, "Iron Ingot"),
            C(fast, sink, "Iron Plate"), C(slow, sink, "Iron Plate"),
        };

        var (cNodes, _) = SaveConsolidation.Consolidate(nodes, conns);

        Assert.Equal(2, cNodes.Count(n => n.Name == "Iron Plate")); // one per distinct clock
    }

    [Fact]
    public void Merged_nodes_keep_their_shared_clock_and_sloops()
    {
        var source = N("Iron Ingot");
        var c1 = N("Iron Plate");
        var c2 = N("Iron Plate");
        foreach (var c in new[] { c1, c2 })
        {
            c.ClockSpeed = new Core.Numerics.Rational(1, 2);
            c.Somersloops = 1;
        }
        var nodes = new[] { source, c1, c2 };
        var conns = new[] { C(source, c1, "Iron Ingot"), C(source, c2, "Iron Ingot") };

        var (cNodes, _) = SaveConsolidation.Consolidate(nodes, conns);

        var merged = Assert.Single(cNodes, n => n.Name == "Iron Plate");
        Assert.Equal("2", merged.Max);
        Assert.Equal(new Core.Numerics.Rational(1, 2), merged.ClockSpeed);
        Assert.Equal(1, merged.Somersloops);
    }

    [Fact]
    public void Merged_connections_keep_their_logistics_kind()
    {
        var source = N("Iron Ingot");
        var c1 = N("Iron Plate");
        var c2 = N("Iron Plate");
        var nodes = new[] { source, c1, c2 };
        var conns = new[]
        {
            new NodeConnection { From = source, To = c1, Part = "Iron Ingot", Logistics = LogisticsKind.Truck },
            new NodeConnection { From = source, To = c2, Part = "Iron Ingot", Logistics = LogisticsKind.Truck },
        };

        var (_, cConns) = SaveConsolidation.Consolidate(nodes, conns);

        var merged = Assert.Single(cConns);
        Assert.Equal(LogisticsKind.Truck, merged.Logistics);
    }

    [Fact]
    public void Snapped_extractors_are_never_merged()
    {
        var smelter = N("Iron Ingot");
        var m1 = N("Iron Ore"); m1.ResourceNodeId = "node-A";
        var m2 = N("Iron Ore"); m2.ResourceNodeId = "node-B";
        var nodes = new[] { m1, m2, smelter };
        var conns = new[] { C(m1, smelter, "Iron Ore"), C(m2, smelter, "Iron Ore") };

        var (cNodes, _) = SaveConsolidation.Consolidate(nodes, conns);

        Assert.Equal(2, cNodes.Count(n => n.ResourceNodeId is not null)); // both miners kept
    }
}
