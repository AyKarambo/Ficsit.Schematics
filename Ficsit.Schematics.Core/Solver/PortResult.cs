using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Solver;

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
