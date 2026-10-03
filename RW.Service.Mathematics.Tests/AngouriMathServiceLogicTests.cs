using RW.Service.Mathematics.Solvers;

namespace RW.Service.Mathematics.Tests
{
    /// <summary>
    /// Boolean algebra: truth tables, classification, satisfiability and equivalence.
    /// </summary>
    /// <remarks>
    /// The classification tests are the ones that matter. AngouriMath's <c>Simplify</c> leaves
    /// <c>a or not a</c> unchanged rather than reducing it to <c>true</c>, so an implementation
    /// that asked the simplifier would report a textbook tautology as "not a tautology" — a
    /// confident wrong answer. These pin the table-based behaviour that avoids it.
    /// </remarks>
    public class AngouriMathServiceLogicTests
    {
        private readonly AngouriMathService _svc = new();

        private TruthTable Table(string expression, params string[] variables)
        {
            var result = _svc.BuildTruthTable(expression, variables.Length == 0 ? null : variables);
            Assert.True(result.Success, result.Error);
            return result.Value!;
        }

        // ---------------------------------------------------------------- shape

        [Fact]
        public void TruthTable_HasTwoToTheNRows()
        {
            Assert.Equal(2, Table("not a").Rows.Count);
            Assert.Equal(4, Table("a and b").Rows.Count);
            Assert.Equal(8, Table("a and b or c").Rows.Count);
        }

        [Fact]
        public void TruthTable_NamesItsColumnsAlphabeticallyWhenNotToldOtherwise()
            => Assert.Equal(new[] { "a", "b", "c" }, Table("c or (b and a)").Variables);

        [Fact]
        public void TruthTable_HonoursAnExplicitColumnOrder()
            => Assert.Equal(new[] { "b", "a" }, Table("a and b", "b", "a").Variables);

        [Fact]
        public void TruthTable_GivesOneInputPerColumn()
            => Assert.All(Table("a and b").Rows, row => Assert.Equal(2, row.Inputs.Count));

        // ---------------------------------------------------------------- the operators

        [Fact]
        public void And_IsTrueOnlyWhenBothAre()
        {
            var rows = Table("a and b").Rows;
            Assert.Single(rows, row => row.Value);
            Assert.All(rows.Where(r => r.Value), row => Assert.All(row.Inputs, Assert.True));
        }

        [Fact]
        public void Or_IsFalseOnlyWhenNeitherIs()
        {
            var rows = Table("a or b").Rows;
            Assert.Equal(3, rows.Count(row => row.Value));
            Assert.All(rows.Where(r => !r.Value), row => Assert.All(row.Inputs, Assert.False));
        }

        [Fact]
        public void Xor_IsTrueWhenExactlyOneIs()
        {
            var rows = Table("a xor b").Rows;
            Assert.Equal(2, rows.Count(row => row.Value));
            Assert.All(rows.Where(r => r.Value), row => Assert.NotEqual(row.Inputs[0], row.Inputs[1]));
        }

        /// <summary>
        /// Implication is false in exactly one case — true implying false. The other three,
        /// including "false implies anything", are true. This is the operator people get wrong,
        /// so it is worth pinning row by row.
        /// </summary>
        [Fact]
        public void Implies_IsFalseOnlyForTrueImplyingFalse()
        {
            var rows = Table("a implies b").Rows;

            Assert.Equal(3, rows.Count(row => row.Value));

            var onlyFalse = Assert.Single(rows.Where(row => !row.Value));
            Assert.True(onlyFalse.Inputs[0]);
            Assert.False(onlyFalse.Inputs[1]);
        }

        // ---------------------------------------------------------------- classification

        [Theory]
        [InlineData("a or not a")]          // the law of the excluded middle
        [InlineData("a implies a")]
        [InlineData("(a and b) implies a")]
        [InlineData("not (a and not a)")]   // non-contradiction
        public void Tautologies_AreRecognised(string expression)
        {
            var table = Table(expression);
            Assert.True(table.IsTautology, $"{expression} should be a tautology");
            Assert.True(table.IsSatisfiable);
            Assert.False(table.IsContradiction);
        }

        [Theory]
        [InlineData("a and not a")]
        [InlineData("a xor a")]
        [InlineData("(a or b) and not a and not b")]
        public void Contradictions_AreRecognised(string expression)
        {
            var table = Table(expression);
            Assert.True(table.IsContradiction, $"{expression} should be a contradiction");
            Assert.False(table.IsSatisfiable);
            Assert.False(table.IsTautology);
        }

        [Fact]
        public void AnOrdinaryExpression_IsNeither()
        {
            var table = Table("a and b");
            Assert.False(table.IsTautology);
            Assert.False(table.IsContradiction);
            Assert.True(table.IsSatisfiable);
        }

        /// <summary>
        /// The reason classification is computed from the table. If this ever starts failing
        /// because Simplify improved, the implementation may be simplified — but only then.
        /// </summary>
        [Fact]
        public void Simplify_AloneWouldNotHaveRecognisedTheExcludedMiddle()
        {
            var simplified = _svc.Simplify("a or not a");
            Assert.True(simplified.Success, simplified.Error);
            Assert.NotEqual("true", simplified.Result.Trim().ToLowerInvariant());

            // ...yet the table says it is a tautology.
            Assert.True(Table("a or not a").IsTautology);
        }

        // ---------------------------------------------------------------- De Morgan

        [Theory]
        [InlineData("not (a and b)", "not a or not b")]
        [InlineData("not (a or b)", "not a and not b")]
        [InlineData("a implies b", "not a or b")]
        [InlineData("a xor b", "(a or b) and not (a and b)")]
        public void EquivalentExpressions_AreRecognised(string left, string right)
        {
            var result = _svc.AreEquivalent(left, right);
            Assert.True(result.Success, result.Error);
            Assert.Equal("equivalent", result.Result);
        }

        [Theory]
        [InlineData("a and b", "a or b")]
        [InlineData("a", "not a")]
        public void InequivalentExpressions_AreRecognised(string left, string right)
            => Assert.Equal("not equivalent", _svc.AreEquivalent(left, right).Result);

        /// <summary>
        /// Equivalence is checked over the union of both sides' variables. Comparing only one
        /// side's variables would make this pair look different, since `b` appears on the right
        /// and not the left.
        /// </summary>
        [Fact]
        public void Equivalence_ComparesOverTheUnionOfBothSidesVariables()
            => Assert.Equal("equivalent", _svc.AreEquivalent("a", "a and (b or not b)").Result);

        // ---------------------------------------------------------------- satisfiability

        [Fact]
        public void SolveBoolean_ListsEverySatisfyingAssignment()
        {
            var result = _svc.SolveBoolean("a xor b");

            Assert.True(result.Success, result.Error);
            Assert.Equal(2, result.Solutions.Count);
            Assert.Contains(result.Solutions, s => s.Contains("a = true") && s.Contains("b = false"));
            Assert.Contains(result.Solutions, s => s.Contains("a = false") && s.Contains("b = true"));
        }

        [Fact]
        public void SolveBoolean_SaysSoWhenNothingSatisfies()
        {
            var result = _svc.SolveBoolean("a and not a");

            Assert.True(result.Success, result.Error);
            Assert.Empty(result.Solutions);
            Assert.Contains("unsatisfiable", result.Result);
        }

        [Fact]
        public void SolveBoolean_SaysSoForATautology()
            => Assert.Contains("tautology", _svc.SolveBoolean("a or not a").Result);

        // ---------------------------------------------------------------- guard rails

        [Fact]
        public void AnExpressionWithNoVariables_IsRejected()
        {
            var result = _svc.BuildTruthTable("true and false");
            Assert.False(result.Success);
            Assert.Contains("no variables", result.Error);
        }

        /// <summary>
        /// Names that are safe to use as boolean variables.
        /// </summary>
        /// <remarks>
        /// <c>e</c>, <c>i</c> and <c>pi</c> are deliberately absent: AngouriMath parses them as
        /// Euler's number, the imaginary unit and π, so they never appear as variables. Using
        /// them here would test the parser's constant handling rather than the truth table.
        /// </remarks>
        private static readonly string[] SafeNames =
            ["a", "b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "q", "r", "s", "u"];

        private static string AndOf(int count) => string.Join(" and ", SafeNames.Take(count));

        [Fact]
        public void TooManyVariables_IsRefusedRatherThanTruncated()
        {
            // 17 variables would be 131,072 rows. A truncated table would support a confident
            // wrong conclusion about satisfiability, so this must fail rather than shorten.
            var result = _svc.BuildTruthTable(AndOf(AngouriMathService.MaxTruthTableVariables + 1));

            Assert.False(result.Success);
            Assert.Contains("limit is", result.Error);
        }

        /// <summary>
        /// The limit is chosen for time, not correctness: 16 variables is 65,536 rows and takes
        /// under a second. If this becomes slow, lower the limit rather than truncating.
        /// </summary>
        [Fact]
        public void AtTheLimit_ItStillWorks()
        {
            var result = _svc.BuildTruthTable(AndOf(AngouriMathService.MaxTruthTableVariables));

            Assert.True(result.Success, result.Error);
            Assert.Equal(1 << AngouriMathService.MaxTruthTableVariables, result.Value!.Rows.Count);
            Assert.Single(result.Value.Rows, row => row.Value);   // only all-true satisfies a conjunction
        }

        [Fact]
        public void AnArithmeticExpression_IsRejectedRatherThanGuessedAt()
        {
            var result = _svc.BuildTruthTable("x + 1");
            Assert.False(result.Success);
            Assert.Contains("not a boolean expression", result.Error);
        }

        /// <summary>
        /// <c>a and e</c> looks like two boolean variables and is not: <c>e</c> is Euler's
        /// number. The error has to say so, because nothing about the input suggests it.
        /// </summary>
        [Theory]
        [InlineData("a and e")]
        [InlineData("a or i")]
        [InlineData("pi and a")]
        public void ReservedConstants_AreExplainedRatherThanJustRejected(string expression)
        {
            var result = _svc.BuildTruthTable(expression);

            Assert.False(result.Success);
            Assert.Contains("reserved constant", result.Error);
        }

        [Fact]
        public void AnIncompleteColumnList_NamesTheMissingVariable()
        {
            var result = _svc.BuildTruthTable("a and b", new[] { "a" });

            Assert.False(result.Success);
            Assert.Contains("No column given for b", result.Error);
        }

        [Fact]
        public void EmptyInput_Fails()
        {
            Assert.False(_svc.BuildTruthTable(string.Empty).Success);
            Assert.False(_svc.SolveBoolean("   ").Success);
        }
    }
}
