using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Services;

/// <summary>Formats exact rationals per the user's per-location number settings.</summary>
public sealed class NumberFormatService(AppState state)
{
    public string Format(Rational value, string location)
    {
        var setting = state.Settings.Numbers.GetValueOrDefault(location)
            ?? new NumberFormatSetting();
        if (setting.DisplayType == "Fraction")
            return value.ToFractionString();
        var mode = setting.RoundingType switch
        {
            "Up" => RoundingMode.Up,
            "Down" => RoundingMode.Down,
            _ => RoundingMode.Nearest,
        };
        return value.ToDecimalString(setting.DecimalPlaces, mode);
    }

    public string Value(Rational value) => Format(value, "value");
    public string Connection(Rational value) => Format(value, "connection");
    public string Summary(Rational value) => Format(value, "summaryPanel");
    public string ValueTooltip(Rational value) => Format(value, "valueToolTip");
}
