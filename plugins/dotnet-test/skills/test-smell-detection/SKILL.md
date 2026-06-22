---
name: test-smell-detection
description: >
  Deep-dive audit using the full testsmells.org 19-smell academic catalog
  for tests in any language. Every finding maps to a named, citable smell
  from the research literature (Assertion Roulette, Duplicate Assert,
  Mystery Guest, Eager Test, Sensitive Equality, Conditional Test Logic,
  Sleepy Test, Magic Number Test, etc.) with research-backed severity.
  Polyglot: .NET (MSTest/xUnit/NUnit/TUnit), Python (pytest/unittest),
  TS/JS (Jest/Vitest/Mocha/node:test), Java (JUnit/TestNG), Go, Ruby
  (RSpec/Minitest), Rust, Swift, Kotlin (JUnit/Kotest), PowerShell
  (Pester), C++ (GoogleTest/Catch2).
  INVOKE ONLY when explicitly asked for the testsmells.org 19-smell
  academic catalog or citable smell names from the literature.
  DO NOT USE FOR: general or pragmatic audits — use test-anti-patterns;
  writing new tests (use code-testing-agent, or writing-mstest-tests for
  MSTest); running tests (use run-tests); framework migration.
license: MIT
---

# Test Smell Detection

Deep formal audit of test code in any supported language using an academic test smell taxonomy. Detects symptoms of bad design or implementation decisions that make tests harder to understand, more fragile, less effective at catching bugs, or more expensive to maintain. Produces a severity-ranked report with specific locations and actionable fixes.

> **Language-specific guidance**: Call the `test-analysis-extensions` skill to discover available extension files, then read the file matching the target codebase. The extension file documents test markers, sleep / time / random APIs, skip annotations, setup/teardown, mystery-guest indicators (file/database/network/env), integration markers, and language-specific calibration notes that drive the smell detectors below.

## Why Test Smells Matter

Test smells erode confidence in a test suite and inflate maintenance costs:

| Problem | Consequence |
|---------|-------------|
| Tests with conditional logic | Some paths never execute — hidden testing gaps |
| Tests that depend on external resources | Flaky failures, slow execution, environment coupling |
| Tests that sleep to wait for results | Non-deterministic timing, slow suites, false failures |
| Tests without assertions | False confidence — coverage looks good but nothing is verified |
| Tests that call many production methods | Hard to diagnose failures, unclear what's being tested |
| Tests with magic numbers | Unreadable intent, unclear boundary conditions |
| Tests relying on ToString for comparison | Brittle to formatting changes, obscure failure messages |
| Tests with exception handling logic | Swallowed failures, tests that pass when they shouldn't |

## When to Use

- User asks for a comprehensive or formal test smell audit
- User asks "are my tests well-written?" and wants a thorough analysis
- User wants a test quality health check with academic rigor
- User asks for a review of test design or structure using standard smell categories
- User suspects tests are fragile, flaky, or giving false confidence and wants a deep investigation

## When Not to Use

- User wants a quick pragmatic test review (use `test-anti-patterns` — faster, covers the most common issues)
- User wants to evaluate assertion diversity specifically (use `assertion-quality`)
- User wants to find duplicated boilerplate across tests (use `exp-test-maintainability`)
- User wants to write new tests from scratch (help them directly)
- User wants to fix a specific failing test (diagnose and fix directly)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | One or more test files or a test project directory to analyze |
| Production code | No | The code under test, for context on whether patterns are justified |

## Workflow

### Step 1: Detect language and load extension

Identify the target codebase's language and test framework. Call the `test-analysis-extensions` skill and read the matching extension file (e.g., `extensions/dotnet.md`, `extensions/python.md`, `extensions/typescript.md`, `extensions/go.md`). The extension file lists the framework-specific test markers, sleep / wait APIs, skip / ignore attributes, mystery-guest indicators, and integration-test markers that the smell detectors below need.

### Step 2: Gather the test code

Read all test files the user provides. If the user points to a directory or project, scan for all test files using the markers in the loaded language extension file.

For a thorough audit, also consult the [extended smell catalog](references/test-smell-catalog.md) which covers 9 additional smell types beyond the core 10 below.

### Step 3: Scan for test smells

For each test method and class, check for the following smell categories. Examples reference .NET attributes but the patterns apply across all supported languages — use the loaded language extension file to map each pattern to the framework you are auditing.

#### Smell 1: Conditional Test Logic

Test methods containing `if`, `else`, `switch`, ternary (`? :`), `for`, `foreach`, `while`, or pattern-match arms that change assertion behavior. Control flow in tests means some paths may never execute, hiding gaps.

**Severity:** High
**Detection:** Any control-flow statement inside a test method body that affects which assertions run.
**Exceptions (per-language idioms, do NOT flag):**
- **Foreach-assert** used solely to assert every item in a known collection (the assertion *is* the loop body).
- **Go / Rust table-driven tests**: `for _, tt := range tests { t.Run(tt.name, func(t *testing.T) { ... }) }` (Go) or `#[rstest]` parametrized loops are idiomatic.
- **`it.each(...)` / `test.each(...)` / `@pytest.mark.parametrize` / `[Theory] + [InlineData]` / `@ParameterizedTest`** parametrization driven by data tables.
- **Pester `-ForEach` / `-TestCases`** and **RSpec `where` blocks**.
- **Catch2 `SECTION`s and `GENERATE(...)`**, **doctest `SUBCASE`**, **GoogleTest `INSTANTIATE_TEST_SUITE_P`**.

#### Smell 2: Mystery Guest

Tests that depend on external resources — files on disk, databases, network endpoints, environment variables — without making the dependency explicit or using test doubles.

**Severity:** High
**Detection:** Test methods that read files, open database connections, make HTTP requests (without a test handler), read environment variables, or use hard-coded file paths. Per language: `File.ReadAllText` / `Directory.GetFiles` / `HttpClient` / `Environment.GetEnvironmentVariable` (.NET); `open()` / `pathlib.Path.read_text()` / `requests.get()` / `os.environ[...]` (Python); `fs.readFileSync` / `fetch(...)` / `process.env.X` (JS/TS); `Files.readAllBytes` / `Files.newInputStream` / `HttpClient.send` / `System.getenv` (Java); `os.ReadFile` / `http.Get` / `os.Getenv` (Go); `File.read` / `Net::HTTP.get` / `ENV[...]` (Ruby); `std::fs::read_to_string` / `reqwest::get` / `std::env::var` (Rust); `String(contentsOfFile:)` / `URLSession.shared.data` / `ProcessInfo.processInfo.environment` (Swift); `File(...).readText()` / `URL(...).openConnection()` / `System.getenv` (Kotlin); `Get-Content` / `Invoke-WebRequest` / `$env:X` (Pester); `std::ifstream` / `curl_easy_perform` / `std::getenv` (C++).
**Exception:** In-memory fakes, test-specific handlers, or hermetic test data factories are fine.

#### Smell 3: Sleepy Test

Tests that call sleep or delay functions to wait for a condition. These introduce non-deterministic timing and slow down the suite.

**Severity:** High
**Detection:** Calls to sleep/delay functions inside test methods: `Thread.Sleep` / `Task.Delay` (.NET); `time.sleep` / `asyncio.sleep` (Python); `setTimeout` / `await new Promise(r => setTimeout(...))` / `jest.advanceTimersByTime` not paired with a wait (JS/TS); `Thread.sleep` / `TimeUnit.SECONDS.sleep` (Java); `time.Sleep` (Go); `sleep` / `Kernel#sleep` (Ruby); `std::thread::sleep` / `tokio::time::sleep` (Rust); `Thread.sleep` / `delay` (Kotlin coroutines); `sleep(_:)` / `Task.sleep` (Swift); `Start-Sleep` (Pester); `std::this_thread::sleep_for` (C++). See the matching language extension file for the full list.

#### Smell 4: Assertion-Free Test (Unknown Test)

Tests that execute code but never assert anything. Test frameworks report these as passing even if the code is completely broken, as long as no exception is thrown.

**Severity:** High
**Detection:** A test method with no assertion calls and no expected-exception annotation. Framework-specific: missing `Assert.*` (.NET); no `assert` / `pytest.raises` (Python); no `expect(...)` or `assert.*` (JS/TS); no `assert*` / `assertThat` (Java); no `t.Error*` / `t.Fatal*` / `assert.*` testify (Go); no `expect`/`.to`/`.eq` (RSpec) or `assert*`/`refute*` (Minitest); no `assert*!` / `assert_eq!` / `panic!` (Rust); no `XCTAssert*` / `#expect` (Swift); no `assert*` / `should*` / Kotest matchers (Kotlin); no `Should -*` (Pester); no `EXPECT_*` / `ASSERT_*` / `REQUIRE` / `CHECK` (C++).
**Calibration:**
- A method named `*_DoesNotThrow` / `*_no_exception` / `should not throw` is implicitly asserting no exception — still flag it but note it may be intentional.
- **Mock-call verifications count as assertions**: `mock.Verify(...)` (Moq), `Mock.AssertWasCalled` (NSubstitute), `mock.assert_called_with(...)` (Python), `expect(mock).toHaveBeenCalledWith(...)` (Jest), `verify(mock).method(...)` (Mockito), `Should -Invoke` (Pester) — do NOT flag tests using these as assertion-free.
- **Bare assertion forms count**: `assert x == y` (pytest), `if got != want { t.Errorf(...) }` (Go), `assert!(cond)` (Rust) are canonical.
- **Snapshot assertions count**: `.toMatchSnapshot()` (Jest), `syrupy` (pytest), `SnapshotTesting` (Swift), `approval-tests` are real assertions.
- **Missing await on async assertions is its own critical smell**: `expect(promise).resolves.toBe(x)` without `await`/`return` (Jest), un-awaited `Assert.ThrowsAsync` (xUnit), un-awaited coroutines in `pytest-asyncio`, Kotest tests without `runTest`, Swift Testing async cases without `await`. These tests have assertion calls but silently pass — flag with a dedicated note.

#### Smell 5: Eager Test

A test method that calls many different production methods, making it unclear what behavior is being tested. When it fails, diagnosis is difficult because the failure could stem from any of the calls.

**Severity:** Medium
**Detection:** A test method that calls 4+ distinct methods on the production object (excluding setup/construction). Count unique method names, not call count.
**Calibration:** Integration / end-to-end / workflow tests may legitimately call multiple methods. Check for integration markers in the loaded language extension file (e.g., `[Trait("Category", "Integration")]`, `@Tag("integration")`, `pytest.mark.integration`, `*_integration_test.go`, `Describe ... -Tag 'Integration'`) and downgrade.

#### Smell 6: Magic Number Test

Assertions that contain unexplained numeric literals. The intent of `Assert.AreEqual(42, result)` / `assert result == 42` / `expect(result).toBe(42)` is unclear without context — what does 42 represent?

**Severity:** Medium
**Detection:** Numeric literals (other than 0, 1, -1, and the literal used in the test name) appearing as `expected` parameters in assertion methods or comparison operands.
**Calibration:** Small integers in context (like count checks `Assert.AreEqual(3, list.Count)` / `assert len(items) == 3` / `expect(arr.length).toBe(3)` where 3 items were just added) are acceptable — only flag when the number's meaning is genuinely unclear.

#### Smell 7: Sensitive Equality

Tests that use string conversion for comparison or assertion. If the underlying string representation changes, the test breaks even though the actual behavior is correct.

**Severity:** Medium
**Detection:** `Assert.AreEqual(expected, obj.ToString())` (.NET); `assert str(obj) == "..."` or `assert repr(obj) == "..."` (Python); `expect(obj.toString()).toBe("...")` or `expect(`${obj}`).toBe(...)` (JS/TS); `assertEquals(expected, obj.toString())` (Java); `assert.Equal(t, "...", fmt.Sprint(obj))` or `obj.String()` chains (Go); `expect(obj.to_s).to eq("...")` (RSpec); `assert_eq!(format!("{}", obj), "...")` or `assert_eq!(format!("{:?}", obj), "...")` (Rust); `XCTAssertEqual(obj.description, "...")` or string-interpolation assertion (Swift); `assertEquals("...", obj.toString())` (Kotlin); `Should -Be "..."` against a `[string]$obj` (Pester); `EXPECT_EQ("...", std::to_string(obj))` (C++).

#### Smell 8: Exception Handling in Tests

Tests that contain `try`/`catch`/`except`/`rescue` blocks or `throw`/`raise`/`panic`/`return err` statements used to manage exception flow instead of asserting on it. This typically means the test is manually managing errors rather than using the framework's built-in exception assertion facilities.

**Severity:** Medium
**Detection:** `try`/`catch` (.NET, Java, JS/TS, Kotlin, Swift, C++); `try`/`except` (Python); `begin`/`rescue` (Ruby); `defer recover()` (Go); manual `if err != nil { t.Fatal(err) }` in Go is canonical and NOT a smell.
**Exception:** `catch`/`except`/`rescue` blocks that capture an exception for further assertion on its properties are a lesser concern — note but don't flag as high severity.

#### Smell 9: General Fixture (Over-broad Setup)

The test setup method, constructor, or fixture initializes fields that are not used by every test method. This means each test pays the cost of setting up objects it doesn't need.

**Severity:** Low
**Detection:** Fields/properties initialized in `[TestInitialize]` / `setUp` / `@BeforeEach` / `beforeEach` / `before(:each)` / `BeforeEach` (Pester) / `setUpWithError` (XCTest) / pytest `fixture(autouse=True)` / xUnit constructor / Kotest `beforeTest` that are referenced by fewer than half the test methods in the class/module/file.

#### Smell 10: Ignored / Disabled / Skipped Test

Tests marked as skipped or disabled. These add overhead and clutter, and the underlying issue they were disabled for may never be addressed.

**Severity:** Low
**Detection:** Skip / ignore / disable annotations or conditional compilation that disables a test. See the loaded language extension file for framework-specific skip attributes — e.g., `[Ignore]` (MSTest/NUnit), `Skip = "..."` (xUnit `Fact`), `@Ignore` (TUnit/JUnit 4), `@Disabled` (JUnit 5), `@pytest.mark.skip` / `pytest.skip(...)` / `pytestmark`, `it.skip` / `xit` / `describe.skip` / `test.skip` (Jest/Vitest/Mocha), `t.Skip(...)` (Go), `pending` / `skip` / `xit` (RSpec), `#[ignore]` (Rust), `XCTSkip` / `@Test(.disabled)` (Swift), `@Ignored` (Kotest), `-Skip` (Pester), `GTEST_SKIP()` / `DISABLED_TestName` (GoogleTest), `[.]` tag (Catch2), `TEST_CASE("...", "[.]")` skip.

### Step 4: Apply calibration rules

Before reporting, calibrate findings to avoid false positives:

- **Integration tests have different norms.** A test class clearly marked as integration (by name, annotation, category, or convention — see the loaded language extension file for markers) legitimately uses external resources, calls multiple methods, and may use delays for async coordination. Downgrade Mystery Guest, Eager Test, and Sleepy Test severity for integration tests — note them but don't flag as problems.
- **Simple loop-assert patterns are fine.** Iterating a collection to assert on every item is readable and correct. Only flag loops with complex branching logic.
- **Idiomatic table-driven and parametrized patterns are NOT Conditional Test Logic.** Go's `for _, tt := range tests { t.Run(...) }`, Rust's `#[rstest]`, pytest's `@parametrize`, Jest/Vitest `.each`, JUnit `@ParameterizedTest`, RSpec `where`, Pester `-ForEach`, Catch2 `SECTION`/`GENERATE`, GoogleTest `INSTANTIATE_TEST_SUITE_P` are canonical and must NOT be flagged.
- **Context matters for magic numbers.** A count assertion right after adding a known number of items is self-documenting. Only flag numbers whose meaning requires looking at production code to understand.
- **Bare `assert` (pytest) is canonical, not assertion-free framework use.** Don't flag.
- **Go's `if err != nil { t.Fatal(err) }` is canonical**, not Exception Handling in Tests. Don't flag.
- **Mock-call verifications and snapshot assertions are real assertions** — do not flag tests using them as Assertion-Free.
- **Missing-await on async assertions is its own critical sub-smell of Assertion-Free** — these tests silently pass even when the underlying assertion fails. Always flag when detected.
- **Inconclusive/pending markers are not assertion-free.** Tests explicitly marked as incomplete should be flagged as Ignored Test, not Assertion-Free.
- **Capture-and-assert exception patterns are borderline.** `try { ... } catch (X x) { Assert.Equal(...) }` style patterns are ugly but functional. Note as a smell and suggest the framework's built-in exception assertion (`Assert.Throws<T>`, `pytest.raises`, `expect(fn).toThrow`, `assertThrows`, `assert.PanicsWithError`, etc.) instead of calling it broken.
- **If the test suite is clean, say so.** A report finding few or no smells is perfectly valid.

### Step 5: Report findings

Present the analysis in this structure:

1. **Summary Dashboard** — Quick overview:
   ```
   | Severity | Smell Count | Affected Tests |
   |----------|-------------|----------------|
   | High     | 3           | 7              |
   | Medium   | 2           | 4              |
   | Low      | 1           | 2              |
   | Total    | 6           | 13             |
   ```

2. **Findings by Severity** — For each smell found:
   - Smell name and category
   - Severity level with rationale
   - Affected test methods (file and method name)
   - Code snippet showing the smell
   - Concrete fix: show what the code should look like after remediation
   - Risk if left unfixed

3. **Smell-Free Patterns** — If any test methods are well-written, briefly acknowledge this. Highlighting what's good helps the user understand the contrast.

4. **Prioritized Remediation Plan** — Rank fixes by:
   - Impact (high-severity smells affecting many tests first)
   - Effort (quick fixes before refactoring)
   - Risk (fixes that prevent false-passes before cosmetic improvements)

## Validation

- [ ] Every finding includes the specific test method name and file location
- [ ] Every finding includes a code snippet showing the smell in context
- [ ] Every finding includes a concrete fix example (not just "fix this")
- [ ] Integration tests are not penalized for patterns that are appropriate for their scope
- [ ] Simple foreach-assert loops are not flagged as conditional test logic
- [ ] Contextually obvious numbers are not flagged as magic numbers
- [ ] If the test suite is clean, the report says so upfront
- [ ] Severity levels are justified, not arbitrary

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Flagging integration tests for using real resources | Check for integration test markers (per the loaded language extension) and adjust severity accordingly |
| Flagging loop-over-collection-assert as conditional logic | Only flag loops with branching or complex logic, not assertion iterations |
| Flagging Go/Rust table-driven loops as Conditional Test Logic | `for _, tt := range tests { t.Run(...) }` (Go) and `#[rstest]` loops (Rust) are canonical and must NOT be flagged |
| Flagging parametrized tests as Duplicate Assert | `@pytest.mark.parametrize`, `it.each`, `[Theory]+[InlineData]`, `@ParameterizedTest`, RSpec `where`, Pester `-ForEach`, Catch2 `SECTION`/`GENERATE` are correct deduplication, not smells |
| Flagging pytest bare `assert` as missing framework | Bare `assert` is canonical pytest assertion — count it |
| Flagging Go's `if err != nil { t.Fatal(err) }` as Exception Handling in Tests | This is canonical Go error checking — do NOT flag |
| Flagging obvious count assertions after adding N items | Consider the immediate context — self-documenting numbers are fine |
| Missing framework-specific assertion syntax | Always read the matching language extension file first; each framework has distinct assertion APIs (xUnit `Assert.Equal`, MSTest `Assert.AreEqual`, NUnit `Is.EqualTo`, pytest bare `assert`, Jest `expect().toBe()`, etc.) |
| Treating mock-call verifications as assertion-free | `mock.Verify(...)`, `expect(mock).toHaveBeenCalledWith(...)`, `Should -Invoke`, `verify(mock).method(...)`, `mock.assert_called_with(...)` are real assertions |
| Missing the async-test silent-pass trap | Always flag `expect(promise).resolves.toBe(x)` without `await`/`return`, un-awaited `Assert.ThrowsAsync` (xUnit), un-awaited coroutines in pytest-asyncio, missing `runTest` in Kotest, un-awaited Swift Testing async assertions |
| Over-flagging try/catch that captures for assertion | Distinguish swallowed exceptions from capture-and-assert patterns |
| Treating skip annotations with reasons same as bare skips | Note that reasoned skips (`Skip = "Tracked by #123"`, `@pytest.mark.skip(reason="...")`, `t.Skip("not yet implemented")`) are less concerning than unexplained ones |
| Flagging `DoesNotThrow`-style tests as assertion-free | These implicitly assert no exception — note but acknowledge the intent |
