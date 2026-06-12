using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// Two-phase primal simplex over exact rationals (no floating-point drift),
/// with Bland's rule for guaranteed termination. Solves
/// <c>minimize c·x subject to A·x = b, x ≥ 0</c>. Problem sizes here are small
/// (≤ a few hundred rows/columns), so a dense tableau is plenty.
/// </summary>
public static class RationalSimplex
{
    public static SimplexSolution Minimize(Rational[][] a, Rational[] b, Rational[] c)
    {
        var m = a.Length;
        var n = c.Length;
        var width = n + m + 1; // real vars + artificials + rhs
        var rhs = width - 1;

        // Tableau with artificial identity; rows normalized to b >= 0.
        var t = new Rational[m][];
        var basis = new int[m];
        for (var i = 0; i < m; i++)
        {
            var row = new Rational[width];
            Array.Fill(row, Rational.Zero); // default(Rational) is 0/0 — never leave cells unset
            var negate = b[i].IsNegative;
            for (var j = 0; j < n; j++) row[j] = negate ? -a[i][j] : a[i][j];
            row[n + i] = Rational.One;
            row[rhs] = negate ? -b[i] : b[i];
            t[i] = row;
            basis[i] = n + i;
        }

        // Phase 1: minimize the sum of artificials.
        var objective = new Rational[width];
        for (var j = 0; j < width; j++)
        {
            var sum = Rational.Zero;
            for (var i = 0; i < m; i++) sum += t[i][j];
            objective[j] = (j >= n && j < n + m ? Rational.One : Rational.Zero) - sum;
        }
        // Artificial reduced costs start at zero by construction.
        if (!Iterate(t, objective, basis, width, candidateLimit: width - 1))
            return new SimplexSolution { Status = PlanStatus.Infeasible };
        if ((-objective[rhs]).IsPositive)
            return new SimplexSolution { Status = PlanStatus.Infeasible };

        // Drive remaining artificials out of the basis where possible.
        for (var i = 0; i < m; i++)
        {
            if (basis[i] < n) continue;
            for (var j = 0; j < n; j++)
            {
                if (t[i][j].IsZero) continue;
                Pivot(t, objective, basis, i, j, width);
                break;
            }
        }

        // Phase 2: the real objective, artificials banned from entering.
        objective = new Rational[width];
        for (var j = 0; j < n; j++) objective[j] = c[j];
        for (var j = n; j < width; j++) objective[j] = Rational.Zero;
        for (var i = 0; i < m; i++)
        {
            if (basis[i] >= n || c[basis[i]].IsZero) continue;
            var factor = c[basis[i]];
            for (var j = 0; j < width; j++) objective[j] -= factor * t[i][j];
        }
        if (!Iterate(t, objective, basis, width, candidateLimit: n))
            return new SimplexSolution { Status = PlanStatus.Unbounded };

        var values = new Rational[n];
        for (var j = 0; j < n; j++) values[j] = Rational.Zero;
        for (var i = 0; i < m; i++)
            if (basis[i] < n) values[basis[i]] = t[i][rhs];

        return new SimplexSolution
        {
            Status = PlanStatus.Optimal,
            Values = values,
            Objective = -objective[rhs],
        };
    }

    /// <summary>Pivots until optimal. False = unbounded in the entering column.</summary>
    private static bool Iterate(Rational[][] t, Rational[] objective, int[] basis, int width, int candidateLimit)
    {
        var m = t.Length;
        var rhs = width - 1;
        while (true)
        {
            // Bland: first improving column.
            var entering = -1;
            for (var j = 0; j < candidateLimit; j++)
            {
                if (objective[j].IsNegative) { entering = j; break; }
            }
            if (entering < 0) return true;

            // Ratio test; Bland tie-break on smallest basis index.
            var pivotRow = -1;
            var bestRatio = Rational.Zero;
            for (var i = 0; i < m; i++)
            {
                if (!t[i][entering].IsPositive) continue;
                var ratio = t[i][rhs] / t[i][entering];
                if (pivotRow < 0 || ratio < bestRatio
                    || (ratio == bestRatio && basis[i] < basis[pivotRow]))
                {
                    pivotRow = i;
                    bestRatio = ratio;
                }
            }
            if (pivotRow < 0) return false;

            Pivot(t, objective, basis, pivotRow, entering, width);
        }
    }

    private static void Pivot(Rational[][] t, Rational[] objective, int[] basis, int row, int column, int width)
    {
        var pivot = t[row][column];
        if (pivot != Rational.One)
        {
            for (var j = 0; j < width; j++)
                if (!t[row][j].IsZero) t[row][j] /= pivot;
        }

        for (var i = 0; i < t.Length; i++)
        {
            if (i == row || t[i][column].IsZero) continue;
            var factor = t[i][column];
            for (var j = 0; j < width; j++)
                if (!t[row][j].IsZero) t[i][j] -= factor * t[row][j];
        }
        if (!objective[column].IsZero)
        {
            var factor = objective[column];
            for (var j = 0; j < width; j++)
                if (!t[row][j].IsZero) objective[j] -= factor * t[row][j];
        }
        basis[row] = column;
    }
}
