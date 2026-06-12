using System.Numerics;
using Ficsit.Schematics.Core.Numerics;
using Xunit;

namespace Ficsit.Schematics.Tests;

/// <summary>
/// Edge cases of the transprecision Rational: the long fast path must promote
/// to BigInteger on overflow without losing exactness, survive long.MinValue,
/// and keep the representation canonical (big values demote when they fit).
/// </summary>
public class RationalTransprecisionTests
{
    [Fact]
    public void Overflowing_products_promote_exactly()
    {
        var a = new Rational(BigInteger.Pow(2, 40), 3);
        var b = new Rational(BigInteger.Pow(2, 40), 5);
        var product = a * b;
        Assert.Equal(BigInteger.Pow(2, 80), product.Numerator);
        Assert.Equal(new BigInteger(15), product.Denominator);
    }

    [Fact]
    public void Overflowing_sums_promote_exactly()
    {
        var nearMax = new Rational(long.MaxValue - 1);
        var sum = nearMax + nearMax;
        Assert.Equal(new BigInteger(long.MaxValue - 1) * 2, sum.Numerator);
        Assert.Equal(BigInteger.One, sum.Denominator);
    }

    [Fact]
    public void Long_min_value_is_handled_safely()
    {
        var minValue = new Rational(long.MinValue);
        Assert.Equal(new BigInteger(long.MinValue), minValue.Numerator);

        var negated = -minValue;
        Assert.Equal(-(BigInteger)long.MinValue, negated.Numerator);

        var halved = minValue / new Rational(2);
        Assert.Equal(new BigInteger(long.MinValue) / 2, halved.Numerator);

        var absolute = minValue.Abs();
        Assert.Equal(-(BigInteger)long.MinValue, absolute.Numerator);

        // Negative denominators normalize, including the MinValue magnitude.
        var viaNegativeDen = new Rational(5, long.MinValue);
        Assert.True(viaNegativeDen.IsNegative);
        Assert.Equal(-(BigInteger)5, viaNegativeDen.Numerator * 1);
    }

    [Fact]
    public void Big_values_demote_to_the_fast_representation_when_reduced()
    {
        // 2^80 / 2^70 reduces to 2^10 — must equal (and hash like) the small form.
        var big = new Rational(BigInteger.Pow(2, 80), BigInteger.Pow(2, 70));
        var small = new Rational(1024);
        Assert.Equal(small, big);
        Assert.Equal(small.GetHashCode(), big.GetHashCode());
    }

    [Fact]
    public void Mixed_magnitude_arithmetic_stays_exact()
    {
        // Accumulate values that overflow long mid-way, then come back down.
        var huge = new Rational(BigInteger.Pow(10, 30));
        var x = new Rational(1, 3);
        var roundTrip = (x + huge) - huge;
        Assert.Equal(new Rational(1, 3), roundTrip);

        var scaled = x * huge / huge;
        Assert.Equal(new Rational(1, 3), scaled);
    }

    [Fact]
    public void Division_normalizes_signs()
    {
        Assert.Equal(new Rational(-3, 2), new Rational(3) / new Rational(-2));
        Assert.Equal(new Rational(3, 2), new Rational(-3) / new Rational(-2));
        Assert.Throws<DivideByZeroException>(() => _ = new Rational(1) / Rational.Zero);
    }
}
