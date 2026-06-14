using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Solver;

public sealed class SolveResult
{
    public Dictionary<int, NodeResult> Nodes { get; } = [];
    public Dictionary<NodeConnection, Rational> Flows { get; } = [];

    public NodeResult For(FactoryNode node)
        => Nodes.TryGetValue(node.Id, out var result) ? result : new NodeResult();

    public Rational FlowOf(NodeConnection connection)
        => Flows.TryGetValue(connection, out var flow) ? flow : Rational.Zero;
}
