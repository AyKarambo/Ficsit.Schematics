using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// Collapses parallel machines into one node with a count. Machines that run the same recipe and
/// are wired to the same set of upstream and downstream machines are a manifold / load-balanced
/// group (10 constructors all fed from the same source and feeding the same sink) — the user wants
/// those shown as a single "Constructor ×10" node, regardless of the belt topology between them.
/// Machines with no wiring at all (manual-fed banks, power-only machines) merge per adjacent row:
/// same identity within <see cref="AdjacentRadius"/>, never across the map.
/// Pure: takes the imported nodes + connections and returns the consolidated graph.
/// </summary>
public static class SaveConsolidation
{
    public static (IReadOnlyList<FactoryNode> Nodes, IReadOnlyList<NodeConnection> Connections) Consolidate(
        IReadOnlyList<FactoryNode> nodes, IReadOnlyList<NodeConnection> connections)
    {
        var sources = nodes.ToDictionary(n => n, _ => new HashSet<FactoryNode>());
        var sinks = nodes.ToDictionary(n => n, _ => new HashSet<FactoryNode>());
        foreach (var c in connections)
        {
            if (sinks.TryGetValue(c.From, out var fs)) fs.Add(c.To);
            if (sources.TryGetValue(c.To, out var ts)) ts.Add(c.From);
        }

        // Group key: recipe identity (incl. clock and sloops — a machine at 50% is not the same
        // as one at 100%) + the exact set of neighbour machines on each side. Distinct manifolds
        // making the same thing have different neighbours, so they don't merge.
        string Key(FactoryNode n)
        {
            var src = string.Join(",", sources[n].Select(s => s.Id).OrderBy(x => x));
            var snk = string.Join(",", sinks[n].Select(s => s.Id).OrderBy(x => x));
            return $"{n.Kind}|{n.Name}|{n.MachineVariant}|{n.Capacity}|{n.ClockSpeed}|{n.Somersloops}|S:{src}|T:{snk}";
        }

        var representative = new Dictionary<FactoryNode, FactoryNode>();
        var result = new List<FactoryNode>();
        foreach (var group in nodes.GroupBy(n =>
                     // Snapped extractors are physical machines on distinct nodes — never merged.
                     n.ResourceNodeId is not null ? $"#{n.Id}" : Key(n)))
        {
            var members = group.ToList();
            var first = members[0];
            if (members.Count == 1 || first.ResourceNodeId is not null)
            {
                foreach (var n in members) { representative[n] = n; result.Add(n); }
                continue;
            }

            if (sources[first].Count == 0 && sinks[first].Count == 0)
            {
                // No wiring at all (manual-fed banks, power-only machines): merge per
                // side-by-side row — same identity within arm's reach — never across the map.
                foreach (var cluster in ProximityClusters(members, AdjacentRadius))
                {
                    if (cluster.Count == 1) { representative[cluster[0]] = cluster[0]; result.Add(cluster[0]); }
                    else result.Add(Merge(cluster, representative));
                }
                continue;
            }

            result.Add(Merge(members, representative));
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

    /// <summary>Isolated machines merge only within this distance (canvas metres): a machine row
    /// is ~10 m spacing, a genuinely separate site is hundreds.</summary>
    private const double AdjacentRadius = 40;

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
