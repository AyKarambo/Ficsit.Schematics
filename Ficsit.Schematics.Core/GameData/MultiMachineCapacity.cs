using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

public sealed class MultiMachineCapacity
{
    public string Name { get; set; } = string.Empty;
    public string? PartsRatio { get; set; }
    public string? PowerRatio { get; set; }
    public bool Default { get; set; }

    /// <summary>Packed RGB accent color from the reference data (e.g. impure orange).</summary>
    public int? Color { get; set; }

    public Rational PartsRatioValue => PartsRatio is null ? Rational.One : Rational.Parse(PartsRatio);

    public Rational PowerRatioValue => PowerRatio is null ? Rational.One : Rational.Parse(PowerRatio);
}
