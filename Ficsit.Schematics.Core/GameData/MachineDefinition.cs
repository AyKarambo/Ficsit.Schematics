using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

public sealed class MachineDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Unlock tier, e.g. "0-2" (tier 0, milestone 2).</summary>
    public Tier Tier { get; set; }

    /// <summary>MW at 100% clock; negative = consumes, positive = generates. Null = no power (e.g. storage).</summary>
    public Rational? AveragePower { get; set; }

    /// <summary>Lower bound for variable-power machines (e.g. Particle Accelerator).</summary>
    public Rational? MinPower { get; set; }

    /// <summary>Constant draw independent of the recipe (e.g. HUB terminal).</summary>
    public Rational? BasePower { get; set; }

    /// <summary>Power boost factors for somerslooped generators.</summary>
    public Rational? BasePowerBoost { get; set; }
    public Rational? FueledBasePowerBoost { get; set; }

    /// <summary>Exponent x in power = base · clock^x (e.g. 1321929/1000000 ≈ 1.32).</summary>
    public Rational? OverclockPowerExponent { get; set; }

    /// <summary>Somersloop slots.</summary>
    public int MaxProductionShards { get; set; }

    /// <summary>Output boost per fully somerslooped machine (1 = +100%).</summary>
    public Rational? ProductionShardMultiplier { get; set; }

    /// <summary>Exponent for the power penalty of somersloop boosting (typically 2).</summary>
    public Rational? ProductionShardPowerExponent { get; set; }

    public List<CostEntry> Cost { get; set; } = [];

    public Rational AveragePowerValue => AveragePower ?? Rational.Zero;

    public Rational OverclockPowerExponentValue => OverclockPowerExponent ?? Rational.One;

    public Rational ProductionShardMultiplierValue => ProductionShardMultiplier ?? Rational.Zero;

    public Rational ProductionShardPowerExponentValue => ProductionShardPowerExponent ?? Rational.One;
}
