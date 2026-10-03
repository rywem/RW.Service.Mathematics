namespace RW.Service.Mathematics
{
    /// <summary>
    /// The outcome of a single algebra operation. Carries a human readable result,
    /// a LaTeX rendering (for the WpfMath surface) and, on failure, the error message.
    /// Never throws to the caller &mdash; inspect <see cref="Success"/>.
    /// </summary>
    public sealed class AlgebraResult
    {
        public string Input { get; init; } = string.Empty;

        /// <summary>Plain-text result, e.g. "2*x + 3" or "x ∈ { -2, 2 }".</summary>
        public string Result { get; init; } = string.Empty;

        /// <summary>LaTeX form of the result, suitable for rendering.</summary>
        public string Latex { get; init; } = string.Empty;

        /// <summary>Individual solutions when the operation produces a solution set.</summary>
        public IReadOnlyList<string> Solutions { get; init; } = System.Array.Empty<string>();

        public bool Success { get; init; }

        public string? Error { get; init; }

        public static AlgebraResult Ok(string input, string result, string latex, IReadOnlyList<string>? solutions = null)
            => new()
            {
                Input = input,
                Result = result,
                Latex = latex,
                Solutions = solutions ?? System.Array.Empty<string>(),
                Success = true
            };

        public static AlgebraResult Fail(string input, string error)
            => new() { Input = input, Success = false, Error = error };

        public override string ToString() => Success ? Result : $"Error: {Error}";
    }

    /// <summary>
    /// The outcome of an operation whose answer is a structured object rather than a line of
    /// text — a truth table, a matrix, a set.
    /// </summary>
    /// <remarks>
    /// Deliberately not derived from <see cref="AlgebraResult"/>. That class promises a
    /// <c>Result</c> string and a <c>Latex</c> string, and inventing those for a table would
    /// mean choosing a rendering in the engine, where the engine has no business deciding how a
    /// client displays anything. The client gets the data and renders it.
    /// </remarks>
    /// <typeparam name="T">The shape of the answer.</typeparam>
    public sealed class AlgebraResult<T>
    {
        public string Input { get; init; } = string.Empty;

        /// <summary>The answer, or <see langword="null"/> when <see cref="Success"/> is false.</summary>
        public T? Value { get; init; }

        public bool Success { get; init; }

        public string? Error { get; init; }

        public static AlgebraResult<T> Ok(string input, T value)
            => new() { Input = input, Value = value, Success = true };

        public static AlgebraResult<T> Fail(string input, string error)
            => new() { Input = input, Success = false, Error = error };

        public override string ToString() => Success ? $"{Value}" : $"Error: {Error}";
    }
}
