using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

public sealed class MultiMachineCapacity
{
    public string Name { get; set; } = string.Empty;
    public Rational? PartsRatio { get; set; }
    public Rational? PowerRatio { get; set; }
    public bool Default { get; set; }

    /// <summary>Packed RGB accent color from the reference data (e.g. impure orange).</summary>
    public int? Color { get; set; }

    public Rational PartsRatioValue => PartsRatio ?? Rational.One;

    public Rational PowerRatioValue => PowerRatio ?? Rational.One;
}
