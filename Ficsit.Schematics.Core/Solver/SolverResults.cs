using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Solver;

// What a solve produces: per-node results, each carrying per-port results, plus
// the connection flows. Looked up by node/connection via SolveResult.

/// <summary>Computed state of one node.</summary>
public sealed class NodeResult
{
    /// <summary>Solved machine count (fractional, exact).</summary>
    public Rational Count { get; set; } = Rational.Zero;

    /// <summary>Value shown on the node: machine count, or ppm for ppm-mode nodes.</summary>
    public Rational DisplayValue { get; set; } = Rational.Zero;

    public bool IsPpmDisplay { get; set; }

    /// <summary>
    /// Auto-Round: per-machine clock rebalanced so <see cref="Count"/> whole machines
    /// produce exactly the solved throughput. Null when the node is not rounded.
    /// </summary>
    public Rational? EffectiveClock { get; set; }

    /// <summary>True when Auto-Round replaced the fractional count with a whole one.</summary>
    public bool IsRounded { get; set; }

    /// <summary>True when the configuration cannot be satisfied (flagged red).</summary>
    public bool IsInvalid { get; set; }

    public Dictionary<string, PortResult> Inputs { get; } = [];
    public Dictionary<string, PortResult> Outputs { get; } = [];

    /// <summary>Signed average MW (negative = consumes).</summary>
    public Rational Power { get; set; } = Rational.Zero;

    /// <summary>Sink points per minute destroyed here (AWESOME Sink only).</summary>
    public Rational SinkPointsPerMinute { get; set; } = Rational.Zero;
}

/// <summary>Computed state of one input or output port (one part on one node).</summary>
public sealed class PortResult
{
    /// <summary>ppm the node wants (inputs) or makes (outputs) at its solved count.</summary>
    public Rational Target { get; set; } = Rational.Zero;

    /// <summary>ppm actually flowing over connections into/out of this port.</summary>
    public Rational Connected { get; set; } = Rational.Zero;

    public bool HasConnections { get; set; }

    /// <summary>Input shortfall ("unmade", flagged) — what must come from outside the model.</summary>
    public Rational Unmade => Rational.Max(Target - Connected, Rational.Zero);

    /// <summary>Output surplus ("unused", flagged).</summary>
    public Rational Unused => Rational.Max(Target - Connected, Rational.Zero);
}

public sealed class SolveResult
{
    public Dictionary<int, NodeResult> Nodes { get; } = [];
    public Dictionary<NodeConnection, Rational> Flows { get; } = [];

    public NodeResult For(FactoryNode node)
        => Nodes.TryGetValue(node.Id, out var result) ? result : new NodeResult();

    public Rational FlowOf(NodeConnection connection)
        => Flows.TryGetValue(connection, out var flow) ? flow : Rational.Zero;
}
