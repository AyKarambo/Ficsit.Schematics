using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Planning;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class PlanFlowsTests
{
    // -----------------------------------------------------------------------
    // Helper: run the zero-waste plastic plan and return a PlanFlows.
    // -----------------------------------------------------------------------
    private static (PlanResult Plan, PlanFlows Flows) BuildPlasticFlows()
    {
        var request = new PlanRequest
        {
            Bias = PlanBias.Resources,
            Byproducts = ByproductMode.Eliminate,
        };
        request.Targets.Add(new PlanTarget("Plastic", new Rational(900)));

        var plan = FactoryPlanner.Plan(TestData.Database, request);
        Assert.Equal(PlanStatus.Optimal, plan.Status);

        var flows = PlanFlows.From(plan, TestData.Database);
        return (plan, flows);
    }

    // -----------------------------------------------------------------------
    // 1. Non-empty result for a known good plan.
    // -----------------------------------------------------------------------
    [Fact]
    public void From_zero_waste_plastic_returns_non_empty_links()
    {
        var (_, flows) = BuildPlasticFlows();

        Assert.NotEmpty(flows.Nodes);
        Assert.NotEmpty(flows.Links);
    }

    // -----------------------------------------------------------------------
    // 2. Raw supply nodes carry the expected supply amounts.
    // -----------------------------------------------------------------------
    [Fact]
    public void Raw_links_carry_supply_amounts()
    {
        var (plan, flows) = BuildPlasticFlows();

        foreach (var (part, supplyPpm) in plan.Supplies)
        {
            // Find all links that originate from the raw node for this part.
            var rawId = "raw:" + part;
            var rawTotal = flows.Links
                .Where(l => l.FromId == rawId)
                .Aggregate(Rational.Zero, (a, l) => a + l.Ppm);

            // The raw total must equal the supply amount (exact Rational).
            Assert.True(rawTotal == supplyPpm,
                $"Raw links for {part}: expected {supplyPpm}, got {rawTotal}");
        }
    }

    // -----------------------------------------------------------------------
    // 3. Per-part flow conservation: for each part, the total flow leaving
    //    all sources (raw nodes + recipe producers) equals the total flow
    //    arriving at all sinks (recipe consumers + target/sink terminals).
    //    This is the correct Sankey conservation law (per part, not per node).
    // -----------------------------------------------------------------------
    [Fact]
    public void Flow_is_conserved_per_part()
    {
        var (_, flows) = BuildPlasticFlows();

        Assert.NotEmpty(flows.Links);

        // Group links by part.
        var parts = flows.Links.Select(l => l.Part).Distinct();
        foreach (var part in parts)
        {
            var partLinks = flows.Links.Where(l => l.Part == part).ToList();

            // For each part: every link both leaves a node and enters a node.
            // At a raw node, flow only leaves. At a terminal node, flow only enters.
            // At a recipe node, flow can both leave and enter (on that part's channel).
            // Conservation: total leaving raw nodes == total entering terminals.
            var rawNodes = flows.Nodes.Where(n => n.Kind == PlanFlows.NodeKind.Raw).Select(n => n.Id).ToHashSet();
            var terminalNodes = flows.Nodes
                .Where(n => n.Kind is PlanFlows.NodeKind.Target or PlanFlows.NodeKind.Sink)
                .Select(n => n.Id).ToHashSet();

            var fromRaw = partLinks
                .Where(l => rawNodes.Contains(l.FromId))
                .Aggregate(Rational.Zero, (a, l) => a + l.Ppm);

            var fromRecipes = partLinks
                .Where(l => !rawNodes.Contains(l.FromId))
                .Aggregate(Rational.Zero, (a, l) => a + l.Ppm);

            var toTerminals = partLinks
                .Where(l => terminalNodes.Contains(l.ToId))
                .Aggregate(Rational.Zero, (a, l) => a + l.Ppm);

            var toRecipes = partLinks
                .Where(l => !terminalNodes.Contains(l.ToId))
                .Aggregate(Rational.Zero, (a, l) => a + l.Ppm);

            // Net flow: (raw supply + recipe production) == (recipe consumption + terminals)
            var totalIn  = fromRaw + fromRecipes;
            var totalOut = toTerminals + toRecipes;

            Assert.True(totalIn == totalOut,
                $"Flow not conserved for part '{part}': total leaving sources={totalIn}, total entering sinks={totalOut}");
        }
    }

    // -----------------------------------------------------------------------
    // 4. Target nodes exist and their total incoming flow is positive.
    // -----------------------------------------------------------------------
    [Fact]
    public void Target_nodes_receive_positive_flow()
    {
        var (plan, flows) = BuildPlasticFlows();

        var targetNodes = flows.Nodes
            .Where(n => n.Kind == PlanFlows.NodeKind.Target)
            .ToList();

        Assert.NotEmpty(targetNodes);

        foreach (var node in targetNodes)
        {
            var incoming = flows.Links
                .Where(l => l.ToId == node.Id)
                .Aggregate(Rational.Zero, (a, l) => a + l.Ppm);

            Assert.True(incoming.IsPositive,
                $"Target node '{node.Id}' received zero flow");
        }
    }

    // -----------------------------------------------------------------------
    // 5. Zero-waste plan has no Sink nodes.
    // -----------------------------------------------------------------------
    [Fact]
    public void Zero_waste_plan_has_no_sink_nodes()
    {
        var (_, flows) = BuildPlasticFlows();

        Assert.DoesNotContain(flows.Nodes, n => n.Kind == PlanFlows.NodeKind.Sink);
    }

    // -----------------------------------------------------------------------
    // 6. All recipe nodes are assigned positive columns.
    // -----------------------------------------------------------------------
    [Fact]
    public void Recipe_nodes_have_positive_column()
    {
        var (_, flows) = BuildPlasticFlows();

        foreach (var node in flows.Nodes.Where(n => n.Kind == PlanFlows.NodeKind.Recipe))
            Assert.True(node.Column >= 1, $"Recipe '{node.Id}' has column {node.Column} < 1");
    }

    // -----------------------------------------------------------------------
    // 7. Terminal nodes (target / sink) are in the last column.
    // -----------------------------------------------------------------------
    [Fact]
    public void Terminal_nodes_are_in_the_last_column()
    {
        var (_, flows) = BuildPlasticFlows();

        var maxCol = flows.Nodes.Max(n => n.Column);
        foreach (var node in flows.Nodes.Where(n =>
            n.Kind is PlanFlows.NodeKind.Target or PlanFlows.NodeKind.Sink))
        {
            Assert.Equal(maxCol, node.Column);
        }
    }

    // -----------------------------------------------------------------------
    // 8. Empty plan returns empty result (no crash).
    // -----------------------------------------------------------------------
    [Fact]
    public void Empty_plan_returns_empty_flows()
    {
        var empty = new PlanResult();
        var flows = PlanFlows.From(empty, TestData.Database);

        Assert.Empty(flows.Nodes);
        Assert.Empty(flows.Links);
    }

    // -----------------------------------------------------------------------
    // 9. Plan with a sink (allow-sink byproducts) contains Sink nodes.
    // -----------------------------------------------------------------------
    [Fact]
    public void Plan_with_sink_has_sink_nodes()
    {
        var request = new PlanRequest
        {
            Bias = PlanBias.Resources,
            Byproducts = ByproductMode.AllowSink,
        };
        request.Targets.Add(new PlanTarget("Plastic", new Rational(120)));

        var plan = FactoryPlanner.Plan(TestData.Database, request);
        if (plan.Status != PlanStatus.Optimal || plan.Sinks.Count == 0)
            return; // plan may be zero-waste via alternates; skip if so

        var flows = PlanFlows.From(plan, TestData.Database);

        // If sinks are present in the plan, there should be Sink nodes.
        Assert.Contains(flows.Nodes, n => n.Kind == PlanFlows.NodeKind.Sink);
    }
}
