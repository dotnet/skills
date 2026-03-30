---
name: crap-score
description: >
  Calculates CRAP (Change Risk Anti-Patterns) score for .NET methods, classes,
  or files. Use when the user asks to assess test quality, identify risky
  untested code, compute CRAP scores, or evaluate whether complex methods have
  sufficient test coverage. Requires code coverage data (Cobertura XML) and
  cyclomatic complexity analysis.
  DO NOT USE FOR: writing tests, general test execution unrelated to coverage/CRAP
  analysis, or general code coverage reporting without CRAP context.
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
| 5-15       | Moderate   | Acceptable for most code |
| 15-30      | High       | Needs more tests or simplification |
| > 30       | Critical   | Refactor and add coverage urgently |

A method with 100% coverage has CRAP = complexity (the minimum). A method with 0% coverage has CRAP = complexity^2 + complexity.

## When to Use

- User wants to assess which methods are risky due to low coverage and high complexity
- User asks for CRAP score of specific methods, classes, or files
- User wants to prioritize which code to test next
- User wants to evaluate test quality beyond simple coverage percentages

## When Not to Use

- User just wants to run tests (use `run-tests` skill)
- User wants to write new tests (use `writing-mstest-tests` skill or general coding assistance)
- User only wants a coverage percentage without complexity analysis

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Target scope | Yes | Method name, class name, or file path to analyze |
| Test project path | No | Path to the test project. Defaults to discovering test projects in the solution. |
| Source project path | No | Path to the source project under analysis |

## Workflow

### Step 1: Collect code coverage data

If no coverage data exists yet (no Cobertura XML available), **always run `dotnet test` with coverage collection first** and mention the exact command in your response. Do not skip this step -- CRAP scores require coverage data.

Check the test project's `.csproj` for the coverage package, then run the appropriate command:

| Coverage Package | Command | Output Location |
|---|---|---|
| `coverlet.collector` | `dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults` | Typically under `TestResults/<guid>/coverage.cobertura.xml`. Search recursively under the results directory (for example, `TestResults/**/coverage.cobertura.xml`) or use any explicit coverage path the user provides. |
| `Microsoft.Testing.Extensions.CodeCoverage` (.NET 9) | `dotnet test -- --coverage --coverage-output-format cobertura --coverage-output ./TestResults` | `--coverage-output` path |
| `Microsoft.Testing.Extensions.CodeCoverage` (.NET 10+) | `dotnet test --coverage --coverage-output-format cobertura --coverage-output ./TestResults` | `--coverage-output` path |

### Step 2: Compute cyclomatic complexity

Analyze the target source files to determine cyclomatic complexity per method. Count the following decision points (each adds 1 to the base complexity of 1):

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

When analyzing, read the source file and count these constructs per method. Report the breakdown.

### Step 3: Extract per-method coverage from Cobertura XML

Parse the Cobertura XML to find each method's `line-rate` attribute under the target `<class>` element. If `line-rate` is not available at method level, compute it from the `<lines>` elements:

$$\text{cov}(m) = \frac{\text{lines with hits} > 0}{\text{total lines}}$$

Method names in Cobertura may differ from source (async methods, lambdas). Match by line ranges when names don't align.

### Step 4: Calculate CRAP scores

For each method in scope, apply the formula:

$$\text{CRAP}(m) = \text{comp}(m)^2 \times (1 - \text{cov}(m))^3 + \text{comp}(m)$$

### Step 5: Present results

Present a sorted table (highest CRAP first):

```
| Method                          | Complexity | Coverage | CRAP Score | Risk     |
|---------------------------------|------------|----------|------------|----------|
| OrderService.ProcessOrder       | 12         | 45%      | 28.4       | High     |
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

Report this as: "To bring `ProcessOrder` (complexity 12) below CRAP 15, increase coverage from 45% to at least 72%." For methods where complexity alone exceeds the threshold, report: "`ComplexMethod` (complexity 18) cannot reach CRAP < 15 through testing alone -- reduce complexity by extracting sub-methods."

## Validation

- Verify that coverage data was collected successfully (Cobertura XML exists and contains data)
- Cross-check that method names in coverage data match the source code
- Confirm CRAP scores by spot-checking the formula on one method manually
- Ensure a 100%-covered method's CRAP equals its complexity exactly

## Common Pitfalls

- **Stale coverage data**: Always regenerate coverage before computing CRAP scores. Old coverage files will produce misleading results.
- **Method name mismatches**: Cobertura XML may use mangled/compiler-generated names for async methods, lambdas, or local functions. Match by line ranges when names don't align.
- **Generated code**: Exclude auto-generated files (e.g., `*.Designer.cs`, `*.g.cs`) from analysis unless explicitly requested.
