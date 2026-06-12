using System.Globalization;
using System.Numerics;

namespace Ficsit.Schematics.Core.Numerics;

/// <summary>
/// Exact rational number with transprecision arithmetic: values that fit in
/// 64-bit integers use native arithmetic (no heap allocations); anything larger
/// is promoted to BigInteger. Overflow detection is branch-based — intermediate
/// products of two longs always fit in Int128, so the fast path never throws.
/// Fractions are always fully reduced (Stein's binary GCD on the fast path) and
/// the denominator is always positive. The representation is canonical: the
/// BigInteger state is only used when the reduced value does not fit in long,
/// so equality and hashing can branch on the representation directly.
/// </summary>
public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
{
    private readonly long _num;                  // valid when !_isBig
    private readonly long _den;                  // valid when !_isBig; always > 0
    private readonly BigInteger _bigNum;         // valid when _isBig
    private readonly BigInteger _bigDen;         // valid when _isBig; always > 0
    private readonly bool _isBig;

    public BigInteger Numerator => _isBig ? _bigNum : _num;
    public BigInteger Denominator => _isBig ? _bigDen : _den;

    public static readonly Rational Zero = new(0L, 1L, RawSmall.Tag);
    public static readonly Rational One = new(1L, 1L, RawSmall.Tag);

    // -------------------------------------------------------------- creation

    private enum RawSmall { Tag }

    /// <summary>Trusted small constructor: value already reduced, den &gt; 0.</summary>
    private Rational(long numerator, long denominator, RawSmall _)
    {
        _num = numerator;
        _den = denominator;
        _bigNum = default;
        _bigDen = default;
        _isBig = false;
    }

    private enum RawBig { Tag }

    /// <summary>Trusted big constructor: value already reduced, den &gt; 0, out of long range.</summary>
    private Rational(BigInteger numerator, BigInteger denominator, RawBig _)
    {
        _num = 0;
        _den = 0;
        _bigNum = numerator;
        _bigDen = denominator;
        _isBig = true;
    }

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
            throw new DivideByZeroException("Rational denominator cannot be zero.");
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (gcd > BigInteger.One)
        {
            numerator /= gcd;
            denominator /= gcd;
        }
        // Canonical form: demote to the long representation whenever it fits.
        if (numerator >= long.MinValue && numerator <= long.MaxValue && denominator <= long.MaxValue)
        {
            _num = (long)numerator;
            _den = (long)denominator;
            _bigNum = default;
            _bigDen = default;
            _isBig = false;
        }
        else
        {
            _num = 0;
            _den = 0;
            _bigNum = numerator;
            _bigDen = denominator;
            _isBig = true;
        }
    }

    public Rational(BigInteger value) : this(value, BigInteger.One) { }

    public static implicit operator Rational(int value) => new(value, 1L, RawSmall.Tag);
    public static implicit operator Rational(long value) => new(value, 1L, RawSmall.Tag);

    // ------------------------------------------------ fast-path helpers

    /// <summary>64×64 multiply with branch-based overflow detection (no exceptions).</summary>
    private static bool TryMul(long a, long b, out long result)
    {
        var high = Math.BigMul(a, b, out long low);
        result = low;
        return high == low >> 63; // high word must be the sign extension
    }

    private static bool TryAdd(long a, long b, out long result)
    {
        result = unchecked(a + b);
        return ((a ^ result) & (b ^ result)) >= 0;
    }

    /// <summary>Magnitude as ulong; safe for long.MinValue.</summary>
    private static ulong Magnitude(long value)
        => (ulong)(value < 0 ? unchecked(-value) : value);

    /// <summary>Reduce a small fraction (any signs, den ≠ 0) entirely in 64-bit.</summary>
    private static Rational SmallFraction(long numerator, long denominator)
    {
        if (denominator == 0)
            throw new DivideByZeroException("Rational denominator cannot be zero.");
        if (numerator == 0) return Zero;

        var negative = (numerator < 0) ^ (denominator < 0);
        var num = Magnitude(numerator);
        var den = Magnitude(denominator);
        var gcd = SteinGcd(num, den);
        if (gcd > 1)
        {
            num /= gcd;
            den /= gcd;
        }

        // Magnitude 2^63 only fits as long.MinValue (negative numerator).
        if (den <= long.MaxValue && (num <= long.MaxValue || (negative && num == 1UL << 63)))
            return new Rational(negative ? unchecked(-(long)num) : (long)num, (long)den, RawSmall.Tag);
        var bigNum = negative ? -(BigInteger)num : num;
        return new Rational(bigNum, den, RawBig.Tag);
    }

    /// <summary>Stein's binary GCD on native 64-bit words (hardware TZCNT, no division).</summary>
    private static ulong SteinGcd(ulong a, ulong b)
    {
        if (a == 0) return b;
        if (b == 0) return a;

        var shiftA = System.Numerics.BitOperations.TrailingZeroCount(a);
        var shiftB = System.Numerics.BitOperations.TrailingZeroCount(b);
        var common = Math.Min(shiftA, shiftB);
        a >>= shiftA;
        b >>= shiftB;

        while (a != b)
        {
            if (a > b) (a, b) = (b, a);
            b -= a;
            b >>= System.Numerics.BitOperations.TrailingZeroCount(b);
        }
        return a << common;
    }

    /// <summary>
    /// Reduce and normalize a 128-bit fraction (den &gt; 0 required), landing in
    /// the long representation whenever the reduced value fits. Products of two
    /// longs always fit in Int128, so callers never overflow getting here.
    /// </summary>
    private static Rational FromInt128(Int128 numerator, Int128 denominator)
    {
        if (denominator == 0)
            throw new DivideByZeroException("Rational denominator cannot be zero.");
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }
        if (numerator == 0) return Zero;

        var gcd = (Int128)SteinGcd(
            (UInt128)(numerator < 0 ? -numerator : numerator),
            (UInt128)denominator);
        if (gcd > 1)
        {
            numerator /= gcd;
            denominator /= gcd;
        }
        if (numerator >= long.MinValue && numerator <= long.MaxValue && denominator <= long.MaxValue)
            return new Rational((long)numerator, (long)denominator, RawSmall.Tag);
        return new Rational((BigInteger)numerator, (BigInteger)denominator, RawBig.Tag);
    }

    /// <summary>
    /// Stein's binary GCD: only shifts, comparisons and subtraction — no
    /// hardware division. Both inputs must be &gt; 0… or zero.
    /// </summary>
    private static UInt128 SteinGcd(UInt128 a, UInt128 b)
    {
        if (a == 0) return b;
        if (b == 0) return a;

        var shiftA = (int)UInt128.TrailingZeroCount(a);
        var shiftB = (int)UInt128.TrailingZeroCount(b);
        var common = Math.Min(shiftA, shiftB);
        a >>= shiftA;
        b >>= shiftB;

        while (a != b)
        {
            if (a > b) (a, b) = (b, a);
            b -= a; // both odd → difference even and non-zero
            b >>= (int)UInt128.TrailingZeroCount(b);
        }
        return a << common;
    }

    // ------------------------------------------------------------ inspection

    public bool IsZero => _isBig ? _bigNum.IsZero : _num == 0;
    public bool IsNegative => _isBig ? _bigNum.Sign < 0 : _num < 0;
    public bool IsPositive => _isBig ? _bigNum.Sign > 0 : _num > 0;
    public bool IsInteger => _isBig ? _bigDen.IsOne : _den == 1;
    public int Sign => _isBig ? _bigNum.Sign : Math.Sign(_num);

    // ------------------------------------------------------------- operators

    public static Rational operator +(Rational a, Rational b)
    {
        if (!a._isBig && !b._isBig)
        {
            // Pure 64-bit when nothing overflows; exact 128-bit otherwise.
            if (TryMul(a._num, b._den, out var x)
                && TryMul(b._num, a._den, out var y)
                && TryAdd(x, y, out var num)
                && TryMul(a._den, b._den, out var den))
                return SmallFraction(num, den);
            return FromInt128(
                (Int128)a._num * b._den + (Int128)b._num * a._den,
                (Int128)a._den * b._den);
        }
        return new(a.Numerator * b.Denominator + b.Numerator * a.Denominator,
            a.Denominator * b.Denominator);
    }

    public static Rational operator -(Rational a, Rational b)
    {
        if (!a._isBig && !b._isBig)
        {
            if (TryMul(a._num, b._den, out var x)
                && TryMul(b._num, a._den, out var y)
                && y != long.MinValue && TryAdd(x, -y, out var num)
                && TryMul(a._den, b._den, out var den))
                return SmallFraction(num, den);
            return FromInt128(
                (Int128)a._num * b._den - (Int128)b._num * a._den,
                (Int128)a._den * b._den);
        }
        return new(a.Numerator * b.Denominator - b.Numerator * a.Denominator,
            a.Denominator * b.Denominator);
    }

    public static Rational operator -(Rational a)
    {
        if (!a._isBig)
            return a._num == long.MinValue
                ? new Rational(-(BigInteger)long.MinValue, a._den, RawBig.Tag)
                : new Rational(-a._num, a._den, RawSmall.Tag);
        return new(-a._bigNum, a._bigDen, RawBig.Tag);
    }

    public static Rational operator *(Rational a, Rational b)
    {
        if (!a._isBig && !b._isBig)
        {
            if (TryMul(a._num, b._num, out var num) && TryMul(a._den, b._den, out var den))
                return SmallFraction(num, den);
            return FromInt128((Int128)a._num * b._num, (Int128)a._den * b._den);
        }
        return new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    }

    public static Rational operator /(Rational a, Rational b)
    {
        if (!a._isBig && !b._isBig)
        {
            if (TryMul(a._num, b._den, out var num) && TryMul(a._den, b._num, out var den))
                return SmallFraction(num, den);
            return FromInt128((Int128)a._num * b._den, (Int128)a._den * b._num);
        }
        return new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }

    public static bool operator ==(Rational a, Rational b) => a.Equals(b);
    public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
    public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
    public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
    public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;

    /// <summary>Canonical representation makes mixed small/big equality impossible.</summary>
    public bool Equals(Rational other)
    {
        if (_isBig != other._isBig) return false;
        return _isBig
            ? _bigNum.Equals(other._bigNum) && _bigDen.Equals(other._bigDen)
            : _num == other._num && _den == other._den;
    }

    public override bool Equals(object? obj) => obj is Rational r && Equals(r);

    public override int GetHashCode()
        => _isBig
            ? HashCode.Combine(true, _bigNum, _bigDen)
            : HashCode.Combine(false, _num, _den);

    public int CompareTo(Rational other)
    {
        if (!_isBig && !other._isBig)
        {
            var lhs = (Int128)_num * other._den;
            var rhs = (Int128)other._num * _den;
            return lhs.CompareTo(rhs);
        }
        return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    }

    public static Rational Min(Rational a, Rational b) => a <= b ? a : b;
    public static Rational Max(Rational a, Rational b) => a >= b ? a : b;

    public Rational Abs() => IsNegative ? -this : this;

    /// <summary>Smallest integer >= this value.</summary>
    public BigInteger Ceiling()
        => BigInteger.DivRem(Numerator, Denominator, out var rem) is var q && rem.IsZero
            ? q
            : Numerator.Sign > 0 ? q + 1 : q;

    /// <summary>Largest integer <= this value.</summary>
    public BigInteger Floor()
        => BigInteger.DivRem(Numerator, Denominator, out var rem) is var q && rem.IsZero
            ? q
            : Numerator.Sign < 0 ? q - 1 : q;

    public double ToDouble() => _isBig
        ? (double)_bigNum / (double)_bigDen
        : (double)_num / _den;

    /// <summary>
    /// Raises the rational to a rational power, approximated through doubles when the
    /// exponent is not an integer (power formulas like clock^1.321929 are irrational anyway).
    /// </summary>
    public double Pow(Rational exponent)
    {
        if (exponent.IsInteger && exponent.Numerator >= 0 && exponent.Numerator < 16)
        {
            double result = 1;
            var b = ToDouble();
            for (var i = BigInteger.Zero; i < exponent.Numerator; i++) result *= b;
            return result;
        }
        return Math.Pow(ToDouble(), exponent.ToDouble());
    }

    // ---------------------------------------------------------------- parsing

    /// <summary>
    /// Parses the formats the reference app accepts: "60", "-4", "1.2", "6/5",
    /// "1 1/5" (mixed), "4.32/3.6" (decimal fraction), "1321929/1000000".
    /// </summary>
    public static Rational Parse(string text)
        => TryParse(text, out var value)
            ? value
            : throw new FormatException($"'{text}' is not a valid number.");

    public static bool TryParse(string? text, out Rational value)
    {
        value = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        var negative = false;
        if (text.StartsWith('-') || text.StartsWith('+'))
        {
            negative = text[0] == '-';
            text = text[1..].TrimStart();
            if (text.Length == 0) return false;
        }

        Rational whole = Zero;
        var spaceIdx = text.IndexOf(' ');
        if (spaceIdx > 0 && text.Contains('/'))
        {
            // mixed number: "1 1/5"
            if (!TryParseDecimal(text[..spaceIdx], out whole)) return false;
            text = text[(spaceIdx + 1)..].TrimStart();
            if (!text.Contains('/')) return false;
        }

        Rational result;
        var slashIdx = text.IndexOf('/');
        if (slashIdx >= 0)
        {
            if (slashIdx == 0 || slashIdx == text.Length - 1) return false;
            if (!TryParseDecimal(text[..slashIdx], out var num)) return false;
            if (!TryParseDecimal(text[(slashIdx + 1)..], out var den)) return false;
            if (den.IsZero || num.IsNegative || den.IsNegative) return false;
            result = whole + num / den;
        }
        else
        {
            if (!TryParseDecimal(text, out result)) return false;
            if (result.IsNegative) return false;
        }

        value = negative ? -result : result;
        return true;
    }

    private static bool TryParseDecimal(string text, out Rational value)
    {
        value = Zero;
        text = text.Trim().Replace(",", ".");
        if (text.Length == 0) return false;

        var dotIdx = text.IndexOf('.');
        string intPart, fracPart;
        if (dotIdx >= 0)
        {
            intPart = text[..dotIdx];
            fracPart = text[(dotIdx + 1)..];
            if (intPart.Length == 0 && fracPart.Length == 0) return false;
        }
        else
        {
            intPart = text;
            fracPart = string.Empty;
        }

        if (intPart.Length == 0) intPart = "0";
        if (!BigInteger.TryParse(intPart, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            return false;
        if (fracPart.Length == 0)
        {
            value = new Rational(integer);
            return true;
        }
        if (!fracPart.All(char.IsAsciiDigit)) return false;
        var fracNum = BigInteger.Parse(fracPart, CultureInfo.InvariantCulture);
        var fracDen = BigInteger.Pow(10, fracPart.Length);
        var negative = integer.Sign < 0 || (integer.IsZero && intPart.StartsWith('-'));
        var abs = new Rational(BigInteger.Abs(integer)) + new Rational(fracNum, fracDen);
        value = negative ? -abs : abs;
        return true;
    }

    // ------------------------------------------------------------- formatting

    /// <summary>Canonical invariant form used in documents: "5", "-4", "8/9".</summary>
    public override string ToString()
        => IsInteger ? Numerator.ToString() : $"{Numerator}/{Denominator}";

    /// <summary>Mixed-number fraction display: 8/9 → "8/9", 25/9 → "2 7/9", 3 → "3".</summary>
    public string ToFractionString()
    {
        if (IsInteger) return Numerator.ToString();
        var abs = Abs();
        var whole = abs.Floor();
        var frac = abs - new Rational(whole);
        var sign = IsNegative ? "-" : string.Empty;
        return whole.IsZero
            ? $"{sign}{frac.Numerator}/{frac.Denominator}"
            : $"{sign}{whole} {frac.Numerator}/{frac.Denominator}";
    }

    /// <summary>
    /// Decimal display with the reference app's rounding modes:
    /// Nearest = standard half-up, Up = away from zero when truncated digits exist,
    /// Down = truncate.
    /// </summary>
    public string ToDecimalString(int decimalPlaces, RoundingMode mode)
    {
        if (decimalPlaces < 0) decimalPlaces = 0;
        var scale = BigInteger.Pow(10, decimalPlaces);
        var scaledNum = BigInteger.Abs(Numerator) * scale;
        var quotient = BigInteger.DivRem(scaledNum, Denominator, out var rem);

        var roundUp = mode switch
        {
            RoundingMode.Up => !rem.IsZero,
            RoundingMode.Nearest => rem * 2 >= Denominator,
            _ => false,
        };
        if (roundUp) quotient += 1;

        var sign = IsNegative && !quotient.IsZero ? "-" : string.Empty;
        if (decimalPlaces == 0) return sign + quotient;

        var digits = quotient.ToString();
        if (digits.Length <= decimalPlaces)
            digits = digits.PadLeft(decimalPlaces + 1, '0');
        var intDigits = digits[..^decimalPlaces];
        var fracDigits = digits[^decimalPlaces..].TrimEnd('0');
        return fracDigits.Length == 0 ? sign + intDigits : $"{sign}{intDigits}.{fracDigits}";
    }
}
