using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// Optimal-basis checkpoint of a revised-simplex solve, used to warm-start a
/// follow-up solve (same constraints, new objective and/or one pinned row)
/// without re-discovering the basis from scratch.
/// </summary>
public sealed class RevisedBasisSnapshot
{
    /// <summary>basis[i] = column index basic in row i (≥ Cols means the row's artificial).</summary>
    public required int[] Basis { get; init; }

    /// <summary>Explicit B⁻¹, row-major flat (RowCount × RowCount).</summary>
    public required Rational[] BasisInverse { get; init; }

    /// <summary>Current basic variable values (B⁻¹·b), length RowCount.</summary>
    public required Rational[] BasicValues { get; init; }

    public required int RowCount { get; init; }

    /// <summary>Structural column count of the system this snapshot belongs to.</summary>
    public required int ColumnCount { get; init; }
}
