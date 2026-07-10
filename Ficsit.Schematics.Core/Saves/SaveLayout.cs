using Ficsit.Schematics.Core.Model;

namespace Ficsit.Schematics.Core.Saves;

/// <summary>
/// Tidies an imported factory so it reads as a schematic: world positions are the right anchor
/// for outposts on the map, but inside an outpost machines sit game-metres apart — as canvas
/// nodes they pile into an unreadable heap. Runs the layered auto-layout over each outpost's
/// members (producers left of what they feed), anchored at the outpost's world position, so the
/// map view is unchanged. Snapped extractors are pinned to their resource nodes and machines
/// outside any outpost keep their world spots. Pure: mutates only member X/Y.
/// </summary>
public static class SaveLayout
{
    /// <summary>
    /// Lay out the members of each outpost in <paramref name="outposts"/>. Only connections
    /// between two members of the same outpost shape its layout; extractors (nodes with a
    /// <see cref="FactoryNode.ResourceNodeId"/>) never move.
    /// </summary>
    public static void ArrangeOutposts(
        IReadOnlyList<FactoryNode> nodes,
        IReadOnlyList<NodeConnection> connections,
        IReadOnlyList<FactoryNode> outposts)
    {
        var graph = new FactoryGraph();
        graph.Connections.AddRange(connections);

        foreach (var outpost in outposts)
        {
            var members = nodes.Where(n => n.Parent == outpost && n.ResourceNodeId is null).ToList();
            if (members.Count < 2) continue;
            foreach (var (node, p) in FactoryAutoLayout.Arrange(members, graph, outpost.X, outpost.Y))
            {
                node.X = p.X;
                node.Y = p.Y;
            }
        }
    }
}
