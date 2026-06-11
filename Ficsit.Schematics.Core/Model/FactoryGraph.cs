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

    /// <summary>All nodes in this scope and nested outpost scopes, depth-first.</summary>
    public IEnumerable<FactoryNode> AllNodes()
    {
        foreach (var node in Nodes)
        {
            yield return node;
            if (node.Children is null) continue;
            foreach (var child in node.Children.AllNodes())
                yield return child;
        }
    }

    /// <summary>All connections in this scope and nested scopes.</summary>
    public IEnumerable<NodeConnection> AllConnections()
    {
        foreach (var connection in Connections)
            yield return connection;
        foreach (var node in Nodes)
            if (node.Children is not null)
                foreach (var nested in node.Children.AllConnections())
                    yield return nested;
    }
}
