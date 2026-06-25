---
name: assertion-quality
description: "Analyzes the variety and depth of assertions across test suites in any language. Use when the user asks to evaluate assertion quality, find shallow tests, identify assertion-free tests (no assertions or only trivial ones like Assert.IsNotNull / toBeTruthy()), flag self-referential or tautological assertions, measure assertion diversity, or audit whether tests verify different facets of behavior. Polyglot: .NET, Python, TS/JS, Java, Go, Ruby, Rust, Swift, Kotlin, PowerShell, C++. DO NOT USE FOR: writing new tests (use code-testing-agent / writing-mstest-tests), mutation reasoning about whether tests would catch a bug (use test-gap-analysis), or a general severity-ranked anti-pattern audit (use test-anti-patterns); fixing assertions."
license: MIT
---

# Assertion Diversity Analysis

Analyze test code in any supported language to measure how varied and meaningful the assertions are. Produce a metrics report that reveals whether tests verify different facets of correctness — not just "output equals X" but also structure, exceptions, state transitions, side effects, and invariants.

> **Language-specific guidance**: Call the `test-analysis-extensions` skill to discover available extension files, then read the file matching the target codebase's language and framework (e.g., `dotnet.md` for .NET, `python.md` for pytest, `typescript.md` for Jest, `go.md` for the standard `testing` package). You MUST read the relevant extension file before classifying assertions, because assertion APIs differ significantly across frameworks.

## Why Assertion Diversity Matters

Low assertion diversity signals shallow testing. Tests may pass while bugs hide in unasserted logic. Common symptoms:

| Problem | Symptom | Consequence |
|---------|---------|-------------|
| Trivial assertions | Test contains only `Assert.IsNotNull(result)` / `assert result is not None` / `expect(x).toBeDefined()` | Test passes but doesn't verify correctness |
| Single-value obsession | Always check one field or return value | Bugs in unasserted logic slip through |
| No negative assertions | Never check what shouldn't happen | Regressions sneak in through false positives |
| No state checks | Don't verify object state changes | Missed side-effects or lifecycle issues |
| No structural checks | Only assert top-level value | Bugs in nested objects go unnoticed |
| Assertion-free tests | Tests that call but don't verify | Code coverage lies; false security |

## When to Use

- User asks to evaluate assertion quality or depth
- User asks "are my tests actually testing anything meaningful?"
- User wants to know if test assertions are too shallow or trivial
- User asks for assertion coverage metrics or diversity analysis
- User suspects tests give false confidence despite passing
- The `code-testing-generator` agent (or any test-generation workflow) calls this skill as a pre-completion self-review step on freshly generated tests, before declaring the run finished

## When Not to Use

- User wants to write new tests (use `code-testing-agent` for any language, or `writing-mstest-tests` for MSTest specifically)
- User wants to detect anti-patterns beyond assertions (use `test-anti-patterns`)
- User wants to fix or rewrite assertions (help them directly)
- User asks about code coverage percentages (out of scope — this analyzes assertion quality, not line coverage)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | One or more test files or a test project directory to analyze |
| Production code | No | The code under test, to evaluate whether assertions cover the important behaviors |

## Workflow

### Step 1: Detect language and load extension

Identify the target codebase's language and test framework. Call the `test-analysis-extensions` skill and read the matching extension file (e.g., `extensions/dotnet.md` for .NET, `extensions/python.md` for pytest, `extensions/typescript.md` for Jest/Vitest, `extensions/go.md` for Go). The extension file lists the framework-specific assertion APIs you will classify in Step 3.

### Step 2: Gather the test code

Read all test files the user provides. If the user points to a directory or project, scan for all test files using the markers in the language extension file (e.g., `[TestMethod]` for MSTest, `def test_*` for pytest, `it()` / `test()` for Jest, `func TestXxx` for Go).

### Step 3: Classify every assertion

For each test method, identify all assertions and classify them into these language-neutral categories:

| Category | What it verifies | Examples across languages |
|----------|------------------|----------------------------|
| **Equality** | Return value matches expected | `Assert.AreEqual` (MSTest), `Assert.Equal` (xUnit), `assert x == y` (pytest), `expect(x).toBe(y)` (Jest), `assertEquals` (JUnit), `if got != want { t.Error... }` / `assert.Equal(t, want, got)` (Go), `x shouldBe y` (Kotest), `Should -Be` (Pester), `EXPECT_EQ` (GoogleTest) |
| **Boolean** | Condition holds | `Assert.IsTrue`, `assert flag` (Python), `expect(x).toBeTruthy()` (Jest), `assertTrue` (JUnit), `assert.True(t, ok)` (testify), `x.shouldBeTrue()` (Kotest), `Should -BeTrue` (Pester), `EXPECT_TRUE` |
| **Null / None / Nil** | Presence/absence of value | `Assert.IsNull` (.NET), `assert x is None` (pytest), `expect(x).toBeNull()` (Jest), `assertNull` (JUnit), `assert.Nil(t, v)` (testify), `XCTAssertNil` (XCTest), `Should -BeNullOrEmpty` (Pester) |
| **Exception / Error** | Error handling behavior | `Assert.Throws<T>()`, `pytest.raises(E)`, `expect(fn).toThrow(E)`, `assertThrows<E>`, `assert.Error(t, err)` / `assert.ErrorIs`, `#[should_panic]` (Rust), `XCTAssertThrowsError`, `Should -Throw`, `EXPECT_THROW` |
| **Type checks** | Runtime type correctness | `Assert.IsInstanceOfType`, `assert isinstance(x, T)`, `expect(x).toBeInstanceOf(T)`, `assertInstanceOf`, `assert.IsType(t, T{}, v)`, `assert!(matches!(value, Pattern))` (Rust), `Should -BeOfType` |
| **String** | Text content and format | `StringAssert.Contains`, `assert sub in s`, `expect(s).toMatch(/x/)`, `assertTrue(s.contains(...))`, `assert.Contains(t, s, sub)`, `s shouldContain sub`, `Should -Match`, `EXPECT_THAT(s, HasSubstr(...))` |
| **Collection** | Collection contents and structure | `CollectionAssert.Contains`, `assert item in collection`, `expect(arr).toContain(x)`, `assertIterableEquals`, `assert.Contains(t, slice, item)`, `col shouldContainExactly listOf(...)`, `Should -Contain`, `EXPECT_THAT(c, ElementsAre(...))` |
| **Comparison** | Ordering and magnitude | `Assert.IsTrue(x > y)`, `Is.GreaterThan`, `assert x > y`, `expect(x).toBeGreaterThan(y)`, `assertTrue(x > y)`, `assert.Greater(t, x, y)` (testify) |
| **Approximate** | Floating-point or tolerance-based | `Assert.AreEqual(expected, actual, delta)`, `pytest.approx(y)`, `expect(x).toBeCloseTo(y)`, `assertEquals(x, y, delta)`, `assert.InDelta(t, x, y, delta)`, `EXPECT_NEAR`, `EXPECT_DOUBLE_EQ` |
| **Negative** | What should NOT happen | `Assert.AreNotEqual`, `assert x != y`, `expect(x).not.toBe(y)`, `assertNotEquals`, `assert.NotEqual(t, x, y)`, `refute` (Minitest / Ruby), `Should -Not -Be` |
| **State / Side-effect** | State transitions and side effects | Assertions on object properties after mutation; mock-call verifications: `mock.Verify(...)` (Moq), `mock_method.assert_called_with(...)` (Python `unittest.mock`), `expect(mock).toHaveBeenCalledWith(...)` (Jest), `verify(mock).method(...)` (Mockito), `Should -Invoke` (Pester), `expect { code }.to change(obj, :attr)` (RSpec) |
| **Structural / Deep** | Deep object correctness | `Assert.AreEqual` with rich-equality types, `assertThat(obj).usingRecursiveComparison()` (AssertJ), `.toEqual({...})` (Jest deep equality), `cmp.Diff` (Go go-cmp), snapshot tests (`.toMatchSnapshot()`, `syrupy`, `SnapshotTesting`), `assertThat(col).extracting(...)` (AssertJ chains) |

A single assertion can belong to multiple categories (e.g., `Assert.AreNotEqual` is both Equality and Negative; `expect(mock).toHaveBeenCalledWith(...)` is both State/Side-effect and a specific-call assertion).

Read the loaded language extension file for the exact framework-specific list of assertion APIs.

### Step 4: Compute metrics

Calculate these metrics for the test suite:

#### Per-test metrics
- **Assertion count**: Number of assertions in each test method
- **Assertion categories**: Which categories each test uses

#### Suite-wide metrics
- **Average assertions per test**: Total assertions / total test methods
- **Assertion type spread**: Number of distinct assertion categories used across the suite (out of 12)
- **Tests with zero assertions**: Count and percentage of test methods with no assertions at all
- **Tests with only trivial assertions**: Count and percentage of tests where every assertion is only a null check or `Assert.IsTrue(true)` — trivial means no meaningful value verification
- **Tests with self-referential assertions**: Count and percentage of tests whose assertions compare an input to a round-tripped or identity-transformed version of itself (e.g., `Assert.AreEqual(input, Parse(input.ToString()))`) or assert a field against itself (`Assert.AreEqual(dto.Name, dto.Name)`). These are tautological — they verify the plumbing, not the behavior.
- **Tests with negative assertions**: Count and percentage (target: at least 10% of tests should verify what should NOT happen)
- **Tests with exception assertions**: Count and percentage
- **Tests with state/side-effect assertions**: Count and percentage
- **Tests with structural/deep assertions**: Count and percentage
- **Single-category tests**: Count and percentage of tests that use only one assertion category

### Step 5: Apply calibration rules

Before reporting, calibrate findings:

- **Trivial means truly trivial.** A null/None/nil check alone is trivial (`Assert.IsNotNull(result)`, `assert result is not None`, `expect(x).toBeDefined()`). But a null check followed by a meaningful value assertion is not trivial — the null check is a guard before the real assertion. Only flag a test as "trivial" if it has no meaningful value assertions.
- **Boolean assertions checking meaningful conditions are not trivial.** `Assert.IsTrue(result.IsValid)` / `assert result.is_valid` / `expect(result.isValid).toBe(true)` check a specific property — these are Boolean assertions, not trivial ones. Always-true assertions (`Assert.IsTrue(true)`, `assert True`, `expect(true).toBe(true)`) are trivial.
- **Consider the test's intent.** A test for a void method that verifies state change on a dependency is legitimate even if it only uses one Boolean assertion.
- **Exception tests are inherently low-assertion-count.** `Assert.ThrowsException<T>(() => ...)` / `with pytest.raises(E): ...` / `expect(fn).toThrow(E)` / `#[should_panic]` may be the only assertion — that's fine for exception-focused tests. Don't penalize them for low assertion count.
- **Mock-call verifications and bare assertion forms count.** Treat `verify(mock).method(...)` (Mockito), `expect(mock).toHaveBeenCalledWith(...)` (Jest), `Should -Invoke` (Pester), `bare assert` (pytest), `if got != want { t.Errorf(...) }` (Go) all as real assertions of the appropriate category. Do not treat them as missing-framework-API smells.
- **Snapshot assertions** (`.toMatchSnapshot()`, `syrupy`, `SnapshotTesting`) count as Structural/Deep assertions. Flag stale or never-updated snapshots separately.
- **Property-based tests** (`@given` Hypothesis, `proptest!`, `forAll` Kotest) generate assertions implicitly through generated cases — count the inner assertion logic, not the outer scaffold.
- **Don't conflate diversity with volume.** A test with 20 equality assertions has high volume but low diversity. A test with one equality, one null check, and one exception assertion has low volume but good diversity.
- **Self-referential assertions are not meaningful equality checks.** Asserting that an output equals an input round-trip looks like a real equality assertion but is tautological when the operation under test is expected to be identity. Flag these separately from normal equality assertions. If the test's *purpose* is to verify a round-trip (serialize/deserialize, encode/decode), the assertion is valid — but it should be accompanied by assertions on non-trivial inputs that exercise the transformation.
- **If assertions are well-diversified, say so.** A report concluding the suite has good diversity is perfectly valid.

### Step 6: Report findings

Present the analysis in this structure:

1. **Summary Dashboard** — A quick-reference table of key metrics:
   ```
   | Metric                        | Value  | Assessment |
   |-------------------------------|--------|------------|
   | Total tests                   | 25     | —          |
   | Average assertions per test   | 2.4    | Moderate   |
   | Assertion type spread         | 5/12   | Low        |
   | Tests with zero assertions    | 3 (12%)| Concerning |
   | Tests with only trivial asserts | 4 (16%)| Acceptable |
   | Tests with negative assertions | 2 (8%) | Below target |
   | Single-category tests         | 15 (60%)| High       |
   ```

2. **Category Breakdown** — For each assertion category, show:
   - How many tests use it
   - Representative examples from the code
   - Whether it's overused or underused relative to the code under test

3. **Gap Analysis** — Based on the production code (if available), identify:
   - Behaviors that are tested but only with equality checks
   - Error paths with no exception assertions
   - State-changing methods with no state verification
   - Collections returned but never checked for contents

4. **Recommendations** — Prioritized list of improvements:
   - Which tests would benefit most from additional assertion types
   - Which assertion categories are missing and why they matter
   - Concrete examples of assertions that could be added

5. **Assertion-free tests** — If any exist, list each one with its method name and what it appears to be testing, so the user can decide whether to add assertions or mark them as intentional smoke tests.

## Validation

- [ ] Every assertion in the test suite was classified into at least one category
- [ ] Metrics are computed correctly (counts add up)
- [ ] Trivial-assertion tests are correctly identified (not over-flagged)
- [ ] Exception tests are not penalized for low assertion count
- [ ] Boolean assertions on meaningful properties are not classified as trivial
- [ ] Recommendations are concrete (name specific test methods and suggest specific assertion types)
- [ ] If the suite has good diversity, the report acknowledges this

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Penalizing exception tests for low assertion count | Exception assertions are complete on their own — skip count warnings for these |
| Flagging null/None/nil checks before value checks as trivial | Only flag tests where the null/None/nil check is the ONLY assertion |
| Counting any Boolean assertion as trivial | Only always-true assertions (`Assert.IsTrue(true)`, `assert True`, `expect(true).toBe(true)`) are trivial |
| Ignoring framework differences | Each framework has distinct assertion APIs — always read the matching language extension first. MSTest's `Assert.AreEqual`, xUnit's `Assert.Equal`, NUnit's `Is.EqualTo`, pytest's bare `assert ==`, Jest's `expect().toBe()`, Go's `if … { t.Error… }` all map to the **Equality** category |
| Treating bare assertion forms as missing-framework | Bare `assert` (pytest), `if got != want { t.Error... }` (Go), and `assert!()` (Rust) are canonical — count them in the right category |
| Treating mock-call verifications as assertion-free | `verify(mock).method(...)`, `expect(mock).toHaveBeenCalledWith(...)`, `Should -Invoke` are State/Side-effect assertions |
| Recommending diversity for diversity's sake | Only suggest adding assertion types that would catch real bugs in the code under test |
| Missing implicit assertions | Exception assertions are both Exception and Negative; snapshot/property-based tests are real assertions with implicit structure |
| Async tests with unawaited assertions | TUnit, Jest with `.resolves`/`.rejects`, pytest-asyncio, Swift Testing, and Kotest all silently pass tests where assertions are not `await`ed — treat as assertion-free even when assertion calls are present |
