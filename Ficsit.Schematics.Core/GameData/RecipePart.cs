using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

public sealed class RecipePart
{
    public string Part { get; set; } = string.Empty;

    /// <summary>Per-batch amount: negative = input, positive = output (exact).</summary>
    public Rational Amount { get; set; } = Rational.Zero;

    public Rational AmountValue => Amount;
}
