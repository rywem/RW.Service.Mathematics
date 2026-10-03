using RW.Service.Mathematics.Solvers;

namespace RW.Service.Mathematics.Tests
{
    /// <summary>
    /// Covers the advanced (Maxima-facing) half of <see cref="AngouriMathService"/>.
    /// </summary>
    /// <remarks>
    /// The expected values here were checked against Maxima's documented behaviour for the same
    /// call, which is the point of naming the operations after Maxima's. Where this engine and
    /// Maxima genuinely differ — the term-count reading of <c>taylor</c>, the numeric flavour of
    /// a definite integral — the test asserts what *this* engine does and the comment says why.
    /// </remarks>
    public class AngouriMathServiceAdvancedTests
    {
        private readonly AngouriMathService _svc = new();

        private static string Norm(string s) => s.Replace(" ", string.Empty).Replace("*", string.Empty);

        // ---------------------------------------------------------------- limit

        [Theory]
        [InlineData("sin(x)/x", "0", "1")]           // Maxima: limit(sin(x)/x, x, 0) => 1
        [InlineData("(1+1/x)^x", "inf", "e")]        // Maxima: limit((1+1/x)^x, x, inf) => %e
        [InlineData("(x^2-1)/(x-1)", "1", "2")]
        public void Limit_MatchesMaxima(string expression, string approaching, string expected)
        {
            var r = _svc.Limit(expression, "x", approaching);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, Norm(r.Result));
        }

        [Fact]
        public void Limit_AcceptsMaximaSpellingsOfInfinity()
        {
            foreach (string spelling in new[] { "inf", "infinity", "oo", "+oo" })
                Assert.True(_svc.Limit("1/x", "x", spelling).Success, spelling);

            Assert.True(_svc.Limit("1/x", "x", "minf").Success);
        }

        [Fact]
        public void Limit_RejectsAnUnknownDirection()
        {
            var r = _svc.Limit("1/x", "x", "0", "sideways");
            Assert.False(r.Success);
            Assert.Contains("sideways", r.Error);
        }

        /// <summary>
        /// The reason <see cref="AngouriMathService.OperationBudget"/> exists. A two-sided limit
        /// across a pole does not terminate in AngouriMath 1.3.0, so the guarantee under test is
        /// not "the right answer" but "an answer at all, in bounded time".
        /// </summary>
        [Fact]
        public void Limit_AcrossAPole_GivesUpRatherThanHanging()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var r = _svc.Limit("1/x", "x", "0", "both");
            clock.Stop();

            Assert.True(clock.Elapsed < AngouriMathService.OperationBudget * 2,
                $"took {clock.Elapsed}, which means the budget did not hold");

            // Either it answered or it timed out; what it must not do is block forever.
            if (!r.Success)
                Assert.Contains("Gave up", r.Error);
        }

        // ---------------------------------------------------------------- taylor

        [Fact]
        public void Taylor_ExpandsExponentialAroundZero()
        {
            // Maxima: taylor(%e^x, x, 0, 3) => 1 + x + x^2/2 + x^3/6.
            // "terms" is a COUNT, not a highest power -- 4 terms reaches x^3.
            var r = _svc.Taylor("e^x", "x", "0", 4);
            Assert.True(r.Success, r.Error);

            string normalised = Norm(r.Result);
            Assert.Contains("1", normalised);
            Assert.Contains("x^2/2", normalised);
            Assert.Contains("x^3/6", normalised);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(31)]
        public void Taylor_RefusesAnUnreasonableTermCount(int terms)
        {
            var r = _svc.Taylor("e^x", "x", "0", terms);
            Assert.False(r.Success);
            Assert.Contains("between 1 and 30", r.Error);
        }

        // ---------------------------------------------------------------- diff

        [Theory]
        [InlineData("x^4", 1, "4x^3")]
        [InlineData("x^4", 2, "12x^2")]
        [InlineData("x^4", 4, "24")]
        [InlineData("x^4", 5, "0")]
        public void Derivative_RepeatsToTheRequestedOrder(string expression, int order, string expected)
        {
            var r = _svc.Derivative(expression, "x", order);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, Norm(r.Result));
        }

        [Fact]
        public void Derivative_RefusesAnUnreasonableOrder()
        {
            Assert.False(_svc.Derivative("x^2", "x", 0).Success);
            Assert.False(_svc.Derivative("x^2", "x", 21).Success);
        }

        // ---------------------------------------------------------------- definite integral

        [Theory]
        [InlineData("x^2", "0", "3", "9")]        // x^3/3 from 0..3
        [InlineData("2*x", "0", "5", "25")]
        [InlineData("1", "2", "7", "5")]
        public void DefiniteIntegral_EvaluatesTheAntiderivativeAtBothBounds(
            string expression, string from, string to, string expected)
        {
            var r = _svc.DefiniteIntegral(expression, "x", from, to);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, Norm(r.Result));
        }

        [Fact]
        public void DefiniteIntegral_AcceptsSymbolicBounds()
        {
            // Maxima: integrate(sin(x), x, 0, %pi) => 2. Bounds are expressions, not just numbers.
            var r = _svc.DefiniteIntegral("sin(x)", "x", "0", "pi");
            Assert.True(r.Success, r.Error);
        }

        // ---------------------------------------------------------------- subst

        [Theory]
        [InlineData("x^2+1", "x", "3", "10")]
        [InlineData("a*x", "a", "2", "2x")]
        public void Substitute_ReplacesThenSimplifies(string expression, string variable, string value, string expected)
        {
            var r = _svc.Substitute(expression, variable, value);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, Norm(r.Result));
        }

        // ---------------------------------------------------------------- systems

        [Fact]
        public void SolveSystem_SolvesALinearPair()
        {
            // Maxima: solve([x+y=3, x-y=1], [x, y]) => [[x=2, y=1]]
            var r = _svc.SolveSystem(new[] { "x + y = 3", "x - y = 1" }, new[] { "x", "y" });

            Assert.True(r.Success, r.Error);
            Assert.Single(r.Solutions);
            Assert.Contains("x = 2", r.Solutions[0]);
            Assert.Contains("y = 1", r.Solutions[0]);
        }

        [Fact]
        public void SolveSystem_AcceptsBareExpressionsAsEqualToZero()
        {
            var withEquals = _svc.SolveSystem(new[] { "x + y = 3", "x - y = 1" }, new[] { "x", "y" });
            var bare = _svc.SolveSystem(new[] { "x + y - 3", "x - y - 1" }, new[] { "x", "y" });

            Assert.True(bare.Success, bare.Error);
            Assert.Equal(withEquals.Solutions, bare.Solutions);
        }

        [Fact]
        public void SolveSystem_RejectsAnEmptyRequest()
        {
            Assert.False(_svc.SolveSystem(Array.Empty<string>(), new[] { "x" }).Success);
            Assert.False(_svc.SolveSystem(new[] { "x = 1" }, Array.Empty<string>()).Success);
        }

        [Fact]
        public void SolveSystem_RejectsAHalfWrittenEquation()
        {
            var r = _svc.SolveSystem(new[] { "x =" }, new[] { "x" });
            Assert.False(r.Success);
        }

        // ---------------------------------------------------------------- number theory

        [Theory]
        [InlineData("360", "2^3 * 3^2 * 5")]   // Maxima: ifactors(360) => [[2,3],[3,2],[5,1]]
        [InlineData("97", "97")]               // prime
        [InlineData("1", "1")]
        [InlineData("64", "2^6")]
        public void FactorInteger_MatchesMaxima(string number, string expected)
        {
            var r = _svc.FactorInteger(number);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, r.Result);
        }

        [Fact]
        public void FactorInteger_AcceptsAnExpression()
        {
            // The toolbox inserts call templates the user then edits, so "2^10" must work.
            var r = _svc.FactorInteger("2^10");
            Assert.True(r.Success, r.Error);
            Assert.Equal("2^10", r.Result);
        }

        [Fact]
        public void FactorInteger_RejectsANonInteger()
        {
            var r = _svc.FactorInteger("x + 1");
            Assert.False(r.Success);
            Assert.Contains("not an integer", r.Error);
        }

        [Theory]
        [InlineData("48", "18", "6")]
        [InlineData("17", "5", "1")]
        [InlineData("0", "5", "5")]
        public void Gcd_MatchesMaxima(string a, string b, string expected)
            => Assert.Equal(expected, _svc.Gcd(a, b).Result);

        [Theory]
        [InlineData("4", "6", "12")]
        [InlineData("21", "6", "42")]
        [InlineData("0", "5", "0")]     // Maxima's convention, and the only one that avoids /0
        public void Lcm_MatchesMaxima(string a, string b, string expected)
            => Assert.Equal(expected, _svc.Lcm(a, b).Result);

        [Theory]
        [InlineData("36", "12")]        // Maxima: totient(36) => 12
        [InlineData("97", "96")]        // prime p => p-1
        [InlineData("1", "1")]
        public void Totient_MatchesMaxima(string number, string expected)
        {
            var r = _svc.Totient(number);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, r.Result);
        }

        [Fact]
        public void Totient_RejectsANonPositiveInteger()
        {
            Assert.False(_svc.Totient("0").Success);
            Assert.False(_svc.Totient("-4").Success);
        }

        [Theory]
        [InlineData("36", "9")]         // 1,2,3,4,6,9,12,18,36
        [InlineData("97", "2")]
        [InlineData("1", "1")]
        public void CountDivisors_MatchesMaxima(string number, string expected)
        {
            var r = _svc.CountDivisors(number);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, r.Result);
        }

        // ---------------------------------------------------------------- shared behaviour

        [Fact]
        public void EveryOperation_ReportsEmptyInputRatherThanThrowing()
        {
            Assert.False(_svc.Limit(string.Empty, "x", "0").Success);
            Assert.False(_svc.Taylor(string.Empty, "x").Success);
            Assert.False(_svc.Derivative(string.Empty, "x").Success);
            Assert.False(_svc.Substitute(string.Empty, "x", "1").Success);
            Assert.False(_svc.FactorInteger(string.Empty).Success);
        }

        [Fact]
        public void AVariableIsAssumedToBeXWhenNotNamed()
        {
            var named = _svc.Derivative("x^2", "x");
            var unnamed = _svc.Derivative("x^2", string.Empty);
            Assert.Equal(named.Result, unnamed.Result);
        }
    }
}
