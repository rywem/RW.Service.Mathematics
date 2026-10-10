using RW.Service.Mathematics.Solvers;

namespace RW.Service.Mathematics.Tests
{
    /// <summary>
    /// Exact rational-function linear algebra as the service exposes it, and cross-checks of
    /// its determinant and inverse against the independent AngouriMath path.
    /// </summary>
    public class AngouriMathServiceExactMatrixTests
    {
        private readonly AngouriMathService _svc = new();

        private static string[][] Mat(string text)
            => text.Split(';').Select(row => row.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();

        private SymbolicMatrixAnswer Run(string operation, string matrix)
        {
            var result = _svc.ExactMatrix(operation, Mat(matrix));
            Assert.True(result.Success, result.Error);
            return result.Value!;
        }

        private string Fail(string operation, string matrix)
        {
            var result = _svc.ExactMatrix(operation, Mat(matrix));
            Assert.False(result.Success);
            return result.Error!;
        }

        private static string Flat(string[][]? cells)
            => string.Join("; ", cells!.Select(row => string.Join(" ", row.Select(c => c.Replace(" ", "")))));

        /// <summary>
        /// The exact rational value of an expression at a = 2, b = 3, c = 5, d = 7, x = 11, y = 13,
        /// as AngouriMath prints it ("1/11", "-4"). Both engines' answers go through the same
        /// evaluator, so equal strings mean equal numbers, with no floating point involved.
        /// </summary>
        private string At(string expression)
        {
            var values = new Dictionary<char, string>
                { ['a'] = "(2)", ['b'] = "(3)", ['c'] = "(5)", ['d'] = "(7)", ['x'] = "(11)", ['y'] = "(13)" };
            string substituted = string.Concat(expression.Select(ch => values.TryGetValue(ch, out var v) ? v : ch.ToString()));
            var evaluated = _svc.Evaluate($"({substituted})");
            Assert.True(evaluated.Success, $"{expression}: {evaluated.Error}");
            return evaluated.Result;
        }

        public static IEnumerable<object[]> CrossCheckMatrices => new[]
        {
            "a b; c d",
            "x 1; 1 x",
            "x a; a x",
            "x^2+1 x; a 1/(x+1)",
            "1/(x+1) 1/(x-1); x y",
            "a b c; b c a; c a b",
            "1 a a^2; 1 b b^2; 1 c c^2",
            "x 1 0; 1 x 1; 0 1 x",
            "a+b a-b; a-b a+b",
            "1/a 1/b; 1/c 1/d",
            "x y; y x",
            "a b c; 0 d x; 0 0 y",
            "x 1 2 3; 1 x 1 2; 2 1 x 1; 3 2 1 x",
            "2 1; 5 3",
            "x/(y+1) 1; a (b+c)/d",
        }.Select(m => new object[] { m });

        // ---------------------------------------------------------------- against AngouriMath
        //
        // Two directions. "Numeric": put the numbers into the matrix first and let AngouriMath
        // do plain rational arithmetic, which it does reliably; every matrix is checked this
        // way. "Symbolic": let AngouriMath work on the symbols and substitute afterwards; that
        // only covers the matrices where its Simplify finishes inside the time budget and is
        // right (see the two tests after these for where it is not).

        private const string Circulant = "a b c; b c a; c a b";
        private const string FourByFour = "x 1 2 3; 1 x 1 2; 2 1 x 1; 3 2 1 x";

        private static readonly string[] SlowSymbolicInverse =
        {
            FourByFour, "x/(y+1) 1; a (b+c)/d", "1 a a^2; 1 b b^2; 1 c c^2", "x 1 0; 1 x 1; 0 1 x",
            "1/(x+1) 1/(x-1); x y", "x^2+1 x; a 1/(x+1)",
        };

        public static IEnumerable<object[]> SymbolicDeterminantMatrices =>
            CrossCheckMatrices.Where(m => (string)m[0] is not (Circulant or FourByFour));

        public static IEnumerable<object[]> SymbolicInverseMatrices =>
            CrossCheckMatrices.Where(m => !SlowSymbolicInverse.Contains((string)m[0]));

        /// <summary>The matrix with every cell replaced by its exact value at the point.</summary>
        private string[][] Numeric(string matrix)
            => Mat(matrix).Select(row => row.Select(At).ToArray()).ToArray();

        [Theory, MemberData(nameof(CrossCheckMatrices))]
        public void Determinant_AtAPoint_EqualsAngouriMathsDeterminantOfTheNumericMatrix(string matrix)
        {
            var theirs = _svc.SymbolicMatrix("determinant", Numeric(matrix));
            Assert.True(theirs.Success, theirs.Error);
            Assert.Equal(At(theirs.Value!.Scalar!), At(Run("determinant", matrix).Scalar!));
        }

        [Theory, MemberData(nameof(CrossCheckMatrices))]
        public void Inverse_AtAPoint_EqualsAngouriMathsInverseOfTheNumericMatrix(string matrix)
        {
            var theirs = _svc.SymbolicMatrix("inverse", Numeric(matrix));
            Assert.True(theirs.Success, theirs.Error);
            AssertSameAtPoint(theirs.Value!.Cells!, Run("inverse", matrix).Cells!);
        }

        [Theory, MemberData(nameof(SymbolicDeterminantMatrices))]
        public void Determinant_AgreesWithAngouriMathsSymbolicDeterminant(string matrix)
        {
            var theirs = _svc.SymbolicMatrix("determinant", Mat(matrix));
            Assert.True(theirs.Success, theirs.Error);
            Assert.Equal(At(theirs.Value!.Scalar!), At(Run("determinant", matrix).Scalar!));
        }

        [Theory, MemberData(nameof(SymbolicInverseMatrices))]
        public void Inverse_AgreesWithAngouriMathsSymbolicInverse(string matrix)
        {
            var theirs = _svc.SymbolicMatrix("inverse", Mat(matrix));
            Assert.True(theirs.Success, theirs.Error);
            AssertSameAtPoint(theirs.Value!.Cells!, Run("inverse", matrix).Cells!);
        }

        private void AssertSameAtPoint(string[][] expected, string[][] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int r = 0; r < actual.Length; r++)
                for (int c = 0; c < actual[r].Length; c++)
                    Assert.Equal(At(expected[r][c]), At(actual[r][c]));
        }

        /// <summary>
        /// A disagreement, and AngouriMath is the one that is wrong. For the circulant matrix
        /// its simplified determinant is 6*b^5/(a*c - b^2) - a^3 + 3*a*b*c + 5*b^3 - c^3, which
        /// is 1550 at a = 2, b = 3, c = 5. The determinant is 3abc - a^3 - b^3 - c^3 = -70, as
        /// the cofactor expansion written out in numbers confirms. Pinned so that an upgrade
        /// which fixes it is noticed.
        /// </summary>
        [Fact]
        public void CirculantDeterminant_IsRight_WhereAngouriMathItselfIsWrong()
        {
            string truth = _svc.Evaluate("2*(5*3-2*2) - 3*(3*3-2*5) + 5*(3*2-5*5)").Result;
            Assert.Equal("-70", truth);

            string ours = Run("determinant", Circulant).Scalar!;
            Assert.Equal("-a^3 + 3*a*b*c - b^3 - c^3", ours);
            Assert.Equal(truth, At(ours));

            // The general entry point must give the same right answer: it is the one the web uses,
            // and it once returned AngouriMath's 1550 here.
            var general = _svc.SymbolicMatrix("determinant", Mat(Circulant));
            Assert.True(general.Success, general.Error);
            Assert.Equal(truth, At(general.Value!.Scalar!));

            // AngouriMath's own determinant, called directly, is still wrong. If this assertion
            // ever fails, an upgrade has fixed it — which changes nothing here, but is worth knowing.
            var grid = new AngouriMath.Entity[3, 3];
            var cells = Mat(Circulant);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++) grid[r, c] = cells[r][c];
            string theirs = AngouriMath.MathS.Matrix(grid).Determinant!.Simplify().Stringize();
            Assert.NotEqual(truth, At(theirs));
        }

        // ------------------------------------------------------------ the general entry point

        [Theory]
        [InlineData("a b; c d")]
        [InlineData("x 1; 1 x")]
        [InlineData("a b c; b c a; c a b")]
        [InlineData("x 1 2; 1 x 1; 2 1 x")]
        [InlineData("1/(x+1) x; 2 x^2")]
        [InlineData("1 2; 3 4")]
        public void GeneralEntryPoint_Determinant_AgreesWithNumbersSubstitutedFirst(string matrix)
        {
            // Substitute numbers, then take the determinant numerically: an independent route to
            // the same value, which no symbolic slip can also satisfy.
            var symbolic = _svc.SymbolicMatrix("determinant", Mat(matrix));
            Assert.True(symbolic.Success, symbolic.Error);
            var numeric = _svc.SymbolicMatrix("determinant", Numeric(matrix));
            Assert.True(numeric.Success, numeric.Error);
            Assert.Equal(At(numeric.Value!.Scalar!), At(symbolic.Value!.Scalar!));
        }

        [Theory]
        [InlineData("a b; c d")]
        [InlineData("x 1; 1 x")]
        [InlineData("x 1 2; 1 x 1; 2 1 x")]
        [InlineData("1/(x+1) x; 2 x^2")]
        public void GeneralEntryPoint_Inverse_AgreesWithNumbersSubstitutedFirst(string matrix)
        {
            var symbolic = _svc.SymbolicMatrix("inverse", Mat(matrix));
            Assert.True(symbolic.Success, symbolic.Error);
            var numeric = _svc.SymbolicMatrix("inverse", Numeric(matrix));
            Assert.True(numeric.Success, numeric.Error);
            AssertSameAtPoint(numeric.Value!.Cells!, symbolic.Value!.Cells!);
        }

        [Theory]
        [InlineData("x a; x a")]
        [InlineData("a b; c d")]
        [InlineData("x 1; 1 x")]
        [InlineData("a b c; b c a; c a b")]
        [InlineData("x 1 2; 1 x 1; 2 1 x")]
        [InlineData("x x^2 1; 1 x x; 2 2*x x+x")]
        [InlineData("a b c d; b a d c")]
        [InlineData("1/(x+1) x; 2 x^2; 1 1")]
        public void TwoIndependentImplementations_AgreeOnRankAndRref(string matrix)
        {
            // Exact polynomial arithmetic on one side, probed minors and Cramer quotients on the
            // other. They share no code, so agreement is real evidence and a disagreement means
            // one of them is wrong.
            var exactRank = _svc.ExactMatrix("rank", Mat(matrix));
            var probedRank = _svc.SymbolicElimination("rank", Mat(matrix));
            Assert.True(exactRank.Success, exactRank.Error);
            Assert.True(probedRank.Success, probedRank.Error);
            Assert.Equal(exactRank.Value!.Scalar, probedRank.Value!.Scalar);

            var exactRref = _svc.ExactMatrix("rref", Mat(matrix));
            var probedRref = _svc.SymbolicElimination("rref", Mat(matrix));
            Assert.True(exactRref.Success, exactRref.Error);
            Assert.True(probedRref.Success, probedRref.Error);
            AssertSameAtPoint(exactRref.Value!.Cells!, probedRref.Value!.Cells!);
        }

        [Fact]
        public void SymbolicFourByFour_ExactPathAnswersWhereAngouriMathRunsOutOfTime()
        {
            Assert.Equal("x^4 - 20*x^2 + 32*x - 12", Run("determinant", FourByFour).Scalar);
            Assert.Equal(4, Run("inverse", FourByFour).Cells!.Length);
        }

        [Theory]
        [InlineData("x a; x a")]
        [InlineData("1 2; 2 4")]
        [InlineData("x+1 x^2-1; 1 x-1")]
        public void SingularInverse_IsRefusedByBothPaths(string matrix)
        {
            Assert.False(_svc.SymbolicMatrix("inverse", Mat(matrix)).Success);
            Assert.Contains("no inverse", Fail("inverse", matrix));
        }

        // ---------------------------------------------------------------- what AngouriMath gets wrong

        [Fact]
        public void Rank_OfTwoEqualSymbolicRows_IsOne()
            => Assert.Equal("1", Run("rank", "x a; x a").Scalar);

        [Fact]
        public void Rref_OfTheGenericTwoByTwo_IsTheIdentity()
            => Assert.Equal("1 0; 0 1", Flat(Run("rref", "a b; c d").Cells));

        [Theory]
        [InlineData("x a; x a", "1 a/x; 0 0")]
        [InlineData("(x+1)^2-x^2-2x-1 1; 0 0", "0 1; 0 0")]
        [InlineData("1/(x+1) 1; 1 x+1", "1 x+1; 0 0")]
        [InlineData("1 2 3; 4 5 6; 7 8 9", "1 0 -1; 0 1 2; 0 0 0")]
        public void Rref(string matrix, string expected)
            => Assert.Equal(expected, Flat(Run("rref", matrix).Cells));

        [Theory]
        [InlineData("a b; c d", "2")]
        [InlineData("x 1; 1 1", "2")]          // generic: x = 1 is not singled out
        [InlineData("1 1; 1 1", "1")]
        [InlineData("0 0; 0 0", "0")]
        [InlineData("a b c; a*x b*x c*x", "1")]
        public void Rank(string matrix, string expected)
            => Assert.Equal(expected, Run("rank", matrix).Scalar);

        [Fact]
        public void NullSpace_IsOneBasisVectorPerColumn()
        {
            Assert.Equal("1; -2; 1", Flat(Run("nullspace", "1 2 3; 4 5 6; 7 8 9").Cells));
            Assert.Equal("(-a)/x; 1", Flat(Run("nullspace", "x a; x a").Cells));
            Assert.Equal("-x -y; 1 0; 0 1", Flat(Run("nullspace", "1 x y").Cells));
        }

        [Fact]
        public void NullSpace_OfAnInvertibleMatrix_IsTrivial()
        {
            var answer = Run("nullspace", "a b; c d");
            Assert.All(answer.Cells!, row => Assert.Empty(row));
            Assert.Contains("0", answer.Latex);
        }

        [Fact]
        public void Solve_ReportsTheKind_ThenParticularSolutionAndBasisAsColumns()
        {
            var unique = Run("solve", "1 1 3; 1 -1 1");
            Assert.Equal("unique", unique.Scalar);
            Assert.Equal("2; 1", Flat(unique.Cells));

            var parametric = Run("solve", "x a b; x a b");
            Assert.Equal("parametric", parametric.Scalar);
            Assert.Equal("b/x (-a)/x; 0 1", Flat(parametric.Cells));

            var inconsistent = Run("solve", "x a 1; x a 2");
            Assert.Equal("inconsistent", inconsistent.Scalar);
            Assert.Null(inconsistent.Cells);
        }

        [Fact]
        public void Determinant_AndInverse_ArePrintedInLowestTerms()
        {
            Assert.Equal("a*d - b*c", Run("determinant", "a b; c d").Scalar);
            Assert.Equal("0", Run("determinant", "x+1 x^2-1; 1 x-1").Scalar);
            Assert.Equal("1/(x-1) (-1)/(x-1); (-1)/(x-1) x/(x-1)", Flat(Run("inverse", "x 1; 1 1").Cells));
        }

        [Theory]
        [InlineData("rref", "a b; c d")]
        [InlineData("inverse", "x 1; 1 x")]
        [InlineData("determinant", "1/(x+1) 1; 1 x")]
        [InlineData("rank", "x a; x a")]
        [InlineData("nullspace", "x a; x a")]
        [InlineData("solve", "a b x; c d y")]
        public void EveryAnswer_CarriesLatex(string operation, string matrix)
            => Assert.False(string.IsNullOrWhiteSpace(Run(operation, matrix).Latex));

        // ---------------------------------------------------------------- refusals

        [Theory]
        [InlineData("sin(x) 1; 1 1", "sin")]
        [InlineData("sqrt(x) 1; 1 1", "sqrt")]
        [InlineData("x^(1/2) 1; 1 1", "not an integer")]
        [InlineData("1 2^x; 1 1", "not an integer")]
        [InlineData("1 1; pi 1", "pi")]
        [InlineData("1 1; 1 i", "imaginary")]
        [InlineData("1 1; 1 1/0", "divides by zero")]
        public void EntriesOutsideTheDomain_AreRefused_ForEveryOperation(string matrix, string reason)
        {
            foreach (var operation in AngouriMathService.ExactMatrixOperations)
            {
                string error = Fail(operation, matrix);
                Assert.Contains(reason, error);
                Assert.Contains("Row", error);
            }
        }

        [Fact]
        public void UnknownOperation_ListsTheAvailableOnes()
            => Assert.Contains("rref, rank, nullspace, solve, determinant, inverse", Fail("eigenvalues", "a b; c d"));

        [Fact]
        public void NonSquare_AndRagged_AndEmpty_AreRefused()
        {
            Assert.Contains("square", Fail("determinant", "a b c; d f g"));
            Assert.Contains("square", Fail("inverse", "a b c"));
            Assert.Contains("same length", _svc.ExactMatrix("rref", new[] { new[] { "a", "b" }, new[] { "c" } }).Error);
            Assert.Contains("empty", _svc.ExactMatrix("rref", Array.Empty<string[]>()).Error);
        }

        [Fact]
        public void OutsideTheRationalDomain_TheGeneralEntryPointFallsBackAndIsStillRight()
        {
            // sin and cos are refused by the exact path, so these go through the probed minors.
            // Checked at x = 0, where sin and cos are exact numbers (0 and 1).
            string AtZero(string expression)
            {
                var evaluated = _svc.Evaluate($"({expression.Replace("x", "0")})");
                Assert.True(evaluated.Success, $"{expression}: {evaluated.Error}");
                return evaluated.Result;
            }

            // det [sin cos; -cos sin] = sin² + cos² = 1.
            var det = _svc.SymbolicMatrix("determinant", Mat("sin(x) cos(x); -cos(x) sin(x)"));
            Assert.True(det.Success, det.Error);
            Assert.Equal("1", AtZero(det.Value!.Scalar!));

            // The inverse of a rotation is its transpose: [sin -cos; cos sin], i.e. [0 -1; 1 0] at 0.
            var inverse = _svc.SymbolicMatrix("inverse", Mat("sin(x) cos(x); -cos(x) sin(x)"));
            Assert.True(inverse.Success, inverse.Error);
            Assert.Equal(new[] { "0", "-1" }, inverse.Value!.Cells![0].Select(AtZero));
            Assert.Equal(new[] { "1", "0" }, inverse.Value.Cells[1].Select(AtZero));

            // Singular, and not a rational function: still refused, not answered with NaN.
            var singular = _svc.SymbolicMatrix("inverse", Mat("sin(x) sin(x); cos(x) cos(x)"));
            Assert.False(singular.Success);
            Assert.Contains("no inverse", singular.Error);
        }
    }
}
