using Ficsit.Schematics.Core.Numerics;

namespace Ficsit.Schematics.Core.Planning;

/// <summary>
/// Compressed sparse column (CSC) constraint matrix: only the non-zero
/// coefficients are stored, so matrix-vector work scales with the entry count
/// instead of the rectangle area (factory LPs are >98% zeros).
/// </summary>
public sealed class SparseMatrix
{
    /// <summary>Non-zero coefficients, column-major.</summary>
    public Rational[] Values { get; }

    /// <summary>Row index of each entry in <see cref="Values"/>.</summary>
    public int[] RowIndices { get; }

    /// <summary>Start offset of each column in <see cref="Values"/>; length Cols + 1.</summary>
    public int[] ColumnPointers { get; }

    public int Rows { get; }
    public int Cols { get; }

    private SparseMatrix(int rows, int cols, Rational[] values, int[] rowIndices, int[] columnPointers)
    {
        Rows = rows;
        Cols = cols;
        Values = values;
        RowIndices = rowIndices;
        ColumnPointers = columnPointers;
    }

    /// <summary>
    /// Build directly from per-column entry lists (the planner already has the
    /// columns in sparse form — no dense intermediate).
    /// </summary>
    public static SparseMatrix FromColumns(int rows, IReadOnlyList<(int Row, Rational Coefficient)[]> columns)
    {
        var total = 0;
        for (var j = 0; j < columns.Count; j++) total += columns[j].Length;

        var values = new Rational[total];
        var rowIndices = new int[total];
        var pointers = new int[columns.Count + 1];
        var cursor = 0;
        for (var j = 0; j < columns.Count; j++)
        {
            pointers[j] = cursor;
            var column = columns[j];
            for (var k = 0; k < column.Length; k++)
            {
                var (row, coefficient) = column[k];
                if (coefficient.IsZero) continue;
                values[cursor] = coefficient;
                rowIndices[cursor] = row;
                cursor++;
            }
        }
        pointers[columns.Count] = cursor;
        if (cursor != total)
        {
            Array.Resize(ref values, cursor);
            Array.Resize(ref rowIndices, cursor);
        }
        return new SparseMatrix(rows, columns.Count, values, rowIndices, pointers);
    }

    /// <summary>Build from a dense array, skipping zeros (tests / interop).</summary>
    public static SparseMatrix FromDense(Rational[][] dense)
    {
        var rows = dense.Length;
        var cols = dense[0].Length;
        var columns = new List<(int, Rational)[]>(cols);
        for (var j = 0; j < cols; j++)
        {
            var entries = new List<(int, Rational)>();
            for (var i = 0; i < rows; i++)
                if (!dense[i][j].IsZero)
                    entries.Add((i, dense[i][j]));
            columns.Add([.. entries]);
        }
        return FromColumns(rows, columns);
    }
}
