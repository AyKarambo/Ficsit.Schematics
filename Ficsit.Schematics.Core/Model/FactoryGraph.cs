namespace Ficsit.Schematics.Core.Model;

/// <summary>One canvas scope: the root factory or the inside of an outpost/blueprint.</summary>
public sealed class FactoryGraph
{
    public List<FactoryNode> Nodes { get; } = [];
    public List<NodeConnection> Connections { get; } = [];

    public IEnumerable<NodeConnection> IncomingTo(FactoryNode node, string? part = null)
        => Connections.Where(c => c.To == node && (part is null || c.Part == part));

    public IEnumerable<NodeConnection> OutgoingFrom(FactoryNode node, string? part = null)
        => Connections.Where(c => c.From == node && (part is null || c.Part == part));

    public void RemoveNode(FactoryNode node)
    {
        Nodes.Remove(node);
        Connections.RemoveAll(c => c.From == node || c.To == node);
    }

    /// <summary>All nodes (the model is flat — outpost membership is via
    /// <see cref="FactoryNode.Parent"/>, not nesting).</summary>
    public IEnumerable<FactoryNode> AllNodes() => Nodes;

    /// <summary>All connections.</summary>
    public IEnumerable<NodeConnection> AllConnections() => Connections;

    /// <summary>Direct members of an outpost (or root-level nodes when <paramref name="outpost"/> is null).</summary>
    public IEnumerable<FactoryNode> MembersOf(FactoryNode? outpost)
        => Nodes.Where(n => n.Parent == outpost);
}
