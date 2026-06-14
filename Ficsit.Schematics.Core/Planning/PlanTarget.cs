using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>One requested output: a part and, in target mode, its rate per minute.
/// In maximize mode the rate acts as the bundle ratio (e.g. 1 of each phase part).</summary>
public sealed record PlanTarget(string Part, Rational Rate);
