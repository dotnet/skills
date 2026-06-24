---
name: test-tagging
description: "Analyzes test suites in any language and tags each test with a standardized set of traits (positive, negative, critical-path, boundary, smoke, regression, integration, performance, security). Use when the user wants to categorize, audit, or label tests with traits. Works with .NET (MSTest TestCategory / xUnit Trait / NUnit Category / TUnit Property), Python (pytest markers; unittest has no canonical tag syntax so report-only), TypeScript/JavaScript (Jest/Vitest test names, describe-block conventions), Java (JUnit 5 @Tag / TestNG groups), Go (subtest naming / build tags / file _test.go), Ruby (RSpec metadata), Rust (cargo test naming / cfg attributes), Swift (XCTest test plans / Swift Testing @Tag), Kotlin (JUnit @Tag / Kotest tags), PowerShell (Pester -Tag), C++ (GoogleTest filter prefixes / Catch2 [tags] / doctest decorators). Auto-edits when the framework has canonical syntax; falls back to report-only otherwise. Do not use for writing new tests, running tests, or migrating frameworks."
license: MIT
---

# Test Trait Tagging

Analyze an existing test suite in any supported language and apply a standardized set of trait tags to each test method, giving teams visibility into their test distribution (positive vs. negative, critical-path coverage, smoke tests, etc.).

> **Language-specific guidance**: Call the `test-analysis-extensions` skill to discover available extension files, then read the file matching the target codebase. The extension file documents framework-specific tag attributes and a "tag-support capability" (auto-edit, report-only, or convention-based) that drives whether this skill modifies source files or only emits a report.

## When to Use

- Auditing a test project to understand the mix of test types
- Adding trait attributes to untagged tests
- Generating a summary report of trait distribution across a test suite
- Reviewing whether critical paths have sufficient coverage

## When Not to Use

- Writing new tests from scratch (use `code-testing-agent` for any language, or `writing-mstest-tests` for MSTest)
- Running or filtering tests (use `run-tests` for .NET; equivalent native runners elsewhere)
- Migrating between test frameworks

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test project or files | Yes | Path to the test project, folder, or specific test files to analyze |
| Scope | No | `tag` (apply attributes when language supports auto-edit), `audit` (report only), or `both` (default: `both`). For languages with no canonical tag syntax, the skill emits a report regardless of scope. |
| Framework | No | Auto-detected. Override when detection fails. |

## Trait Taxonomy

Use exactly these trait names and values. Do not invent new trait values outside this table.

| Trait Value | Meaning | Heuristics |
|-------------|---------|------------|
| `positive` | Verifies expected behavior under normal/valid conditions | Asserts success, valid output, expected state, no exceptions for valid input |
| `negative` | Verifies correct handling of invalid input, errors, or edge cases | Asserts exceptions, error codes, validation failures, rejects bad input |
| `boundary` | Tests limits, thresholds, empty/null/None/nil inputs, min/max values | Operates on `0`, `-1`, `int.MaxValue` / `sys.maxsize` / `Number.MAX_SAFE_INTEGER` / `math.MaxInt64` / `i32::MAX`, empty string, null/None/nil/undefined, empty collection, boundary of valid range |
| `critical-path` | Core workflow that must never break; breakage blocks users | Tests the primary success scenario of a key public API or user-facing feature |
| `smoke` | Quick sanity check that the system is operational | Fast, no complex setup, verifies basic wiring (e.g., service resolves, endpoint returns 200) |
| `regression` | Reproduces a specific previously-reported bug | References a bug ID, issue number, or describes a fix in its name or comments |
| `integration` | Crosses process, network, or persistence boundaries | Uses real database, HTTP client, file system, external service, or multi-component setup |
| `end-to-end` | Full user workflow spanning the entire application stack | Exercises a complete scenario from entry point to final result, distinct from single-boundary `integration` |
| `performance` | Validates timing, throughput, or resource consumption | Asserts on elapsed time, memory, allocations, or uses benchmark harness (BenchmarkDotNet, pytest-benchmark, benchmark.js, JMH, `go test -bench`, criterion.rs, XCTMetric, kotlinx-benchmark, Google Benchmark) |
| `security` | Verifies authentication, authorization, input sanitization, or secrets handling | Tests for SQL injection, XSS, CSRF, unauthorized access, token validation, permission checks |
| `concurrency` | Validates thread safety, parallelism, or async correctness | Uses `Task.WhenAll` / `Parallel.ForEach` / `SemaphoreSlim` (.NET); `asyncio.gather` / `threading.Lock` / `multiprocessing` (Python); `Promise.all` / worker threads (JS/TS); `CompletableFuture` / `ExecutorService` / `synchronized` (Java); `go func` / `sync.WaitGroup` / `sync.Mutex` / `chan` (Go); `Mutex` / `Thread.new` (Ruby); `tokio::spawn` / `Arc<Mutex<_>>` / `crossbeam` (Rust); `DispatchQueue` / `actor` (Swift); `coroutineScope` / `Mutex` (Kotlin); `Start-Job` / `RunspacePool` (PowerShell); `std::thread` / `std::mutex` (C++); reproduces race conditions |
| `resilience` | Tests retry logic, timeouts, circuit breakers, or graceful degradation | Asserts behavior under transient failures, network drops, or service unavailability (e.g., Polly, tenacity, p-retry, resilience4j, hystrix, opossum, retry-go) |
| `destructive` | Mutates shared or external state that is hard to roll back | Deletes records, drops resources, modifies global config -- useful for CI isolation decisions |
| `configuration` | Verifies settings loading, defaults, environment behavior | Tests missing config keys, invalid values, environment variable fallbacks, options validation |
| `flaky` | Known to intermittently fail (meta-tag for test health tracking) | Mark tests the team knows are unreliable; used to quarantine or prioritize stabilization |

A single test may have **multiple traits** (e.g., both `negative` and `boundary`). At minimum, every test should receive one of `positive` or `negative`.

## Workflow

### Step 1: Detect the language, framework, and tagging capability

Identify the codebase's language and test framework. Call the `test-analysis-extensions` skill and read the matching extension file. The extension file declares a **tag-support capability** for each framework:

- **`auto-edit`** — framework has canonical tag syntax this skill can safely insert (.NET `[TestCategory]` / `[Trait]` / `[Category]` / `[Property]`, pytest `@pytest.mark.<name>`, JUnit 5 `@Tag("...")`, TestNG `groups = {"..."}`, RSpec metadata `it "..." , :tag => true`, Pester `-Tag '...'`, Kotest `@Tags(...)`, Swift Testing `@Tag(.tagName)`, Catch2 `[tag]`, doctest `* doctest::test_suite("tag")` decorator).
- **`report-only`** — framework has no canonical, agreed-upon tag attribute; report tags in a Markdown table only and do not edit source (Go standard `testing` without build-tag conventions, Jest/Vitest without consistent describe-prefix convention, Rust without project-specific cfg conventions, XCTest without a test plan, GoogleTest without test-name prefix conventions, Mocha without describe-prefix conventions).
- **`convention-based`** — framework uses naming or file conventions for tagging (Go `//go:build integration` build tags, file-name suffixes like `*_integration_test.go`, GoogleTest `INTEGRATION_*` filter prefix). Only emit canonical edits when the user has confirmed the project convention; otherwise treat as `report-only`.

Capture the capability before Step 4.

### Step 2: Scan existing traits

Check which tests already have trait attributes. Use the loaded language extension as the source of truth — examples:

| Framework | Existing Attribute | Example |
|-----------|--------------------|---------|
| MSTest | `[TestCategory("...")]` | `[TestCategory("positive")]` |
| xUnit | `[Trait("Category", "...")]` | `[Trait("Category", "positive")]` |
| NUnit | `[Category("...")]` | `[Category("positive")]` |
| TUnit | `[Property("Category", "...")]` | `[Property("Category", "positive")]` |
| JUnit 5 | `@Tag("...")` | `@Tag("positive")` |
| TestNG | `@Test(groups = {"..."})` | `@Test(groups = {"positive"})` |
| pytest | `@pytest.mark.<name>` | `@pytest.mark.positive` |
| RSpec | metadata after `it` | `it "...", :positive do` |
| Pester | `-Tag '...'` | `It '...' -Tag 'positive'` |
| Kotest | `@Tags(...)` | `@Tags(Positive)` |
| Swift Testing | `@Tag(.<name>)` | `@Test(.tags(.positive))` |
| Catch2 | `[tag]` in name | `TEST_CASE("...", "[positive]")` |
| doctest | `* doctest::test_suite("...")` decorator | `TEST_CASE("..." *doctest::test_suite("positive"))` |

Record which tests already have tags to avoid duplication.

### Step 3: Classify each test method

For each test method without traits, analyze:

1. **Method name** -- names containing `Invalid`, `Fail`, `Error`, `Throw`, `Reject`, `BadInput`, `Null`, `None`, `Nil`, `Negative`, `raises_`, `_throws_`, `_returns_error` suggest `negative`
2. **Assertion type** -- `Assert.ThrowsException` / `Assert.Throws` / `Should().Throw()` / `pytest.raises` / `expect(fn).toThrow` / `assertThrows` / `assert.Error(t, err)` / `expect { ... }.to raise_error` / `#[should_panic]` / `XCTAssertThrowsError` / `Should -Throw` / `EXPECT_THROW` suggest `negative`
3. **Input values** -- `null` / `None` / `nil` / `undefined`, `""`, `0`, `-1`, `int.MaxValue` / `sys.maxsize` / `Number.MAX_SAFE_INTEGER` / `math.MaxInt64` / `i32::MAX`, empty collections suggest `boundary`
4. **Setup complexity** -- minimal setup with basic assertions suggests `smoke`; external dependencies (file/db/net/env) suggest `integration`
5. **Comments and names** -- references to issue numbers or "regression" / "bug" / "fix for #..." suggest `regression`
6. **Timing assertions** -- `Stopwatch`, `BenchmarkDotNet`, elapsed-time checks; pytest-benchmark fixtures; benchmark.js; JMH `@Benchmark`; `go test -bench`; criterion.rs; XCTMetric; Google Benchmark; kotlinx-benchmark suggest `performance`
7. **Feature centrality** -- tests on primary public API entry points or critical user workflows suggest `critical-path`
8. **Security patterns** -- validates auth, checks permissions, sanitizes input, tests for injection, handles tokens/secrets suggest `security`
9. **Parallel/async constructs** -- per-language concurrency primitives (see Trait Taxonomy table) suggest `concurrency`
10. **Fault injection** -- simulates failures, tests retries, timeouts, or circuit breakers suggest `resilience`
11. **State mutation** -- deletes external records, drops resources, modifies shared/global state suggest `destructive`
12. **Full-stack flow** -- test spans entry point through data layer to final response, covering a complete user scenario suggest `end-to-end`
13. **Config/settings** -- loads configuration, tests missing keys, validates options, checks environment variables suggest `configuration`
14. **Known instability** -- test has skip / ignore annotations with comments about flakiness, or names contain "flaky" / "intermittent" suggest `flaky`
15. **Default** -- if the test verifies a normal success path, tag `positive`

When in doubt between `positive` and `negative`, read the assertion: if it asserts success -> `positive`; if it asserts failure -> `negative`.

### Step 4: Apply trait attributes (or report only)

**If the loaded language extension declares `auto-edit` for the framework**, add the appropriate attribute to each test method. Place trait attributes adjacent to the existing test attribute. Examples:

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

**pytest:**
```python
@pytest.mark.negative
@pytest.mark.boundary
def test_parse_none_input_raises_value_error():
    ...
```

**JUnit 5:**
```java
@Test
@Tag("positive")
@Tag("critical-path")
void createOrder_validItems_returnsConfirmation() { ... }
```

**TestNG:**
```java
@Test(groups = {"negative", "boundary"})
public void parse_nullInput_throwsIllegalArgumentException() { ... }
```

**RSpec:**
```ruby
it "rejects null input", :negative, :boundary do
  ...
end
```

**Pester:**
```powershell
It 'Rejects null input' -Tag 'negative','boundary' {
    ...
}
```

**Kotest:**
```kotlin
@Tags(Negative, Boundary)
class ParserSpec : StringSpec({
    "rejects null input" { ... }
})
```

**Swift Testing:**
```swift
@Test(.tags(.negative, .boundary))
func parseNullInputThrows() throws { ... }
```

**Catch2:**
```cpp
TEST_CASE("Parse null input throws", "[negative][boundary]") { ... }
```

**If the loaded language extension declares `report-only` for the framework** (Go standard `testing`, plain Jest/Vitest without convention, Rust without project-specific cfg, plain XCTest, plain GoogleTest, plain Mocha), do NOT modify source files. Instead emit a Markdown table mapping each test to its suggested tags, and recommend a project-wide convention the team can adopt (build tags, file suffix, describe-block prefix, GoogleTest filter prefix, test-plan grouping, etc.).

**If the loaded language extension declares `convention-based`** (e.g., Go `//go:build integration`, `*_integration_test.go`, GoogleTest `INTEGRATION_*` prefix), only emit canonical edits when the user has confirmed the project's convention. Otherwise treat as `report-only`.

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

- [ ] Every test method has at least one trait classification (`positive` or `negative` at minimum) — in the report for `report-only` frameworks, or as an attribute for `auto-edit` frameworks
- [ ] No invented trait values outside the taxonomy table
- [ ] Existing trait attributes were preserved, not duplicated
- [ ] The trait summary table was generated
- [ ] For `auto-edit` frameworks, the project still builds / tests still discover after changes (`dotnet build` / `pytest --collect-only` / `mvn test-compile` / `go vet ./...` / `cargo check --tests` / `npm run test:list` / `Invoke-Pester -PassThru -Skip` / equivalent)
- [ ] For `report-only` frameworks, no source files were modified
- [ ] For `convention-based` frameworks, edits were applied ONLY when a project convention was confirmed

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Guessing traits without reading the test body | Always read assertions and setup to classify accurately |
| Tagging a test only as `boundary` without `positive`/`negative` | Every test should also be `positive` or `negative` -- `boundary` is additive |
| Using the wrong attribute syntax for the detected framework | Match the attribute style to the loaded language extension (don't put `[TestCategory]` in an xUnit project or `@pytest.mark.x` in a unittest test) |
| Duplicating an existing category attribute | Check for pre-existing traits in Step 2 before adding |
| Over-tagging as `critical-path` | Reserve for tests on primary public entry points, not every helper |
| Editing Go / plain Jest / plain Rust / plain XCTest / plain GoogleTest source | These are `report-only` by default — emit a Markdown table instead. Only edit if the user confirms a project-wide convention (build tag, file suffix, describe-prefix, test-plan grouping). |
| Inventing tag prefixes for convention-based frameworks | Confirm the project's existing convention before adopting one — don't guess between `_integration_test.go`, `//go:build integration`, or `IntegrationTest` prefix |
| Missing language-specific concurrency / async primitives | Each language has its own primitives — read the loaded language extension and the Trait Taxonomy concurrency row before classifying as `concurrency` |
