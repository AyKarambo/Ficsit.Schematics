using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

public sealed class CostEntry
{
    public string Part { get; set; } = string.Empty;
    public Rational Amount { get; set; } = Rational.Zero;

    public Rational AmountValue => Amount;
}
