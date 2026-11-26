---
name: assertion-quality
description: "Analyze weak, shallow, trivial, self-referential, assertion-free, or presence-only assertions; Contains fragments or totals replacing predictable generated code, JSON, CLI, report, or protocol output. Supports MSTest, Jest, pytest, and Go. DO NOT USE for direct assertion fixes (writing-mstest-tests for MSTest, code-testing for other frameworks), new cases (code-testing), missing scenarios or identified production changes (test-gap-analysis), or general severity-ranked audits (test-anti-patterns)."
license: MIT
---

# Assertion Quality Analysis

Analyze whether assertions reject wrong observable outcomes, not merely whether they use varied APIs. Prioritize incomplete checks of predictable output, then assertion-free and vacuous tests. A single exact comparison can fully protect a contract; many fragment checks can leave most of it unprotected. Report diversity metrics only when they help explain a behavioral gap or the caller requests them.

> **Language-specific guidance**: Read the caller-provided or runtime-listed
> `test-analysis-extensions` catalog, then its matching language file
> (`dotnet.md`, `python.md`, `typescript.md`, `go.md`, etc.).
> `test-analysis-extensions` is reference-only; do not invoke it as a skill.
> If the bundle is absent, use the pinned framework and this skill's rules,
> report the missing reference, and do not search installation directories.

## When to Use

- User asks to evaluate assertion quality or depth
- User asks "are my tests actually testing anything meaningful?"
- User wants to know if test assertions are too shallow or trivial
- User asks for assertion coverage metrics or diversity analysis
- User suspects tests give false confidence despite passing
- The `test-engineer` agent (or any test-generation workflow) calls this skill as a pre-completion self-review step on freshly generated tests, before declaring the run finished

## When Not to Use

- User wants to write new tests (use `code-testing` for any language, or `writing-mstest-tests` for MSTest specifically)
- User wants to detect anti-patterns beyond assertions (use `test-anti-patterns`)
- User wants to fix or rewrite assertions (use `writing-mstest-tests` for MSTest or `code-testing` for other frameworks)
- User asks about code coverage percentages (out of scope — this analyzes assertion quality, not line coverage)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | One or more test files or a test project directory to analyze |
| Production code | No | Discover the relevant producer, fixtures, and assertion helpers in the requested scope to establish what output is predictable |

## Workflow

### Step 1: Detect language and load extension

Identify the language and test framework. Read the corresponding file in
`extensions/` relative to the supplied `test-analysis-extensions` catalog.
Use that catalog for other language filenames. Check only that known
directory when necessary; an unavailable reference changes the evidence limit,
not the workspace root or the requested review.

### Step 2: Gather the test code

Read all test files the user provides. If the user points to a directory or project, scan for all test files using the markers in the language extension file (e.g., `[TestMethod]` for MSTest, `def test_*` for pytest, `it()` / `test()` for Jest, `func TestXxx` for Go).

Inventory every in-scope method before triage. For supplied files, account for
each method as weak, meaningful, intentionally partial, or unassessed with a
reason. Do not silently drop diagnostic or summary tests because the request's
area label emphasizes another output. An exact check of a test-local literal
or collection is not production verification; trace the asserted value to the
producer before crediting it.

### Step 3: Classify every assertion

#### First check predictable-output completeness

For output-focused tests, read the full body, setup/input, producer, and assertion
helpers before counting assertion categories. Inspect nearby tests for existing
exact-output or parsing helpers, but never credit their assertions to the weak
test. Treat searches for `Contains`, substring membership, regexes, `Any`, or
totals as candidates, not findings.

Establish the test's claimed contract and independently expected output. Ask
whether a wrong output can preserve every checked fragment or aggregate while
breaking that contract. Check content, cardinality, duplicates, ordering when
promised, and association with the correct test/provider/stream. A count plus
membership checks is not enough if duplicates or substituted records still pass.

| Output under test | Defect fragment/aggregate checks can miss | Focused repair |
|---|---|---|
| Generated source or registry | Extra/duplicate registrations, missing surrounding syntax, wrong registration target | Compare the complete expected generated source, preferably a raw string or existing approved snapshot. Assert the full generated-file set when claimed; compilation/diagnostics complement but do not replace content checks. |
| JSON embedded in HTML or a report | A matching fragment inside unrelated text; correct totals with results assigned to the wrong test | Extract the intended payload with the repository helper, parse JSON, and compare the complete stable structure: result identities, outcomes, counts, and unexpected entries. Do not substring-search JSON or depend on property order. |
| CLI `--info` or provider listings | Correct lines under the wrong provider, extra lines, duplicates, reordered sections | Compare the complete expected line sequence, using the repository's sequence assertion. Include section/provider boundaries. |
| Markdown summaries and deterministic error messages | One correct row/token while the heading, other rows, punctuation, parameter context, or totals are wrong | Compare the full predictable text with an independent raw-string expectation or existing exact-text helper. Exception type alone may suffice when message formatting is not the claimed contract. |
| Terminal events and protocol records | Expected text occurs, but belongs to another test or stream; duplicate/missing/reordered records | Compare complete record tuples including test ID, stream, kind, and payload. Require sequence equality only for promised ordering; otherwise compare multisets that preserve multiplicity. |
| Acceptance run output | Totals match while different tests ran; `" total: 3"` also matches `" total: 30"` | Pin selected test identities, outcomes, and complete stable summary lines. Prefer an existing `AssertOutputMatchesLines` helper after checking its semantics, rather than adding more fragments or a totals-only regex. |

Reuse the framework version and repository conventions: for example,
`Assert.AreEqual(expected, actual)` for exact MSTest text and an existing
`Assert.AreSequenceEqual`/`CollectionAssert.AreEqual` for ordered lines.
Do not prescribe an unavailable API or introduce a snapshot dependency solely
for this review.

Do not force byte equality on nondeterministic output. Normalize only documented
variable fields (timestamps, absolute paths, durations, newline conventions),
validate their required shape separately, and retain all stable fields and
record associations. Never sort away contractual ordering, deduplicate actual
records, or strip stable lines just to make an exact comparison pass.
Legitimate substring contracts (e.g. a log must include a warning amid unrelated
events), partial object contracts, and focused count-only tests are not defects
merely because broader equality is possible. Exact output equality also does
not need extra assertion types for diversity.

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

### Step 4: Count only what supports a finding

Count test methods exactly from the inventory, then count tests that are
assertion-free, trivial-only, self-referential, or incomplete for predictable
output. A lone null check is weak only when the contract promises a value; it
can fully verify an expected absence. Self-referential means the expected value
derives from the actual value or the input cannot distinguish the claimed
transformation from a no-op; a round-trip check is valid when preservation is
the contract and the input exercises a real transformation.

Category counts describe syntax, not strength. Report them only when the caller
asks for metrics, and define every number shown. Never present a category
"spread" score or a target percentage, and never recommend negative, exception,
or other assertion types to meet a quota. Every count must agree with the
per-test findings (for example, do not claim "no state checks" after crediting
an equality on a post-action count as a state check).

### Step 5: Apply calibration rules

Before reporting, calibrate findings:

- **Evaluate the matcher predicate before describing its weakness.** For every
  weak assertion, name one realistic defective value or behavior that would
  still satisfy that exact predicate. For every assertion credited as
  meaningful, name the behavior it pins. If you cannot give such a
  counterexample from the test and available production contract, do not
  speculate that the assertion is weak.
- **State exception semantics precisely.** In every mainstream framework an
  uncaught exception, rejected awaited promise, panic, or failed fixture fails
  the test. An assertion-free test therefore still proves "does not throw";
  its weakness is that a wrong result passes. Only swallowed exceptions
  (empty `catch`, `except: pass`, catch-and-return) or un-awaited async work
  let a throwing operation pass. Never claim a thrown error would pass a test
  unless the code shows such a path.
- **Judge null checks against the contract.** A lone `IsNotNull` is weak when the test promises a particular result but never verifies it. A lone `IsNull` can completely verify a promised absence, and `toBeDefined()` can verify a contract that excludes `undefined`. A null guard before further checks does not make the overall test trivial.
- **Use exact Jest semantics.** `toBeDefined()` rejects only `undefined`;
  `null` does satisfy it, but mention that only when `null` is a realistic
  contract-breaking result. `toMatchObject(expected)` verifies the expected
  subset structurally; it neither proves object identity nor full-object
  equality. Never claim that it does.
- **Boolean assertions checking meaningful conditions are not trivial.** `Assert.IsTrue(result.IsValid)` / `assert result.is_valid` / `expect(result.isValid).toBe(true)` check a specific property — these are Boolean assertions, not trivial ones. Always-true assertions (`Assert.IsTrue(true)`, `assert True`, `expect(true).toBe(true)`) are trivial.
- **Exact construction and mapping checks are meaningful.** A test that constructs an
  object and pins each requested property to an independent expected literal can catch
  swapped, dropped, or incorrectly assigned values. Do not downgrade it merely because
  the implementation is a constructor, record, property mapping, or in-memory store.
- **Consider the test's intent.** A test for a void method that verifies state change on a dependency is legitimate even if it only uses one Boolean assertion.
- **Exception tests are inherently low-assertion-count.** `Assert.ThrowsException<T>(() => ...)` / `with pytest.raises(E): ...` / `expect(fn).toThrow(E)` / `#[should_panic]` may be the only assertion — that's fine for exception-focused tests. Don't penalize them for low assertion count.
- **Mock-call verifications and bare assertion forms count.** Treat `verify(mock).method(...)` (Mockito), `expect(mock).toHaveBeenCalledWith(...)` (Jest), `Should -Invoke` (Pester), `bare assert` (pytest), `if got != want { t.Errorf(...) }` (Go) all as real assertions of the appropriate category. Do not treat them as missing-framework-API smells.
- **Snapshot assertions** (`.toMatchSnapshot()`, `syrupy`, `SnapshotTesting`) count as Structural/Deep assertions. Flag stale or never-updated snapshots separately.
- **Property-based tests** (`@given` Hypothesis, `proptest!`, `forAll` Kotest) generate assertions implicitly through generated cases — count the inner assertion logic, not the outer scaffold.
- **Don't conflate diversity with volume.** A test with 20 equality assertions has high volume but low diversity. A test with one equality, one null check, and one exception assertion has low volume but good diversity.
- **Distinguish identity from transformation.** Comparing an actual value with itself is tautological. Comparing an input with a round-trip result can be meaningful when preservation is the contract and the input exercises parsing, encoding, serialization, or another transformation. Flag it only when the production contract or test data shows the check cannot distinguish the intended behavior from a defect; do not require unrelated assertions just to increase variety.
- **Match recommendations to the named behavior.** Formatting tests should pin the
  exact formatted representation, validation tests need rejected inputs, and round-trip
  tests need inputs that exercise escaping, null/empty handling, or another transformation
  boundary. For each assertion-free create/update/delete operation, recommend its specific
  returned value or observable post-condition rather than one generic "check state" remedy.
- **If assertions are well-diversified, say so.** A report concluding the suite has good diversity is perfectly valid.

### Step 6: Report findings

**Scale the report to the request.** Lead with the behavioral verdict and
concrete findings. Account for every in-scope test, distinguishing deliberate
smoke checks from correctness tests. State only counterexamples supported by
the assertion predicate and available production behavior.

For predictable-output findings use:

| Test / location | Claimed output and actual check | Wrong output that still passes (static unless executed) | Repair |
|---|---|---|---|

Name each affected method and line range, the accepted defective output, and the
exact comparison, sequence, or parsed fields that reject it. Rank by the size
and consequence of the unchecked contract, not the number of `Contains` calls.
Keep strong controls and legitimate partial contracts credited. Distinguish
confirmed inspected tests from search-only candidates or unavailable producer
context; do not report an exhaustive count from a grep scan. A static review
does not require editing tests or running mutations.

Otherwise use one compact table:

| Test | Verdict (weak / meaningful / intentional smoke / unassessed) | What it pins, or a wrong result it accepts | Concrete repair |
|---|---|---|---|

Then list repairs in priority order. Each repair names the test and the exact
assertion to add or replace; do not recommend assertion types in the abstract.
When the caller requests metrics, add a short table of the Step 4 counts with
their definitions; omit category breakdowns otherwise.

If the suite is sound, say so first, count its tests exactly, and do not invent
findings to fill the report. A mention of an optional improvement must be
labelled optional and must not be framed as a defect.

When the user forbids running commands, use only file read/search tools: no
shell, build, test, or script execution, even to list files. Report results as
static analysis.

## Validation

- [ ] Every in-scope test method is accounted for and the test count is exact
- [ ] Every count shown is defined and agrees with the per-test findings
- [ ] No claim that a thrown exception passes a test unless the code swallows it
- [ ] Trivial-assertion tests are correctly identified (not over-flagged)
- [ ] Exception tests are not penalized for low assertion count
- [ ] Boolean assertions on meaningful properties are not classified as trivial
- [ ] Every weak-assertion claim includes a realistic counterexample that the
      exact matcher would accept
- [ ] Predictable-output checks cover stable content, identities, multiplicity,
      associations, and contractual ordering, not just fragments or totals
- [ ] Repairs reuse available exact-text, sequence, JSON, or acceptance helpers
      without masking stable differences through over-normalization
- [ ] Complete equality checks and legitimate partial contracts are credited
      without demanding assertion diversity
- [ ] Jest matcher semantics are precise (`toBeDefined` versus `undefined`;
      `toMatchObject` subset matching versus identity/full equality)
- [ ] Recommendations are concrete (name specific test methods and suggest specific assertion types)
- [ ] If the suite has good diversity, the report acknowledges this

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Penalizing exception tests for low assertion count | Exception assertions are complete on their own — skip count warnings for these |
| Calling every null/None/nil-only test trivial | Check whether presence or absence is itself the promised outcome |
| Counting any Boolean assertion as trivial | Only always-true assertions (`Assert.IsTrue(true)`, `assert True`, `expect(true).toBe(true)`) are trivial |
| Ignoring framework differences | Each framework has distinct assertion APIs — always read the matching language extension first. MSTest's `Assert.AreEqual`, xUnit's `Assert.Equal`, NUnit's `Is.EqualTo`, pytest's bare `assert ==`, Jest's `expect().toBe()`, Go's `if … { t.Error… }` all map to the **Equality** category |
| Treating bare assertion forms as missing-framework | Bare `assert` (pytest), `if got != want { t.Error... }` (Go), and `assert!()` (Rust) are canonical — count them in the right category |
| Treating mock-call verifications as assertion-free | `verify(mock).method(...)`, `expect(mock).toHaveBeenCalledWith(...)`, `Should -Invoke` are State/Side-effect assertions |
| Recommending diversity for diversity's sake | Only suggest adding assertion types that would catch real bugs in the code under test |
| Missing implicit assertions | Exception assertions are both Exception and Negative; snapshot/property-based tests are real assertions with implicit structure |
| Async tests with unawaited assertions | TUnit, Jest with `.resolves`/`.rejects`, pytest-asyncio, Swift Testing, and Kotest all silently pass tests where assertions are not `await`ed — treat as assertion-free even when assertion calls are present |
