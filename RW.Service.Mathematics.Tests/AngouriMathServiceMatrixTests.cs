using RW.Service.Mathematics.Solvers;

namespace RW.Service.Mathematics.Tests
{
    /// <summary>Linear algebra on matrices whose entries are expressions.</summary>
    /// <remarks>
    /// Answers are compared by value, not by spelling: AngouriMath is free to print
    /// <c>a*d - b*c</c> in any order, so each test substitutes numbers and checks the arithmetic.
    /// </remarks>
    public class AngouriMathServiceMatrixTests
    {
        private readonly AngouriMathService _svc = new();

        private SymbolicMatrixAnswer Run(string operation, params string[][] cells)
        {
            var result = _svc.SymbolicMatrix(operation, cells);
            Assert.True(result.Success, result.Error);
            return result.Value!;
        }

        private string Fail(string operation, params string[][] cells)
        {
            var result = _svc.SymbolicMatrix(operation, cells);
            Assert.False(result.Success);
            return result.Error!;
        }

        /// <summary>Evaluates an expression at a = 2, b = 3, c = 5, d = 7, x = 11.</summary>
        private double At(string expression)
        {
            var evaluated = _svc.Evaluate($"({expression})".Replace("a", "(2)").Replace("b", "(3)")
                .Replace("c", "(5)").Replace("d", "(7)").Replace("x", "(11)"));
            Assert.True(evaluated.Success, $"{expression}: {evaluated.Error}");
            // Evaluate answers exactly, so a non-integer arrives as "1/11" rather than a decimal.
            var parts = evaluated.Result.Split('/');
            double Number(string text) => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            return parts.Length == 2 ? Number(parts[0]) / Number(parts[1]) : Number(parts[0]);
        }

        private static string[] Row(params string[] cells) => cells;

        // ---------------------------------------------------------------- determinant

        [Fact]
        public void Determinant_OfTwoByTwo_IsAdMinusBc()
            => Assert.Equal(2 * 7 - 3 * 5, At(Run("determinant", Row("a", "b"), Row("c", "d")).Scalar!), 9);

        [Fact]
        public void Determinant_OfNumbers_IsTheNumber()
            => Assert.Equal(-2, At(Run("determinant", Row("1", "2"), Row("3", "4")).Scalar!), 9);

        [Fact]
        public void Determinant_OfTriangular_IsTheDiagonalProduct()
            => Assert.Equal(11 * 2 * 3, At(Run("determinant",
                Row("x", "5", "7"), Row("0", "a", "1"), Row("0", "0", "b")).Scalar!), 9);

        [Fact]
        public void Determinant_WithRepeatedRow_IsZero()
            => Assert.Equal(0, At(Run("determinant", Row("x", "a"), Row("x", "a")).Scalar!), 9);

        [Fact]
        public void Determinant_HandlesFunctionsOfX()
            // | x^2  1 |
            // | x    1 |  =  x^2 - x  =  110 at x = 11
            => Assert.Equal(110, At(Run("determinant", Row("x^2", "1"), Row("x", "1")).Scalar!), 9);

        [Fact]
        public void Determinant_OfOneByOne_IsTheEntry()
            => Assert.Equal(12, At(Run("determinant", Row("x + 1")).Scalar!), 9);

        [Fact]
        public void Determinant_CarriesLatex()
            => Assert.False(string.IsNullOrWhiteSpace(Run("determinant", Row("a", "b"), Row("c", "d")).Latex));

        [Fact]
        public void Determinant_RejectsNonSquare()
            => Assert.Contains("square", Fail("determinant", Row("a", "b", "c"), Row("1", "2", "3")));

        // ---------------------------------------------------------------- transpose

        [Fact]
        public void Transpose_SwapsRowsAndColumns()
        {
            var t = Run("transpose", Row("a", "b", "c"), Row("1", "2", "x"));
            Assert.Equal(3, t.Cells!.Length);
            Assert.Equal(2, t.Cells[0].Length);
            Assert.Equal("b", t.Cells[1][0]);
            Assert.Equal("x", t.Cells[2][1]);
            Assert.Null(t.Scalar);
        }

        [Fact]
        public void Transpose_Twice_IsTheOriginal()
        {
            var once = Run("transpose", Row("a", "b"), Row("c", "x^2"));
            var twice = Run("transpose", once.Cells!);
            Assert.Equal(new[] { "a", "b" }, twice.Cells![0]);
            Assert.Equal("c", twice.Cells[1][0]);
        }

        // ---------------------------------------------------------------- inverse

        [Fact]
        public void Inverse_TimesOriginal_IsIdentity()
        {
            // A = [a b; c d] at (2,3,5,7): A·A⁻¹ must be I, entry by entry.
            double[,] original = { { 2, 3 }, { 5, 7 } };
            var inverse = Run("inverse", Row("a", "b"), Row("c", "d")).Cells!;

            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                {
                    double sum = 0;
                    for (int k = 0; k < 2; k++) sum += original[r, k] * At(inverse[k][c]);
                    Assert.Equal(r == c ? 1 : 0, sum, 9);
                }
        }

        [Fact]
        public void Inverse_OfDiagonal_IsReciprocals()
        {
            var inverse = Run("inverse", Row("x", "0"), Row("0", "a")).Cells!;
            Assert.Equal(1.0 / 11, At(inverse[0][0]), 9);
            Assert.Equal(0.5, At(inverse[1][1]), 9);
            Assert.Equal(0, At(inverse[0][1]), 9);
        }

        [Fact]
        public void Inverse_OfSingular_IsRefused()
            => Assert.Contains("no inverse", Fail("inverse", Row("x", "a"), Row("x", "a")));

        [Fact]
        public void Inverse_RejectsNonSquare()
            => Assert.Contains("square", Fail("inverse", Row("a", "b")));

        // ---------------------------------------------------------------- rref and rank
        // These two cases are the ones AngouriMath 1.3.0 itself gets wrong (rank 2; NaN cells).
        // They are answered by the elimination written in this repo; see the Elimination tests.

        [Fact]
        public void Rank_OfRepeatedRow_IsOne_WhereTheLibraryItselfSaysTwo()
            => Assert.Equal("1", Run("rank", Row("x", "a"), Row("x", "a")).Scalar);

        [Fact]
        public void Rref_OfGenericTwoByTwo_IsIdentity_WhereTheLibraryItselfGivesNaN()
        {
            var rref = Run("rref", Row("a", "b"), Row("c", "d")).Cells!;
            Assert.Equal(1, At(rref[0][0]), 9);
            Assert.Equal(0, At(rref[0][1]), 9);
            Assert.Equal(0, At(rref[1][0]), 9);
            Assert.Equal(1, At(rref[1][1]), 9);
        }

        // ---------------------------------------------------------------- refusals

        [Fact]
        public void UnknownOperation_ListsWhatIsAvailable()
            => Assert.Contains("determinant", Fail("eigenvalues", Row("a", "b"), Row("c", "d")));

        [Fact]
        public void RaggedRows_AreRefused()
            => Assert.Contains("same length", Fail("transpose", Row("a", "b"), Row("c")));

        [Fact]
        public void EmptyMatrix_IsRefused()
            => Assert.Contains("empty", Fail("transpose"));

        [Fact]
        public void UnparseableCell_IsAFailureNotAnException()
            => Assert.False(_svc.SymbolicMatrix("determinant", new[] { Row("x +* 2") }).Success);
    }
}
