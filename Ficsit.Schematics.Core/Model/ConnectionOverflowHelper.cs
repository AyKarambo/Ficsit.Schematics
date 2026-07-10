using System.Numerics;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Model;

/// <summary>
/// Pure helper for belt / pipe capacity overflow detection.  The renderer calls
/// this for each connection; tests exercise it independently.
/// </summary>
public static class ConnectionOverflowHelper
{
    /// <summary>
    /// Returns an overflow result when <paramref name="flow"/> exceeds
    /// <paramref name="threshold"/>, including the number of parallel lines needed.
    /// Returns <c>null</c> when the connection is within capacity.
    /// </summary>
    public static ConnectionOverflow? Check(Rational flow, Rational threshold)
    {
        if (threshold <= Rational.Zero || flow <= threshold) return null;

        // ceil(flow / threshold): smallest integer n such that n * threshold >= flow.
        var quotient = flow / threshold;
        var linesNeeded = (int)Ceiling(quotient);
        return new ConnectionOverflow(flow, threshold, linesNeeded);
    }

    private static BigInteger Ceiling(Rational r)
    {
        var num = r.Numerator;
        var den = r.Denominator; // always positive
        // Integer ceiling of a positive rational: (num + den - 1) / den
        return (num + den - 1) / den;
    }
}

/// <summary>Overflow information for a single connection.</summary>
/// <param name="Flow">The actual flow (per minute).</param>
/// <param name="Threshold">The maximum single-line throughput (per minute).</param>
/// <param name="LinesNeeded">The minimum number of parallel belts/pipes required.</param>
public sealed record ConnectionOverflow(Rational Flow, Rational Threshold, int LinesNeeded);
