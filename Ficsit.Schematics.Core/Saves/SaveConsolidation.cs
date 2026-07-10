using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// Collapses parallel machines into one node with a count. Two machines merge when a pioneer
/// would call them "the same machines side by side": same recipe identity (incl. clock and
/// sloops), within <see cref="AdjacentRadius"/> of each other, and the same wiring shape — the
/// set of (direction, part, coarse neighbour type) they touch. That covers a shared manifold,
/// a load-balancer, N parallel dedicated lines (pump₁→packager₁→refinery₁ … collapses into
/// "Packager ×N → Refinery ×N"), and unwired banks alike. The shape is deliberately one hop
/// and clock-blind about the neighbours: real factories carry small quirks (a stray machine at
/// 100%, a bank with one pump instead of two) that must not keep two rows apart — while a
/// machine with an extra tap or a different part flowing still stays separate. Snapped
/// extractors sit on distinct map nodes and are never merged themselves. Pure: takes the
/// imported nodes + connections and returns the consolidated graph.
/// </summary>
public static class SaveConsolidation
{
    /// <summary>Machines merge only within this distance (canvas metres, chained). Sized for
    /// banks of the big buildings (fuel generators, refineries: neighbouring banks sit 40–50 m
    /// apart) while staying under the outpost-clustering radius (80 m) — a genuinely separate
    /// site is hundreds of metres away.</summary>
    private const double AdjacentRadius = 60;

    public static (IReadOnlyList<FactoryNode> Nodes, IReadOnlyList<NodeConnection> Connections) Consolidate(
        IReadOnlyList<FactoryNode> nodes, IReadOnlyList<NodeConnection> connections)
    {
        // What a machine is, exactly (merge candidates must match all of it) …
        string Identity(FactoryNode n)
            => $"{n.Kind}|{n.Name}|{n.MachineVariant}|{n.Capacity}|{n.ClockSpeed}|{n.Somersloops}";
        // … and what a machine looks like as someone else's neighbour (coarse: no clock/sloops,
        // so a quirky supplier doesn't split an otherwise identical row).
        string Coarse(FactoryNode n) => $"{n.Kind}|{n.Name}|{n.MachineVariant}|{n.Capacity}";

        var shapes = nodes.ToDictionary(n => n, _ => new SortedSet<string>(StringComparer.Ordinal));
        foreach (var c in connections)
        {
            if (shapes.TryGetValue(c.From, out var fs) && shapes.TryGetValue(c.To, out var ts))
            {
                fs.Add($">{c.Part}:{Coarse(c.To)}");
                ts.Add($"<{c.Part}:{Coarse(c.From)}");
            }
        }

        var representative = new Dictionary<FactoryNode, FactoryNode>();
        var result = new List<FactoryNode>();
        foreach (var group in nodes.GroupBy(n =>
                     // Snapped extractors are physical machines on distinct map nodes — never merged.
                     n.ResourceNodeId is not null ? $"#{n.Id}" : $"{Identity(n)}|{string.Join(",", shapes[n])}"))
        {
            var members = group.ToList();
            if (members.Count == 1 || members[0].ResourceNodeId is not null)
            {
                foreach (var n in members) { representative[n] = n; result.Add(n); }
                continue;
            }

            // Same identity + same wiring shape: merge per side-by-side row, never across the map.
            foreach (var cluster in ProximityClusters(members, AdjacentRadius))
            {
                if (cluster.Count == 1) { representative[cluster[0]] = cluster[0]; result.Add(cluster[0]); }
                else result.Add(Merge(cluster, representative));
            }
        }

        var mergedConnections = new List<NodeConnection>();
        var seen = new HashSet<(int, int, string)>();
        foreach (var c in connections)
        {
            var from = representative[c.From];
            var to = representative[c.To];
            if (from == to) continue; // collapsed within one group
            if (seen.Add((from.Id, to.Id, c.Part)))
                mergedConnections.Add(new NodeConnection
                    { From = from, To = to, Part = c.Part, Logistics = c.Logistics });
        }

        return (result, mergedConnections);
    }

    private static FactoryNode Merge(List<FactoryNode> members, Dictionary<FactoryNode, FactoryNode> representative)
    {
        var first = members[0];
        var merged = new FactoryNode
        {
            Name = first.Name,
            Kind = first.Kind,
            MachineVariant = first.MachineVariant,
            Capacity = first.Capacity,
            ClockSpeed = first.ClockSpeed,
            Somersloops = first.Somersloops,
            X = members.Average(n => n.X),
            Y = members.Average(n => n.Y),
            Max = members.Count.ToString(), // N physical machines of this recipe
        };
        foreach (var n in members) representative[n] = merged;
        return merged;
    }

    /// <summary>Connected components of the proximity graph (chained: a row of machines each
    /// ~10 m apart is one cluster). Groups are small, so the O(n²) union-find is fine.</summary>
    private static List<List<FactoryNode>> ProximityClusters(List<FactoryNode> nodes, double radius)
    {
        var parent = Enumerable.Range(0, nodes.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

        var r2 = radius * radius;
        for (var i = 0; i < nodes.Count; i++)
            for (var j = i + 1; j < nodes.Count; j++)
            {
                var dx = nodes[i].X - nodes[j].X;
                var dy = nodes[i].Y - nodes[j].Y;
                if (dx * dx + dy * dy > r2) continue;
                var ri = Find(i);
                var rj = Find(j);
                if (ri != rj) parent[ri] = rj;
            }

        var byRoot = new Dictionary<int, List<FactoryNode>>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var root = Find(i);
            if (!byRoot.TryGetValue(root, out var list)) byRoot[root] = list = [];
            list.Add(nodes[i]);
        }
        return byRoot.Values.ToList();
    }
}
