using Ficsit.Schematics.Core.Numerics;
using Xunit;

namespace Ficsit.Schematics.Tests;

public class RationalTests
{
    [Theory]
    [InlineData("60", "60")]
    [InlineData("-4", "-4")]
    [InlineData("1.2", "6/5")]
    [InlineData("6/5", "6/5")]
    [InlineData("1 1/5", "6/5")]
    [InlineData("4.32/3.6", "6/5")]
    [InlineData("1321929/1000000", "1321929/1000000")]
    [InlineData("0.5", "1/2")]
    [InlineData("2 7/9", "25/9")]
    [InlineData("-1 1/2", "-3/2")]
    public void Parses_all_reference_formats(string input, string canonical)
    {
        var value = Rational.Parse(input);
        Assert.Equal(canonical, value.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1/0")]
    [InlineData("/5")]
    [InlineData("5/")]
    [InlineData("1 2")]
    public void Rejects_invalid_input(string input)
    {
        Assert.False(Rational.TryParse(input, out _));
    }

    [Fact]
    public void Arithmetic_is_exact()
    {
        var a = Rational.Parse("1/3");
        var b = Rational.Parse("1/6");
        Assert.Equal(Rational.Parse("1/2"), a + b);
        Assert.Equal(Rational.Parse("1/6"), a - b);
        Assert.Equal(Rational.Parse("1/18"), a * b);
        Assert.Equal(Rational.Parse("2"), a / b);
        Assert.True(a > b);
    }

    [Fact]
    public void Eight_ninths_displays_like_the_reference()
    {
        var count = new Rational(8, 9);
        Assert.Equal("0.89", count.ToDecimalString(2, RoundingMode.Nearest));
        Assert.Equal("8/9", count.ToFractionString());
    }

    [Theory]
    [InlineData("1040/3", 2, RoundingMode.Nearest, "346.67")]
    [InlineData("50/3", 2, RoundingMode.Nearest, "16.67")]
    [InlineData("10/3", 2, RoundingMode.Nearest, "3.33")]
    [InlineData("10/3", 2, RoundingMode.Down, "3.33")]
    [InlineData("10/3", 2, RoundingMode.Up, "3.34")]
    [InlineData("5", 2, RoundingMode.Nearest, "5")]
    [InlineData("-4", 2, RoundingMode.Nearest, "-4")]
    [InlineData("1/2", 0, RoundingMode.Nearest, "1")]
    [InlineData("1/3", 0, RoundingMode.Up, "1")]
    [InlineData("1/3", 0, RoundingMode.Down, "0")]
    public void Decimal_formatting_matches_reference_modes(string value, int places, RoundingMode mode, string expected)
    {
        Assert.Equal(expected, Rational.Parse(value).ToDecimalString(places, mode));
    }

    [Fact]
    public void Mixed_fraction_formatting()
    {
        Assert.Equal("2 7/9", new Rational(25, 9).ToFractionString());
        Assert.Equal("-2 7/9", new Rational(-25, 9).ToFractionString());
        Assert.Equal("3", new Rational(3, 1).ToFractionString());
    }

    [Fact]
    public void Ceiling_and_floor()
    {
        Assert.Equal(1, (int)new Rational(8, 9).Ceiling());
        Assert.Equal(0, (int)new Rational(8, 9).Floor());
        Assert.Equal(-0, (int)new Rational(-8, 9).Ceiling());
        Assert.Equal(-1, (int)new Rational(-8, 9).Floor());
        Assert.Equal(3, (int)new Rational(3, 1).Ceiling());
    }

    [Fact]
    public void Ceiling_and_floor_are_identity_on_exact_integers()
    {
        Assert.Equal(3, (int)new Rational(3, 1).Floor());
        Assert.Equal(-7, (int)new Rational(-7, 1).Ceiling());
        Assert.Equal(-7, (int)new Rational(-7, 1).Floor());
        Assert.Equal(0, (int)Rational.Zero.Ceiling());
        Assert.Equal(0, (int)Rational.Zero.Floor());
        Assert.Equal(2, (int)new Rational(14, 7).Ceiling()); // reduces to 2/1
    }

    [Fact]
    public void Ceiling_and_floor_handle_negative_fractions_and_big_numerators()
    {
        Assert.Equal(-2, (int)new Rational(-25, 9).Ceiling());
        Assert.Equal(-3, (int)new Rational(-25, 9).Floor());

        // Beyond long: 10^25 + 1/3.
        var big = System.Numerics.BigInteger.Pow(10, 25);
        var value = new Rational(big * 3 + 1, 3);
        Assert.Equal(big + 1, value.Ceiling());
        Assert.Equal(big, value.Floor());
        Assert.Equal(-big, (-value).Ceiling());
        Assert.Equal(-(big + 1), (-value).Floor());
    }
}
