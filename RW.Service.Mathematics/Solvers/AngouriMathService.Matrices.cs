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
    /// <b>AngouriMath's own matrix algebra is not used, except to transpose.</b> On symbolic
    /// input its Rank and RREF are wrong (rank of <c>[x a; x a]</c> is 2; RREF of
    /// <c>[a b; c d]</c> has NaN cells), because its zero test is structural and does not see
    /// that <c>x - x</c> is zero. Its Determinant is wrong too, less often: the circulant
    /// <c>[a b c; b c a; c a b]</c> comes back with a spurious <c>6b⁵/(ac − b²)</c> term, 1550
    /// at (2, 3, 5) where the true value is −70. A dense symbolic 4×4 also exhausts the budget.
    /// </para>
    /// </remarks>
    public partial class AngouriMathService
    {
        /// <summary>The operations <see cref="SymbolicMatrix"/> accepts.</summary>
        public static readonly IReadOnlyList<string> SymbolicMatrixOperations =
            new[] { "transpose" }.Concat(SymbolicEliminationOperations).ToArray();

        /// <summary>
        /// Runs <paramref name="operation"/> on the matrix whose cells are the given expressions.
        /// This is the one entry point; it chooses how the work is done.
        /// </summary>
        /// <remarks>
        /// Determinant and inverse are tried exactly first (<see cref="ExactMatrix"/>), which
        /// succeeds whenever every cell is a rational function — the common case, and the one
        /// where a wrong answer is least forgivable. Anything it cannot take (<c>sin</c>,
        /// <c>sqrt</c>, an intermediate result grown too large) falls through to
        /// <see cref="SymbolicElimination"/>, which also produces the error message when the
        /// request itself is at fault.
        /// </remarks>
        /// <param name="operation">One of <see cref="SymbolicMatrixOperations"/>.</param>
        /// <param name="cells">Rows of expressions. Every row must be the same length.</param>
        public AlgebraResult<SymbolicMatrixAnswer> SymbolicMatrix(string operation, string[][] cells)
        {
            if (operation is "determinant" or "inverse")
            {
                var exact = ExactMatrix(operation, cells);
                if (exact.Success) return exact;
            }

            if (operation != "transpose")
                return SymbolicElimination(operation, cells);

            return Attempt(operation, () =>
            {
                if (cells is not { Length: > 0 } || cells[0].Length == 0)
                    throw new ArgumentException("The matrix is empty.");
                if (cells.Any(row => row.Length != cells[0].Length))
                    throw new ArgumentException("Every row must be the same length.");

                var grid = new Entity[cells.Length, cells[0].Length];
                for (int r = 0; r < cells.Length; r++)
                    for (int c = 0; c < cells[0].Length; c++)
                        grid[r, c] = MathS.FromString(cells[r][c]);

                return Grid(MathS.Matrix(grid).T);
            });
        }

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
