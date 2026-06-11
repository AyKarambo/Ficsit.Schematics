using System.Globalization;
using System.Numerics;

namespace Ficsit.Schematics.Core.Numerics;

/// <summary>
/// Exact rational number (BigInteger numerator/denominator, always normalized:
/// denominator &gt; 0, fraction fully reduced). All factory math uses this type so
/// results like 8/9 machines survive without floating-point drift, matching the
/// reference application.
/// </summary>
public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public static readonly Rational Zero = new(0, 1);
    public static readonly Rational One = new(1, 1);

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
        Numerator = numerator;
        Denominator = denominator.IsZero ? BigInteger.One : denominator;
    }

    public Rational(BigInteger value) : this(value, BigInteger.One) { }

    public static implicit operator Rational(int value) => new(value);
    public static implicit operator Rational(long value) => new(value);

    public bool IsZero => Numerator.IsZero;
    public bool IsNegative => Numerator.Sign < 0;
    public bool IsPositive => Numerator.Sign > 0;
    public bool IsInteger => Denominator.IsOne;
    public int Sign => Numerator.Sign;

    public static Rational operator +(Rational a, Rational b)
        => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a, Rational b)
        => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a) => new(-a.Numerator, a.Denominator);

    public static Rational operator *(Rational a, Rational b)
        => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static Rational operator /(Rational a, Rational b)
        => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    public static bool operator ==(Rational a, Rational b) => a.Equals(b);
    public static bool operator !=(Rational a, Rational b) => !a.Equals(b);
    public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;
    public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;
    public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;

    public bool Equals(Rational other)
        => Numerator.Equals(other.Numerator) && Denominator.Equals(other.Denominator);

    public override bool Equals(object? obj) => obj is Rational r && Equals(r);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public int CompareTo(Rational other)
        => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public static Rational Min(Rational a, Rational b) => a <= b ? a : b;
    public static Rational Max(Rational a, Rational b) => a >= b ? a : b;

    public Rational Abs() => IsNegative ? -this : this;

    /// <summary>Smallest integer ≥ this value.</summary>
    public BigInteger Ceiling()
        => BigInteger.DivRem(Numerator, Denominator, out var rem) is var q && rem.IsZero
            ? q
            : Numerator.Sign > 0 ? q + 1 : q;

    /// <summary>Largest integer ≤ this value.</summary>
    public BigInteger Floor()
        => BigInteger.DivRem(Numerator, Denominator, out var rem) is var q && rem.IsZero
            ? q
            : Numerator.Sign < 0 ? q - 1 : q;

    public double ToDouble() => (double)Numerator / (double)Denominator;

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
