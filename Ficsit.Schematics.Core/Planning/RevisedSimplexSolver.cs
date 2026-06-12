using System.Buffers;
using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// Exact revised simplex over a sparse (CSC) constraint matrix: instead of
/// updating a dense tableau, it maintains an explicit basis inverse B⁻¹ (flat
/// row-major array, O(m²) per pivot) while the original hyper-sparse A stays
/// untouched — pricing and direction computations scale with the non-zero
/// count. Bland's rule guarantees termination; all arithmetic is exact
/// <see cref="Rational"/>. Artificial variables are virtual identity columns
/// (indices ≥ Cols), never materialized.
///
/// Warm start: <see cref="MinimizeWarm"/> continues from a previous optimal
/// basis after appending one pinning row (the stage-1 → stage-2 transition),
/// extending B⁻¹ in O(m²) instead of re-solving from the origin.
/// </summary>
public static class RevisedSimplexSolver
{
    public static RevisedSolveResult Minimize(SparseMatrix a, Rational[] b, Rational[] costs)
    {
        ValidateInputs(a, b, costs);
        var m = a.Rows;
        var state = new State(a, costs, m);
        try
        {
            // Cold start: all-artificial basis, B⁻¹ = I, x_B = b.
            for (var i = 0; i < m; i++)
            {
                state.Basis[i] = a.Cols + i;
                state.BasicValues[i] = b[i];
                state.SetIdentityRow(i);
            }
            state.RebuildIsBasic();

            if (!state.RunPhase1())
                return new RevisedSolveResult { Status = PlanStatus.Infeasible };
            state.DriveOutArtificials();
            return state.RunPhase2();
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    /// Solve a system that equals the snapshot's system plus exactly one new
    /// trailing row pinning <paramref name="pinnedColumn"/> to its stage-1
    /// value. Requires the pinned column to be basic in the snapshot; falls
    /// back to a cold solve otherwise.
    /// </summary>
    public static RevisedSolveResult MinimizeWarm(
        SparseMatrix a, Rational[] b, Rational[] costs,
        RevisedBasisSnapshot warm, int pinnedColumn)
    {
        ValidateInputs(a, b, costs);
        var m = a.Rows;
        if (warm.RowCount != m - 1 || warm.ColumnCount != a.Cols)
            return Minimize(a, b, costs);

        // The pinned column must be basic; find its row.
        var pinnedRow = -1;
        for (var i = 0; i < warm.RowCount; i++)
            if (warm.Basis[i] == pinnedColumn) { pinnedRow = i; break; }
        if (pinnedRow < 0)
            return Minimize(a, b, costs);

        var state = new State(a, costs, m);
        try
        {
            var old = m - 1;
            for (var i = 0; i < old; i++)
            {
                state.Basis[i] = warm.Basis[i] >= warm.ColumnCount
                    ? a.Cols + (warm.Basis[i] - warm.ColumnCount) // re-map artificials to new numbering
                    : warm.Basis[i];
                state.BasicValues[i] = warm.BasicValues[i];
                for (var k = 0; k < old; k++)
                    state.BasisInverse[i * m + k] = warm.BasisInverse[i * old + k];
                state.BasisInverse[i * m + old] = Rational.Zero;
            }
            // New row: its artificial joins the basis at value b_new − x_pinned = 0.
            // Block inverse: bottom-left = −(row of B⁻¹ where the pinned column is basic).
            state.Basis[old] = a.Cols + old;
            for (var k = 0; k < old; k++)
                state.BasisInverse[old * m + k] = -warm.BasisInverse[pinnedRow * old + k];
            state.BasisInverse[old * m + old] = Rational.One;
            state.BasicValues[old] = b[old] - warm.BasicValues[pinnedRow];
            state.RebuildIsBasic();

            if (state.BasicValues[old].IsNegative || state.BasicValues[old].IsPositive)
                return Minimize(a, b, costs); // pin value mismatch — warm start invalid

            state.DriveOutArtificials();
            return state.RunPhase2();
        }
        finally
        {
            state.Release();
        }
    }

    private static void ValidateInputs(SparseMatrix a, Rational[] b, Rational[] costs)
    {
        if (b.Length != a.Rows) throw new ArgumentException("RHS length must match row count.");
        if (costs.Length != a.Cols) throw new ArgumentException("Cost length must match column count.");
        for (var i = 0; i < b.Length; i++)
            if (b[i].IsNegative)
                throw new ArgumentException("RHS must be non-negative (normalize rows first).");
    }

    // ------------------------------------------------------------------ state

    private sealed class State
    {
        private readonly SparseMatrix _a;
        private readonly Rational[] _costs;
        private readonly int _m;
        private readonly bool[] _isBasic;        // structural + artificial
        private readonly Rational[] _duals;      // y, pooled (initial pricing only)
        private readonly Rational[] _direction;  // d = B⁻¹·a_j, pooled
        private readonly Rational[] _reducedCosts; // maintained incrementally, pooled

        public readonly int[] Basis;
        public readonly Rational[] BasisInverse; // flat m×m, pooled
        public readonly Rational[] BasicValues;  // pooled

        public State(SparseMatrix a, Rational[] costs, int m)
        {
            _a = a;
            _costs = costs;
            _m = m;
            Basis = new int[m];
            _isBasic = new bool[a.Cols + m];
            BasisInverse = ArrayPool<Rational>.Shared.Rent(m * m);
            BasicValues = ArrayPool<Rational>.Shared.Rent(m);
            _duals = ArrayPool<Rational>.Shared.Rent(m);
            _direction = ArrayPool<Rational>.Shared.Rent(m);
            _reducedCosts = ArrayPool<Rational>.Shared.Rent(a.Cols + m);
        }

        public void Release()
        {
            // Rationals can reference BigInteger arrays; clear so the pool
            // does not pin them alive.
            ArrayPool<Rational>.Shared.Return(BasisInverse, clearArray: true);
            ArrayPool<Rational>.Shared.Return(BasicValues, clearArray: true);
            ArrayPool<Rational>.Shared.Return(_duals, clearArray: true);
            ArrayPool<Rational>.Shared.Return(_direction, clearArray: true);
            ArrayPool<Rational>.Shared.Return(_reducedCosts, clearArray: true);
        }

        public void SetIdentityRow(int i)
        {
            for (var k = 0; k < _m; k++)
                BasisInverse[i * _m + k] = i == k ? Rational.One : Rational.Zero;
        }

        public void RebuildIsBasic()
        {
            Array.Clear(_isBasic);
            for (var i = 0; i < _m; i++) _isBasic[Basis[i]] = true;
        }

        private Rational CostOf(int column, bool phase1)
            => phase1
                ? (column >= _a.Cols ? Rational.One : Rational.Zero)
                : (column >= _a.Cols ? Rational.Zero : _costs[column]);

        /// <summary>y = c_B · B⁻¹ (dense, O(m²); skips zero basic costs).</summary>
        private void ComputeDuals(bool phase1)
        {
            for (var k = 0; k < _m; k++) _duals[k] = Rational.Zero;
            for (var i = 0; i < _m; i++)
            {
                var cost = CostOf(Basis[i], phase1);
                if (cost.IsZero) continue;
                var rowBase = i * _m;
                for (var k = 0; k < _m; k++)
                {
                    var entry = BasisInverse[rowBase + k];
                    if (!entry.IsZero) _duals[k] += cost * entry;
                }
            }
        }

        /// <summary>Reduced cost of one column against the current duals (sparse).</summary>
        private Rational ReducedCost(int column, bool phase1)
        {
            var value = CostOf(column, phase1);
            if (column >= _a.Cols)
                return value - _duals[column - _a.Cols];
            for (var idx = _a.ColumnPointers[column]; idx < _a.ColumnPointers[column + 1]; idx++)
            {
                var dual = _duals[_a.RowIndices[idx]];
                if (!dual.IsZero) value -= dual * _a.Values[idx];
            }
            return value;
        }

        /// <summary>One-time pricing of every column for the current phase;
        /// afterwards <see cref="UpdateReducedCosts"/> keeps them current per
        /// pivot, exactly like the dense tableau's objective row (the repeated
        /// transformations also keep the fractions small).</summary>
        private void InitializeReducedCosts(bool phase1, int totalColumns)
        {
            ComputeDuals(phase1);
            for (var j = 0; j < totalColumns; j++)
                _reducedCosts[j] = ReducedCost(j, phase1);
        }

        /// <summary>rc′ = rc − rc_e · (row r of B⁻¹A), using the post-pivot row.</summary>
        private void UpdateReducedCosts(int pivotRow, Rational enteringReducedCost, int totalColumns)
        {
            var rowBase = pivotRow * _m;
            for (var j = 0; j < _a.Cols && j < totalColumns; j++)
            {
                var alpha = Rational.Zero;
                for (var idx = _a.ColumnPointers[j]; idx < _a.ColumnPointers[j + 1]; idx++)
                {
                    var entry = BasisInverse[rowBase + _a.RowIndices[idx]];
                    if (!entry.IsZero) alpha += entry * _a.Values[idx];
                }
                if (!alpha.IsZero) _reducedCosts[j] -= enteringReducedCost * alpha;
            }
            for (var k = _a.Cols; k < totalColumns; k++)
            {
                var entry = BasisInverse[rowBase + (k - _a.Cols)];
                if (!entry.IsZero) _reducedCosts[k] -= enteringReducedCost * entry;
            }
        }

        /// <summary>d = B⁻¹ · a_column (O(m · nnz)).</summary>
        private void ComputeDirection(int column)
        {
            if (column >= _a.Cols)
            {
                var row = column - _a.Cols;
                for (var i = 0; i < _m; i++) _direction[i] = BasisInverse[i * _m + row];
                return;
            }
            for (var i = 0; i < _m; i++) _direction[i] = Rational.Zero;
            for (var idx = _a.ColumnPointers[column]; idx < _a.ColumnPointers[column + 1]; idx++)
            {
                var row = _a.RowIndices[idx];
                var coefficient = _a.Values[idx];
                for (var i = 0; i < _m; i++)
                {
                    var entry = BasisInverse[i * _m + row];
                    if (!entry.IsZero) _direction[i] += entry * coefficient;
                }
            }
        }

        private void Pivot(int row, int column)
        {
            var pivot = _direction[row];
            var rowBase = row * _m;
            if (pivot != Rational.One)
            {
                for (var k = 0; k < _m; k++)
                    if (!BasisInverse[rowBase + k].IsZero) BasisInverse[rowBase + k] /= pivot;
                BasicValues[row] /= pivot;
            }
            for (var i = 0; i < _m; i++)
            {
                if (i == row) continue;
                var factor = _direction[i];
                if (factor.IsZero) continue;
                var iBase = i * _m;
                for (var k = 0; k < _m; k++)
                {
                    var entry = BasisInverse[rowBase + k];
                    if (!entry.IsZero) BasisInverse[iBase + k] -= factor * entry;
                }
                if (!BasicValues[row].IsZero) BasicValues[i] -= factor * BasicValues[row];
            }
            _isBasic[Basis[row]] = false;
            _isBasic[column] = true;
            Basis[row] = column;
        }

        /// <summary>
        /// Simplex iterations for one phase. Pricing is index-order ("first
        /// negative", scale-robust → small fractions); when the solve stalls in
        /// a run of degenerate pivots it temporarily switches to Dantzig
        /// (most-negative) to punch through the degeneracy, returning to index
        /// order on real progress. Termination is guaranteed regardless of
        /// pricing by the lexicographic ratio test below. False = unbounded.
        /// </summary>
        private bool Iterate(bool phase1, bool allowArtificialEntering)
        {
            var totalColumns = _a.Cols + (allowArtificialEntering ? _m : 0);
            var stallThreshold = 2 * _m + 10;
            var consecutiveDegenerate = 0;
            InitializeReducedCosts(phase1, totalColumns);
            while (true)
            {
                var entering = -1;
                if (consecutiveDegenerate < stallThreshold)
                {
                    for (var j = 0; j < totalColumns; j++)
                    {
                        if (_isBasic[j]) continue;
                        if (_reducedCosts[j].IsNegative) { entering = j; break; }
                    }
                }
                else
                {
                    var mostNegative = Rational.Zero;
                    for (var j = 0; j < totalColumns; j++)
                    {
                        if (_isBasic[j]) continue;
                        var rc = _reducedCosts[j];
                        if (rc.IsNegative && rc < mostNegative)
                        {
                            mostNegative = rc;
                            entering = j;
                        }
                    }
                }
                if (entering < 0) return true;

                ComputeDirection(entering);

                // Lexicographic ratio test: ties on the ratio are broken by the
                // lexicographically smallest row of [x_B | B⁻¹] / d_i, which
                // provably prevents basis cycling under any pricing rule.
                var pivotRow = -1;
                var bestRatio = Rational.Zero;
                for (var i = 0; i < _m; i++)
                {
                    if (!_direction[i].IsPositive) continue;
                    var ratio = BasicValues[i] / _direction[i];
                    if (pivotRow < 0 || ratio < bestRatio)
                    {
                        pivotRow = i;
                        bestRatio = ratio;
                    }
                    else if (ratio == bestRatio && LexicographicallySmaller(i, pivotRow))
                    {
                        pivotRow = i;
                    }
                }
                if (pivotRow < 0) return false;

                consecutiveDegenerate = bestRatio.IsZero ? consecutiveDegenerate + 1 : 0;

                var enteringReducedCost = _reducedCosts[entering];
                Pivot(pivotRow, entering);
                UpdateReducedCosts(pivotRow, enteringReducedCost, totalColumns);
            }
        }

        /// <summary>True when row i precedes row p in the lexicographic order of B⁻¹ rows scaled by 1/d.</summary>
        private bool LexicographicallySmaller(int i, int p)
        {
            var di = _direction[i];
            var dp = _direction[p];
            var iBase = i * _m;
            var pBase = p * _m;
            for (var k = 0; k < _m; k++)
            {
                // Compare Binv[i,k]/di vs Binv[p,k]/dp without forming quotients:
                // both d are positive, so cross-multiplication preserves order.
                var lhs = BasisInverse[iBase + k] * dp;
                var rhs = BasisInverse[pBase + k] * di;
                if (lhs == rhs) continue;
                return lhs < rhs;
            }
            return false; // identical rows cannot happen (B⁻¹ is invertible)
        }

        public bool RunPhase1()
        {
            // Already feasible without artificials? Then phase 1 is a no-op.
            var anyArtificial = false;
            for (var i = 0; i < _m; i++)
                if (Basis[i] >= _a.Cols && BasicValues[i].IsPositive) { anyArtificial = true; break; }
            if (anyArtificial && !Iterate(phase1: true, allowArtificialEntering: true))
                return false; // phase 1 cannot be unbounded; defensive

            for (var i = 0; i < _m; i++)
                if (Basis[i] >= _a.Cols && BasicValues[i].IsPositive)
                    return false; // residual infeasibility
            return true;
        }

        /// <summary>Pivot basic artificials (at level 0) out wherever a structural column can take over.</summary>
        public void DriveOutArtificials()
        {
            for (var row = 0; row < _m; row++)
            {
                if (Basis[row] < _a.Cols) continue;
                var rowBase = row * _m;
                for (var j = 0; j < _a.Cols; j++)
                {
                    if (_isBasic[j]) continue;

                    // Cheap test first: α = (B⁻¹A)_{row,j} via one sparse dot.
                    var alpha = Rational.Zero;
                    for (var idx = _a.ColumnPointers[j]; idx < _a.ColumnPointers[j + 1]; idx++)
                    {
                        var entry = BasisInverse[rowBase + _a.RowIndices[idx]];
                        if (!entry.IsZero) alpha += entry * _a.Values[idx];
                    }
                    if (alpha.IsZero) continue;

                    ComputeDirection(j);
                    Pivot(row, j);
                    break;
                }
            }
        }

        public RevisedSolveResult RunPhase2()
        {
            if (!Iterate(phase1: false, allowArtificialEntering: false))
                return new RevisedSolveResult { Status = PlanStatus.Unbounded };

            var values = new Rational[_a.Cols];
            for (var j = 0; j < _a.Cols; j++) values[j] = Rational.Zero;
            var objective = Rational.Zero;
            for (var i = 0; i < _m; i++)
            {
                if (Basis[i] >= _a.Cols) continue;
                values[Basis[i]] = BasicValues[i];
                var cost = _costs[Basis[i]];
                if (!cost.IsZero) objective += cost * BasicValues[i];
            }

            var snapshot = new RevisedBasisSnapshot
            {
                Basis = (int[])Basis.Clone(),
                BasisInverse = CopyFlat(BasisInverse, _m * _m),
                BasicValues = CopyFlat(BasicValues, _m),
                RowCount = _m,
                ColumnCount = _a.Cols,
            };
            return new RevisedSolveResult
            {
                Status = PlanStatus.Optimal,
                Values = values,
                Objective = objective,
                Snapshot = snapshot,
            };
        }

        private static Rational[] CopyFlat(Rational[] pooled, int length)
        {
            var copy = new Rational[length];
            Array.Copy(pooled, copy, length);
            return copy;
        }
    }
}
