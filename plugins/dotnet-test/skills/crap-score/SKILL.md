---
name: crap-score
description: >
  Calculates CRAP (Change Risk Anti-Patterns) for a named .NET method, class, or
  file. USE FOR: explicit CRAP calculation or coverage-and-complexity risk
  within that named target, including which tests to prioritize. DO NOT USE
  FOR: project-wide coverage/CRAP, plateaus, or project-wide blockers/priorities
  (coverage-analysis); behavioral/pseudo-mutation gaps (test-gap-analysis);
  writing tests; test runs without CRAP context.
license: MIT
---

# CRAP Score Analysis

Calculate CRAP (Change Risk Anti-Patterns) scores for .NET methods to identify code that is both complex and undertested.

## Background

The CRAP score combines **cyclomatic complexity** and **code coverage** into a single metric:

$$\text{CRAP}(m) = \text{comp}(m)^2 \times (1 - \text{cov}(m))^3 + \text{comp}(m)$$

Where:

- $\text{comp}(m)$ = cyclomatic complexity of method $m$
- $\text{cov}(m)$ = code coverage ratio (0.0 to 1.0) of method $m$

| CRAP Score | Risk Level | Interpretation |
|------------|------------|----------------|
| < 5        | Low        | Simple and well-tested |
| 5 to < 15  | Moderate   | Acceptable for most code |
| 15 to 30   | High       | Needs more tests or simplification |
| > 30       | Critical   | Refactor and add coverage urgently |

A method with 100% coverage has CRAP = complexity (the minimum). A method with 0% coverage has CRAP = complexity^2 + complexity.

## When to Use

- User wants to assess which methods are risky due to low coverage and high complexity
- User asks for CRAP score of specific methods, classes, or files
- User wants to prioritize what to test next within a named method, class, or file based on coverage-and-complexity risk
- User wants to evaluate test quality beyond simple coverage percentages

## When Not to Use

- User just wants to run tests (use `run-tests` skill)
- User wants to write new tests (use `code-testing-agent`)
- User only wants a coverage percentage without complexity analysis
- User wants project-wide coverage/CRAP analysis or priorities (use `coverage-analysis`)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Target scope | Yes | Method name, class name, or file path to analyze |
| Test project path | No | Path to the test project. Defaults to discovering test projects in the solution. |
| Source project path | No | Path to the source project under analysis |

## Workflow

### Step 1: Collect code coverage data

If the user supplies a valid Cobertura report that contains the requested
target, use it directly and do not rerun tests. If the supplied report is
malformed, empty, internally contradictory, or missing the target, treat it as
failed input: regenerate it with a repository-compatible command when possible,
or request a valid report when collection is unavailable. Otherwise invoke
`run-tests` to classify the repository's test platform and confirm the
compatible command shape, then require a command that emits Cobertura:

| Coverage provider | Cobertura command |
|---|---|
| `coverlet.collector` with VSTest | `dotnet test <test.csproj> --collect:"XPlat Code Coverage" --results-directory <results-dir>` |
| `Microsoft.Testing.Extensions.CodeCoverage` with .NET 9 bridged MTP | `dotnet test <test.csproj> -- --coverage --coverage-output-format cobertura --coverage-output <output-path>` |
| `Microsoft.Testing.Extensions.CodeCoverage` with .NET 10+ native MTP | `dotnet test --project <test.csproj> --coverage --coverage-output-format cobertura --coverage-output <output-path>` |

Use an equivalent repository-owned command when the project defines one. Search
the results directory recursively when the collector creates a GUID subfolder.
Do not substitute a generic binary `.coverage` command when no converter is
available.

Do not stop at the first restore, compilation, test, or collector failure.
Classify the failing layer, inspect every report the command emitted, and
exhaust non-persistent retries before asking for input. Safe retries include
command-line MSBuild properties that leave source and manifests unchanged and
an already-installed or repository-provided alternative collector. A trivial
source error is a collection blocker, not the final analysis, when a reversible
command-line setting can compile the same source. Never call an empty Cobertura
file a successful fallback.

For classic non-SDK projects (`ToolsVersion`, explicit compile items, or
`packages.config`), use only a repository-provided coverage command that emits
Cobertura. If none exists, request Cobertura XML and stop; do not migrate the
project or inject an SDK-style provider. CRAP scores always require real
coverage data.

#### Never estimate coverage

**Guessed coverage produces wrong CRAP scores, which is worse than no answer.**
For a classic project with no repository coverage command or existing report,
stop here and request Cobertura.

Do not add coverage packages, change project manifests, or install global tools
unless the user explicitly authorized dependency/tooling changes. If the
repository lacks a usable provider or converter, report that exact prerequisite
and the compatible command shape identified through `run-tests`, then stop. If
an existing binary `.coverage` report is present, convert it only with an
already-installed or repository-provided converter; otherwise request
authorization or a Cobertura export. If tests execute with failures but still
emit valid coverage, continue with that data and note the failures. Report
complexity on its own if useful, but never publish a CRAP number derived from
assumed coverage.

Before using a report, verify that it parses, contains at least one class and
method, and contains the requested target. An empty report or a report that
omits the target is failed collection or filtering, not 0% coverage. Regenerate
coverage when possible; otherwise stop without publishing a CRAP score.

If the user supplies an existing report, state that it was not regenerated.
Do not describe its data as current unless its provenance is established by
running the repository's coverage command in this analysis.

### Step 2: Compute cyclomatic complexity

Prefer a machine-produced per-method complexity from a repository-provided code
metrics report or from the Cobertura method's `complexity` attribute when that
report maps to the current source. Microsoft.CodeAnalysis.Metrics can generate
method-level `CyclomaticComplexity` data through `msbuild /t:Metrics`, but do
not add the package or modify the project without user approval.

If no machine-produced metric exists, analyze the current target source and
label the result as a manual complexity count. Count the following decision
points (each adds 1 to the base complexity of 1):

| Construct | Example |
|-----------|---------|
| `if` | `if (x > 0)` |
| `else if` | `else if (y < 0)` |
| `case` (each) | `case 1:` |
| `for` | `for (int i = 0; ...)` |
| `foreach` | `foreach (var item in list)` |
| `while` | `while (running)` |
| `do...while` | `do { } while (cond)` |
| `catch` (each) | `catch (Exception ex)` |
| `&&` | `if (a && b)` |
| `\|\|` (OR) | `if (a \|\| b)` |
| `??` | `value ?? fallback` |
| `?.` | `obj?.Method()` |
| `? :` (ternary) | `x > 0 ? a : b` |
| Pattern match arm | `x is > 0 and < 10` |

Base complexity is 1 for every method. Each decision point adds 1.

When counting manually, read the source file, report the construct-by-construct
breakdown, and do not use a source comment as evidence. Count every occurrence,
including operators nested inside arguments or return expressions; before
declaring a conflict, rescan specifically for `&&`, `||`, `??`, `?.`, ternaries,
and switch/pattern arms. If a supplied report maps to the current method and a
careful recount agrees, use its metric decisively. If a genuine disagreement
remains, label both sources; when the user explicitly asked to use that report,
calculate the primary CRAP result from its machine-produced metric and present
the manual count as a caveat rather than withholding the requested result.

### Step 3: Extract per-method coverage from Cobertura XML

Parse the Cobertura XML to find each method's `line-rate` attribute under the target `<class>` element. If `line-rate` is not available at method level, compute it from the `<lines>` elements:

$$\text{cov}(m) = \frac{\text{lines with hits} > 0}{\text{total lines}}$$

Method names in Cobertura may differ from source (async methods, lambdas). Match by line ranges when names don't align.

When both `line-rate` and `<lines>` exist, recompute the hit ratio and compare
them. Allow only normal report rounding (one percentage point); if they differ
more, the report contradicts itself. Regenerate it or report the conflict and
stop without calculating CRAP. Never silently choose whichever value produces
the expected score.

### Step 4: Calculate CRAP scores

For each method in scope, apply the formula:

$$\text{CRAP}(m) = \text{comp}(m)^2 \times (1 - \text{cov}(m))^3 + \text{comp}(m)$$

Use a calculator or script for the arithmetic and show the substituted
complexity and coverage. Do not calculate the formula mentally. Answer a named
method directly; analyze unrelated methods only when the requested scope is a
class or file. Once the requested result is established, do not append
hypothetical refactor scores or coverage targets unless the user asked for
them. Any numeric example must also come from the calculator or script.

### Step 5: Present results

Present a sorted table (highest CRAP first):

```text
| Method                          | Complexity | Coverage | CRAP Score | Risk     |
|---------------------------------|------------|----------|------------|----------|
| OrderService.ProcessOrder       | 10         | 45%      | 26.6       | High     |
| OrderService.ValidateItems      | 8          | 90%      | 8.1        | Moderate |
| OrderService.CalculateTotal     | 3          | 100%     | 3.0        | Low      |
```

Include:

- **Summary**: total methods analyzed, how many in each risk category
- **Top offenders**: methods with CRAP > 30, with specific recommendations
- **Quick wins**: methods with high complexity but where small coverage improvements would drop the score significantly

### Step 6: Provide actionable recommendations

For high-CRAP methods, suggest one or both:

1. **Add tests** -- identify uncovered branches and suggest specific test cases
2. **Reduce complexity** -- suggest extract-method refactoring for deeply nested logic

Calculate the **coverage needed** to bring a method below a CRAP threshold of 15:

$$\text{cov}_{\text{needed}} = 1 - \left(\frac{15 - \text{comp}}{\text{comp}^2}\right)^{1/3}$$

This formula only applies when comp < 15. When comp >= 15, the minimum possible CRAP score (at 100% coverage) is comp itself, which already meets or exceeds the threshold. In that case, **coverage alone cannot bring the CRAP score below the threshold** -- the method must be refactored to reduce its cyclomatic complexity first.

Report this as: "To bring `ProcessOrder` (complexity 10) below CRAP 15, increase coverage from 45% to more than 63.2% (at least 64% when reporting whole percentages)." For methods where complexity alone exceeds the threshold, report: "`ComplexMethod` (complexity 18) cannot reach CRAP < 15 through testing alone -- reduce complexity by extracting sub-methods."

## Validation

- Verify that coverage data was collected successfully (Cobertura XML exists and contains data)
- Confirm the target method is present; absence is not evidence of 0% coverage
- Confirm every coverage figure came from that XML — no estimated, assumed, or source-comment-derived values
- Cross-check method `line-rate` against its line-hit ratio when both exist
- Cross-check that method names in coverage data match the source code
- Confirm CRAP scores with calculator or script output
- Ensure a 100%-covered method's CRAP equals its complexity exactly

## Common Pitfalls

- **Estimating coverage when collection fails**: never do it — the resulting CRAP scores are wrong in the direction that matters. Use the repository-compatible Cobertura path confirmed through `run-tests`, then report the blocker instead.
- **Treating an empty report or missing method as 0% coverage**: this is failed collection, filtering, or method mapping; do not manufacture a score.
- **Trusting contradictory Cobertura fields**: compare `line-rate` with the line-hit ratio and stop if they disagree beyond rounding.
- **Trusting a stale complexity comment in the source**: compute cyclomatic complexity from the current code; a `// complexity: 7` comment left by a previous author is not evidence.
- **Mental CRAP arithmetic**: use a calculator or script and show the substituted inputs.
- **Changing tooling to bypass a collector error**: report the failed repository-compatible command and missing prerequisite; do not install a global collector or edit manifests without explicit authorization.
- **Stale coverage data**: regenerate when the user asks for current results or the source/binaries changed; otherwise disclose that a supplied report was not regenerated.
- **Method name mismatches**: Cobertura XML may use mangled/compiler-generated names for async methods, lambdas, or local functions. Match by line ranges when names don't align.
- **Generated code**: Exclude auto-generated files (e.g., `*.Designer.cs`, `*.g.cs`) from analysis unless explicitly requested.
