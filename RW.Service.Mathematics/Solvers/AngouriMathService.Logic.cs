using AngouriMath;
using static AngouriMath.Entity;

namespace RW.Service.Mathematics.Solvers
{
    /// <summary>One row of a truth table: the inputs, and what the expression evaluates to.</summary>
    /// <param name="Inputs">One value per variable, in the order given by the header.</param>
    /// <param name="Value">The value of the whole expression for that assignment.</param>
    public sealed record TruthRow(IReadOnlyList<bool> Inputs, bool Value);

    /// <summary>A complete truth table, plus the classification that follows from it.</summary>
    /// <param name="Expression">The expression as it was parsed back out, for confirmation.</param>
    /// <param name="Variables">Column headers, in the order the rows use.</param>
    /// <param name="Rows">Every assignment, counting up in binary.</param>
    /// <param name="IsTautology">True for every assignment.</param>
    /// <param name="IsContradiction">False for every assignment.</param>
    /// <param name="IsSatisfiable">True for at least one assignment.</param>
    public sealed record TruthTable(
        string Expression,
        IReadOnlyList<string> Variables,
        IReadOnlyList<TruthRow> Rows,
        bool IsTautology,
        bool IsContradiction,
        bool IsSatisfiable);

    /// <summary>
    /// Boolean algebra: truth tables, classification, and satisfying assignments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The vocabulary is <c>and</c>, <c>or</c>, <c>not</c>, <c>xor</c>, <c>implies</c>, and the
    /// literals <c>true</c> and <c>false</c> — matching both AngouriMath's parser and Maxima's
    /// logic package closely enough that either habit works.
    /// </para>
    /// <para>
    /// <b>Classification is computed from the table, not from simplification.</b> That is a
    /// deliberate choice made after measuring: AngouriMath's <c>Simplify</c> leaves
    /// <c>a or not a</c> exactly as written rather than reducing it to <c>true</c>, so asking
    /// the simplifier "is this a tautology?" answers "no" for a textbook tautology. Enumerating
    /// the table is exponential in the number of variables but it is *correct*, and for the
    /// sizes a person types by hand the exponent is irrelevant. Correct and slow beats fast and
    /// wrong, especially when the wrong answer looks confident.
    /// </para>
    /// </remarks>
    public partial class AngouriMathService
    {
        /// <summary>
        /// The largest number of distinct variables a truth table may have.
        /// </summary>
        /// <remarks>
        /// 2^16 is 65,536 rows — already far more than anyone reads, and the point past which
        /// the response is measured in megabytes. The limit is reported as a normal failure
        /// rather than being silently truncated, because a truncated truth table would support
        /// a confident wrong conclusion about satisfiability.
        /// </remarks>
        public const int MaxTruthTableVariables = 16;

        /// <summary>Build the full truth table for a boolean expression.</summary>
        /// <param name="expression">e.g. <c>a and (b or not c)</c>.</param>
        /// <param name="variables">
        /// Column order. Leave empty to use every variable in the expression, alphabetically.
        /// </param>
        public AlgebraResult<TruthTable> BuildTruthTable(string expression, IReadOnlyList<string>? variables = null)
            => Attempt<TruthTable>(expression, () =>
            {
                Entity parsed = MathS.FromString(expression);
                var columns = ResolveVariables(parsed, variables);

                // AngouriMath returns the table as a matrix: one row per assignment, the
                // variable values first and the expression's value last. It throws rather than
                // returning a partial table when the expression is not purely boolean, so the
                // translation to a useful message happens here.
                Matrix table = Tabulate(parsed, columns);

                var rows = new List<TruthRow>(table.RowCount);
                for (int r = 0; r < table.RowCount; r++)
                {
                    var inputs = new bool[columns.Count];
                    for (int c = 0; c < columns.Count; c++)
                        inputs[c] = IsTrue(table[r, c]);

                    rows.Add(new TruthRow(inputs, IsTrue(table[r, table.ColumnCount - 1])));
                }

                return new TruthTable(
                    parsed.Stringize(),
                    columns.Select(v => v.Name).ToList(),
                    rows,
                    IsTautology: rows.All(row => row.Value),
                    IsContradiction: rows.All(row => !row.Value),
                    IsSatisfiable: rows.Any(row => row.Value));
            });

        /// <summary>
        /// Every assignment that makes the expression true — Maxima's satisfiability question,
        /// and the same thing a logic puzzle asks.
        /// </summary>
        public AlgebraResult SolveBoolean(string expression, IReadOnlyList<string>? variables = null)
        {
            var table = BuildTruthTable(expression, variables);
            if (!table.Success)
                return AlgebraResult.Fail(expression, table.Error!);

            TruthTable value = table.Value!;

            var satisfying = value.Rows
                .Where(row => row.Value)
                .Select(row => string.Join(", ",
                    value.Variables.Select((name, i) => $"{name} = {(row.Inputs[i] ? "true" : "false")}")))
                .ToList();

            string summary = satisfying.Count == 0
                ? "unsatisfiable — no assignment makes this true"
                : value.IsTautology
                    ? "tautology — true for every assignment"
                    : string.Join("  |  ", satisfying);

            return AlgebraResult.Ok(expression, summary, summary, satisfying);
        }

        /// <summary>
        /// Whether two boolean expressions agree on every assignment.
        /// </summary>
        /// <remarks>
        /// Compared over the union of both expressions' variables, so <c>a and b</c> versus
        /// <c>b and a</c> is a fair comparison and <c>a</c> versus <c>a and (b or not b)</c>
        /// correctly comes out equivalent. Simplifying both and comparing the results would not
        /// establish this — two forms can be equivalent without simplifying to the same tree.
        /// </remarks>
        public AlgebraResult AreEquivalent(string left, string right)
        {
            string echo = $"({left}) ≡ ({right})";

            return Run(echo, () =>
            {
                Entity a = MathS.FromString(left), b = MathS.FromString(right);

                var union = a.Vars.Concat(b.Vars)
                    .Select(v => v.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal)
                    .Select(MathS.Var).ToArray();

                if (union.Length > MaxTruthTableVariables)
                    throw new ArgumentException(TooManyVariables(union.Length));

                Matrix table = Tabulate(a & b | !a & !b, union);

                // The expression above is true exactly where the two agree, so the pair is
                // equivalent precisely when its table is a tautology.
                bool equivalent = true;
                for (int r = 0; r < table.RowCount && equivalent; r++)
                    equivalent = IsTrue(table[r, table.ColumnCount - 1]);

                return equivalent ? "equivalent" : "not equivalent";
            });
        }

        // ----------------------------------------------------------------- helpers

        /// <summary>
        /// Build the table, translating AngouriMath's exceptions into something a person can
        /// act on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>CannotEvalException</c> means a cell did not reduce to true or false once every
        /// listed variable had been substituted. In practice that has exactly two causes, and
        /// the second is the one nobody guesses:
        /// </para>
        /// <list type="number">
        /// <item>the expression is not boolean at all — <c>x + 1</c>;</item>
        /// <item>it uses <c>e</c>, <c>i</c> or <c>pi</c> as if they were variables. Those are
        /// <b>reserved constants</b> in AngouriMath — Euler's number, the imaginary unit and π.
        /// They never appear in the variable list, so they are never substituted, and the cell
        /// is left with a number in it. <c>a and e</c> fails for this reason while looking
        /// perfectly reasonable.</item>
        /// </list>
        /// </remarks>
        private static Matrix Tabulate(Entity expression, IReadOnlyList<Variable> columns)
        {
            try
            {
                return MathS.Boolean.BuildTruthTable(expression, columns.ToArray());
            }
            catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
            {
                string reserved = string.Join(", ",
                    ReservedNames.Where(name => expression.ToString()!.Contains(name, StringComparison.Ordinal)));

                throw new ArgumentException(
                    $"'{expression.Stringize()}' is not a boolean expression. Use and, or, not, xor, " +
                    "implies, true and false." +
                    (reserved.Length > 0
                        ? $" Note that {reserved} is a reserved constant here, not a variable — rename it."
                        : string.Empty));
            }
        }

        /// <summary>
        /// Names AngouriMath parses as constants rather than variables, so they can never be
        /// truth-table columns.
        /// </summary>
        private static readonly string[] ReservedNames = ["pi", "e", "i"];

        private static IReadOnlyList<Variable> ResolveVariables(Entity parsed, IReadOnlyList<string>? requested)
        {
            var named = requested?.Where(v => !string.IsNullOrWhiteSpace(v))
                                  .Select(v => v.Trim()).Distinct().ToList();

            // Alphabetical when we choose for ourselves, so the column order is predictable
            // rather than dependent on where a variable happens to appear in the tree.
            List<Variable> columns = named is { Count: > 0 }
                ? named.Select(MathS.Var).ToList()
                : parsed.Vars.Select(v => v.Name).Distinct()
                        .OrderBy(n => n, StringComparer.Ordinal).Select(MathS.Var).ToList();

            if (columns.Count == 0)
                throw new ArgumentException("That expression has no variables, so it has no truth table.");

            // Every variable must get a column. AngouriMath requires the counts to match, and a
            // variable left un-substituted would make its cells unevaluable — reported here as
            // the naming mistake it actually is rather than as an arity error.
            var missing = parsed.Vars.Select(v => v.Name)
                                     .Distinct()
                                     .Except(columns.Select(c => c.Name), StringComparer.Ordinal)
                                     .ToList();

            if (missing.Count > 0)
                throw new ArgumentException(
                    $"No column given for {string.Join(", ", missing)}. " +
                    "List every variable in the expression, or leave the list empty to use them all.");

            if (columns.Count > MaxTruthTableVariables)
                throw new ArgumentException(TooManyVariables(columns.Count));

            return columns;
        }

        private static string TooManyVariables(int count) =>
            $"{count} variables would need {1L << count:N0} rows. " +
            $"The limit is {MaxTruthTableVariables}; fix some variables to a value and try again.";

        /// <summary>
        /// Read one cell of AngouriMath's truth-table matrix as a bool.
        /// </summary>
        /// <remarks>
        /// The cells come back as <c>Entity.Boolean</c>, which has an explicit conversion to
        /// <see cref="bool"/>. Anything else means the expression was not purely boolean — an
        /// arithmetic subexpression, say — and that is worth an error rather than a guess.
        /// </remarks>
        private static bool IsTrue(Entity cell) => cell switch
        {
            Entity.Boolean b => (bool)b,
            _ => throw new ArgumentException(
                $"'{cell.Stringize()}' is not a boolean value. Use and, or, not, xor, implies, true and false.")
        };
    }
}
