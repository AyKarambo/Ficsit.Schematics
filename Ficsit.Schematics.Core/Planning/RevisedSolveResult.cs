using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>Outcome of a revised-simplex solve, including the warm-start snapshot.</summary>
public sealed class RevisedSolveResult
{
    public required PlanStatus Status { get; init; }
    public Rational[] Values { get; init; } = [];
    public Rational Objective { get; init; } = Rational.Zero;

    /// <summary>Optimal basis for warm-starting follow-up solves (Optimal only).</summary>
    public RevisedBasisSnapshot? Snapshot { get; init; }
}
