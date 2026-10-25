# RW.Service.Mathematics

A computer-algebra service in C#: simplify, expand, factor, differentiate, integrate, solve,
limits, Taylor series, number theory, Boolean logic and matrices with symbols in them. It is a
safe facade over [AngouriMath](https://am.angouri.org/) (MIT), plus exact matrix work done by
`RW.Library.Mathematics`.

- **Never throws.** Every method returns an `AlgebraResult`; check `Success`, then read `Result`
  (or `Error`).
- **Budgeted.** Each call gets ten seconds (`AngouriMathService.OperationBudget`) on a thread of
  its own, then gives up with an error instead of hanging your app.
- **Exact where it can be.** `ExactMatrix` keeps rationals as rationals and letters as letters.

Private: not published to NuGet or anywhere else. You get it by cloning this repository.

## Quick start

You need the .NET 10 SDK. This project references `RW.Library.Mathematics` by relative path,
so clone both repositories **side by side** in one folder:

```bash
mkdir maths && cd maths
git clone <RW.Library.Mathematics repository> RW.Library.Mathematics
git clone <this repository> RW.Service.Mathematics
cd RW.Service.Mathematics
dotnet build RW.Service.Mathematics -c Release
# → RW.Service.Mathematics/bin/Release/net10.0/RW.Service.Mathematics.dll
#   and RW.Library.Mathematics.dll copied beside it
```

Reference both DLLs and the AngouriMath package from your own project:

```xml
<ItemGroup>
  <Reference Include="../maths/RW.Service.Mathematics/RW.Service.Mathematics/bin/Release/net10.0/RW.Service.Mathematics.dll" />
  <Reference Include="../maths/RW.Service.Mathematics/RW.Service.Mathematics/bin/Release/net10.0/RW.Library.Mathematics.dll" />
  <PackageReference Include="AngouriMath" Version="1.3.0" />
</ItemGroup>
```

Or, with the clones beside your project, a `<ProjectReference>` to
`RW.Service.Mathematics/RW.Service.Mathematics.csproj` brings in both. That is what RW.Web.Workbench does.

```csharp
using RW.Service.Mathematics.Solvers;

var cas = new AngouriMathService();
Console.WriteLine(cas.Differentiate("x^3 + 2x", order: 2).Result);   // 6 * x
Console.WriteLine(cas.Solve("x^2 = 4").Result);                       // x ∈ { 2, -2 }

var bad = cas.Simplify("2x +");
Console.WriteLine(bad.Success ? bad.Result : $"error: {bad.Error}"); // error: Could not parse the input: ...

var inverse = cas.ExactMatrix("inverse", [["1/2", "a"], ["0", "3"]]);
foreach (var row in inverse.Value!.Cells!) Console.WriteLine(string.Join(", ", row));
// 2, (-2*a)/3
// 0, 1/3
```

## Using the API

Everything is on one class, `AngouriMathService` (namespace `RW.Service.Mathematics.Solvers`).
It holds no state: make one and share it. Inputs are text in AngouriMath's syntax (`x^2`,
`sin(x)`, `sqrt(2)`, `exp(x)`).

| Methods | Result |
|---|---|
| `Simplify`, `Expand`, `Factor`, `Evaluate`, `ToLatex` | `AlgebraResult` with `Result` (text) and `Latex` |
| `Differentiate(expr, variable = "x", order = 1)`, `Integrate`, `DefiniteIntegral`, `Limit`, `Taylor`, `Substitute` | `AlgebraResult` |
| `Solve(equation, variable = "x")`, `SolveSystem(equations, variables)` | `AlgebraResult`; the roots are also in `Solutions` |
| `FactorInteger`, `Gcd`, `Lcm`, `Totient`, `CountDivisors` | `AlgebraResult` |
| `BuildTruthTable`, `SolveBoolean`, `AreEquivalent` | `AlgebraResult<TruthTable>` or `AlgebraResult` |
| `ExactMatrix(op, cells)` | `AlgebraResult<SymbolicMatrixAnswer>`; ops in `ExactMatrixOperations`. Exact over rational functions. |
| `SymbolicMatrix(op, cells)`, `SymbolicElimination(op, cells)` | Same shape, through AngouriMath, for entries `ExactMatrix` cannot take (`sin(t)`, `sqrt(2)`). |

`AlgebraResult<T>` carries a structured answer in `Value` instead of text. A
`SymbolicMatrixAnswer` has `Cells` (a grid) or `Scalar` (rank, determinant, solution kind),
plus `Latex`.

Know before you use it:

- `DefiniteIntegral` is **numeric**: AngouriMath samples the integrand. A clean-looking `2` is a
  converged approximation, and a singularity inside the interval gives a confident wrong number.
- `Evaluate` is numeric too. For exact answers stay with `Simplify` or `ExactMatrix`.
- `Factor` is AngouriMath's and can print things such as `sqrt(1)`. For exact factoring of a
  one-variable polynomial over the rationals, use `PolynomialFactorization` in RW.Library.Mathematics.

## Tests

```bash
dotnet test RW.Service.Mathematics.Tests
```

Tests run one at a time on purpose (`AssemblyInfo.cs`): several check the ten-second budget,
and in parallel they starve each other. On a busy machine one or two budget tests can still fail;
rerun them alone before believing it.

## More

Design notes and history live in the sibling docs repo, `RW.Mathematics.Documentation/service-mathematics/`.
