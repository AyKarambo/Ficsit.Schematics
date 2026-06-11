using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

public sealed class MultiMachineVariant
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Throughput multiplier relative to the recipe rate (Mk.1 = 60, Mk.2 = 120, …).</summary>
    public string? PartsRatio { get; set; }

    public bool Default { get; set; }

    public Rational PartsRatioValue => PartsRatio is null ? Rational.One : Rational.Parse(PartsRatio);
}
