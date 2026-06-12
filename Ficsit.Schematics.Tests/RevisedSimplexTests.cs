using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Planning;
using Xunit;

namespace Ficsit.Schematics.Tests;

/// <summary>
/// The dense two-phase simplex (RationalSimplex) acts as the oracle: the sparse
/// revised solver must agree on status and exact optimal objective for a swarm
/// of deterministic pseudo-random LPs, and its solutions must satisfy A·x = b.
/// </summary>
public class RevisedSimplexTests
{
    [Fact]
    public void Matches_the_dense_oracle_on_random_systems()
    {
        var random = new Random(421337);
        for (var instance = 0; instance < 120; instance++)
        {
            var m = random.Next(2, 7);
            var n = random.Next(2, 11);
            var dense = new Rational[m][];
            for (var i = 0; i < m; i++)
            {
                dense[i] = new Rational[n];
                for (var j = 0; j < n; j++)
                    dense[i][j] = new Rational(random.Next(-3, 4)); // sparse-ish, incl. zeros
            }
            var b = new Rational[m];
            for (var i = 0; i < m; i++) b[i] = new Rational(random.Next(0, 9));
            var costs = new Rational[n];
            for (var j = 0; j < n; j++) costs[j] = new Rational(random.Next(-2, 5));

            var oracle = RationalSimplex.Minimize(dense, b, costs);
            var revised = RevisedSimplexSolver.Minimize(SparseMatrix.FromDense(dense), b, costs);

            Assert.Equal(oracle.Status, revised.Status);
            if (oracle.Status != PlanStatus.Optimal) continue;

            Assert.Equal(oracle.Objective, revised.Objective);

            // The revised solution must itself be feasible: A·x = b, x ≥ 0.
            for (var j = 0; j < n; j++)
                Assert.False(revised.Values[j].IsNegative, $"x[{j}] negative in instance {instance}");
            for (var i = 0; i < m; i++)
            {
                var lhs = Rational.Zero;
                for (var j = 0; j < n; j++)
                    lhs += dense[i][j] * revised.Values[j];
                Assert.True(lhs == b[i], $"Row {i} violated in instance {instance}: {lhs} != {b[i]}");
            }
        }
    }

    [Fact]
    public void Warm_started_pinned_solve_matches_cold_solve()
    {
        var random = new Random(99173);
        var checkedInstances = 0;
        for (var instance = 0; instance < 80 && checkedInstances < 25; instance++)
        {
            var m = random.Next(2, 6);
            var n = random.Next(3, 9);
            var dense = new Rational[m][];
            for (var i = 0; i < m; i++)
            {
                dense[i] = new Rational[n];
                for (var j = 0; j < n; j++)
                    dense[i][j] = new Rational(random.Next(-2, 4));
            }
            var b = new Rational[m];
            for (var i = 0; i < m; i++) b[i] = new Rational(random.Next(0, 7));
            var stage1Costs = new Rational[n];
            for (var j = 0; j < n; j++) stage1Costs[j] = new Rational(random.Next(-3, 3));

            var sparse = SparseMatrix.FromDense(dense);
            var stage1 = RevisedSimplexSolver.Minimize(sparse, b, stage1Costs);
            if (stage1.Status != PlanStatus.Optimal || stage1.Snapshot is null) continue;

            // Pick a basic structural column to pin at its optimal value.
            var pinColumn = -1;
            foreach (var basic in stage1.Snapshot.Basis)
                if (basic < n) { pinColumn = basic; break; }
            if (pinColumn < 0) continue;
            checkedInstances++;

            // Extended system: one extra row "x_pin = value".
            var extended = new Rational[m + 1][];
            for (var i = 0; i < m; i++) extended[i] = dense[i];
            extended[m] = new Rational[n];
            Array.Fill(extended[m], Rational.Zero);
            extended[m][pinColumn] = Rational.One;
            var bExtended = new Rational[m + 1];
            Array.Copy(b, bExtended, m);
            bExtended[m] = stage1.Values[pinColumn];

            var stage2Costs = new Rational[n];
            for (var j = 0; j < n; j++) stage2Costs[j] = new Rational(random.Next(-1, 4));

            var sparseExtended = SparseMatrix.FromDense(extended);
            var cold = RevisedSimplexSolver.Minimize(sparseExtended, bExtended, stage2Costs);
            var warm = RevisedSimplexSolver.MinimizeWarm(
                sparseExtended, bExtended, stage2Costs, stage1.Snapshot, pinColumn);

            Assert.Equal(cold.Status, warm.Status);
            if (cold.Status == PlanStatus.Optimal)
                Assert.Equal(cold.Objective, warm.Objective);
        }
        Assert.True(checkedInstances >= 10, $"Only {checkedInstances} pinnable instances exercised.");
    }
}
