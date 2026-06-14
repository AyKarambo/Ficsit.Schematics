namespace Ficsit.Schematics.Core.Model;

/// <summary>
/// Partitions a set of nodes into "sub-factories" for collapsing into outposts, one
/// per <em>key intermediate</em>. A key intermediate is a produced part consumed by
/// two or more recipes (a shared building block — e.g. Iron Ingot feeding plates,
/// rods and screws). Each node is assigned to the single key intermediate it
/// exclusively serves downstream; nodes feeding several (the shared trunk), the final
/// products, and sinks stay ungrouped. Pure and UI-free.
/// </summary>
public static class FactoryAutoGroup
{
    public static List<(string Part, List<FactoryNode> Nodes)> KeyIntermediateGroups(
        IReadOnlyList<FactoryNode> nodes, FactoryGraph graph)
    {
        var set = new HashSet<FactoryNode>(nodes);
        var outParts = new Dictionary<FactoryNode, HashSet<string>>();
        var consumers = new Dictionary<FactoryNode, List<FactoryNode>>();
        var consumersByPart = new Dictionary<string, HashSet<FactoryNode>>();
        foreach (var node in nodes) { outParts[node] = []; consumers[node] = []; }
        foreach (var c in graph.Connections)
        {
            if (c.From == c.To || !set.Contains(c.From) || !set.Contains(c.To)) continue;
            outParts[c.From].Add(c.Part);
            consumers[c.From].Add(c.To);
            if (!consumersByPart.TryGetValue(c.Part, out var cs)) consumersByPart[c.Part] = cs = [];
            cs.Add(c.To);
        }

        var keyParts = new HashSet<string>();
        foreach (var (part, cs) in consumersByPart)
            if (cs.Count >= 2) keyParts.Add(part);
        if (keyParts.Count == 0) return [];

        // owner[n] = the lone key intermediate n exclusively serves downstream, else null.
        var owner = new Dictionary<FactoryNode, string?>();
        foreach (var node in nodes)
        {
            var reached = ReachKeyParts(node, outParts, consumers, keyParts);
            owner[node] = reached.Count == 1 ? reached.First() : null;
        }

        var groups = new List<(string Part, List<FactoryNode> Nodes)>();
        foreach (var key in keyParts.OrderBy(k => k))
        {
            var members = nodes.Where(n => owner[n] == key).ToList();
            if (members.Count >= 2) groups.Add((key, members));
        }
        return groups;
    }

    /// <summary>The key parts first reached going downstream, stopping at the recipe that
    /// produces each (so a chain dedicated to one key part resolves to exactly that part).</summary>
    private static HashSet<string> ReachKeyParts(
        FactoryNode start,
        Dictionary<FactoryNode, HashSet<string>> outParts,
        Dictionary<FactoryNode, List<FactoryNode>> consumers,
        HashSet<string> keyParts)
    {
        var result = new HashSet<string>();
        var seen = new HashSet<FactoryNode> { start };
        var queue = new Queue<FactoryNode>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            var producesKey = false;
            foreach (var part in outParts[node])
                if (keyParts.Contains(part)) { result.Add(part); producesKey = true; }
            if (producesKey) continue; // a key producer caps the walk (including start itself)
            foreach (var consumer in consumers[node])
                if (seen.Add(consumer)) queue.Enqueue(consumer);
        }
        return result;
    }
}
