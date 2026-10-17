using AngouriMath;
using RW.Library.Mathematics.RationalFunctions;

namespace RW.Service.Mathematics.Solvers
{
    /// <summary>
    /// Exact linear algebra on matrices whose entries are rational functions, including the
    /// operations <see cref="SymbolicMatrix"/> has to refuse (rank, RREF, null space, solve).
    /// </summary>
    /// <remarks>
    /// The arithmetic is <see cref="RationalMatrix"/> in RW.Library.Mathematics, not AngouriMath:
    /// there a pivot is zero exactly when its numerator is the zero polynomial, so elimination
    /// needs no simplifier. AngouriMath is used here only to typeset the answer. Entries outside
    /// the rational-function domain (sin, sqrt, fractional powers) are refused with a message.
    /// Answers are generic in the variables; see <see cref="RationalMatrix"/>.
    /// </remarks>
    public partial class AngouriMathService
    {
        /// <summary>The operations <see cref="ExactMatrix"/> accepts.</summary>
        public static readonly IReadOnlyList<string> ExactMatrixOperations =
            new[] { "rref", "rank", "nullspace", "solve", "determinant", "inverse" };

        /// <summary>
        /// Runs <paramref name="operation"/> exactly. Shapes of the answer:
        /// <c>rank</c> and <c>determinant</c> fill <c>Scalar</c>; <c>rref</c> and <c>inverse</c>
        /// fill <c>Cells</c>; <c>nullspace</c> fills <c>Cells</c> with one basis vector per
        /// column (no columns when only the zero vector works); <c>solve</c> treats the last
        /// column as the right-hand side, puts "unique", "parametric" or "inconsistent" in
        /// <c>Scalar</c>, and in <c>Cells</c> a particular solution as the first column followed
        /// by the homogeneous basis vectors (null when inconsistent).
        /// </summary>
        public AlgebraResult<SymbolicMatrixAnswer> ExactMatrix(string operation, string[][] cells)
            => Attempt(operation, () =>
            {
                var matrix = RationalMatrix.Parse(cells);
                switch (operation)
                {
                    case "rref": return ExactGrid(Rows(RationalMatrix.Rref(matrix)));
                    case "inverse": return ExactGrid(Rows(RationalMatrix.Inverse(matrix)));
                    case "rank": return ExactScalar(RationalMatrix.Rank(matrix).ToString());
                    case "determinant": return ExactScalar(RationalMatrix.Determinant(matrix).ToString());
                    case "nullspace": return ExactGrid(Columns(RationalMatrix.NullSpace(matrix), matrix.GetLength(1)));
                    case "solve":
                        var solution = RationalMatrix.Solve(matrix);
                        string kind = solution.Kind.ToString().ToLowerInvariant();
                        if (solution.Particular is null)
                            return new SymbolicMatrixAnswer(null, kind, @"\varnothing");
                        var columns = solution.Homogeneous.Prepend(solution.Particular).ToArray();
                        return ExactGrid(Columns(columns, solution.Particular.Length)) with { Scalar = kind };
                    default:
                        throw new ArgumentException(
                            $"'{operation}' is not an exact matrix operation. " +
                            $"Available: {string.Join(", ", ExactMatrixOperations)}.");
                }
            });

        private static string[][] Rows(RationalFunction[,] m)
            => Enumerable.Range(0, m.GetLength(0))
                .Select(r => Enumerable.Range(0, m.GetLength(1)).Select(c => m[r, c].ToString()).ToArray())
                .ToArray();

        private static string[][] Columns(RationalFunction[][] vectors, int length)
            => Enumerable.Range(0, length)
                .Select(r => vectors.Select(v => v[r].ToString()).ToArray())
                .ToArray();

        private static SymbolicMatrixAnswer ExactScalar(string value)
            => new(null, value, Parse(value).Latexise());

        private static SymbolicMatrixAnswer ExactGrid(string[][] cells)
        {
            if (cells.Length == 0 || cells[0].Length == 0)
                return new SymbolicMatrixAnswer(cells, null, @"\{\mathbf{0}\}");
            var grid = new Entity[cells.Length, cells[0].Length];
            for (int r = 0; r < cells.Length; r++)
                for (int c = 0; c < cells[0].Length; c++)
                    grid[r, c] = Parse(cells[r][c]);
            return new SymbolicMatrixAnswer(cells, null, MathS.Matrix(grid).Latexise());
        }
    }
}
