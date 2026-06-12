using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>Outcome of an exact-rational LP solve.</summary>
public sealed class SimplexSolution
{
    public required PlanStatus Status { get; init; }
    public Rational[] Values { get; init; } = [];
    public Rational Objective { get; init; } = Rational.Zero;
}
