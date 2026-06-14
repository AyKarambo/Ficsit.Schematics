using Ficsit.Schematics.Core.GameData;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// Computes the Sankey flow graph for a solved <see cref="PlanResult"/>.
/// Produces nodes (raw sources, recipe nodes, target/sink terminals) and
/// directed links (part flows in parts/min) suitable for a Sankey renderer.
/// </summary>
public sealed class PlanFlows
{
    // ------------------------------------------------------------------ types

    public enum NodeKind { Raw, Recipe, Target, Sink }

    public sealed record FlowNode(string Id, NodeKind Kind, string Label, int Column);

    /// <summary>
    /// A directed flow link. <c>FromId</c> and <c>ToId</c> always reference a
    /// node in <see cref="PlanFlows.Nodes"/> by its <c>Id</c>.
    /// Raw-source links originate from a "raw:&lt;part&gt;" node;
    /// terminal links flow into a "target:&lt;part&gt;" or "sink:&lt;part&gt;" node.
    /// </summary>
    public sealed record FlowLink(string FromId, string ToId, string Part, Rational Ppm);

    // ---------------------------------------------------------------- factory

    public IReadOnlyList<FlowNode> Nodes { get; }
    public IReadOnlyList<FlowLink> Links { get; }

    private PlanFlows(List<FlowNode> nodes, List<FlowLink> links)
    {
        Nodes = nodes;
        Links = links;
    }

    /// <summary>Build a Sankey graph from a solved plan and the game database.</summary>
    public static PlanFlows From(PlanResult plan, GameDatabase data)
    {
        if (plan.Recipes.Count == 0)
            return new PlanFlows([], []);

        // ----------------------------------------------------------------
        // 1. Gather every recipe definition referenced by the plan.
        // ----------------------------------------------------------------
        var recipeDefs = plan.Recipes
            .Where(r => data.RecipesByName.ContainsKey(r.Recipe))
            .Select(r => (Planned: r, Def: data.RecipesByName[r.Recipe]))
            .ToList();

        // ----------------------------------------------------------------
        // 2. Build per-part supply/demand tables.
        //    producerOut[recipeName][part]  = total ppm produced by that recipe
        //    consumerIn[recipeName][part]   = total ppm consumed by that recipe
        // ----------------------------------------------------------------
        var producerOut = new Dictionary<string, Dictionary<string, Rational>>();
        var consumerIn  = new Dictionary<string, Dictionary<string, Rational>>();

        foreach (var (planned, def) in recipeDefs)
        {
            var machineCount = planned.Machines;
            var po = new Dictionary<string, Rational>();
            var ci = new Dictionary<string, Rational>();

            foreach (var rp in def.Parts)
            {
                var ratePerMachine = def.RatePerMinute(rp.Part);
                var totalRate = ratePerMachine * machineCount;
                if (totalRate.IsPositive) po[rp.Part] = totalRate;
                else if (totalRate.IsNegative) ci[rp.Part] = -totalRate; // store as positive
            }

            producerOut[def.Name] = po;
            consumerIn[def.Name]  = ci;
        }

        // ----------------------------------------------------------------
        // 3. Collect all parts that flow through the plan.
        // ----------------------------------------------------------------
        var allParts = new HashSet<string>(
            recipeDefs.SelectMany(t => t.Def.Parts.Select(p => p.Part)));

        // Also include any supplied parts not appearing in recipe parts
        // (edge case: supply of a part that's only a terminal input).
        foreach (var part in plan.Supplies.Keys) allParts.Add(part);

        // ----------------------------------------------------------------
        // 4. Compute links per part.
        //    For each part:
        //      producers = recipe nodes with positive output + "raw:<part>" supply node
        //      consumers = recipe nodes with positive input + "target:<part>" / "sink:<part>"
        //
        //    Distribute each producer's output proportionally across consumers
        //    (by their fraction of totalConsumed).
        // ----------------------------------------------------------------
        var links = new List<FlowLink>();

        var rawNodeIds    = new HashSet<string>();
        var sinkNodeIds   = new HashSet<string>();
        var targetNodeIds = new HashSet<string>();

        foreach (var part in allParts)
        {
            // Skip the virtual power part in link computation (handled below).
            if (part == FactoryPlanner.PowerPart) continue;

            // ----- Producer side -----
            var producers = new List<(string NodeId, Rational TotalOut)>();

            foreach (var (_, def) in recipeDefs)
                if (producerOut[def.Name].TryGetValue(part, out var ppm))
                    producers.Add((def.Name, ppm));

            if (plan.Supplies.TryGetValue(part, out var supplyPpm) && supplyPpm.IsPositive)
            {
                var rawId = "raw:" + part;
                rawNodeIds.Add(rawId);
                producers.Add((rawId, supplyPpm));
            }

            if (producers.Count == 0) continue;

            // ----- Consumer side -----
            var consumers = new List<(string NodeId, Rational TotalIn)>();

            foreach (var (_, def) in recipeDefs)
                if (consumerIn[def.Name].TryGetValue(part, out var ppm))
                    consumers.Add((def.Name, ppm));

            if (plan.Sinks.ContainsKey(part))
            {
                var sinkId = "sink:" + part;
                sinkNodeIds.Add(sinkId);
                // Sinked amount = total produced - total internally consumed.
                var totalProduced = producers.Aggregate(Rational.Zero, (a, p) => a + p.TotalOut);
                var totalInternallyConsumed = consumers.Aggregate(Rational.Zero, (a, c) => a + c.TotalIn);
                var sinkAmount = totalProduced - totalInternallyConsumed;
                if (sinkAmount.IsPositive)
                    consumers.Add((sinkId, sinkAmount));
            }

            if (plan.Outputs.ContainsKey(part))
            {
                var targetId = "target:" + part;
                targetNodeIds.Add(targetId);
                var totalProduced = producers.Aggregate(Rational.Zero, (a, p) => a + p.TotalOut);
                var totalInternallyConsumed = consumers.Aggregate(Rational.Zero, (a, c) => a + c.TotalIn);
                var targetAmount = totalProduced - totalInternallyConsumed;
                if (targetAmount.IsPositive)
                    consumers.Add((targetId, targetAmount));
            }

            if (consumers.Count == 0) continue;

            // Distribute each producer's output proportionally to consumers.
            var totalConsumedAll = consumers.Aggregate(Rational.Zero, (a, c) => a + c.TotalIn);
            if (!totalConsumedAll.IsPositive) continue;

            foreach (var (prodId, prodOut) in producers)
            {
                foreach (var (consId, consIn) in consumers)
                {
                    var linkPpm = prodOut * consIn / totalConsumedAll;
                    if (!linkPpm.IsPositive) continue;
                    links.Add(new FlowLink(prodId, consId, part, linkPpm));
                }
            }
        }

        // Power links: generator recipes produce PowerPart, flow to a "Power" target node.
        if (plan.PowerGeneratedMW.IsPositive)
        {
            var powerTarget = "target:" + FactoryPlanner.PowerPart;
            targetNodeIds.Add(powerTarget);
            foreach (var (_, def) in recipeDefs)
            {
                if (producerOut[def.Name].TryGetValue(FactoryPlanner.PowerPart, out var ppm) && ppm.IsPositive)
                    links.Add(new FlowLink(def.Name, powerTarget, FactoryPlanner.PowerPart, ppm));
            }
        }

        // ----------------------------------------------------------------
        // 5. Assign columns (dependency depth) via bounded longest-path.
        //    Column 0 = raw sources.  Recipes are layered by depth from raw.
        //    Terminals occupy the last column.
        // ----------------------------------------------------------------
        var recipeIds = recipeDefs.Select(t => t.Def.Name).ToHashSet();

        var outAdj = new Dictionary<string, HashSet<string>>();
        foreach (var id in recipeIds) outAdj[id] = [];

        foreach (var link in links)
        {
            // Only recipe→recipe edges contribute to the depth calculation.
            if (!recipeIds.Contains(link.FromId) || !recipeIds.Contains(link.ToId)) continue;
            outAdj[link.FromId].Add(link.ToId);
        }

        // Longest-path depth from raw sources (bounded passes to handle cycles).
        var depth = new Dictionary<string, int>();
        foreach (var id in recipeIds) depth[id] = 0;

        var n = recipeIds.Count;
        for (var pass = 0; pass < n; pass++)
        {
            var changed = false;
            foreach (var id in recipeIds)
                foreach (var succ in outAdj[id])
                    if (depth[id] + 1 > depth[succ])
                    {
                        depth[succ] = depth[id] + 1;
                        changed = true;
                    }
            if (!changed) break;
        }

        var maxDepth = depth.Count > 0 ? depth.Values.Max() : 0;
        var terminalColumn = maxDepth + 2;

        // ----------------------------------------------------------------
        // 6. Build node list.
        // ----------------------------------------------------------------
        var nodes = new List<FlowNode>();

        foreach (var rawId in rawNodeIds.OrderBy(x => x))
        {
            var part = rawId["raw:".Length..];
            nodes.Add(new FlowNode(rawId, NodeKind.Raw, part, 0));
        }

        foreach (var (_, def) in recipeDefs)
        {
            var col = depth.TryGetValue(def.Name, out var d) ? d + 1 : 1;
            nodes.Add(new FlowNode(def.Name, NodeKind.Recipe, def.Name, col));
        }

        foreach (var targetId in targetNodeIds.OrderBy(x => x))
        {
            var part = targetId["target:".Length..];
            var label = part == FactoryPlanner.PowerPart ? "Power (MW)" : part;
            nodes.Add(new FlowNode(targetId, NodeKind.Target, label, terminalColumn));
        }

        foreach (var sinkId in sinkNodeIds.OrderBy(x => x))
        {
            var part = sinkId["sink:".Length..];
            nodes.Add(new FlowNode(sinkId, NodeKind.Sink, "Sink: " + part, terminalColumn));
        }

        return new PlanFlows(nodes, links);
    }
}
