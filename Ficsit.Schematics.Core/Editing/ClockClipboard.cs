using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Editing;

/// <summary>
/// Formatting helper for the machine-editor clock-copy affordance.
/// Keeps the game-precision constant and the exact-Rational → string
/// conversion in a UI-free place so it can be unit-tested.
/// </summary>
public static class ClockClipboard
{
    /// <summary>
    /// Number of decimal places the game's overclock field accepts.
    /// Matches <c>ToDecimalString(4, …)</c> used elsewhere in the editor.
    /// </summary>
    public const int GameClockDecimals = 4;

    /// <summary>
    /// Converts a clock fraction (e.g. 1 = 100%, 1.5625 = 156.25%) to the
    /// human-readable percentage string that should be placed on the clipboard.
    /// The result is trimmed of trailing zeros (e.g. "156.25", not "156.2500").
    /// </summary>
    /// <param name="clockFraction">
    /// The exact clock as a fraction of 100 % — i.e. <c>node.ClockSpeed</c>
    /// (free clock) or <c>workload / count</c> (Auto-Round effective clock).
    /// </param>
    public static string FormatClockPercent(Rational clockFraction)
    {
        var percent = clockFraction * 100;
        var formatted = percent.ToDecimalString(GameClockDecimals, RoundingMode.Nearest);
        return TrimNumber(formatted);
    }

    private static string TrimNumber(string text)
        => text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
}
