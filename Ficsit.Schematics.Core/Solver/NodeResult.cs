using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Solver;

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
