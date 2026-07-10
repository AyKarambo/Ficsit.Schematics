using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Xunit;

namespace Ficsit.Schematics.Tests;

/// <summary>Tests for the belt/pipe capacity overflow helper.</summary>
public class ConnectionOverflowTests
{
    // Belt threshold: 1200/min (Mk.6); pipe threshold: 600/min (Mk.2).

    [Fact]
    public void No_overflow_when_flow_equals_threshold()
    {
        var result = ConnectionOverflowHelper.Check(new Rational(1200), new Rational(1200));
        Assert.Null(result);
    }

    [Fact]
    public void No_overflow_when_flow_below_threshold()
    {
        var result = ConnectionOverflowHelper.Check(new Rational(1000), new Rational(1200));
        Assert.Null(result);
    }

    [Fact]
    public void No_overflow_for_zero_flow()
    {
        var result = ConnectionOverflowHelper.Check(Rational.Zero, new Rational(1200));
        Assert.Null(result);
    }

    [Fact]
    public void Overflow_solid_1500_over_1200_needs_2_belts()
    {
        // 1500/min solid, threshold 1200/min → ceil(1500/1200) = ceil(1.25) = 2.
        var result = ConnectionOverflowHelper.Check(new Rational(1500), new Rational(1200));
        Assert.NotNull(result);
        Assert.Equal(2, result!.LinesNeeded);
        Assert.Equal(new Rational(1500), result.Flow);
        Assert.Equal(new Rational(1200), result.Threshold);
    }

    [Fact]
    public void Overflow_solid_exactly_double_needs_2_belts()
    {
        // 2400/min solid, threshold 1200/min → ceil(2400/1200) = 2.
        var result = ConnectionOverflowHelper.Check(new Rational(2400), new Rational(1200));
        Assert.NotNull(result);
        Assert.Equal(2, result!.LinesNeeded);
    }

    [Fact]
    public void Overflow_solid_3000_over_1200_needs_3_belts()
    {
        // 3000/min solid, threshold 1200/min → ceil(3000/1200) = ceil(2.5) = 3.
        var result = ConnectionOverflowHelper.Check(new Rational(3000), new Rational(1200));
        Assert.NotNull(result);
        Assert.Equal(3, result!.LinesNeeded);
    }

    [Fact]
    public void Overflow_fluid_700_over_600_needs_2_pipes()
    {
        // 700/min fluid, threshold 600/min → ceil(700/600) = ceil(1.166…) = 2.
        var result = ConnectionOverflowHelper.Check(new Rational(700), new Rational(600));
        Assert.NotNull(result);
        Assert.Equal(2, result!.LinesNeeded);
    }

    [Fact]
    public void Overflow_fluid_within_capacity_returns_null()
    {
        // 300/min fluid, threshold 600/min → no overflow.
        var result = ConnectionOverflowHelper.Check(new Rational(300), new Rational(600));
        Assert.Null(result);
    }

    [Fact]
    public void Overflow_fractional_flow_rounds_up_correctly()
    {
        // 1201/min over 1200 → ceil(1201/1200) = 2.
        var result = ConnectionOverflowHelper.Check(new Rational(1201), new Rational(1200));
        Assert.NotNull(result);
        Assert.Equal(2, result!.LinesNeeded);
    }
}
