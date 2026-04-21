---
name: exp-test-tagging
description: "Analyzes test suites and tags each test with a standardized set of traits (e.g., positive, negative, critical-path, boundary, smoke, regression). Use when the user wants to categorize, audit, or label tests with traits. Do not use for writing new tests, running tests, or migrating test frameworks."
---

# Test Trait Tagging

Analyze an existing test suite and apply a standardized set of trait tags to each test method, giving teams visibility into their test distribution (positive vs. negative, critical-path coverage, smoke tests, etc.).

## When to Use

- Auditing a test project to understand the mix of test types
- Adding trait attributes to untagged tests
- Generating a summary report of trait distribution across a test suite
- Reviewing whether critical paths have sufficient coverage

## When Not to Use

- Writing new tests from scratch (use `writing-mstest-tests`)
- Running or filtering tests (use `run-tests`)
- Migrating between test frameworks

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test project or files | Yes | Path to the test project, folder, or specific test files to analyze |
| Scope | No | `tag` (apply attributes), `audit` (report only), or `both` (default: `both`) |
| Framework | No | Auto-detected. Override with `mstest`, `xunit`, or `nunit` if detection fails |

## Trait Taxonomy

Use exactly these trait names and values. Do not invent new trait values outside this table.

| Trait Value | Meaning | Heuristics |
|-------------|---------|------------|
| `positive` | Verifies expected behavior under normal/valid conditions | Asserts success, valid output, expected state, no exceptions for valid input |
| `negative` | Verifies correct handling of invalid input, errors, or edge cases | Asserts exceptions, error codes, validation failures, rejects bad input |
| `boundary` | Tests limits, thresholds, empty/null inputs, min/max values | Operates on `0`, `-1`, `int.MaxValue`, empty string, null, empty collection, boundary of valid range |
| `critical-path` | Core workflow that must never break; breakage blocks users | Tests the primary success scenario of a key public API or user-facing feature |
| `smoke` | Quick sanity check that the system is operational | Fast, no complex setup, verifies basic wiring (e.g., service resolves, endpoint returns 200) |
| `regression` | Reproduces a specific previously-reported bug | References a bug ID, issue number, or describes a fix in its name or comments |
| `integration` | Crosses process, network, or persistence boundaries | Uses real database, HTTP client, file system, external service, or multi-component setup |
| `end-to-end` | Full user workflow spanning the entire application stack | Exercises a complete scenario from entry point to final result, distinct from single-boundary `integration` |
| `performance` | Validates timing, throughput, or resource consumption | Asserts on elapsed time, memory, allocations, or uses benchmark harness |
| `security` | Verifies authentication, authorization, input sanitization, or secrets handling | Tests for SQL injection, XSS, CSRF, unauthorized access, token validation, permission checks |
| `concurrency` | Validates thread safety, parallelism, or async correctness | Uses `Task.WhenAll`, locks, `Parallel.ForEach`, `SemaphoreSlim`, reproduces race conditions |
| `resilience` | Tests retry logic, timeouts, circuit breakers, or graceful degradation | Asserts behavior under transient failures, network drops, or service unavailability (e.g., Polly policies) |
| `destructive` | Mutates shared or external state that is hard to roll back | Deletes records, drops resources, modifies global config -- useful for CI isolation decisions |
| `configuration` | Verifies settings loading, defaults, environment behavior | Tests missing config keys, invalid values, environment variable fallbacks, options validation |
| `flaky` | Known to intermittently fail (meta-tag for test health tracking) | Mark tests the team knows are unreliable; used to quarantine or prioritize stabilization |

A single test may have **multiple traits** (e.g., both `negative` and `boundary`). At minimum, every test should receive one of `positive` or `negative`.

## Workflow

### Step 1: Detect the test framework

Examine project files and source code to determine the framework — see the `exp-dotnet-test-frameworks` skill for the complete detection table (package references, test markers, assertion APIs, and skip annotations).

### Step 2: Scan existing traits

Check which tests already have trait attributes:

| Framework | Existing Attribute | Example |
|-----------|--------------------|---------|
| MSTest | `[TestCategory("...")]` | `[TestCategory("positive")]` |
| xUnit | `[Trait("Category", "...")]` | `[Trait("Category", "positive")]` |
| NUnit | `[Category("...")]` | `[Category("positive")]` |

Record which tests already have tags to avoid duplication.

### Step 3: Classify each test method

For each test method without traits, analyze:

1. **Method name** -- names containing `Invalid`, `Fail`, `Error`, `Throw`, `Reject`, `BadInput`, `Null`, `Negative` suggest `negative`
2. **Assertion type** -- `Assert.ThrowsException`, `Assert.Throws`, `Should().Throw()` suggest `negative`
3. **Input values** -- `null`, `""`, `0`, `-1`, `int.MaxValue`, `int.MinValue`, empty collections suggest `boundary`
4. **Setup complexity** -- minimal setup with basic assertions suggests `smoke`; external dependencies suggest `integration`
5. **Comments and names** -- references to issue numbers or "regression" / "bug" / "fix for #..." suggest `regression`
6. **Timing assertions** -- `Stopwatch`, `BenchmarkDotNet`, elapsed-time checks suggest `performance`
7. **Feature centrality** -- tests on primary public API entry points or critical user workflows suggest `critical-path`
8. **Security patterns** -- validates auth, checks permissions, sanitizes input, tests for injection, handles tokens/secrets suggest `security`
9. **Parallel/async constructs** -- `Task.WhenAll`, `Parallel.ForEach`, locks, `SemaphoreSlim`, `ConcurrentDictionary`, race condition names suggest `concurrency`
10. **Fault injection** -- simulates failures, tests retries, timeouts, or circuit breakers suggest `resilience`
11. **State mutation** -- deletes external records, drops resources, modifies shared/global state suggest `destructive`
12. **Full-stack flow** -- test spans entry point through data layer to final response, covering a complete user scenario suggest `end-to-end`
13. **Config/settings** -- loads configuration, tests missing keys, validates options, checks environment variables suggest `configuration`
14. **Known instability** -- test has `[Ignore]`/`[Skip]` comments about flakiness, or names contain "flaky"/"intermittent" suggest `flaky`
15. **Default** -- if the test verifies a normal success path, tag `positive`

When in doubt between `positive` and `negative`, read the assertion: if it asserts success -> `positive`; if it asserts failure -> `negative`.

### Step 4: Apply trait attributes

Add the appropriate attribute to each test method. Place trait attributes on the line directly above or below the existing test attribute.

**MSTest:**
```csharp
[TestMethod]
[TestCategory("negative")]
[TestCategory("boundary")]
public void Parse_NullInput_ThrowsArgumentNullException() { ... }
```

**xUnit:**
```csharp
[Fact]
[Trait("Category", "positive")]
[Trait("Category", "critical-path")]
public void CreateOrder_ValidItems_ReturnsConfirmation() { ... }
```

**NUnit:**
```csharp
[Test]
[Category("regression")]
[Category("negative")]
public void Calculate_OverflowInput_ReturnsError() // Fix for #1234
{ ... }
```

### Step 5: Generate trait summary

After tagging, produce a summary table:

```
## Trait Distribution

| Trait         | Count | % of Total |
|---------------|-------|------------|
| positive      |    42 |      53.8% |
| negative      |    22 |      28.2% |
| boundary      |     8 |      10.3% |
| critical-path |    12 |      15.4% |
| smoke         |     3 |       3.8% |
| regression    |     5 |       6.4% |
| integration   |     4 |       5.1% |
| end-to-end    |     2 |       2.6% |
| performance   |     1 |       1.3% |
| security      |     3 |       3.8% |
| concurrency   |     2 |       2.6% |
| resilience    |     1 |       1.3% |
| destructive   |     1 |       1.3% |
| configuration |     2 |       2.6% |
| flaky         |     1 |       1.3% |
| **Total tests** | **78** | -- |

Note: Percentages exceed 100% because tests can have multiple traits.
```

Include observations such as:
- Ratio of positive to negative tests
- Whether critical-path tests exist for key public APIs
- Any tests that could not be confidently classified (list them for manual review)

## Validation

- [ ] Every test method has at least one trait attribute (`positive` or `negative` at minimum)
- [ ] No invented trait values outside the taxonomy table
- [ ] Existing trait attributes were preserved, not duplicated
- [ ] The trait summary table was generated
- [ ] The project still builds after changes (`dotnet build`)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Guessing traits without reading the test body | Always read assertions and setup to classify accurately |
| Tagging a test only as `boundary` without `positive`/`negative` | Every test should also be `positive` or `negative` -- `boundary` is additive |
| Using `TestCategory` syntax in an xUnit project | Match the attribute style to the detected framework |
| Duplicating an existing category attribute | Check for pre-existing traits in Step 2 before adding |
| Over-tagging as `critical-path` | Reserve for tests on primary public entry points, not every helper |
