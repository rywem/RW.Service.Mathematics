using System.Diagnostics;
using AngouriMath;
using static AngouriMath.Entity;

namespace RW.Service.Mathematics.Solvers
{
    /// <summary>
    /// Row reduction on matrices whose entries are expressions: rank, RREF, null space and
    /// linear systems, which AngouriMath 1.3.0 gets wrong on symbolic input.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How it works.</b> This is fraction-free (Bareiss) elimination written in closed form.
    /// Every entry Bareiss would produce at step k is a (k+1)×(k+1) minor of the original
    /// matrix, so instead of updating cells in place — and needing an exact polynomial division
    /// the engine does not have — each pivot candidate is built directly as that minor and
    /// handed to the zero test. Once the pivot rows and columns are known, each RREF cell is one
    /// Cramer quotient (minor / pivot minor), simplified once. Nothing is simplified
    /// repeatedly, so errors and expression growth do not compound from step to step.
    /// </para>
    /// <para>
    /// <b>Answers are generic.</b> The rank of <c>[x 1; 1 1]</c> is reported as 2 although it is
    /// 1 at x = 1. <see cref="SymbolicMatrixAnswer.Condition"/> carries the expression that
    /// must be non-zero for the answer to hold.
    /// </para>
    /// <para>
    /// <b>The zero test</b> is <see cref="Eliminator.IsZero"/>; read its remarks before
    /// trusting it further than it deserves.
    /// </para>
    /// </remarks>
    public partial class AngouriMathService
    {
        /// <summary>The operations <see cref="SymbolicElimination"/> accepts.</summary>
        public static readonly IReadOnlyList<string> SymbolicEliminationOperations =
            new[] { "rref", "rank", "nullspace", "solve", "trace", "adjugate", "determinant", "inverse" };

        /// <summary>Largest row or column count accepted; minors are expanded in 2^n steps.</summary>
        public const int SymbolicEliminationMaxSize = 8;

        /// <summary>
        /// Runs <paramref name="operation"/> on the matrix whose cells are the given expressions.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item><c>rref</c> — Cells is the reduced row echelon form.</item>
        /// <item><c>rank</c> — Scalar is the rank.</item>
        /// <item><c>nullspace</c> — Cells holds one basis vector per <i>column</i>; a trivial
        /// null space is n rows of zero cells.</item>
        /// <item><c>solve</c> — the last column is the right-hand side. Kind is "unique",
        /// "infinite" or "none". Column 0 of Cells is a particular solution and every further
        /// column is a direction a free parameter moves along; Cells is null for "none".</item>
        /// <item><c>trace</c> — Scalar. <c>adjugate</c> — Cells.</item>
        /// </list>
        /// </remarks>
        public AlgebraResult<SymbolicMatrixAnswer> SymbolicElimination(string operation, string[][] cells)
            => Attempt(operation, () =>
            {
                if (cells is not { Length: > 0 } || cells[0].Length == 0)
                    throw new ArgumentException("The matrix is empty.");
                if (cells.Any(row => row.Length != cells[0].Length))
                    throw new ArgumentException("Every row must be the same length.");

                int m = cells.Length, n = cells[0].Length;
                if (m > SymbolicEliminationMaxSize || n > SymbolicEliminationMaxSize)
                    throw new ArgumentException(
                        $"The matrix is {m}×{n}; the largest supported is " +
                        $"{SymbolicEliminationMaxSize}×{SymbolicEliminationMaxSize}.");

                var a = new Entity[m, n];
                for (int r = 0; r < m; r++)
                    for (int c = 0; c < n; c++)
                    {
                        a[r, c] = MathS.FromString(cells[r][c]).InnerSimplified;
                        if (a[r, c] is Number.Complex { IsFinite: false })
                            throw new ArgumentException($"The cell '{cells[r][c]}' is not a finite number.");
                    }

                var work = new Eliminator(a);

                switch (operation)
                {
                    case "trace":
                        if (m != n) throw new ArgumentException($"A trace needs a square matrix; this one is {m}×{n}.");
                        Entity sum = 0;
                        for (int k = 0; k < n; k++) sum += a[k, k];
                        return Scalar(work.Tidy(sum));

                    case "adjugate":
                    {
                        if (m != n) throw new ArgumentException($"An adjugate needs a square matrix; this one is {m}×{n}.");
                        var adj = new Entity[n, n];
                        for (int r = 0; r < n; r++)
                            for (int c = 0; c < n; c++)
                            {
                                // adj[r, c] is the cofactor of a[c, r]: delete row c and column r.
                                Entity minor = work.Minor(Without(n, c), Without(n, r));
                                adj[r, c] = work.Tidy((r + c) % 2 == 0 ? minor : -minor);
                            }
                        return Grid(adj, work);
                    }

                    case "determinant":
                    {
                        if (m != n) throw new ArgumentException($"A determinant needs a square matrix; this one is {m}×{n}.");
                        int[] all = Enumerable.Range(0, n).ToArray();
                        return Scalar(work.Tidy(work.Minor(all, all)));
                    }

                    case "inverse":
                    {
                        if (m != n) throw new ArgumentException($"An inverse needs a square matrix; this one is {m}×{n}.");
                        int[] all = Enumerable.Range(0, n).ToArray();
                        if (work.IsZeroMinor(all, all))
                            throw new ArgumentException("The determinant is 0, so the matrix has no inverse.");

                        // A⁻¹ = adj(A) / det(A), every entry one quotient of two minors.
                        Entity det = work.Minor(all, all);
                        var inverse = new Entity[n, n];
                        for (int r = 0; r < n; r++)
                            for (int c = 0; c < n; c++)
                            {
                                Entity minor = work.Minor(Without(n, c), Without(n, r));
                                inverse[r, c] = work.Tidy(((r + c) % 2 == 0 ? minor : -minor) / det);
                            }
                        return Grid(inverse, work) with { Notes = work.NotesOrNull };
                    }

                    case "rank":
                        work.FindPivots();
                        return Scalar(work.Pivots.Count) with { Condition = work.Condition(), Notes = work.NotesOrNull };

                    case "rref":
                        work.FindPivots();
                        return Grid(work.Rref(), work) with { Condition = work.Condition() };

                    case "nullspace":
                    {
                        work.FindPivots();
                        var basis = NullSpace(work.Rref(), work.Pivots, n);
                        if (basis.GetLength(1) > 0)
                            return Grid(basis, work) with { Condition = work.Condition() };
                        return new SymbolicMatrixAnswer(
                            Enumerable.Range(0, n).Select(_ => Array.Empty<string>()).ToArray(), null,
                            @"\left\{\mathbf{0}\right\}", Condition: work.Condition(), Notes: work.NotesOrNull);
                    }

                    case "solve":
                    {
                        if (n < 2)
                            throw new ArgumentException(
                                "Solving needs an augmented matrix: the coefficients, then one last column for the right-hand side.");
                        work.FindPivots();
                        if (work.Pivots.Contains(n - 1))
                            return new SymbolicMatrixAnswer(null, "no solution", @"\text{no solution}",
                                "none", work.Condition(), work.NotesOrNull);

                        var rref = work.Rref();
                        var directions = NullSpace(rref, work.Pivots, n - 1);
                        var solution = new Entity[n - 1, 1 + directions.GetLength(1)];
                        for (int r = 0; r < n - 1; r++)
                        {
                            int pivotRow = work.Pivots.IndexOf(r);
                            solution[r, 0] = pivotRow < 0 ? 0 : rref[pivotRow, n - 1];
                            for (int f = 0; f < directions.GetLength(1); f++)
                                solution[r, f + 1] = directions[r, f];
                        }

                        string latex = Column(solution, 0);
                        for (int f = 1; f < solution.GetLength(1); f++)
                            latex += $" + t_{{{f}}}" + Column(solution, f);

                        return Grid(solution, work) with
                        {
                            Latex = latex,
                            Kind = solution.GetLength(1) == 1 ? "unique" : "infinite",
                            Condition = work.Condition()
                        };
                    }

                    default:
                        throw new ArgumentException(
                            $"'{operation}' is not available for a matrix with symbolic entries. " +
                            $"Available: {string.Join(", ", SymbolicMatrixOperations)}.");
                }
            });

        private static int[] Without(int count, int skipped)
            => Enumerable.Range(0, count).Where(k => k != skipped).ToArray();

        /// <summary>
        /// One basis vector per free column among the first <paramref name="unknowns"/> columns:
        /// 1 in the free position, minus the RREF entry in each pivot position.
        /// </summary>
        private static Entity[,] NullSpace(Entity[,] rref, List<int> pivots, int unknowns)
        {
            int[] free = Enumerable.Range(0, unknowns).Where(c => !pivots.Contains(c)).ToArray();
            var basis = new Entity[unknowns, free.Length];
            for (int f = 0; f < free.Length; f++)
                for (int r = 0; r < unknowns; r++)
                {
                    int pivotRow = pivots.IndexOf(r);
                    basis[r, f] = r == free[f] ? 1 : pivotRow < 0 ? 0 : (-rref[pivotRow, free[f]]).InnerSimplified;
                }
            return basis;
        }

        private static string Column(Entity[,] grid, int column)
        {
            var cells = new Entity[grid.GetLength(0), 1];
            for (int r = 0; r < cells.GetLength(0); r++) cells[r, 0] = grid[r, column];
            return MathS.Matrix(cells).Latexise();
        }

        /// <summary>Cells that are already tidy; unlike <see cref="Grid(Matrix)"/> nothing is simplified again.</summary>
        private static SymbolicMatrixAnswer Grid(Entity[,] grid, Eliminator work)
        {
            var cells = new string[grid.GetLength(0)][];
            for (int r = 0; r < cells.Length; r++)
            {
                cells[r] = new string[grid.GetLength(1)];
                for (int c = 0; c < cells[r].Length; c++) cells[r][c] = grid[r, c].Stringize();
            }
            return new SymbolicMatrixAnswer(cells, null, MathS.Matrix(grid).Latexise(), Notes: work.NotesOrNull);
        }

        /// <summary>
        /// Runs <paramref name="step"/> with a slice of the operation's budget. Null if the
        /// slice ran out; if the whole budget ran out the cancellation propagates as usual.
        /// </summary>
        /// <remarks>
        /// <c>Simplify</c> has no useful upper bound — a 3×3 Cramer quotient plus one
        /// trigonometric term was measured at 49 seconds — so no single call to it may be
        /// allowed to spend the whole budget when a cruder answer is still a correct one.
        /// </remarks>
        private static Entity? Within(TimeSpan slice, Func<Entity> step)
        {
            if (slice <= TimeSpan.Zero) return null;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(_budget);
            limit.CancelAfter(slice);
            MathS.Multithreading.SetLocalCancellationToken(limit.Token);
            try { return step(); }
            catch (OperationCanceledException) when (!_budget.IsCancellationRequested) { return null; }
            finally { MathS.Multithreading.SetLocalCancellationToken(_budget); }
        }

        private sealed class Eliminator
        {
            private const int Probes = 5;

            // Symbolic confirmation of zeros stops at the first mark, tidying of output cells at
            // the second; what is left of the 10-second budget is slack for the exact work.
            private static readonly TimeSpan ConfirmUntil = TimeSpan.FromSeconds(4);
            private static readonly TimeSpan TidyUntil = TimeSpan.FromSeconds(8);
            private static readonly TimeSpan PerCall = TimeSpan.FromSeconds(1.5);

            // Simplify is only asked about expressions up to this size. Its cancellation is not
            // prompt on large ones: a 4×4 minor of trigonometric cells (complexity 671) was
            // measured still running 18 seconds after its token fired, which would outlive the
            // operation's budget as a leaked thread. A generic 4×4 minor of plain symbols is 127.
            private const int LargestSimplified = 300;

            // About a 5×5 minor of single-letter cells. Past that the condition is megabytes.
            private const int LargestConditionShown = 3000;

            // With 100-digit arithmetic, anything under 1e-60 after cancellation is rounding.
            // The engine's default of 1e-16 would call 10^-30 * sin(x) zero.
            private static readonly PeterO.Numbers.EDecimal Negligible = PeterO.Numbers.EDecimal.FromString("1e-60");

            private readonly Entity[,] _a;
            private readonly Variable[] _variables;
            private readonly Entity[][] _points;

            /// <summary>The matrix evaluated at each probe point where every cell is a finite number.</summary>
            private readonly List<Entity[,]> _probed = new();
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private readonly List<string> _notes = new();

            /// <summary>Pivot rows in the order they were chosen, and their columns, ascending.</summary>
            public List<int> Rows { get; } = new();
            public List<int> Pivots { get; } = new();

            /// <summary>The minor on <see cref="Rows"/> × <see cref="Pivots"/>; non-zero by the zero test.</summary>
            private Entity? _pivotMinorCache;
            private Entity PivotMinor => _pivotMinorCache ??= Minor(Rows, Pivots);
            private int _tooLargeToTidy = int.MaxValue;

            public IReadOnlyList<string>? NotesOrNull => _notes.Count == 0 ? null : _notes.Distinct().ToArray();

            public Eliminator(Entity[,] a)
            {
                _a = a;
                _variables = a.Cast<Entity>().SelectMany(cell => cell.Vars).Distinct()
                    .OrderBy(v => v.Name, StringComparer.Ordinal).ToArray();

                // Fixed seed: the same matrix must get the same answer every time. Rationals
                // with prime denominators, so polynomial and rational entries are evaluated
                // exactly; random signs, so abs(x) - x is caught on some probe.
                var random = new Random(20261010);
                int[] primes = { 101, 103, 107, 109, 113, 127, 131, 137, 139, 149 };
                _points = Enumerable.Range(0, Probes).Select(_ => _variables.Select(_ =>
                {
                    int denominator = primes[random.Next(primes.Length)];
                    int numerator = random.Next(denominator / 4, denominator * 3) * (random.Next(2) == 0 ? 1 : -1);
                    return (Entity)MathS.FromString($"{numerator}/{denominator}");
                }).ToArray()).ToArray();

                // Substitution commutes with taking a determinant, so a minor is probed by
                // taking the minor of these number grids: 2^n multiplications of numbers
                // instead of substituting into an expression with n! terms.
                for (int k = 0; k < (_variables.Length == 0 ? 1 : Probes); k++)
                {
                    var grid = new Entity[a.GetLength(0), a.GetLength(1)];
                    bool finite = true;
                    for (int r = 0; r < grid.GetLength(0) && finite; r++)
                        for (int c = 0; c < grid.GetLength(1) && finite; c++)
                            finite = (grid[r, c] = At(a[r, c], k)) is Number.Complex { IsFinite: true };
                    if (finite) _probed.Add(grid);
                }
            }

            /// <summary>The value at probe point <paramref name="k"/>; a number unless evaluation failed.</summary>
            private Entity At(Entity value, int k)
            {
                _budget.ThrowIfCancellationRequested();
                for (int v = 0; v < _variables.Length; v++)
                    value = value.Substitute(_variables[v], _points[k][v]);
                return MathS.Settings.PrecisionErrorZeroRange.As(Negligible, () => value.Evaled);
            }

            /// <summary>
            /// The determinant of the submatrix on the given rows and columns, by Laplace
            /// expansion along each row in turn, memoised on the set of columns already used.
            /// </summary>
            public Entity Minor(IReadOnlyList<int> rows, IReadOnlyList<int> columns, Entity[,]? grid = null)
            {
                // On a grid of numbers every partial sum is evaluated outright. On the symbolic
                // matrix only constants are folded: InnerSimplified keeps expressions exact, and
                // for that reason will not multiply two decimals, which is why it cannot serve both.
                bool numeric = grid is not null;
                grid ??= _a;
                var memo = new Dictionary<int, Entity>();

                Entity Expand(int depth, int used)
                {
                    if (depth == rows.Count) return 1;
                    if (memo.TryGetValue(used, out var known)) return known;
                    _budget.ThrowIfCancellationRequested();

                    Entity sum = 0;
                    bool negative = false;
                    for (int c = 0; c < columns.Count; c++)
                    {
                        if ((used & (1 << c)) != 0) continue;
                        Entity cell = grid[rows[depth], columns[c]];
                        if (cell != 0)
                        {
                            Entity rest = Expand(depth + 1, used | (1 << c));
                            if (rest != 0) sum = negative ? sum - cell * rest : sum + cell * rest;
                        }
                        negative = !negative;
                    }
                    return memo[used] = numeric ? sum.Evaled : sum.InnerSimplified;
                }

                return Expand(0, 0);
            }

            /// <summary>
            /// Chooses pivot columns left to right. Column j is a pivot exactly when some row i
            /// outside the chosen ones makes the minor on (rows + i) × (pivots + j) non-zero.
            /// </summary>
            public void FindPivots()
            {
                int m = _a.GetLength(0), n = _a.GetLength(1);
                for (int j = 0; j < n && Rows.Count < m; j++)
                    for (int i = 0; i < m; i++)
                    {
                        if (Rows.Contains(i)) continue;
                        int[] rows = Rows.Append(i).ToArray(), columns = Pivots.Append(j).ToArray();
                        if (IsZeroMinor(rows, columns)) continue;
                        Rows.Add(i);
                        Pivots.Add(j);
                        break;
                    }
            }

            /// <summary>What must be non-zero for the pivots found to be the right ones; null if nothing.</summary>
            public string? Condition()
            {
                if (_variables.Length == 0 || Rows.Count == 0) return null;
                Entity minor = PivotMinor;
                if (minor is Number) return null;
                if (minor.Complexity <= LargestConditionShown) return Tidy(minor).Stringize();

                _notes.Add($"The answer holds wherever the {Rows.Count}×{Rows.Count} minor on rows " +
                    $"{string.Join(",", Rows.Select(r => r + 1))} and columns {string.Join(",", Pivots.Select(c => c + 1))} " +
                    "is non-zero; it is too large to write out.");
                return null;
            }

            /// <summary>Each non-trivial cell is one Cramer quotient: (pivot minor with column k swapped for j) / pivot minor.</summary>
            public Entity[,] Rref()
            {
                int m = _a.GetLength(0), n = _a.GetLength(1);
                var rref = new Entity[m, n];
                for (int r = 0; r < m; r++)
                    for (int j = 0; j < n; j++)
                    {
                        rref[r, j] = 0;
                        if (r >= Pivots.Count) continue;
                        if (j == Pivots[r]) rref[r, j] = 1;
                        // Left of its pivot a row is zero, and so is every other pivot column.
                        if (j <= Pivots[r] || Pivots.Contains(j)) continue;

                        int[] swapped = Pivots.ToArray();
                        swapped[r] = j;
                        // Zero by the same rule the pivots use, so it is written as 0 rather
                        // than as an expression that happens to vanish.
                        if (ProbeMinor(Rows, swapped) != Verdict.Zero)
                            rref[r, j] = Tidy(Minor(Rows, swapped) / PivotMinor);
                    }
                return rref;
            }

            /// <summary>
            /// Simplifies an output cell if that can be done in time and without changing its
            /// value at the probe points; otherwise returns it with constants folded only.
            /// </summary>
            public Entity Tidy(Entity value)
            {
                Entity folded = value.InnerSimplified;
                if (folded is Number) return folded;

                // Once one cell has run out its slice, cells at least as large are not attempted:
                // in a generic matrix they are all the same size and would all run out too.
                if (folded.Complexity >= _tooLargeToTidy || folded.Complexity > LargestSimplified) return folded;
                Entity? simplified = Within(Slice(TidyUntil), () => folded.Simplify());
                if (simplified is null) _tooLargeToTidy = folded.Complexity;
                // Simplify is not always value-preserving (it will cancel across a branch cut),
                // so its answer is only taken when the probes cannot tell it from the original.
                return simplified is not null && Probe(folded - simplified) != Verdict.NonZero ? simplified : folded;
            }

            private TimeSpan Slice(TimeSpan until)
            {
                TimeSpan left = until - _clock.Elapsed;
                return left < PerCall ? left : PerCall;
            }

            public enum Verdict { Zero, NonZero, Unknown }

            /// <summary>
            /// One finite non-zero value settles it; Unknown means no probe gave a finite number.
            /// </summary>
            private static Verdict Judge(IEnumerable<Entity> values)
            {
                bool evaluated = false;
                foreach (Entity value in values)
                {
                    // A pole, 0/0, or something that is not a number at all: this probe says nothing.
                    if (value is not Number.Complex { IsFinite: true } number) continue;
                    if (!number.IsZero) return Verdict.NonZero;
                    evaluated = true;
                }
                return evaluated ? Verdict.Zero : Verdict.Unknown;
            }

            /// <summary>Probes any expression by substituting each point into it.</summary>
            private Verdict Probe(Entity value)
                => Judge(Enumerable.Range(0, _variables.Length == 0 ? 1 : Probes).Select(k => At(value, k)));

            /// <summary>Probes a minor by taking the same minor of each evaluated grid.</summary>
            private Verdict ProbeMinor(IReadOnlyList<int> rows, IReadOnlyList<int> columns)
                => Judge(_probed.Select(grid =>
                    MathS.Settings.PrecisionErrorZeroRange.As(Negligible, () => Minor(rows, columns, grid))));

            public bool IsZeroMinor(IReadOnlyList<int> rows, IReadOnlyList<int> columns)
            {
                Verdict probed = ProbeMinor(rows, columns);
                return probed != Verdict.NonZero && IsZero(Minor(rows, columns), probed);
            }

            /// <summary>Decides whether an expression is identically zero.</summary>
            /// <remarks>
            /// <para>In order:</para>
            /// <list type="number">
            /// <item>A literal number is compared with 0 exactly.</item>
            /// <item><b>Numeric probing.</b> Five fixed pseudo-random rational points are
            /// substituted for every variable. (For a minor, the points are substituted into the
            /// matrix cells once and the minor of the resulting numbers is taken — the same
            /// value, far cheaper. A point where any cell is undefined is not used at all.) Polynomial and rational expressions are then
            /// evaluated in exact rational arithmetic; anything else in 100-digit decimals with
            /// values under 1e-60 counted as 0. <b>Any finite non-zero value means non-zero,
            /// and that is final</b> — <c>Simplify</c> is never asked, so a wrong simplification
            /// cannot turn a real pivot into a zero.</item>
            /// <item>If every probe that could be evaluated gave 0, <c>Simplify</c> and
            /// <c>Expand().Simplify()</c> are asked to confirm, within a time slice, unless the
            /// expression is too large for <c>Simplify</c> to be stopped reliably.</item>
            /// <item><b>Disagreement:</b> probes say 0 but simplification does not reach 0 (or
            /// runs out of time) — <b>treated as zero</b>, with a note. Dividing by a pivot that
            /// is really zero ruins the whole answer, while a non-zero expression vanishing at
            /// five unrelated points is far less likely than <c>Simplify</c> missing an identity.</item>
            /// <item>If no probe could be evaluated at all, simplification alone decides, and a
            /// note says so when it does not reach 0.</item>
            /// </list>
            /// <para><b>Ways this is wrong:</b></para>
            /// <list type="bullet">
            /// <item>It is a generic test. <c>x - 1</c> is non-zero, although it is zero at x = 1.</item>
            /// <item>An expression that is zero on a region but not everywhere — <c>abs(x) - x</c>,
            /// a piecewise — is called non-zero if any probe lands outside the region, and zero
            /// if all five land inside it.</item>
            /// <item>A non-rational value smaller than 1e-60 in magnitude is called zero.</item>
            /// <item>Probes are real, so an identity that only holds on the reals is accepted
            /// as an identity.</item>
            /// </list>
            /// </remarks>
            public bool IsZero(Entity value, Verdict? known = null)
            {
                if (value is Number.Complex literal) return literal.IsZero;

                Verdict probed = known ?? Probe(value);
                if (probed == Verdict.NonZero) return false;

                bool confirmed = value.Complexity <= LargestSimplified && (
                    Within(Slice(ConfirmUntil), () => value.Simplify()) is Number.Complex { IsZero: true } ||
                    Within(Slice(ConfirmUntil), () => value.Expand().Simplify()) is Number.Complex { IsZero: true });
                if (confirmed) return true;

                string shown = value.Stringize();
                if (shown.Length > 120) shown = shown[..120] + "…";
                _notes.Add(probed == Verdict.Zero
                    ? $"Treated as zero on numeric evidence only; simplification did not confirm it: {shown}"
                    : $"Could not be evaluated at any probe point and did not simplify to zero; treated as non-zero: {shown}");
                return probed == Verdict.Zero;
            }
        }
    }
}
