using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Serialization;
using Ficsit.Schematics.Core.Solver;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class SolverTests
{
    private static SolveResult Solve(FactoryDocument document)
        => new BasicSolver(TestData.Database).Solve(document);

    private static FactoryNode Node(FactoryDocument doc, string recipe, string? max = null)
    {
        var node = new FactoryNode
        {
            Name = recipe,
            Kind = SfmdSerializer.KindFor(recipe),
            Max = max,
        };
        doc.Root.Nodes.Add(node);
        return node;
    }

    private static void Connect(FactoryDocument doc, FactoryNode from, string part, FactoryNode to)
        => doc.Root.Connections.Add(new NodeConnection { From = from, To = to, Part = part });

    [Fact]
    public void Simple_chain_pushes_supply_downstream()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");
        var smelter = Node(doc, "Iron Ingot");
        Connect(doc, miner, "Iron Ore", smelter);

        var result = Solve(doc);
        // Smelter consumes 30 ore/min per machine → 2 machines from 60 ore.
        Assert.Equal(new Rational(2), result.For(smelter).Count);
        // Miner shows 60 ppm.
        Assert.Equal(new Rational(60), result.For(miner).DisplayValue);
        Assert.True(result.For(miner).IsPpmDisplay);
    }

    [Fact]
    public void Demand_pull_drives_unlimited_sources()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore"); // no limit
        var smelter = Node(doc, "Iron Ingot", "2");
        Connect(doc, miner, "Iron Ore", smelter);

        var result = Solve(doc);
        Assert.Equal(new Rational(2), result.For(smelter).Count);
        Assert.Equal(new Rational(60), result.For(miner).DisplayValue); // pulls 60 ore
    }

    [Fact]
    public void No_limits_anywhere_solves_to_zero()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore");
        var smelter = Node(doc, "Iron Ingot");
        Connect(doc, miner, "Iron Ore", smelter);

        var result = Solve(doc);
        Assert.Equal(Rational.Zero, result.For(smelter).Count);
        Assert.Equal(Rational.Zero, result.For(miner).DisplayValue);
    }

    [Fact]
    public void Unconnected_inputs_do_not_constrain_but_flag_unmade()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Limestone", "60");
        var concrete = Node(doc, "Fine Concrete");
        Connect(doc, miner, "Limestone", concrete);

        var result = Solve(doc);
        var node = result.For(concrete);
        Assert.Equal(Rational.One, node.Count);
        // Silica: needed 15/min, nothing connected → unmade 15.
        Assert.Equal(new Rational(15), node.Inputs["Silica"].Unmade);
        Assert.False(node.Inputs["Silica"].HasConnections);
        // Limestone fully supplied.
        Assert.Equal(new Rational(60), node.Inputs["Limestone"].Connected);
    }

    [Fact]
    public void Reference_save_reproduces_the_screenshot_numbers()
    {
        var doc = SfmdSerializer.Deserialize(File.ReadAllText(TestData.ReferenceSavePath));
        var result = Solve(doc);

        var nodes = doc.Root.Nodes;
        var concrete = nodes.Single(n => n.Name == "Fine Concrete");
        var pipe = nodes.Single(n => n.Name == "Encased Industrial Pipe");
        var frame = nodes.Single(n => n.Name == "Heavy Flexible Frame");

        Assert.Equal(Rational.One, result.For(concrete).Count);
        Assert.Equal(new Rational(5, 2), result.For(pipe).Count);
        // The famous 0.89: exactly 8/9 manufacturers.
        Assert.Equal(new Rational(8, 9), result.For(frame).Count);

        var framePorts = result.For(frame);
        Assert.Equal("50/3", framePorts.Inputs["Modular Frame"].Target.ToString());      // 16.67
        Assert.Equal("200/3", framePorts.Inputs["Rubber"].Target.ToString());            // 66.67
        Assert.Equal("1040/3", framePorts.Inputs["Screw"].Target.ToString());            // 346.67
        Assert.Equal("10/3", framePorts.Outputs["Heavy Modular Frame"].Target.ToString()); // 3.33
        // EIB fully supplied: 10/min in, no shortfall.
        Assert.Equal(new Rational(10), framePorts.Inputs["Encased Industrial Beam"].Connected);
        Assert.Equal(Rational.Zero, framePorts.Inputs["Encased Industrial Beam"].Unmade);
        // Unused output surplus is fully green (nothing consumes the frames).
        Assert.Equal("10/3", framePorts.Outputs["Heavy Modular Frame"].Unused.ToString());

        // Steel pipe unmade on the pipe assembler: 24 × 2.5 = 60/min.
        Assert.Equal(new Rational(60), result.For(pipe).Inputs["Steel Pipe"].Unmade);
    }

    [Fact]
    public void Splitter_shares_supply_proportionally_to_demand()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");
        var smelterA = Node(doc, "Iron Ingot", "1"); // wants 30
        var smelterB = Node(doc, "Iron Ingot", "3"); // wants 90 → total 120 > 60
        Connect(doc, miner, "Iron Ore", smelterA);
        Connect(doc, miner, "Iron Ore", smelterB);

        var result = Solve(doc);
        // Proportional: A gets 60·30/120 = 15 → ½ machine; B gets 45 → 1.5 machines.
        Assert.Equal(new Rational(1, 2), result.For(smelterA).Count);
        Assert.Equal(new Rational(3, 2), result.For(smelterB).Count);
    }

    [Fact]
    public void Merger_combines_two_sources()
    {
        var doc = new FactoryDocument();
        var minerA = Node(doc, "Iron Ore", "30");
        var minerB = Node(doc, "Iron Ore", "30");
        var smelter = Node(doc, "Iron Ingot");
        Connect(doc, minerA, "Iron Ore", smelter);
        Connect(doc, minerB, "Iron Ore", smelter);

        var result = Solve(doc);
        Assert.Equal(new Rational(2), result.For(smelter).Count); // 60 ore → 2 machines
    }

    [Fact]
    public void Clock_speed_scales_rates()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");
        var smelter = Node(doc, "Iron Ingot");
        smelter.ClockSpeed = new Rational(2); // 200%: 60 ore/min per machine
        Connect(doc, miner, "Iron Ore", smelter);

        var result = Solve(doc);
        Assert.Equal(Rational.One, result.For(smelter).Count);
    }

    [Fact]
    public void Awesome_sink_absorbs_and_scores_points()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");
        var sink = Node(doc, "AWESOME Sink");
        Connect(doc, miner, "Iron Ore", sink);

        var result = Solve(doc);
        Assert.Equal(new Rational(60), result.For(sink).DisplayValue);
        // Iron ore sinks for 1 point each → 60 points/min.
        Assert.Equal(new Rational(60), result.For(sink).SinkPointsPerMinute);
    }

    [Fact]
    public void Power_is_negative_for_consumers()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");
        var smelter = Node(doc, "Iron Ingot");
        Connect(doc, miner, "Iron Ore", smelter);

        var result = Solve(doc);
        // Smelter: 4 MW each, 2 machines → −8 MW.
        Assert.Equal(new Rational(-8), result.For(smelter).Power);
    }

    [Fact]
    public void Manual_solver_pins_entered_counts()
    {
        var doc = new FactoryDocument { Solver = "Manual" };
        var miner = Node(doc, "Iron Ore", "30");
        var smelter = Node(doc, "Iron Ingot", "5"); // wants 150 ore, only 30 arrives
        Connect(doc, miner, "Iron Ore", smelter);

        var result = SolverFactory.Create("Manual", TestData.Database).Solve(doc);
        Assert.Equal(new Rational(5), result.For(smelter).Count); // pinned, not clamped
        var ore = result.For(smelter).Inputs["Iron Ore"];
        Assert.True(ore.Connected < ore.Target); // mismatch visible
    }
}
