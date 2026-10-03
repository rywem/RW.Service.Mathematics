using System.Diagnostics;
using AngouriMath;
using AngouriMath.Extensions;
using static AngouriMath.Entity;

namespace RW.Service.Mathematics.Solvers
{
    /// <summary>
    /// A thin, exception-safe facade over the AngouriMath computer-algebra system.
    /// Every method returns an <see cref="AlgebraResult"/> rather than throwing, so the
    /// UI can bind directly to the outcome. See https://am.angouri.org/ for the engine.
    /// </summary>
    public partial class AngouriMathService
    {
        /// <summary>Simplify an expression, e.g. "2x + 3x - 1" &rarr; "5*x - 1".</summary>
        public AlgebraResult Simplify(string expression)
            => Transform(expression, e => e.Simplify());

        /// <summary>Expand products/powers, e.g. "(x + 1)^2" &rarr; "x^2 + 2*x + 1".</summary>
        public AlgebraResult Expand(string expression)
            => Transform(expression, e => e.Expand().Simplify());

        /// <summary>Factor an expression, e.g. "x^2 - 1" &rarr; "(x - 1)(x + 1)".</summary>
        public AlgebraResult Factor(string expression)
            => Transform(expression, e => e.Factorize());

        /// <summary>Differentiate with respect to <paramref name="variable"/> (default x).</summary>
        public AlgebraResult Differentiate(string expression, string variable = "x")
            => Transform(expression, e => e.Differentiate(MathS.Var(variable)).Simplify());

        /// <summary>Indefinite integral with respect to <paramref name="variable"/> (default x).</summary>
        public AlgebraResult Integrate(string expression, string variable = "x")
            => Transform(expression, e => e.Integrate(MathS.Var(variable)).Simplify());

        /// <summary>Numerically evaluate a closed-form expression, e.g. "2^10 + 1" &rarr; "1025".</summary>
        public AlgebraResult Evaluate(string expression)
            => Transform(expression, e => e.EvalNumerical());

        /// <summary>
        /// Solve an equation for <paramref name="variable"/>. Accepts either a bare
        /// expression (treated as "= 0") or a full "lhs = rhs" equation.
        /// </summary>
        public AlgebraResult Solve(string equation, string variable = "x")
        {
            if (string.IsNullOrWhiteSpace(equation))
                return AlgebraResult.Fail(equation ?? string.Empty, "Enter an equation to solve.");

            try
            {
                Entity target = ToSolvableEntity(equation);
                var variableEntity = MathS.Var(variable);

                Set solutionSet = target.Solve(variableEntity);
                var simplified = (Set)solutionSet.Simplify();

                var solutions = EnumerateSolutions(simplified);
                string pretty = solutions.Count > 0
                    ? $"{variable} ∈ {{ {string.Join(", ", solutions)} }}"
                    : simplified.Stringize();

                return AlgebraResult.Ok(equation, pretty, $"{variable} \\in {simplified.Latexise()}", solutions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Solve failed for '{equation}': {ex}");
                return AlgebraResult.Fail(equation, Describe(ex));
            }
        }

        /// <summary>Return the LaTeX form of an expression without otherwise changing it.</summary>
        public AlgebraResult ToLatex(string expression)
            => Transform(expression, e => e);

        // ----------------------------------------------------------------- helpers

        private static AlgebraResult Transform(string expression, Func<Entity, Entity> operation)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return AlgebraResult.Fail(expression ?? string.Empty, "Enter an expression.");

            try
            {
                Entity parsed = MathS.FromString(expression);
                Entity result = operation(parsed);
                return AlgebraResult.Ok(expression, result.Stringize(), result.Latexise());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Operation failed for '{expression}': {ex}");
                return AlgebraResult.Fail(expression, Describe(ex));
            }
        }

        /// <summary>
        /// Produce an equation <i>statement</i> for AngouriMath's solver. A full "lhs = rhs"
        /// is parsed as-is; a bare expression is promoted to "expression = 0".
        /// </summary>
        private static Entity ToSolvableEntity(string equation)
        {
            int eq = equation.IndexOf('=');
            if (eq < 0)
                return MathS.FromString($"{equation} = 0");

            string lhs = equation[..eq].Trim();
            string rhs = equation[(eq + 1)..].Trim();
            if (lhs.Length == 0 || rhs.Length == 0)
                throw new FormatException("An equation needs an expression on both sides of '='.");

            return MathS.FromString(equation);
        }

        private static List<string> EnumerateSolutions(Set set)
        {
            var results = new List<string>();
            if (set is Set.FiniteSet finite)
            {
                foreach (var element in finite)
                    results.Add(element.Stringize());
            }
            return results;
        }

        private static string Describe(Exception ex) => ex switch
        {
            FormatException => ex.Message,
            _ when ex.GetType().Name.Contains("Parse") => $"Could not parse the input: {ex.Message}",
            _ => ex.Message
        };
    }
}
