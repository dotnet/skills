---
name: test-tagging
description: >
  Classifies existing tests by standard traits and reports their distribution.
  USE FOR: tagging all tests with category attributes, categorizing/tagging/
  labeling each
  test, compare happy vs error paths, audit the test mix, describe coverage shape
  by test type, or tag then verify the project builds. Read bodies when names
  mislead. Apply canonical attributes; otherwise report only. DO NOT USE FOR:
  requests owned by test-anti-patterns, coverage-analysis, crap-score,
  test-gap-analysis, code-testing-agent, or migration skills.
license: MIT
---

# Test Trait Tagging

Analyze an existing test suite in any supported language and apply a standardized set of trait tags to each test method, giving teams visibility into their test distribution (positive vs. negative, critical-path coverage, smoke tests, etc.).

> **Language-specific guidance**: Try `test-analysis-extensions` once. If it is
> unavailable, continue immediately with the built-in framework table below;
> never block tagging on the helper.

## When to Use

- Auditing a test project to understand the mix of test types
- Adding trait attributes to untagged tests
- Generating a summary report of trait distribution across a test suite
- Reviewing whether critical paths have sufficient coverage

## When Not to Use

- Writing new tests from scratch (use `code-testing-agent` for any language, or `writing-mstest-tests` for MSTest)
- Running or filtering tests (use `run-tests` for .NET; equivalent native runners elsewhere)
- Migrating between test frameworks
- General quality, smell, flakiness, or assertion audits (use `test-anti-patterns` or the matching analysis skill)
- Diagnostic .NET executed line/branch/Cobertura interpretation or project-wide CRAP risk (use `coverage-analysis`); raw coverage collection (use `run-tests` for .NET, native tooling otherwise)
- CRAP analysis for a named method, class, or file (use `crap-score`)
- Behavioral gaps where a test would survive broken production logic (use `test-gap-analysis`)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test project or files | No | Path to the test project, folder, or specific test files. Discover from the current workspace when omitted. |
| Scope | No | `tag` (apply canonical attributes, or a confirmed project convention), `audit` (report only), or `both` (default: `both`). Frameworks declared `report-only` always emit a report; `convention-based` frameworks edit only after the user confirms the convention. |
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

Resolve the requested test scope from the current workspace before asking for a
path. The skill context's `Base directory` contains these instructions, not the
user's repository. Always inspect the current working directory before claiming
that repository files are unavailable. If the prompt's relative path is absent,
search the workspace for the named project/file and retry the exact result. A
successful search proves that the target is present; if the normal reader then
reports that same path missing, treat the contradiction as a reader
path-normalization or transport failure rather than asking the user for files.
Use a shell text reader (`sed`/`cat` on Unix,
`Get-Content` on PowerShell) only for a confirmed reader availability,
transport, or path-normalization failure and only after verifying the canonical
path remains inside the current workspace. Stop on content-exclusion,
permission/policy, workspace-boundary, or unknown read failures. Never ask the
user for a path or file contents after a workspace search found a readable
target.

For an `auto-edit` framework, a failed patch/editor call is not a stopping
condition only when the failure is confirmed tool availability, transport, or
path normalization. Do not bypass stale-context, concurrent-change,
permission/policy, or path-boundary errors. Before a shell fallback, resolve
the canonical path inside the current workspace, freshly read the file, and use
an anchored transformation that aborts unless the expected old text and exact
match count are unchanged. Then re-open the complete file, inspect the diff,
and run Step 6 validation. Do not report proposed attributes as completion when
the user asked to apply them.

Identify the language and framework. Try the matching
`test-analysis-extensions` guidance once. If unavailable, classify capability
from the built-in rules below:

- **`auto-edit`** — framework has canonical tag syntax this skill can safely insert (.NET `[TestCategory]` / `[Trait]` / `[Category]` / `[Property]`, pytest `@pytest.mark.<name>`, JUnit 5 `@Tag("...")`, TestNG `groups = {"..."}`, RSpec metadata `it "..." , :tag => true`, Pester `-Tag '...'`, Kotest `@Tags(...)`, Swift Testing `@Tag(.tagName)`, Catch2 `[tag]`, doctest `* doctest::test_suite("tag")` decorator).
- **`report-only`** — framework has no canonical, agreed-upon tag attribute; report tags in a Markdown table only and do not edit source (Go standard `testing` without build-tag conventions, Jest/Vitest without consistent describe-prefix convention, Rust without project-specific cfg conventions, XCTest without a test plan, GoogleTest without test-name prefix conventions, Mocha without describe-prefix conventions).
- **`convention-based`** — framework uses naming or file conventions for tagging (Go `//go:build integration` build tags, file-name suffixes like `*_integration_test.go`, GoogleTest `INTEGRATION_*` filter prefix). Only emit canonical edits when the user has confirmed the project convention; otherwise treat as `report-only`.

Capture the capability before Step 4.

### Step 2: Scan existing traits

Check which tests already have trait attributes. Use the extension when loaded;
otherwise use this built-in table as the source of truth:

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

Build one canonical inventory containing each discovered test exactly once.
Record the test identifier, behavioral classification, and traits in that
inventory; use the same rows for source edits, per-test reporting, totals, and
distribution counts. Do not hand-count a separate denominator. Before
publishing, reconcile the reported total with the number of inventory rows and
verify that every row contributes to each displayed trait count.

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

For a requested distribution or coverage-shape audit, use available production
code to map each test to the exact outcome it exercises before summarizing.
Call out duplicated boundary coverage and whether the test inventory represents
both sides of named thresholds and the observable collaborator outcomes on
business-critical paths. Keep these as concise distribution observations, not
new trait values. Do not perform mutation reasoning, prescribe new tests, or
expand into the behavioral-gap audit owned by `test-gap-analysis`.

### Step 4: Apply trait attributes (or report only)

**If the resolved capability is `auto-edit`**, add the appropriate attribute to
each test method. Place trait attributes adjacent to the existing test
attribute. Examples:

Apply traits at the individual test-method/case level. Do not substitute one
class-level category for method-level classification: different methods usually
exercise different positive, negative, and boundary behavior.

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

**If the resolved capability is `report-only`** (Go standard `testing`, plain
Jest/Vitest without convention, Rust without project-specific cfg, plain
XCTest, plain GoogleTest, plain Mocha), do NOT modify source files. Instead emit
a concise mapping from each test to its suggested tags. Recommend a project-wide
convention only when the user asks how to persist or filter those tags; an
analysis-only request should report and stop.

**If the resolved capability is `convention-based`** (e.g., Go
`//go:build integration`, `*_integration_test.go`, GoogleTest `INTEGRATION_*`
prefix), only emit canonical edits when the user has confirmed the project's
convention. Otherwise treat as `report-only`.

### Step 5: Generate trait summary

After tagging, produce a summary table. Include only traits with a non-zero
count unless the user asks for the full taxonomy; zero-filled rows obscure the
suite's actual shape. For a small report-only suite, keep the per-test mapping
and non-zero distribution together rather than expanding into a dashboard.

```
## Trait Distribution

| Trait         | Count | % of Total |
|---------------|-------|------------|
| positive      |    50 |      64.1% |
| negative      |    28 |      35.9% |
| boundary      |     8 |      10.3% |
| critical-path |    12 |      15.4% |
| **Total tests** | **78** | -- |

Note: Percentages exceed 100% because tests can have multiple traits.
```

Include observations such as:
- Ratio of positive to negative tests
- Whether critical-path tests exist for key public APIs
- Any tests that could not be confidently classified (list them for manual review)

`boundary` and every other specialized trait are additive. A boundary success
case still counts as `positive`; a rejected boundary still counts as `negative`.
Derive the positive/negative distribution after applying this rule.

### Step 6: Verify edits before reporting

For every `auto-edit` framework, run the narrowest command that compiles the
edited attributes and confirms test discovery. This is required even when the
user asks only to add tags: syntactically plausible attributes are not a
completed edit.

| Framework | Minimum verification |
|---|---|
| .NET | Run `dotnet build <test-project>`, then confirm discovery with `dotnet test <test-project> --list-tests --no-build`. Do not execute the suite unless the user asks; route execution to `run-tests`. |
| pytest | collect the edited suite with the repository's configured pytest command |
| JUnit/TestNG | compile tests through the repository's Maven/Gradle test task |
| Other auto-edit frameworks | Use the repository's narrowest compile or test-discovery command |

If an edit or patch application was uncertain, re-open the complete edited file
before verification and reconcile every inventory row with the actual
attribute next to that test. Do not report success from a partial diff or from
the intended patch. If verification fails, report the exact command and error;
never publish a successful distribution handoff for uncompiled edits.

## Validation

- [ ] Every test method has at least one trait classification (`positive` or `negative` at minimum) — in the report for `report-only` frameworks, or as an attribute for `auto-edit` frameworks
- [ ] The total equals the per-test inventory count, and displayed trait counts were derived from that inventory
- [ ] No invented trait values outside the taxonomy table
- [ ] Existing trait attributes were preserved, not duplicated
- [ ] The trait summary table was generated
- [ ] For `auto-edit` frameworks, the project still builds / tests still discover without executing unrequested tests (`dotnet build` plus list mode / `pytest --collect-only` / `mvn test-compile` / `go vet ./...` / `cargo check --tests` / `npm run test:list` / equivalent)
- [ ] The final summary cites successful validation commands and the discovered test count when a discovery command is available
- [ ] For `report-only` frameworks, no source files were modified
- [ ] For `convention-based` frameworks, edits were applied ONLY when a project convention was confirmed

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Guessing traits without reading the test body | Always read assertions and setup to classify accurately |
| Tagging a test only as `boundary` without `positive`/`negative` | Every test should also be `positive` or `negative` -- `boundary` is additive |
| Using the wrong attribute syntax for the detected framework | Match the loaded extension or built-in table (don't put `[TestCategory]` in xUnit or `@pytest.mark.x` in unittest) |
| Duplicating an existing category attribute | Check for pre-existing traits in Step 2 before adding |
| Over-tagging as `critical-path` | Reserve for tests on primary public entry points, not every helper |
| Editing Go / plain Jest / plain Rust / plain XCTest / plain GoogleTest source | These are `report-only` by default — emit a Markdown table instead. Only edit if the user confirms a project-wide convention (build tag, file suffix, describe-prefix, test-plan grouping). |
| Inventing tag prefixes for convention-based frameworks | Confirm the project's existing convention before adopting one — don't guess between `_integration_test.go`, `//go:build integration`, or `IntegrationTest` prefix |
| Missing language-specific concurrency / async primitives | Use the loaded extension when available; otherwise use the Trait Taxonomy concurrency row |
