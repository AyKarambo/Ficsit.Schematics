using Ficsit.Schematics.Core.Editing;
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
    public void Snapped_extractor_scales_output_with_overclock()
    {
        // A miner placed on a map resource node is one physical machine: overclocking it must
        // scale the parts-per-minute (issue #7). The auto-applied ppm default Max ("60") must
        // not cap output at the 100% value.
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");
        miner.ResourceNodeId = "/Game/.../BP_ResourceNode_42"; // marks it snapped to a node
        miner.ClockSpeed = new Rational(3, 2);                  // 150%

        var result = Solve(doc);
        // Mk.1 miner on a Normal node at 150% → 90/min, one machine.
        Assert.Equal(new Rational(90), result.For(miner).DisplayValue);
        Assert.Equal(Rational.One, result.For(miner).Count);
        Assert.True(result.For(miner).IsPpmDisplay);
    }

    [Fact]
    public void Snapped_extractor_shows_full_output_for_node_purity()
    {
        // Snapping adopts the node purity (×2 for Pure); the displayed rate must reflect it,
        // not the default ppm Max (issue #7).
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");
        miner.ResourceNodeId = "/Game/.../BP_ResourceNode_7";
        miner.Capacity = "Pure";

        var result = Solve(doc);
        Assert.Equal(new Rational(120), result.For(miner).DisplayValue);
        Assert.Equal(Rational.One, result.For(miner).Count);
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
    public void Full_solver_routes_priority_splitter_by_branch_order()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");
        var splitter = Node(doc, "Priority Splitter");
        var a = Node(doc, "Iron Ingot", "2"); // first branch — wants 60
        var b = Node(doc, "Iron Ingot", "2"); // second branch — wants 60 (total 120 > 60)
        Connect(doc, miner, "Iron Ore", splitter);
        Connect(doc, splitter, "Iron Ore", a);
        Connect(doc, splitter, "Iron Ore", b);

        // Basic shares proportionally: equal demand → 30 each → one machine each.
        var basic = SolverFactory.Create("Basic", TestData.Database).Solve(doc);
        Assert.Equal(Rational.One, basic.For(a).Count);
        Assert.Equal(Rational.One, basic.For(b).Count);

        // Full fills the first branch fully (2 machines), starving the second.
        var full = SolverFactory.Create("Full", TestData.Database).Solve(doc);
        Assert.Equal(new Rational(2), full.For(a).Count);
        Assert.Equal(Rational.Zero, full.For(b).Count);
    }

    [Fact]
    public void Full_solver_drains_priority_merger_by_branch_order()
    {
        var doc = new FactoryDocument();
        var minerA = Node(doc, "Iron Ore", "60"); // first branch (priority)
        var minerB = Node(doc, "Iron Ore", "60"); // second branch
        var merger = Node(doc, "Priority Merger");
        var smelter = Node(doc, "Iron Ingot", "2"); // wants 60 total
        Connect(doc, minerA, "Iron Ore", merger);
        Connect(doc, minerB, "Iron Ore", merger);
        Connect(doc, merger, "Iron Ore", smelter);

        var full = SolverFactory.Create("Full", TestData.Database).Solve(doc);

        var aConn = doc.Root.Connections.First(c => c.From == minerA);
        var bConn = doc.Root.Connections.First(c => c.From == minerB);
        Assert.Equal(new Rational(60), full.Flows[aConn]); // first supplier drained first
        Assert.Equal(Rational.Zero, full.Flows[bConn]);    // second untouched
        Assert.Equal(new Rational(2), full.For(smelter).Count);
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

    // ------------------------------------------------------------ Outposts (flat model)

    [Fact]
    public void A_member_connects_across_the_outpost_boundary()
    {
        // Flat model: an outpost is a grouping. A member (Parent = outpost) connects to a node
        // outside it as an ordinary connection; flow crosses the boundary freely.
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");        // root
        var outpost = Node(doc, "Outpost");              // grouping
        var smelter = Node(doc, "Iron Ingot", "2");      // member, wants 60 ore
        smelter.Parent = outpost;
        Connect(doc, miner, "Iron Ore", smelter);

        var result = Solve(doc);
        Assert.Equal(new Rational(2), result.For(smelter).Count);
        Assert.Equal(new Rational(60), result.Flows[doc.Root.Connections.Single()]);
    }

    [Fact]
    public void Outpost_container_is_inert_in_the_solve()
    {
        var doc = new FactoryDocument();
        var outpost = Node(doc, "Outpost");
        Assert.Equal(Rational.Zero, Solve(doc).For(outpost).Count); // a bracket, not a machine
    }

    [Fact]
    public void Deleting_an_outpost_deletes_its_members()
    {
        var doc = new FactoryDocument();
        var outpost = Node(doc, "Outpost");
        var smelter = Node(doc, "Iron Ingot");
        smelter.Parent = outpost;

        var editor = new FactoryEditor(TestData.Database);
        editor.LoadDocument(doc);
        editor.DeleteNodes([outpost]);

        Assert.Empty(doc.Root.Nodes); // the outpost and its member are both removed
    }

    [Fact]
    public void Adding_a_node_inside_an_outpost_sets_its_parent()
    {
        var editor = new FactoryEditor(TestData.Database);
        editor.LoadDocument(new FactoryDocument());
        var outpost = editor.AddNode("Outpost", 0, 0);
        editor.EnterOutpost(outpost);
        var inner = editor.AddNode("Iron Ingot", 10, 10);

        Assert.Equal(outpost, inner.Parent);
        Assert.Contains(inner, editor.VisibleNodes);
        Assert.DoesNotContain(outpost, editor.VisibleNodes); // outpost itself lives at root
    }

    [Fact]
    public void Outpost_member_exports_across_the_boundary()
    {
        // Flat model: a member produces to a node outside the outpost as an ordinary connection —
        // no Import/Export handle. 60 ore crosses out of the outpost and runs 2 smelters.
        var doc = new FactoryDocument();
        var outpost = Node(doc, "Outpost");
        var miner = Node(doc, "Iron Ore", "60");
        miner.Parent = outpost;                  // member
        var smelter = Node(doc, "Iron Ingot");   // root, pulls the ore out
        Connect(doc, miner, "Iron Ore", smelter);

        var result = Solve(doc);
        Assert.Equal(new Rational(2), result.For(smelter).Count);
        Assert.Equal(new Rational(60), result.Flows[doc.Root.Connections.Single()]);
    }

    [Fact]
    public void Crossing_connection_runs_both_directions_through_a_nested_outpost()
    {
        // miner (root) → smelter (member) → constructor (root): flow crosses in and back out of
        // the outpost, both as plain connections. The outpost itself stays inert.
        var doc = new FactoryDocument();
        var outpost = Node(doc, "Outpost");
        var miner = Node(doc, "Iron Ore", "60");
        var smelter = Node(doc, "Iron Ingot");
        smelter.Parent = outpost;
        var rod = Node(doc, "Iron Rod");
        Connect(doc, miner, "Iron Ore", smelter);
        Connect(doc, smelter, "Iron Ingot", rod);

        var result = Solve(doc);
        Assert.Equal(new Rational(2), result.For(smelter).Count);  // 60 ore → 2 smelters → 60 ingot
        Assert.Equal(Rational.Zero, result.For(outpost).Count);    // a bracket, not a machine
    }

    // ------------------------------------------------------------ Generators (unified, any fuel)

    private static FactoryNode Generator(FactoryDocument doc, string machine, string? max = null)
    {
        var node = new FactoryNode { Name = machine, Kind = NodeKind.Generator, Max = max };
        doc.Root.Nodes.Add(node);
        return node;
    }

    [Fact]
    public void Generator_burns_the_connected_fuel_and_outputs_rated_power()
    {
        var doc = new FactoryDocument();
        var supply = Node(doc, "Storage Container");
        supply.StorageMode = StorageMode.Full; // open source of Fuel
        var gen = Generator(doc, "Fuel-Powered Generator", "2");
        Connect(doc, supply, "Fuel", gen);

        var result = Solve(doc);
        Assert.Equal(new Rational(2), result.For(gen).Count);    // 2 generators (the limit)
        Assert.Equal(new Rational(500), result.For(gen).Power);  // 2 × 250 MW (positive = generates)
        // Fuel Generator: In Fuel 1, Batch 3 → 20/min each → 40/min for two.
        Assert.Equal(new Rational(40), result.For(gen).Inputs["Fuel"].Target);
    }

    [Fact]
    public void Generator_accepts_a_different_fuel_at_that_fuels_rate()
    {
        var doc = new FactoryDocument();
        var supply = Node(doc, "Storage Container");
        supply.StorageMode = StorageMode.Full;
        var gen = Generator(doc, "Fuel-Powered Generator", "1");
        Connect(doc, supply, "Turbofuel", gen); // same machine, different fuel — just connect

        var result = Solve(doc);
        Assert.Equal(Rational.One, result.For(gen).Count);
        // Turbofuel Generator: In Turbofuel 1, Batch 8 → 7.5/min each.
        Assert.Equal(new Rational(15, 2), result.For(gen).Inputs["Turbofuel"].Target);
    }

    [Fact]
    public void Generator_with_no_fuel_shows_rated_power_for_its_count()
    {
        var doc = new FactoryDocument();
        var gen = Generator(doc, "Fuel-Powered Generator", "3");

        var result = Solve(doc);
        Assert.Equal(new Rational(3), result.For(gen).Count);    // a placed generator, no fuel yet
        Assert.Equal(new Rational(750), result.For(gen).Power);  // 3 × 250 MW rated
    }

    [Fact]
    public void Coal_generator_is_limited_by_its_water_supply()
    {
        // Coal Generator: In Coal 1 (Batch 4 → 15/min), In Water 3 (→ 45/min) per generator.
        var doc = new FactoryDocument();
        var coal = Node(doc, "Coal", "30");   // enough coal for 2 generators
        var water = Node(doc, "Storage Container");
        water.StorageMode = StorageMode.Full;
        var gen = Generator(doc, "Coal-Powered Generator");
        Connect(doc, coal, "Coal", gen);
        Connect(doc, water, "Water", gen);

        var result = Solve(doc);
        Assert.Equal(new Rational(2), result.For(gen).Count);   // coal-limited to 2
        Assert.Equal(new Rational(90), result.For(gen).Inputs["Water"].Target); // 2 × 45 water
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

    // ------------------------------------------------------------ Auto-Round

    /// <summary>165 ore/min into 30/min-per-machine smelters → exactly 5.5 machines.</summary>
    private static (FactoryDocument Doc, FactoryNode Miner, FactoryNode Smelter) HalfMachineFixture(bool autoRound)
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "165");
        var smelter = Node(doc, "Iron Ingot");
        smelter.AutoRound = autoRound;
        Connect(doc, miner, "Iron Ore", smelter);
        return (doc, miner, smelter);
    }

    [Fact]
    public void Auto_round_rounds_count_up_and_rebalances_clock()
    {
        var (doc, _, smelter) = HalfMachineFixture(autoRound: true);
        var result = Solve(doc);

        var node = result.For(smelter);
        Assert.Equal(new Rational(6), node.Count);
        Assert.Equal(new Rational(6), node.DisplayValue);
        Assert.Equal(new Rational(11, 12), node.EffectiveClock); // 5.5/6, exactly
        Assert.True(node.IsRounded);
    }

    [Fact]
    public void Auto_round_off_keeps_fractional_count()
    {
        var (doc, _, smelter) = HalfMachineFixture(autoRound: false);
        var result = Solve(doc);

        var node = result.For(smelter);
        Assert.Equal(new Rational(11, 2), node.Count);
        Assert.Equal(new Rational(11, 2), node.DisplayValue);
        Assert.Null(node.EffectiveClock);
        Assert.False(node.IsRounded);
    }

    [Fact]
    public void Auto_round_never_changes_port_flows()
    {
        var (docOff, minerOff, smelterOff) = HalfMachineFixture(autoRound: false);
        var (docOn, minerOn, smelterOn) = HalfMachineFixture(autoRound: true);
        var off = Solve(docOff);
        var on = Solve(docOn);

        Assert.Equal(off.For(smelterOff).Inputs["Iron Ore"].Target, on.For(smelterOn).Inputs["Iron Ore"].Target);
        Assert.Equal(off.For(smelterOff).Inputs["Iron Ore"].Connected, on.For(smelterOn).Inputs["Iron Ore"].Connected);
        Assert.Equal(off.For(smelterOff).Outputs["Iron Ingot"].Target, on.For(smelterOn).Outputs["Iron Ingot"].Target);
        Assert.Equal(new Rational(165), on.For(smelterOn).Inputs["Iron Ore"].Target); // 5.5 machines' worth
        Assert.Equal(off.For(minerOff).DisplayValue, on.For(minerOn).DisplayValue);
        Assert.Equal(
            off.Flows[docOff.Root.Connections.Single()],
            on.Flows[docOn.Root.Connections.Single()]);
    }

    [Fact]
    public void Auto_round_power_uses_the_effective_clock()
    {
        var (doc, _, smelter) = HalfMachineFixture(autoRound: true);
        var result = Solve(doc);

        // Smelter: −4 MW at 100%, exponent 1.321929; rounded to micro-MW like the
        // solver's power path, then × 6 machines at the rebalanced 11/12 clock.
        var clockFactor = new Rational(11, 12).Pow(new Rational(1321929, 1000000));
        var perMachine = new Rational((long)Math.Round(-4.0 * clockFactor * 1_000_000.0), 1_000_000);
        Assert.Equal(new Rational(6) * perMachine, result.For(smelter).Power);
    }

    [Fact]
    public void Auto_round_keeps_ppm_display_throughput_based()
    {
        var (doc, _, smelter) = HalfMachineFixture(autoRound: true);
        smelter.ShowPpm = true;
        var result = Solve(doc);

        var node = result.For(smelter);
        Assert.True(node.IsPpmDisplay);
        Assert.Equal(new Rational(165), node.DisplayValue); // 5.5 machines × 30/min, unrounded
        Assert.Equal(new Rational(6), node.Count);          // popup still gets the whole count
        Assert.Equal(new Rational(11, 12), node.EffectiveClock);
    }

    [Fact]
    public void Auto_round_stepped_clock_round_trips_through_serializer()
    {
        // Stepping 5.5 machine-equivalents to 5 machines stores clock W/N' = 11/10.
        var (doc, _, smelter) = HalfMachineFixture(autoRound: true);
        smelter.ClockSpeed = new Rational(11, 10);

        var restored = SfmdSerializer.Deserialize(SfmdSerializer.Serialize(doc));
        var restoredSmelter = restored.Root.Nodes.Single(n => n.Name == "Iron Ingot");
        Assert.True(restoredSmelter.AutoRound);
        Assert.Equal(new Rational(11, 10), restoredSmelter.ClockSpeed);

        var result = Solve(restored);
        var node = result.For(restoredSmelter);
        Assert.Equal(new Rational(5), node.Count);              // ceil(W / (W/5)) == 5: stable
        Assert.Equal(new Rational(11, 10), node.EffectiveClock); // exactly the stored clock
        Assert.Equal(new Rational(165), node.Inputs["Iron Ore"].Target);
    }

    [Fact]
    public void Stepping_a_limited_auto_round_node_moves_count_not_throughput()
    {
        // Bug #15: a count-display Max pins the count independent of clock, so the old
        // stepper (clock only) changed the output instead of the machine count. The fix
        // moves the limit with the clock so the count steps and throughput is preserved.
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "300");   // ample ore, so the smelter's Max binds
        var smelter = Node(doc, "Iron Ingot", "6");  // count limit = 6 machines
        smelter.AutoRound = true;
        Connect(doc, miner, "Iron Ore", smelter);

        var editor = new FactoryEditor(TestData.Database);
        editor.LoadDocument(doc);

        var before = editor.Result.For(smelter);
        Assert.Equal(new Rational(6), before.Count);
        var outputBefore = before.Outputs["Iron Ingot"].Target;
        var oreBefore = before.Inputs["Iron Ore"].Target;

        editor.StepAutoRound(smelter, +1); // one more machine

        var after = editor.Result.For(smelter);
        Assert.Equal(new Rational(7), after.Count);                      // count moved 6 → 7
        Assert.Equal(outputBefore, after.Outputs["Iron Ingot"].Target); // throughput unchanged
        Assert.Equal(oreBefore, after.Inputs["Iron Ore"].Target);
    }

    [Fact]
    public void Moving_a_node_refreshes_without_resolving_but_edits_resolve()
    {
        var doc = new FactoryDocument();
        var miner = Node(doc, "Iron Ore", "60");
        var smelter = Node(doc, "Iron Ingot");
        Connect(doc, miner, "Iron Ore", smelter);

        var editor = new FactoryEditor(TestData.Database);
        editor.LoadDocument(doc);

        var solves = 0;
        var geometryChanges = 0;
        editor.Solved += () => solves++;
        editor.GeometryChanged += () => geometryChanges++;

        editor.MoveNodes([smelter], 50, 50, coalesce: false);
        Assert.Equal(0, solves);          // a pure position change must not re-solve
        Assert.Equal(1, geometryChanges); // but the view is told to refresh

        editor.SetClockSpeed(smelter, new Rational(3, 2));
        Assert.Equal(1, solves);          // a real edit still re-solves
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
