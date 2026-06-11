using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

public sealed class MachineDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Unlock tier, e.g. "0-2" (tier 0, milestone 2).</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>MW at 100% clock; negative = consumes, positive = generates. Null = no power (e.g. storage).</summary>
    public string? AveragePower { get; set; }

    /// <summary>Lower bound for variable-power machines (e.g. Particle Accelerator).</summary>
    public string? MinPower { get; set; }

    /// <summary>Constant draw independent of the recipe (e.g. HUB terminal).</summary>
    public string? BasePower { get; set; }

    /// <summary>Power boost factors for somerslooped generators.</summary>
    public string? BasePowerBoost { get; set; }
    public string? FueledBasePowerBoost { get; set; }

    /// <summary>Exponent x in power = base · clock^x (e.g. 1321929/1000000 ≈ 1.32).</summary>
    public string? OverclockPowerExponent { get; set; }

    /// <summary>Somersloop slots.</summary>
    public int MaxProductionShards { get; set; }

    /// <summary>Output boost per fully somerslooped machine (1 = +100%).</summary>
    public string? ProductionShardMultiplier { get; set; }

    /// <summary>Exponent for the power penalty of somersloop boosting (typically 2).</summary>
    public string? ProductionShardPowerExponent { get; set; }

    public List<CostEntry> Cost { get; set; } = [];

    public Rational AveragePowerValue => GameDatabase.ParseOrZero(AveragePower);

    public Rational OverclockPowerExponentValue =>
        OverclockPowerExponent is null ? Rational.One : Rational.Parse(OverclockPowerExponent);

    public Rational ProductionShardMultiplierValue => GameDatabase.ParseOrZero(ProductionShardMultiplier);

    public Rational ProductionShardPowerExponentValue =>
        ProductionShardPowerExponent is null ? Rational.One : Rational.Parse(ProductionShardPowerExponent);
}
