# HANDOFF — RW.Service.Mathematics

A thin, exception-safe facade over **AngouriMath** (MIT). Wrapped by the `algebra` module.

**Read [`Solutions/Mathematics/handoff-consumed.md`](../../Solutions/Mathematics/handoff-consumed.md) first.**

**Path:** `E:\Development\Projects\RW.Service.Mathematics`
**Branch:** `feature/rw-math-workbench` (pushed to `github.com/rywem/RW.Service.Mathematics`)
**Tests:** 26

---

## 1. What it is

One class, `AngouriMathService`, exposing symbolic algebra:

`Simplify` · `Expand` · `Factor` · `Solve` · `Differentiate` · `Integrate` · `Evaluate` ·
`ToLatex`

Every method returns `AlgebraResult` — plain text, LaTeX, solution set, and an error message
on failure. **Nothing throws to the caller.**

It started as a stub with a single sample method. Everything here now is built on top of
that starting point.

## 2. Why the facade exists

Three reasons it earns its place rather than calling AngouriMath directly:

1. **Exception safety.** AngouriMath throws for malformed input; the rest of the system
   assumes results, not exceptions.
2. **A stable seam.** AngouriMath is a third-party dependency. Everything above talks to
   `AlgebraResult`, so replacing or supplementing the CAS touches one file.
3. **Behaviour normalisation** — see §3.

Keep it thin. Mathematics that is genuinely ours belongs in a module, not here.

## 3. AngouriMath behaviours worth knowing

These were found empirically and are pinned by tests:

| Behaviour | Consequence |
|---|---|
| **`Solve` needs an equation, not an expression** | `ToSolvableEntity` promotes a bare expression to `"expr = 0"`. Passing `lhs - rhs` silently fails |
| **A trailing digit is an exponent** | `R1` is `R¹`, `V0` is `V⁰ = 1`. Handled in `SymbolicLinearAlgebra.SymbolName`, **not here** — but be aware if you add symbol handling. MISTAKES **M-005** |
| `Factorize` leaves most quadratics untouched | Why the WPF app prefers our own exact factorizer and falls back to this |
| Names differ from Maxima | `ln` vs `log`, `e` vs `%e` — the parity tests translate |
| Exact rationals are preserved | `1/3` stays `1/3`, not `0.333…`. Do not "helpfully" convert to double |
| `Expand` orders terms ascending | Never assert on term order; compare values |

## 4. Testing

26 tests, deliberately **behavioural rather than string-matching** where the CAS may
legitimately reformat:

- factorization verified by round-trip (`simplify(input − factored) == 0`)
- integration verified by differentiating back
- implicit multiplication (`2x + 3x - 1`)
- graceful failure on empty/whitespace/malformed input

Also cross-checked against Maxima in the modules repo (`MaximaParityTests`), which is the
stronger oracle.

## 5. If you change something here

1. Test this repo, then the whole solution — the `algebra` module, RPC, CLI and the WPF app
   all sit on top.
2. Keep the never-throw contract. Callers rely on it and carry no try/catch.
3. Resist adding maths. If it is not "adapt AngouriMath to our result type", it probably
   belongs in a module.

## 6. Licensing note

AngouriMath is **MIT** — no copyleft, safe to ship, including to the App Store. This matters:
it is one of only two third-party dependencies in the portable core, and its licence is part
of why an iOS port stays viable. See `RW.Mathematics.Modules/Documentation/LICENSING.md`.
