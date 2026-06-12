using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// An input the user is willing to provide, capped at a rate per minute.
/// <para>
/// <see cref="Exclusive"/> = true means "this is all there is": the planner
/// must not build extra production for the part, and in target mode the whole
/// output scales down if the supply bottlenecks the chain. False means the
/// supply is a free head start and the planner produces whatever more it needs.
/// </para>
/// </summary>
public sealed record PlanProvision(string Part, Rational MaxPerMinute, bool Exclusive = false);
