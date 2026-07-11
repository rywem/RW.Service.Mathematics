using RW.Service.Mathematics;
using RW.Service.Mathematics.Solvers;

namespace RW.Service.Mathematics.Tests
{
    public class AngouriMathServiceTests
    {
        private readonly AngouriMathService _svc = new();

        /// <summary>Strip spaces and explicit multiplication so we can compare canonical shapes.</summary>
        private static string Norm(string s) => s.Replace(" ", "").Replace("*", "");

        // ---------------------------------------------------------------- Simplify

        [Theory]
        [InlineData("2*x + 3*x - 1", "5x-1")]
        [InlineData("2x + 3x - 1", "5x-1")]     // implicit multiplication
        [InlineData("x + x", "2x")]
        [InlineData("x - x", "0")]
        [InlineData("(x^2)/x", "x")]
        public void Simplify_ReducesExpression(string input, string expected)
        {
            var r = _svc.Simplify(input);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, Norm(r.Result));
        }

        // ---------------------------------------------------------------- Expand

        [Fact]
        public void Expand_BinomialSquare()
        {
            var r = _svc.Expand("(x + 1)^2");
            Assert.True(r.Success, r.Error);
            var n = Norm(r.Result);
            Assert.Contains("x^2", n);
            Assert.Contains("2x", n);
            Assert.Contains("1", n);
        }

        [Fact]
        public void Expand_ProductOfBinomials()
        {
            // (x + 2)(x - 3) = x^2 - x - 6
            var r = _svc.Expand("(x + 2) * (x - 3)");
            Assert.True(r.Success, r.Error);
            Assert.Equal("x^2-x-6", Norm(r.Result));
        }

        // ---------------------------------------------------------------- Factor (round-trip)

        [Theory]
        [InlineData("x^2 - 1")]
        [InlineData("x^2 + 2*x + 1")]
        [InlineData("x^2 - 5*x + 6")]
        public void Factor_RoundTripsThroughExpand(string input)
        {
            var factored = _svc.Factor(input);
            Assert.True(factored.Success, factored.Error);

            // The factored form must be algebraically identical to the input:
            // simplifying (input - factored) collapses to 0 regardless of term ordering.
            var difference = _svc.Simplify($"({input}) - ({factored.Result})");
            Assert.True(difference.Success, difference.Error);
            Assert.Equal("0", Norm(difference.Result));
        }

        // ---------------------------------------------------------------- Differentiate

        [Theory]
        [InlineData("x^3", "3x^2")]
        [InlineData("x^2", "2x")]
        [InlineData("sin(x)", "cos(x)")]
        [InlineData("2*x + 1", "2")]
        public void Differentiate_Polynomials(string input, string expected)
        {
            var r = _svc.Differentiate(input);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, Norm(r.Result));
        }

        // ---------------------------------------------------------------- Integrate

        [Fact]
        public void Integrate_ThenDifferentiate_RoundTrips()
        {
            var integral = _svc.Integrate("2*x");
            Assert.True(integral.Success, integral.Error);

            var derivative = _svc.Differentiate(integral.Result);
            Assert.True(derivative.Success, derivative.Error);
            Assert.Equal("2x", Norm(derivative.Result));
        }

        // ---------------------------------------------------------------- Evaluate

        [Theory]
        [InlineData("2^10 + 1", "1025")]
        [InlineData("sqrt(16)", "4")]
        [InlineData("3 * 4 + 2", "14")]
        public void Evaluate_ClosedForm(string input, string expected)
        {
            var r = _svc.Evaluate(input);
            Assert.True(r.Success, r.Error);
            Assert.Equal(expected, Norm(r.Result));
        }

        // ---------------------------------------------------------------- Solve

        [Fact]
        public void Solve_Quadratic_TwoRoots()
        {
            var r = _svc.Solve("x^2 - 4 = 0");
            Assert.True(r.Success, r.Error);
            Assert.Contains("2", r.Solutions);
            Assert.Contains("-2", r.Solutions);
        }

        [Fact]
        public void Solve_Linear()
        {
            var r = _svc.Solve("2*x + 4 = 0");
            Assert.True(r.Success, r.Error);
            Assert.Contains("-2", r.Solutions);
        }

        [Fact]
        public void Solve_AcceptsBareExpression_AsEqualsZero()
        {
            var r = _svc.Solve("x^2 - 9");
            Assert.True(r.Success, r.Error);
            Assert.Contains("3", r.Solutions);
            Assert.Contains("-3", r.Solutions);
        }

        [Fact]
        public void Solve_EquationWithBothSides()
        {
            // x^2 = 2x + 3  ->  x = 3 or x = -1
            var r = _svc.Solve("x^2 = 2*x + 3");
            Assert.True(r.Success, r.Error);
            Assert.Contains("3", r.Solutions);
            Assert.Contains("-1", r.Solutions);
        }

        // ---------------------------------------------------------------- Failure handling

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("2 x + )(")]
        public void InvalidInput_FailsGracefully(string input)
        {
            var r = _svc.Simplify(input);
            Assert.False(r.Success);
            Assert.NotNull(r.Error);
        }

        [Fact]
        public void Result_CarriesLatex()
        {
            var r = _svc.Differentiate("x^3");
            Assert.True(r.Success, r.Error);
            Assert.False(string.IsNullOrWhiteSpace(r.Latex));
        }
    }
}
