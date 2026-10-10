using System.Globalization;
using AngouriMath;
using RW.Service.Mathematics.Solvers;

namespace RW.Service.Mathematics.Tests
{
    /// <summary>
    /// Keeps these CPU-heavy cases from running alongside the other classes, whose own
    /// time-budgeted tests (a 65,536-row truth table) fail when the machine is saturated.
    /// </summary>
    [CollectionDefinition(nameof(EliminationCollection), DisableParallelization = true)]
    public class EliminationCollection { }

    /// <summary>Rank, RREF, null space, linear systems, trace and adjugate on symbolic matrices.</summary>
    /// <remarks>
    /// Answers are compared by value, not by spelling. Most cases go through one oracle: the
    /// matrix and the answer are both evaluated at a fixed generic point, and the answer must
    /// agree with plain floating-point Gauss-Jordan run on the evaluated matrix.
    /// </remarks>
    [Collection(nameof(EliminationCollection))]
    public class AngouriMathServiceEliminationTests
    {
        private readonly AngouriMathService _svc = new();

        // ------------------------------------------------------------------ helpers

        /// <summary>"a,b;c,d" is two rows of two cells.</summary>
        private static string[][] M(string text)
            => text.Split(';').Select(row => row.Split(',').Select(cell => cell.Trim()).ToArray()).ToArray();

        private SymbolicMatrixAnswer Run(string operation, string matrix)
        {
            var result = _svc.SymbolicElimination(operation, M(matrix));
            Assert.True(result.Success, $"{operation} of {matrix}: {result.Error}");
            return result.Value!;
        }

        private string Fail(string operation, string matrix)
        {
            var result = _svc.SymbolicElimination(operation, M(matrix));
            Assert.False(result.Success, $"{operation} of {matrix} should have been refused");
            return result.Error!;
        }

        /// <summary>A generic point: no value is special, and no two are related.</summary>
        private static readonly (string Name, string Value)[] Point =
        {
            ("a", "1.3"), ("b", "-2.7"), ("c", "0.45"), ("d", "3.1"), ("f", "-0.8"), ("g", "2.2"),
            ("h", "1.9"), ("j", "-1.4"), ("k", "0.6"), ("l", "2.9"), ("m", "-3.3"), ("n", "1.1"),
            ("p", "0.7"), ("q", "-1.7"), ("r", "2.3"), ("s", "0.9"), ("t", "1.6"), ("u", "-0.35"),
            ("w", "2.6"), ("x", "0.83"), ("y", "1.37"), ("z", "-2.15"),
        };

        private static double Eval(string expression, params (string Name, string Value)[] overrides)
        {
            Entity value = MathS.FromString(expression);
            foreach (var (name, number) in overrides.Concat(Point))
                value = value.Substitute(MathS.Var(name), MathS.FromString(number));
            var evaluated = value.EvalNumerical();
            Assert.True(evaluated.IsFinite, $"{expression} is not finite at the test point");
            Assert.True(Math.Abs(evaluated.ImaginaryPart.EDecimal.ToDouble()) < 1e-9, $"{expression} is not real at the test point");
            return evaluated.RealPart.EDecimal.ToDouble();
        }

        private static double[,] Eval(string[][] cells, params (string, string)[] overrides)
        {
            int columns = cells.Length == 0 ? 0 : cells[0].Length;
            var values = new double[cells.Length, columns];
            for (int r = 0; r < cells.Length; r++)
                for (int c = 0; c < columns; c++)
                    values[r, c] = Eval(cells[r][c], overrides);
            return values;
        }

        /// <summary>The oracle: floating-point Gauss-Jordan with partial pivoting.</summary>
        private static (double[,] Rref, int Rank) NumericRref(double[,] source)
        {
            var a = (double[,])source.Clone();
            int m = a.GetLength(0), n = a.GetLength(1), rank = 0;
            for (int col = 0; col < n && rank < m; col++)
            {
                int best = rank;
                for (int r = rank + 1; r < m; r++)
                    if (Math.Abs(a[r, col]) > Math.Abs(a[best, col])) best = r;
                if (Math.Abs(a[best, col]) < 1e-9) { for (int r = rank; r < m; r++) a[r, col] = 0; continue; }

                for (int c = 0; c < n; c++) (a[rank, c], a[best, c]) = (a[best, c], a[rank, c]);
                double pivot = a[rank, col];
                for (int c = 0; c < n; c++) a[rank, c] /= pivot;
                for (int r = 0; r < m; r++)
                {
                    if (r == rank) continue;
                    double factor = a[r, col];
                    for (int c = 0; c < n; c++) a[r, c] -= factor * a[rank, c];
                }
                rank++;
            }
            return (a, rank);
        }

        private static double[,] Multiply(double[,] left, double[,] right)
        {
            var product = new double[left.GetLength(0), right.GetLength(1)];
            for (int r = 0; r < product.GetLength(0); r++)
                for (int c = 0; c < product.GetLength(1); c++)
                    for (int k = 0; k < right.GetLength(0); k++)
                        product[r, c] += left[r, k] * right[k, c];
            return product;
        }

        private static void AssertSame(double[,] expected, double[,] actual, string what)
        {
            Assert.Equal(expected.GetLength(0), actual.GetLength(0));
            Assert.Equal(expected.GetLength(1), actual.GetLength(1));
            for (int r = 0; r < expected.GetLength(0); r++)
                for (int c = 0; c < expected.GetLength(1); c++)
                    Assert.True(Math.Abs(expected[r, c] - actual[r, c]) < 1e-7,
                        $"{what}: cell [{r},{c}] is {actual[r, c]}, expected {expected[r, c]}");
        }

        private static void AssertZero(double[,] actual, string what)
            => AssertSame(new double[actual.GetLength(0), actual.GetLength(1)], actual, what);

        // ------------------------------------------------------------------ the oracle sweep

        public static TheoryData<string, int> Matrices => new()
        {
            // numbers
            { "5", 1 }, { "0", 0 }, { "1,2;3,4", 2 }, { "1,2;2,4", 1 }, { "0,0;0,0", 0 },
            { "0,1;0,0", 1 }, { "0,0,1;0,0,0", 1 }, { "0,0;0,3;0,0", 1 },
            { "1,2,3;4,5,6;7,8,9", 2 }, { "2,1,-1,8;-3,-1,2,-11;-2,1,2,-3", 3 },
            { "1,2;3,4;5,6", 2 }, { "1,2,3;2,4,6", 1 }, { "1,2,3,4", 1 }, { "1;2;3", 1 },
            { "16,2,3,13;5,11,10,8;9,7,6,12;4,14,15,1", 3 },
            { "1,1/2,1/3;1/2,1/3,1/4;1/3,1/4,1/5", 3 },
            { "1/2,1/3;1/4,1/6", 1 }, { "1/2,1/3;1/4,1/5", 2 }, { "0.5,0.25;1,0.5", 1 },
            { "0,2,1;0,0,3;0,0,0", 2 }, { "0,0,2;0,3,1;4,0,0", 3 },
            { "1,2,3,4,5;2,4,6,8,10;1,0,1,0,1;0,1,0,1,0;3,3,3,3,3", 3 },
            { "2,0,0,0,0,0;0,2,0,0,0,0;0,0,2,0,0,0;0,0,0,2,0,0;0,0,0,0,2,0;1,1,1,1,1,1", 6 },
            // constants that are not variables
            { "e,pi;1,2", 2 }, { "e,e^2;1,e", 1 }, { "sqrt(2),2;1,sqrt(2)", 1 }, { "pi,2*pi;3,6", 1 },
            // symbols
            { "x", 1 }, { "x - x", 0 }, { "x,a;x,a", 1 }, { "a,b;c,d", 2 }, { "x,1;1,1", 2 },
            { "x,a;x*y,a*y", 1 }, { "a,b;a*x,b*x;a*y,b*y", 1 }, { "a,a*x,a*y", 1 },
            { "(x+1)^2,1;x^2+2*x+1,1", 1 }, { "(x+1)^2,x^2+2*x+1;1,1", 1 },
            { "x^2-1,x+1;x-1,1", 1 }, { "(x^2-1)/(x-1),1;x+1,1", 1 },
            { "sin(x)^2+cos(x)^2-1,1;0,x", 1 }, { "sin(x)^2+cos(x)^2,1;1,1", 1 },
            { "sin(x)^2+cos(x)^2-1,0;0,0", 0 },
            { "sin(x),cos(x);-cos(x),sin(x)", 2 }, { "sin(x),cos(x);sin(x)*cos(x),cos(x)^2", 1 },
            { "sin(2*x),1;2*sin(x)*cos(x),1", 1 }, { "e^x,e^(2*x);1,e^x", 1 },
            { "1/x,1;1,x", 1 }, { "x/2,1/3;x,2/3", 1 }, { "1/(x+1),x;1,x^2+x", 1 },
            { "a/b,c/d;a*d,b*c", 1 }, { "sqrt(x),x;1,sqrt(x)", 1 },
            { "a,b;c,d;f,g", 2 }, { "a,b,c;d,f,g", 2 }, { "a,b,c,d", 1 }, { "a;b;c", 1 },
            { "1,x,x^2;1,y,y^2;1,z,z^2", 3 }, { "1,x,x^2;1,y,y^2;1,x,x^2", 2 },
            { "a,b,c;d,x,y;a+d,b+x,c+y", 2 }, { "a,b,c;d,x,y;a*z+d,b*z+x,c*z+y", 2 },
            { "a,b,c;2*a,2*b,2*c;x*a,x*b,x*c", 1 }, { "x,0,0;0,y,0;0,0,z", 3 },
            { "0,a,b;0,0,c;0,0,0", 2 }, { "0,0,a;0,b,0;c,0,0", 3 }, { "x,y;0,0;z,a", 2 },
            { "a,b,c;d,f,g;h,j,k", 3 },
            { "a,b,c,d;f,g,h,j;k,l,m,n;p,q,r,s", 4 },
            { "a,b,c,d;f,g,h,j;k,l,m,n;a*x+f,b*x+g,c*x+h,d*x+j", 3 },
            { "a,b,c,d;f,g,h,j;a+f,b+g,c+h,d+j;a-f,b-g,c-h,d-j", 2 },
            { "a,b,c,d;a*x,b*x,c*x,d*x;a*y,b*y,c*y,d*y;a*z,b*z,c*z,d*z", 1 },
            { "x,1,0,0;0,x,1,0;0,0,x,1;0,0,0,x", 4 }, { "1,x,0,0;x,x^2,0,0;0,0,1,y;0,0,y,y^2", 2 },
            { "a,b,c,d,f;g,h,j,k,l;m,n,p,q,r", 3 }, { "a,b,c;d,f,g;h,j,k;l,m,n", 3 },
        };

        [Theory, MemberData(nameof(Matrices))]
        public void Rank_AgreesWithTheOracle(string matrix, int expected)
        {
            // The expected rank is hand-computed; the oracle must agree with it too, or the
            // test point is not generic for this matrix.
            Assert.Equal(expected, NumericRref(Eval(M(matrix))).Rank);
            Assert.Equal(expected.ToString(), Run("rank", matrix).Scalar);
        }

        [Theory, MemberData(nameof(Matrices))]
        public void Rref_AgreesWithTheOracle(string matrix, int _)
        {
            var answer = Run("rref", matrix);
            AssertSame(NumericRref(Eval(M(matrix))).Rref, Eval(answer.Cells!), $"rref of {matrix}");
            Assert.False(string.IsNullOrWhiteSpace(answer.Latex));
        }

        [Theory, MemberData(nameof(Matrices))]
        public void Rref_IsIdempotent(string matrix, int _)
        {
            var once = Run("rref", matrix).Cells!;
            var twice = _svc.SymbolicElimination("rref", once);
            Assert.True(twice.Success, twice.Error);
            AssertSame(Eval(once), Eval(twice.Value!.Cells!), $"rref of rref of {matrix}");
        }

        [Theory, MemberData(nameof(Matrices))]
        public void NullSpace_IsAnnihilated_AndHasTheRightDimension(string matrix, int rank)
        {
            var cells = M(matrix);
            var basis = Run("nullspace", matrix).Cells!;
            int unknowns = cells[0].Length;

            Assert.Equal(unknowns, basis.Length);
            Assert.All(basis, row => Assert.Equal(unknowns - rank, row.Length));
            if (unknowns == rank) return;

            var vectors = Eval(basis);
            AssertZero(Multiply(Eval(cells), vectors), $"A·nullspace for {matrix}");
            // Independent: the basis, as a matrix, has as many pivots as it has columns.
            Assert.Equal(unknowns - rank, NumericRref(vectors).Rank);
        }

        [Theory, MemberData(nameof(Matrices))]
        public void Solve_SatisfiesTheSystem_OrSaysWhyNot(string matrix, int rank)
        {
            var cells = M(matrix);
            int unknowns = cells[0].Length - 1;
            if (unknowns == 0)
            {
                Assert.Contains("augmented", Fail("solve", matrix));
                return;
            }

            string[][] left = cells.Select(row => row[..unknowns]).ToArray();
            string[][] right = cells.Select(row => row[unknowns..]).ToArray();
            int leftRank = NumericRref(Eval(left)).Rank;
            var answer = Run("solve", matrix);

            if (leftRank < rank)
            {
                Assert.Equal("none", answer.Kind);
                Assert.Null(answer.Cells);
                return;
            }

            Assert.Equal(leftRank == unknowns ? "unique" : "infinite", answer.Kind);
            Assert.Equal(unknowns, answer.Cells!.Length);
            Assert.All(answer.Cells, row => Assert.Equal(1 + unknowns - leftRank, row.Length));

            var solution = Eval(answer.Cells);
            var image = Multiply(Eval(left), solution);
            var expected = new double[image.GetLength(0), image.GetLength(1)];
            var rhs = Eval(right);
            for (int r = 0; r < expected.GetLength(0); r++) expected[r, 0] = rhs[r, 0];
            // Column 0 must hit the right-hand side; every other column must be sent to zero.
            AssertSame(expected, image, $"A·[particular | directions] for {matrix}");
            Assert.Equal(1 + unknowns - leftRank - (IsZeroColumn(solution, 0) ? 1 : 0), NumericRref(solution).Rank);
        }

        private static double Determinant(double[,] a)
        {
            int n = a.GetLength(0);
            if (n == 1) return a[0, 0];
            double sum = 0;
            for (int skip = 0; skip < n; skip++)
            {
                var minor = new double[n - 1, n - 1];
                for (int r = 1; r < n; r++)
                    for (int c = 0, to = 0; c < n; c++)
                        if (c != skip) minor[r - 1, to++] = a[r, c];
                sum += (skip % 2 == 0 ? 1 : -1) * a[0, skip] * Determinant(minor);
            }
            return sum;
        }

        private static bool IsZeroColumn(double[,] grid, int column)
            => Enumerable.Range(0, grid.GetLength(0)).All(r => Math.Abs(grid[r, column]) < 1e-9);

        [Theory, MemberData(nameof(Matrices))]
        public void Adjugate_TimesOriginal_IsDeterminantTimesIdentity(string matrix, int rank)
        {
            var cells = M(matrix);
            if (cells.Length != cells[0].Length)
            {
                Assert.Contains("square", Fail("adjugate", matrix));
                Assert.Contains("square", Fail("trace", matrix));
                return;
            }

            int n = cells.Length;
            var original = Eval(cells);
            var product = Multiply(original, Eval(Run("adjugate", matrix).Cells!));

            // Not SymbolicMatrix("determinant"): on a symbolic 4×4 that runs out of budget.
            double determinant = Determinant(original);
            Assert.Equal(rank < n, Math.Abs(determinant) < 1e-9);

            var expected = new double[n, n];
            for (int k = 0; k < n; k++) expected[k, k] = determinant;
            AssertSame(expected, product, $"A·adj(A) for {matrix}");

            double trace = 0;
            for (int k = 0; k < n; k++) trace += original[k, k];
            Assert.Equal(trace, Eval(Run("trace", matrix).Scalar!), 7);
        }

        // ------------------------------------------------------------------ hand-computed, exact

        [Fact]
        public void Rref_OfTheTextbookSystem_IsExact()
        {
            var rref = Run("rref", "2,1,-1,8;-3,-1,2,-11;-2,1,2,-3").Cells!;
            Assert.Equal(new[] { "1", "0", "0", "2" }, rref[0]);
            Assert.Equal(new[] { "0", "1", "0", "3" }, rref[1]);
            Assert.Equal(new[] { "0", "0", "1", "-1" }, rref[2]);
        }

        [Fact]
        public void Rref_OfNumbers_KeepsExactFractions()
        {
            // [1 2 3; 4 5 6; 7 8 9] reduces to [1 0 -1; 0 1 2; 0 0 0].
            var rref = Run("rref", "1,2,3;4,5,6;7,8,9").Cells!;
            Assert.Equal(new[] { "1", "0", "-1" }, rref[0]);
            Assert.Equal(new[] { "0", "1", "2" }, rref[1]);
            Assert.Equal(new[] { "0", "0", "0" }, rref[2]);

            // [2 1; 0 0] reduces to [1 1/2; 0 0].
            Assert.Equal("1/2", Run("rref", "2,1;0,0").Cells![0][1]);
        }

        [Fact]
        public void Rref_OfGenericTwoByTwo_IsExactlyTheIdentity()
        {
            // AngouriMath's own RREF answers NaN here.
            var rref = Run("rref", "a,b;c,d");
            Assert.Equal(new[] { "1", "0" }, rref.Cells![0]);
            Assert.Equal(new[] { "0", "1" }, rref.Cells[1]);
        }

        [Fact]
        public void Rref_OfGenericFourByFour_IsExactlyTheIdentity()
        {
            var rref = Run("rref", "a,b,c,d;f,g,h,j;k,l,m,n;p,q,r,s").Cells!;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    Assert.Equal(r == c ? "1" : "0", rref[r][c]);
        }

        [Fact]
        public void Rank_OfRepeatedSymbolicRow_IsOne()
            // AngouriMath's own Rank answers 2 here.
            => Assert.Equal("1", Run("rank", "x,a;x,a").Scalar);

        [Fact]
        public void Rref_OfRepeatedSymbolicRow_IsOneRowAndAZeroRow()
        {
            var rref = Run("rref", "x,a;x,a").Cells!;
            Assert.Equal("1", rref[0][0]);
            Assert.Equal(1.3 / 0.83, Eval(rref[0][1]), 9);
            Assert.Equal(new[] { "0", "0" }, rref[1]);
        }

        [Fact]
        public void Rref_LeavesZerosThatAreExactlyZero()
        {
            // The free column depends on the first pivot only; the second row's entry is 0, not
            // an expression that happens to evaluate to 0.
            var rref = Run("rref", "a,a*x,b;c,c*x,d").Cells!;
            Assert.Equal(0.83, Eval(rref[0][1]), 9);
            Assert.Equal("0", rref[1][1]);
        }

        [Fact]
        public void NullSpace_OfOneTwoTwoFour_IsMinusTwoOne()
        {
            var basis = Run("nullspace", "1,2;2,4").Cells!;
            Assert.Equal(new[] { "-2" }, basis[0]);
            Assert.Equal(new[] { "1" }, basis[1]);
        }

        [Fact]
        public void NullSpace_OfZeroMatrix_IsTheStandardBasis()
        {
            var basis = Run("nullspace", "0,0,0;0,0,0").Cells!;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    Assert.Equal(r == c ? "1" : "0", basis[r][c]);
        }

        [Fact]
        public void NullSpace_OfInvertible_IsTrivial()
        {
            var answer = Run("nullspace", "a,b;c,d");
            Assert.Equal(2, answer.Cells!.Length);
            Assert.All(answer.Cells, row => Assert.Empty(row));
            Assert.Contains("0", answer.Latex);
        }

        [Fact]
        public void Solve_Unique_Numeric()
        {
            var answer = Run("solve", "2,1,-1,8;-3,-1,2,-11;-2,1,2,-3");
            Assert.Equal("unique", answer.Kind);
            Assert.Equal(new[] { "2", "3", "-1" }, answer.Cells!.Select(row => row.Single()).ToArray());
            Assert.Null(answer.Condition);
            Assert.Null(answer.Notes);
        }

        [Fact]
        public void Solve_Unique_Symbolic_IsCramersRule()
        {
            // a·u + b·v = x,  c·u + d·v = y
            var answer = Run("solve", "a,b,x;c,d,y");
            double a = 1.3, b = -2.7, c = 0.45, d = 3.1, x = 0.83, y = 1.37, det = a * d - b * c;
            Assert.Equal("unique", answer.Kind);
            Assert.Equal((x * d - b * y) / det, Eval(answer.Cells![0][0]), 9);
            Assert.Equal((a * y - c * x) / det, Eval(answer.Cells[1][0]), 9);
            Assert.Equal(det, Math.Abs(Eval(answer.Condition!)), 9);
        }

        [Fact]
        public void Solve_Infinite_GivesParticularPlusOneDirectionPerFreeUnknown()
        {
            // u + v + w = 6,  v + w = 3   →   (3, 3, 0) + t·(0, -1, 1)
            var answer = Run("solve", "1,1,1,6;0,1,1,3");
            Assert.Equal("infinite", answer.Kind);
            Assert.Equal(new[] { "3", "0" }, answer.Cells![0]);
            Assert.Equal(new[] { "3", "-1" }, answer.Cells[1]);
            Assert.Equal(new[] { "0", "1" }, answer.Cells[2]);
            Assert.Contains("t_{1}", answer.Latex);
        }

        [Fact]
        public void Solve_None_Numeric()
        {
            var answer = Run("solve", "1,1,1;1,1,2");
            Assert.Equal("none", answer.Kind);
            Assert.Null(answer.Cells);
            Assert.Equal("no solution", answer.Scalar);
        }

        [Theory]
        [InlineData("0,0,0", "infinite")]
        [InlineData("0,0,1", "none")]
        [InlineData("0,5", "none")]
        [InlineData("0,0", "infinite")]
        [InlineData("2,6", "unique")]
        [InlineData("1,0,2;0,1,3;0,0,0", "unique")]       // a redundant equation
        [InlineData("1,0,2;0,1,3;1,1,5", "unique")]       // a dependent equation
        [InlineData("1,0,2;0,1,3;1,1,6", "none")]         // an overdetermined contradiction
        [InlineData("x,x^2,x^3;1,x,x^2", "infinite")]     // the same equation twice, in disguise
        [InlineData("x,x^2,x^3;1,x,x^2+1", "none")]
        [InlineData("sin(x)^2,1,1;1-cos(x)^2,1,1", "infinite")]
        [InlineData("a,b,0;c,d,0", "unique")]             // homogeneous: only the zero solution
        public void Solve_Classifies(string matrix, string kind)
            => Assert.Equal(kind, Run("solve", matrix).Kind);

        [Fact]
        public void Solve_Homogeneous_ParticularSolutionIsZero()
            => Assert.All(Run("solve", "a,b,0;c,d,0").Cells!, row => Assert.Equal("0", row.Single()));

        [Fact]
        public void Trace_IsTheDiagonalSum()
        {
            Assert.Equal("5/6", Run("trace", "1/2,7;9,1/3").Scalar);
            Assert.Equal(1.3 + 3.1, Eval(Run("trace", "a,b;c,d").Scalar!), 9);
            Assert.Equal("0", Run("trace", "x,1;1,-x").Scalar);
        }

        [Fact]
        public void Adjugate_OfTwoByTwo_SwapsAndNegates()
        {
            var adj = Run("adjugate", "a,b;c,d").Cells!;
            Assert.Equal(new[] { "d", "-b" }, adj[0]);
            Assert.Equal(new[] { "-c", "a" }, adj[1]);
        }

        [Fact]
        public void Adjugate_OfOneByOne_IsOne()
            => Assert.Equal("1", Run("adjugate", "x").Cells![0][0]);

        [Fact]
        public void Adjugate_OfNumbers_IsExact()
        {
            // adj [1 2; 3 4] = [4 -2; -3 1]
            var adj = Run("adjugate", "1,2;3,4").Cells!;
            Assert.Equal(new[] { "4", "-2" }, adj[0]);
            Assert.Equal(new[] { "-3", "1" }, adj[1]);
        }

        // ------------------------------------------------------------------ the zero test

        [Theory]
        [InlineData("(x+1)^2 - (x^2+2*x+1)")]
        [InlineData("sin(x)^2 + cos(x)^2 - 1")]
        [InlineData("x*y - y*x")]
        [InlineData("(x^2-1)/(x-1) - x - 1")]
        [InlineData("sin(2*x) - 2*sin(x)*cos(x)")]
        [InlineData("cos(2*x) - cos(x)^2 + sin(x)^2")]
        [InlineData("tan(x) - sin(x)/cos(x)")]
        [InlineData("e^(x+y) - e^x*e^y")]
        [InlineData("(a+b)^3 - a^3 - 3*a^2*b - 3*a*b^2 - b^3")]
        [InlineData("1/(x*(x+1)) - 1/x + 1/(x+1)")]
        [InlineData("sqrt(2)^2 - 2")]
        [InlineData("e^(i*pi) + 1")]
        [InlineData("sin(pi)")]
        [InlineData("cosh(x)^2 - sinh(x)^2 - 1")]
        [InlineData("(a*d - b*c)*(a*d + b*c) - a^2*d^2 + b^2*c^2")]
        public void HiddenZero_IsFound(string zero)
        {
            Assert.Equal("0", Run("rank", zero).Scalar);
            // The same zero as a would-be pivot: the second column must become the pivot.
            var rref = Run("rref", $"{zero},1;0,0").Cells!;
            Assert.Equal(new[] { "0", "1" }, rref[0]);
        }

        [Theory]
        [InlineData("x - 1")]
        [InlineData("x^2 + 1")]
        [InlineData("sin(x)")]
        [InlineData("sin(x)^2 + cos(x)^2 - 2")]
        [InlineData("(x+1)^2 - x^2 - 2*x")]
        [InlineData("10^(-30)*sin(x)")]          // below the engine's own 1e-16 cut-off, still not zero
        [InlineData("x/10^40")]
        [InlineData("1/x")]
        [InlineData("i")]
        [InlineData("e - 2718281828/1000000000")]
        public void NonZero_IsNotMistakenForZero(string value)
            => Assert.Equal("1", Run("rank", value).Scalar);

        [Fact]
        public void ZeroConfirmedBySimplification_LeavesNoNotes()
            => Assert.Null(Run("rank", "sin(x)^2+cos(x)^2-1,0;0,0").Notes);

        [Fact]
        public void ZeroTooLargeToConfirm_IsStillZero_AndSaysSo()
        {
            // Row 4 = x·row 1 + row 2, so the 4×4 minor vanishes, but it is too large to hand to
            // Simplify. The probes decide, and the answer admits that nothing confirmed them.
            const string matrix =
                "sin(a),cos(b),c^2,d;e^f,g,ln(h+3),j;k,sqrt(l+2),m,n;" +
                "x*sin(a)+e^f,x*cos(b)+g,x*c^2+ln(h+3),x*d+j";
            var rank = Run("rank", matrix);
            Assert.Equal("3", rank.Scalar);
            Assert.Contains(rank.Notes!, note => note.Contains("numeric evidence only"));

            var basis = Run("nullspace", matrix).Cells!;
            Assert.All(basis, row => Assert.Single(row));
            AssertZero(Multiply(Eval(M(matrix)), Eval(basis)), "A·nullspace");
        }

        [Fact]
        public void SameQuestion_SameAnswer()
        {
            const string matrix = "a,b,c;d,x,y;a*z+d,b*z+x,c*z+y";
            Assert.Equal(Run("rref", matrix).Cells, Run("rref", matrix).Cells);
        }

        // ------------------------------------------------------------------ generic answers

        [Fact]
        public void Condition_NamesWhatMustNotVanish()
        {
            var rank = Run("rank", "a,b;c,d");
            Assert.Equal(Math.Abs(1.3 * 3.1 - (-2.7) * 0.45), Math.Abs(Eval(rank.Condition!)), 9);
            Assert.Null(Run("rank", "1,2;3,4").Condition);
        }

        [Fact]
        public void Limit_RankIsGeneric_NotPointwise()
        {
            // [x 1; 1 1] has rank 1 at x = 1. The answer is 2, and the condition says where
            // that stops being true.
            var rank = Run("rank", "x,1;1,1");
            Assert.Equal("2", rank.Scalar);
            Assert.Equal(0, Eval(rank.Condition!, ("x", "1")), 9);
            Assert.NotEqual(0, Eval(rank.Condition!), 9);
        }

        [Fact]
        public void Limit_RrefIsGeneric_NotPointwise()
        {
            // Identity generically; at a = 0 the true RREF is [0 1; 0 0] (with b, c, d ≠ 0 it is
            // still the identity, but the condition a·d - b·c ≠ 0 is what the answer rests on).
            var rref = Run("rref", "x,1;1,1");
            Assert.Equal(new[] { "1", "0" }, rref.Cells![0]);
            Assert.Equal(0, Eval(rref.Condition!, ("x", "1")), 9);
        }

        [Fact]
        public void Limit_InconsistencyIsGeneric()
        {
            // u + v = a and u + v = b has solutions exactly when a = b. Reported: none.
            var answer = Run("solve", "1,1,a;1,1,b");
            Assert.Equal("none", answer.Kind);
            Assert.Equal(0, Eval(answer.Condition!, ("b", "1.3")), 9);
        }

        [Fact]
        public void Limit_UniqueSolutionIsGeneric()
        {
            // x·u = 1 has no solution at x = 0. Reported: unique, u = 1/x, valid when x ≠ 0.
            var answer = Run("solve", "x,1");
            Assert.Equal("unique", answer.Kind);
            Assert.Equal(1 / 0.83, Eval(answer.Cells![0][0]), 9);
            Assert.Equal(0, Eval(answer.Condition!, ("x", "0")), 9);
        }

        [Fact]
        public void Limit_ZeroOnlyOnARegion_CountsAsNonZero()
            // abs(x) - x is 0 for every x ≥ 0 and non-zero for x < 0. Reported rank: 1.
            => Assert.Equal("1", Run("rank", "abs(x) - x").Scalar);

        [Fact]
        public void Limit_IdentitiesThatNeedPositiveArguments_AreNotIdentities()
        {
            // ln(x·y) = ln(x) + ln(y) and sqrt(x^2) = x hold for positive reals only; the probes
            // include negative values, so neither difference is treated as zero.
            Assert.Equal("1", Run("rank", "ln(x*y) - ln(x) - ln(y)").Scalar);
            Assert.Equal("1", Run("rank", "sqrt(x^2) - x").Scalar);
        }

        [Fact]
        public void Limit_SmallerThanTheCutOff_IsCalledZero()
            // Non-rational and under 1e-60 in magnitude: indistinguishable from rounding.
            => Assert.Equal("0", Run("rank", "sin(x)/10^70").Scalar);

        [Fact]
        public void Limit_DigitsInNamesAreExponents()
        {
            // An AngouriMath parsing rule, not something this code decides: "a2" is a^2, so
            // [a1 1; a2 a] is [a 1; a^2 a], which has rank 1 — not the rank 2 a reader who
            // meant two different variables would expect.
            Assert.Equal("1", Run("rank", "a1,1;a2,a").Scalar);
            Assert.Equal("2", Run("rank", "a_1,1;a_2,a").Scalar);
        }

        [Fact]
        public void ReservedConstants_AreNumbersNotUnknowns()
        {
            // i·i = -1, so [i -1; 1 i] is singular; with i a free variable it would not be.
            Assert.Equal("1", Run("rank", "i,-1;1,i").Scalar);
            Assert.Null(Run("rank", "e,pi;1,2").Condition);
        }

        // ------------------------------------------------------------------ refusals and limits

        [Fact]
        public void UnknownOperation_ListsWhatIsAvailable()
            => Assert.Contains("nullspace", Fail("eigenvalues", "a,b;c,d"));

        [Fact]
        public void RaggedRows_AreRefused()
            => Assert.Contains("same length", Fail("rref", "a,b;c"));

        [Fact]
        public void EmptyMatrix_IsRefused()
            => Assert.Contains("empty", _svc.SymbolicElimination("rank", Array.Empty<string[]>()).Error);

        [Fact]
        public void UnparseableCell_IsAFailureNotAnException()
            => Assert.False(_svc.SymbolicElimination("rank", M("x +* 2")).Success);

        [Fact]
        public void DivisionByZeroInACell_IsRefused()
            => Assert.Contains("finite", Fail("rank", "1/0,1;1,1"));

        [Fact]
        public void TooLarge_IsRefused()
        {
            string row = string.Join(",", Enumerable.Repeat("1", 9));
            Assert.Contains("largest", Fail("rank", row));
        }

        [Fact]
        public void TheGeneralEntryPoint_RoutesEliminationHere()
        {
            // SymbolicMatrix is the one door the web layer uses; it must reach these operations.
            Assert.True(_svc.SymbolicMatrix("rref", M("a,b;c,d")).Success);
            Assert.Equal("1", _svc.SymbolicMatrix("rank", M("x,a;x,a")).Value!.Scalar);
            Assert.Contains("rref", AngouriMathService.SymbolicMatrixOperations);
        }

        // ------------------------------------------------------------------ time

        [Theory]
        [InlineData("rref", "a,b,c,d;f,g,h,j;k,l,m,n;p,q,r,s")]
        [InlineData("rank", "a,b,c,d;f,g,h,j;k,l,m,n;a*x+f,b*x+g,c*x+h,d*x+j")]
        [InlineData("nullspace", "a,b,c,d;f,g,h,j;k,l,m,n;a*x+f,b*x+g,c*x+h,d*x+j")]
        [InlineData("solve", "a,b,c,d,t;f,g,h,j,u;k,l,m,n,w;p,q,r,s,x")]
        [InlineData("adjugate", "a,b,c,d;f,g,h,j;k,l,m,n;p,q,r,s")]
        [InlineData("rref", "sin(x),cos(y),e^z,a;ln(b+3),sqrt(c+2),d,x*y;z^2,a*b,c/d,1;x,y,z,a")]
        [InlineData("rank", "a,b,c,d,f;g,h,j,k,l;m,n,p,q,r;s,t,u,w,x;y,z,a,b,c")]
        public void SymbolicFourByFour_FinishesInsideTheBudget(string operation, string matrix)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = _svc.SymbolicElimination(operation, M(matrix));
            Assert.True(result.Success, result.Error);
            Assert.True(clock.Elapsed < AngouriMathService.OperationBudget, $"took {clock.Elapsed}");
        }

        [Fact]
        public void LargestTranscendentalInput_StaysInsideTheBudget()
        {
            // 8×8 with every cell a different transcendental expression in eight variables.
            string[] names = { "a", "b", "c", "d", "x", "y", "z", "t" };
            var cells = Enumerable.Range(0, 8).Select(r => Enumerable.Range(0, 8)
                .Select(c => $"sin({names[r]}*{names[c]} + {r + 1}) + {names[(r + c) % 8]}^{c + 1}").ToArray()).ToArray();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = _svc.SymbolicElimination("rref", cells);
            Assert.True(clock.Elapsed < AngouriMathService.OperationBudget + TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
            // Giving up would be acceptable; a hang is not. As it happens this is answered:
            // full rank, so the RREF is the identity and no large cell is ever written out.
            if (!result.Success) Assert.Contains("Gave up", result.Error);
            else Assert.Equal("1", result.Value!.Cells![7][7]);
        }
    }
}
