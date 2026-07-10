using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

/// <summary>
/// Somersloop production-amplification factors, shared by the live solver
/// (<c>NodeProfile</c>) and the auto-planner (<c>FactoryPlanner</c>) so the two never
/// disagree on what slooping a machine does. Output scales linearly with the filled
/// slot fraction; power scales by the same fraction raised to the machine's power
/// exponent (≈ quadratic) — the game's asymmetric trade.
/// </summary>
public static class Amplification
{
    /// <summary>
    /// Output multiplier for running <paramref name="sloops"/> Somersloops in one machine:
    /// <c>1 + (sloops / slots) · multiplier</c>. Returns 1 when the machine has no slots
    /// or no sloops are used.
    /// </summary>
    public static Rational OutputFactor(MachineDefinition machine, int sloops)
        => sloops > 0 && machine.MaxProductionShards > 0
            ? Rational.One + new Rational(sloops, machine.MaxProductionShards) * machine.ProductionShardMultiplierValue
            : Rational.One;

    /// <summary>
    /// Power multiplier for <paramref name="sloops"/> Somersloops:
    /// <c>((slots + sloops) / slots) ^ exponent</c> (exponent ≈ 2 ⇒ quadratic). Returns 1
    /// when the machine has no slots or no sloops are used. Approximated through doubles
    /// for non-integer exponents, exactly as the live power model does.
    /// </summary>
    public static double PowerFactor(MachineDefinition machine, int sloops)
        => sloops > 0 && machine.MaxProductionShards > 0
            ? new Rational(machine.MaxProductionShards + sloops, machine.MaxProductionShards)
                .Pow(machine.ProductionShardPowerExponentValue)
            : 1.0;
}
