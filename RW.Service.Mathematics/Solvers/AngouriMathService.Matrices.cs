using AngouriMath;
using static AngouriMath.Entity;

namespace RW.Service.Mathematics.Solvers
{
    /// <summary>The answer to one operation on a matrix whose entries are expressions.</summary>
    /// <param name="Cells">The resulting matrix, one expression per cell; null for a scalar answer.</param>
    /// <param name="Scalar">The resulting expression; null for a matrix answer.</param>
    /// <param name="Latex">The answer as LaTeX, whichever shape it has.</param>
    /// <param name="Kind">For "solve" only: "unique", "infinite" or "none".</param>
    /// <param name="Condition">
    /// Set when the answer is generic: it holds wherever this expression is non-zero, and may
    /// be wrong where it vanishes. Null when the answer holds unconditionally.
    /// </param>
    /// <param name="Notes">Anything the zero test could not settle; null when there is nothing to say.</param>
    public sealed record SymbolicMatrixAnswer(
        string[][]? Cells, string? Scalar, string Latex,
        string? Kind = null, string? Condition = null, IReadOnlyList<string>? Notes = null);

    /// <summary>
    /// Linear algebra on matrices whose entries are expressions rather than numbers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are AngouriMath's own matrix operations; nothing here is a new algorithm. The
    /// service only parses the cells, runs the operation under the shared time budget, and
    /// simplifies what comes back.
    /// </para>
    /// <para>
    /// <b>AngouriMath's own Rank and RREF are never called.</b> Both are wrong on symbolic
    /// input: the rank of <c>[x a; x a]</c> comes back as 2, and the RREF of <c>[a b; c d]</c>
    /// comes back with <c>NaN</c> cells, because its zero test is structural and does not see
    /// that <c>x - x</c> is zero. Everything that needs elimination is routed to
    /// <see cref="SymbolicElimination"/>, which is written here.
    /// </para>
    /// </remarks>
    public partial class AngouriMathService
    {
        /// <summary>The operations <see cref="SymbolicMatrix"/> accepts.</summary>
        public static readonly IReadOnlyList<string> SymbolicMatrixOperations =
            new[] { "determinant", "inverse", "transpose" }.Concat(SymbolicEliminationOperations).ToArray();

        /// <summary>
        /// Runs <paramref name="operation"/> on the matrix whose cells are the given expressions.
        /// </summary>
        /// <param name="operation">One of <see cref="SymbolicMatrixOperations"/>.</param>
        /// <param name="cells">Rows of expressions. Every row must be the same length.</param>
        public AlgebraResult<SymbolicMatrixAnswer> SymbolicMatrix(string operation, string[][] cells)
            => SymbolicEliminationOperations.Contains(operation)
            ? SymbolicElimination(operation, cells)
            : Attempt(operation, () =>
            {
                if (cells is not { Length: > 0 } || cells[0].Length == 0)
                    throw new ArgumentException("The matrix is empty.");
                if (cells.Any(row => row.Length != cells[0].Length))
                    throw new ArgumentException("Every row must be the same length.");

                var grid = new Entity[cells.Length, cells[0].Length];
                for (int r = 0; r < cells.Length; r++)
                    for (int c = 0; c < cells[0].Length; c++)
                        grid[r, c] = MathS.FromString(cells[r][c]);

                Matrix matrix = MathS.Matrix(grid);
                bool square = matrix.RowCount == matrix.ColumnCount;

                switch (operation)
                {
                    case "determinant":
                        if (!square) throw new ArgumentException(NeedsSquare("A determinant", matrix));
                        return Scalar(matrix.Determinant!.Simplify());

                    case "inverse":
                        if (!square) throw new ArgumentException(NeedsSquare("An inverse", matrix));
                        // Checked first: asked for the inverse of a singular matrix the engine
                        // returns a grid of NaN rather than refusing.
                        if (matrix.Determinant!.Simplify() == 0)
                            throw new ArgumentException("The determinant is 0, so the matrix has no inverse.");
                        return Grid(matrix.Inverse);

                    case "transpose":
                        return Grid(matrix.T);

                    default:
                        throw new ArgumentException(
                            $"'{operation}' is not available for a matrix with symbolic entries. " +
                            $"Available: {string.Join(", ", SymbolicMatrixOperations)}.");
                }
            });

        private static string NeedsSquare(string what, Matrix matrix)
            => $"{what} needs a square matrix; this one is {matrix.RowCount}×{matrix.ColumnCount}.";

        private static SymbolicMatrixAnswer Scalar(Entity value)
            => new(null, value.Stringize(), value.Latexise());

        private static SymbolicMatrixAnswer Grid(Matrix matrix)
        {
            var cells = new string[matrix.RowCount][];
            var simplified = new Entity[matrix.RowCount, matrix.ColumnCount];
            for (int r = 0; r < matrix.RowCount; r++)
            {
                cells[r] = new string[matrix.ColumnCount];
                for (int c = 0; c < matrix.ColumnCount; c++)
                {
                    simplified[r, c] = matrix[r, c].Simplify();
                    cells[r][c] = simplified[r, c].Stringize();
                }
            }
            return new SymbolicMatrixAnswer(cells, null, MathS.Matrix(simplified).Latexise());
        }
    }
}
