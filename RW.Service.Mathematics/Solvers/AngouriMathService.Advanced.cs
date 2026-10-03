using AngouriMath;
using AngouriMath.Core;
using static AngouriMath.Entity;

namespace RW.Service.Mathematics.Solvers
{
    /// <summary>
    /// The advanced half of the computer-algebra surface: the operations a Maxima user reaches
    /// for once <c>simplify</c> and <c>solve</c> are not enough.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each method is named after its Maxima counterpart so that the documentation can be read
    /// alongside Maxima's own. The behaviour is AngouriMath's, not Maxima's, and the two do
    /// disagree in places — where that is known, the method says so.
    /// </para>
    /// <para>
    /// <b>Every operation runs under a time budget.</b> This is not defensive padding: a
    /// two-sided limit taken across a pole (<c>limit(1/x, x, 0)</c>) does not terminate in
    /// AngouriMath 1.3.0. Without a budget that hangs the calling HTTP request forever, and a
    /// handful of them exhaust the thread pool and take the whole site down. A timeout is
    /// reported as a normal failed <see cref="AlgebraResult"/>, so a caller cannot tell a slow
    /// answer from a wrong one and does not have to.
    /// </para>
    /// <para>
    /// The budget is enforced with AngouriMath's own thread-local cancellation token, so an
    /// overrunning call is genuinely stopped rather than abandoned. Get this wrong -- by only
    /// waiting on the task -- and every timed-out request leaks a worker thread that keeps
    /// computing forever.
    /// </para>
    /// </remarks>
    public partial class AngouriMathService
    {
        /// <summary>How long any single algebra operation may run before it is abandoned.</summary>
        public static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Maxima's <c>limit(expr, var, point)</c> and <c>limit(expr, var, point, dir)</c>.
        /// <paramref name="direction"/> accepts "plus"/"right", "minus"/"left", or nothing for
        /// a two-sided limit. Use <c>oo</c> or <c>+oo</c>/<c>-oo</c> for infinity.
        /// </summary>
        public AlgebraResult Limit(string expression, string variable, string approaching, string? direction = null)
            => Run(expression, () =>
            {
                Variable v = MathS.Var(Named(variable));
                Entity destination = MathS.FromString(NormaliseInfinity(approaching));
                Entity body = MathS.FromString(expression);

                return ParseDirection(direction) is { } from
                    ? body.Limit(v, destination, from).Simplify()
                    : body.Limit(v, destination).Simplify();
            });

        /// <summary>
        /// Maxima's <c>taylor(expr, var, point, n)</c>. <paramref name="terms"/> is a count of
        /// terms, not a highest power: 4 yields up to x^3 around 0, matching AngouriMath.
        /// </summary>
        public AlgebraResult Taylor(string expression, string variable, string around = "0", int terms = 5)
            => Run(expression, () =>
            {
                if (terms is < 1 or > 30)
                    throw new ArgumentException("Ask for between 1 and 30 terms.");

                return MathS.Series.Taylor(
                    MathS.FromString(expression),
                    terms,
                    (MathS.Var(Named(variable)), MathS.FromString(around))).Simplify();
            });

        /// <summary>
        /// Maxima's <c>diff(expr, var, n)</c>. Differentiating n times is exactly what the
        /// single-step operation repeated n times means, and AngouriMath's own nth-derivative
        /// entry point is marked obsolete, so this composes the supported one.
        /// </summary>
        public AlgebraResult Derivative(string expression, string variable, int order = 1)
            => Run(expression, () =>
            {
                if (order is < 1 or > 20)
                    throw new ArgumentException("Order must be between 1 and 20.");

                Variable v = MathS.Var(Named(variable));
                Entity result = MathS.FromString(expression);
                for (int i = 0; i < order; i++)
                    result = result.Differentiate(v);

                return result.Simplify();
            });

        /// <summary>
        /// Maxima's <c>integrate(expr, var, a, b)</c>. The bounds are expressions, so
        /// <c>pi</c>, <c>2*pi</c> and <c>e</c> all work.
        /// </summary>
        /// <remarks>
        /// The answer is <b>numeric</b>, not symbolic: AngouriMath evaluates a definite integral
        /// by sampling. Do not read a clean-looking <c>2</c> as an exact result — it is a
        /// converged approximation, and an integrand with a singularity inside the interval will
        /// produce a confident wrong number rather than an error.
        /// </remarks>
        public AlgebraResult DefiniteIntegral(string expression, string variable, string from, string to)
            => Run(expression, () =>
            {
                Variable v = MathS.Var(Named(variable));
                Entity antiderivative = MathS.FromString(expression).Integrate(v).Simplify();

                // F(b) - F(a). Preferred over AngouriMath's sampling integrator, which is
                // obsolete in 1.3.0, because when the antiderivative is found in closed form
                // this is exact rather than approximate.
                Entity upper = antiderivative.Substitute(v, MathS.FromString(to));
                Entity lower = antiderivative.Substitute(v, MathS.FromString(from));
                return (upper - lower).Simplify();
            });

        /// <summary>Maxima's <c>subst(value, var, expr)</c> — replace a variable, then simplify.</summary>
        public AlgebraResult Substitute(string expression, string variable, string value)
            => Run(expression, () => MathS.FromString(expression)
                .Substitute(MathS.Var(Named(variable)), MathS.FromString(value))
                .Simplify());

        /// <summary>
        /// Maxima's <c>solve([eq1, eq2], [x, y])</c> for a system. Each equation may be written
        /// "lhs = rhs" or as a bare expression understood as "= 0".
        /// </summary>
        /// <remarks>
        /// Returns one row of values per solution, in the order the variables were given.
        /// An unsolvable or inconsistent system yields no solutions rather than an error.
        /// </remarks>
        public AlgebraResult SolveSystem(IReadOnlyList<string>? equations, IReadOnlyList<string>? variables)
        {
            string echo = string.Join("; ", equations ?? Array.Empty<string>());

            if (equations is not { Count: > 0 })
                return AlgebraResult.Fail(echo, "Give at least one equation.");
            if (variables is not { Count: > 0 })
                return AlgebraResult.Fail(echo, "Name at least one variable to solve for.");

            return Run(echo, () =>
            {
                Entity[] system = equations.Select(AsHomogeneous).ToArray();
                Variable[] unknowns = variables.Select(v => MathS.Var(Named(v))).ToArray();

                Matrix? solved = MathS.Equations(system).Solve(unknowns);
                if (solved is null)
                    return null;

                var rows = new List<string>();
                for (int r = 0; r < solved.RowCount; r++)
                {
                    var assignments = new List<string>();
                    for (int c = 0; c < solved.ColumnCount; c++)
                        assignments.Add($"{variables[c]} = {solved[r, c].Simplify().Stringize()}");
                    rows.Add(string.Join(", ", assignments));
                }

                return rows;
            });
        }

        /// <summary>Maxima's <c>ifactors(n)</c> — the prime factorisation of an integer.</summary>
        public AlgebraResult FactorInteger(string number)
            => Run(number, () =>
            {
                var factors = Factorisation(number);
                return factors.Count == 0
                    ? "1"
                    : string.Join(" * ", factors.Select(f => f.Power == 1 ? $"{f.Prime}" : $"{f.Prime}^{f.Power}"));
            });

        /// <summary>Maxima's <c>gcd(a, b)</c> for integers.</summary>
        public AlgebraResult Gcd(string a, string b)
            => Run($"gcd({a}, {b})", () => Integers.Gcd(AsInteger(a), AsInteger(b)).ToString());

        /// <summary>
        /// Maxima's <c>lcm(a, b)</c> for integers, via <c>|a*b| / gcd(a, b)</c>.
        /// <c>lcm(0, n)</c> is 0, which is the convention Maxima uses and the only value that
        /// keeps the identity above from dividing by zero.
        /// </summary>
        public AlgebraResult Lcm(string a, string b)
            => Run($"lcm({a}, {b})", () =>
            {
                System.Numerics.BigInteger left = AsInteger(a), right = AsInteger(b);
                if (left.IsZero || right.IsZero)
                    return "0";

                return (System.Numerics.BigInteger.Abs(left * right) / Integers.Gcd(left, right)).ToString();
            });

        /// <summary>
        /// Maxima's <c>totient(n)</c> — how many integers in 1..n are coprime to n.
        /// </summary>
        /// <remarks>
        /// Computed from the factorisation as n * prod(1 - 1/p) rather than by counting, so it
        /// is exact and fast for large n. AngouriMath's own <c>Phi</c> over an
        /// <see cref="Entity"/> returns the call unevaluated, which is not useful here.
        /// </remarks>
        public AlgebraResult Totient(string number)
            => Run(number, () =>
            {
                System.Numerics.BigInteger n = AsInteger(number);
                if (n <= 0)
                    throw new ArgumentException("The totient is defined for positive integers.");

                System.Numerics.BigInteger result = n;
                foreach (var factor in Factorisation(number))
                    result = result / factor.Prime * (factor.Prime - 1);

                return result.ToString();
            });

        /// <summary>Maxima's <c>divisors(n)</c>, counted rather than listed.</summary>
        public AlgebraResult CountDivisors(string number)
            => Run(number, () =>
            {
                System.Numerics.BigInteger count = 1;
                foreach (var factor in Factorisation(number))
                    count *= factor.Power + 1;

                return count.ToString();
            });

        // ----------------------------------------------------------------- helpers

        private static IReadOnlyList<(System.Numerics.BigInteger Prime, int Power)> Factorisation(string number)
        {
            System.Numerics.BigInteger n = System.Numerics.BigInteger.Abs(AsInteger(number));
            if (n <= 1)
                return Array.Empty<(System.Numerics.BigInteger, int)>();

            var factors = new List<(System.Numerics.BigInteger, int)>();
            foreach (var (prime, power) in MathS.NumberTheory.Factorize((Number.Integer)MathS.FromString(n.ToString())))
                factors.Add((System.Numerics.BigInteger.Parse(prime.Stringize()), int.Parse(power.Stringize())));

            return factors;
        }

        private static System.Numerics.BigInteger AsInteger(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Enter an integer.");

            // Accept an expression ("2^10", "6*7") as well as a literal, because the toolbox
            // inserts call templates that a user then edits.
            Entity simplified = MathS.FromString(value).Simplify();
            if (simplified is Number.Integer integer)
                return System.Numerics.BigInteger.Parse(integer.Stringize());

            throw new ArgumentException($"'{value}' is not an integer.");
        }

        /// <summary>Turn "lhs = rhs" into "lhs - (rhs)" so the solver sees an expression equal to zero.</summary>
        private static Entity AsHomogeneous(string equation)
        {
            int eq = equation.IndexOf('=');
            if (eq < 0)
                return MathS.FromString(equation);

            string lhs = equation[..eq].Trim(), rhs = equation[(eq + 1)..].Trim();
            if (lhs.Length == 0 || rhs.Length == 0)
                throw new FormatException("An equation needs an expression on both sides of '='.");

            return MathS.FromString($"({lhs}) - ({rhs})");
        }

        private static ApproachFrom? ParseDirection(string? direction) => direction?.Trim().ToLowerInvariant() switch
        {
            null or "" or "both" or "bothsides" => null,
            "plus" or "right" or "+" => ApproachFrom.Right,
            "minus" or "left" or "-" => ApproachFrom.Left,
            _ => throw new ArgumentException($"Unknown direction '{direction}'. Use plus, minus, or leave it blank.")
        };

        /// <summary>Maxima writes infinity as <c>inf</c>; AngouriMath wants <c>+oo</c>.</summary>
        private static string NormaliseInfinity(string point) => point.Trim().ToLowerInvariant() switch
        {
            "inf" or "infinity" or "oo" or "+inf" => "+oo",
            "minf" or "-inf" or "-infinity" => "-oo",
            _ => point
        };

        private static string Named(string? variable)
            => string.IsNullOrWhiteSpace(variable) ? "x" : variable.Trim();

        // Every operation above funnels through Budgeted, which owns the time limit and the
        // exception-to-message conversion. These wrappers only choose how to present what came
        // back: as an expression, a plain string, a list of solution rows, or a typed object.

        private static AlgebraResult Run(string input, Func<Entity> operation)
            => Budgeted(input, operation, e => AlgebraResult.Ok(input, e.Stringize(), e.Latexise()));

        private static AlgebraResult Run(string input, Func<string> operation)
            => Budgeted(input, operation, s => AlgebraResult.Ok(input, s, s));

        private static AlgebraResult Run(string input, Func<List<string>?> operation)
            => Budgeted(input, operation, rows => rows is not { Count: > 0 }
                ? AlgebraResult.Ok(input, "no solutions", @"\varnothing")
                : AlgebraResult.Ok(input, string.Join("  |  ", rows), string.Join(@" \quad ", rows), rows));

        /// <summary>
        /// The same contract as the <c>Run</c> overloads, for operations whose answer is a
        /// structured object rather than something renderable as one line of text.
        /// </summary>
        private static AlgebraResult<T> Attempt<T>(string input, Func<T> operation)
            => Budgeted(input, operation,
                onSuccess: value => AlgebraResult<T>.Ok(input, value),
                onFailure: error => AlgebraResult<T>.Fail(input, error));

        private static AlgebraResult Budgeted<T>(string input, Func<T> operation, Func<T, AlgebraResult> describe)
            => Budgeted(input, operation, describe, error => AlgebraResult.Fail(input, error));

        /// <summary>
        /// The one place an algebra operation actually runs: under the time budget, with every
        /// exception turned into a message. Both result shapes are built from its outcome, so
        /// the operation is executed exactly once.
        /// </summary>
        private static TResult Budgeted<T, TResult>(
            string input,
            Func<T> operation,
            Func<T, TResult> onSuccess,
            Func<string, TResult> onFailure)
        {
            if (string.IsNullOrWhiteSpace(input))
                return onFailure("Enter an expression.");

            using var budget = new CancellationTokenSource(OperationBudget);

            try
            {
                // AngouriMath checks a *thread-local* cancellation token at its recursion points,
                // so the token has to be installed on the worker thread rather than passed in.
                // That is what makes an overrunning call genuinely stop: it throws
                // OperationCanceledException from inside the engine rather than running on
                // unattended. Verified against the non-terminating case -- limit(1/x, x, 0)
                // two-sided -- which aborts on the budget instead of spinning forever.
                var work = Task.Run(() =>
                {
                    MathS.Multithreading.SetLocalCancellationToken(budget.Token);
                    return operation();
                }, budget.Token);

                // Wait slightly longer than the budget. The token is the real mechanism; this
                // only catches the case where a call ignores it between two check points, and
                // keeps the request bounded either way.
                if (!work.Wait(OperationBudget + TimeSpan.FromSeconds(2)))
                    return onFailure(GaveUpMessage);

                return onSuccess(work.Result);
            }
            catch (Exception ex)
            {
                Exception inner = (ex as AggregateException)?.InnerException ?? ex;
                return onFailure(inner is OperationCanceledException or TaskCanceledException
                    ? GaveUpMessage
                    : inner.Message);
            }
        }

        private static string GaveUpMessage =>
            $"Gave up after {OperationBudget.TotalSeconds:0} seconds. Some expressions have no " +
            "closed form the engine can reach \u2014 a two-sided limit across a pole is the usual " +
            "one. Try a one-sided limit, or simplify the input.";
    }

    /// <summary>Integer helpers that <see cref="System.Numerics.BigInteger"/> does not expose by the name used here.</summary>
    internal static class Integers
    {
        internal static System.Numerics.BigInteger Gcd(System.Numerics.BigInteger a, System.Numerics.BigInteger b)
            => System.Numerics.BigInteger.GreatestCommonDivisor(a, b);
    }
}
