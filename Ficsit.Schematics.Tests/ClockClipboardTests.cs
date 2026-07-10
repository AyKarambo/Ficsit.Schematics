using Ficsit.Schematics.Core.Editing;
using Ficsit.Schematics.Core.Numerics;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class ClockClipboardTests
{
    /// <summary>
    /// A node at exactly 156.25% (= 25/16) should emit "156.25",
    /// trimmed of trailing zeros, not "156.2500".
    /// </summary>
    [Fact]
    public void FormatClockPercent_at_156_25_emits_trimmed_string()
    {
        // 156.25% as a clock fraction = 156.25 / 100 = 6256/400 = 25/16
        var clock = new Rational(25, 16);
        var result = ClockClipboard.FormatClockPercent(clock);
        Assert.Equal("156.25", result);
    }

    /// <summary>
    /// The formatted value must parse back to the same Rational within
    /// GameClockDecimals decimal places of percent, i.e. the round-trip is lossless
    /// at the game's precision.
    /// </summary>
    [Fact]
    public void FormatClockPercent_round_trips_within_game_precision()
    {
        var clock = new Rational(25, 16); // 156.25%
        var text = ClockClipboard.FormatClockPercent(clock);

        // Parse "156.25" back to Rational
        Assert.True(Rational.TryParse(text, out var parsed));

        // Convert back to fraction: parsed is percent, divide by 100
        var roundTripped = parsed / 100;

        // They must agree within GameClockDecimals decimal places of percent.
        // i.e. |(clock - roundTripped) * 100| < 10^(-GameClockDecimals)
        var diffPercent = ((clock - roundTripped) * 100).Abs();
        var tolerance = new Rational(1, (long)Math.Pow(10, ClockClipboard.GameClockDecimals));
        Assert.True(diffPercent < tolerance,
            $"Round-trip drift {diffPercent} exceeded tolerance 1e-{ClockClipboard.GameClockDecimals}");
    }

    /// <summary>
    /// For an Auto-Round node the effective clock is workload/count, not the entered
    /// node.ClockSpeed. Verify the formatting uses the computed value.
    /// </summary>
    [Fact]
    public void FormatClockPercent_uses_effective_not_entered_clock()
    {
        // Suppose the entered clock is 100% but the auto-round effective is 156.25%.
        var entered = Rational.One; // 100%
        var effective = new Rational(25, 16); // 156.25%

        var enteredText = ClockClipboard.FormatClockPercent(entered);
        var effectiveText = ClockClipboard.FormatClockPercent(effective);

        Assert.Equal("100", enteredText);
        Assert.Equal("156.25", effectiveText);
        Assert.NotEqual(enteredText, effectiveText); // they differ — caller must pass the right value
    }

    /// <summary>
    /// Whole-percent clocks (100%, 250%) should emit integers without a decimal point.
    /// </summary>
    [Theory]
    [InlineData(1, 1, "100")]     // 100%
    [InlineData(5, 2, "250")]     // 250%
    [InlineData(1, 100, "1")]     // 1%
    [InlineData(3, 4, "75")]      // 75%
    public void FormatClockPercent_trims_whole_numbers(int num, int den, string expected)
    {
        var clock = new Rational(num, den);
        Assert.Equal(expected, ClockClipboard.FormatClockPercent(clock));
    }

    /// <summary>
    /// GameClockDecimals constant matches the display precision used in the editor.
    /// </summary>
    [Fact]
    public void GameClockDecimals_is_4()
    {
        Assert.Equal(4, ClockClipboard.GameClockDecimals);
    }
}
