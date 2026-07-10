using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData.Catalog;

/// <summary>
/// The export-derived numbers of one machine: unlock tier, power figures, overclock
/// exponent, somersloop slots and build cost. The generated <c>MachineStats</c> table
/// holds one per machine name; the hand-authored machine groups pull from it so the
/// catalog generator never needs to touch their structure (families, marks, capacities).
/// </summary>
public sealed record MachineStat(
    Tier Tier,
    Rational? Power = null,
    Rational? MinPower = null,
    Rational? BasePower = null,
    Rational? BasePowerBoost = null,
    Rational? FueledBasePowerBoost = null,
    Rational? OverclockExp = null,
    int Sloops = 0,
    Rational? SloopMultiplier = null,
    Rational? SloopPowerExp = null,
    CostEntry[]? Cost = null,
    Rational? Throughput = null);
