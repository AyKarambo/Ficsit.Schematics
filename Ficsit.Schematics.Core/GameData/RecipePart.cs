using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.GameData;

public sealed class RecipePart
{
    public string Part { get; set; } = string.Empty;
    public string Amount { get; set; } = "0";

    public Rational AmountValue => GameDatabase.ParseOrZero(Amount);
}
