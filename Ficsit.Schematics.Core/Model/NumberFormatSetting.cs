namespace Ficsit.Schematics.Core.Model;

/// <summary>One number-format rule (the Numbers settings table has one per display location).</summary>
public sealed class NumberFormatSetting
{
    public string DisplayType { get; set; } = "Decimal"; // "Decimal" | "Fraction"
    public int DecimalPlaces { get; set; } = 2;
    public string RoundingType { get; set; } = "Nearest"; // "Nearest" | "Up" | "Down"
}
