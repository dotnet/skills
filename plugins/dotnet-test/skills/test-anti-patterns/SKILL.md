---
name: test-anti-patterns
description: >
  Audits existing test code in any language for anti-patterns and quality
  issues — produces a severity-ranked report (Critical / Warning / Info)
  with concrete code-level fixes. Polyglot: .NET (MSTest/xUnit/NUnit/
  TUnit), Python (pytest/unittest), TS/JS (Jest/Vitest/Mocha/node:test),
  Java (JUnit/TestNG), Go, Ruby (RSpec/Minitest), Rust, Swift, Kotlin
  (JUnit/Kotest), PowerShell (Pester), C++ (GoogleTest/Catch2).
  INVOKE when asked to audit, review, rank, or find problems in existing
  tests — "audit my tests", "test smell audit", "rank by severity", tests
  that pass but verify nothing, no/missing assertions, swallowed
  exceptions, always-true / self-comparing / tautological assertions,
  broad exception types, flakiness (sleep/Date.now/time.sleep), ordering
  dependency, shared global state, duplicated tests, magic values,
  missing await on async assertions.
  DO NOT USE FOR: writing new tests (use code-testing-agent, or
  writing-mstest-tests for MSTest); running tests (use run-tests);
  framework migration.
license: MIT
---

# Test Anti-Pattern Detection

Quick, pragmatic analysis of test code in any supported language for anti-patterns and quality issues that undermine test reliability, maintainability, and diagnostic value.

> **Language-specific guidance**: Call the `test-analysis-extensions` skill to discover available extension files, then read the file matching the target codebase (e.g., `extensions/dotnet.md`, `extensions/python.md`, `extensions/typescript.md`, `extensions/go.md`). The extension file tells you which sleep / time / random / skip / setup-teardown / mystery-guest APIs to look for in that language.

## When to Use

- User asks to review test quality or find test smells
- User wants to know why tests are flaky or unreliable
- User asks "are my tests good?" or "what's wrong with my tests?"
- User requests a test audit or test code review
- User wants to improve existing test code

## When Not to Use

- User wants to write new tests from scratch (use `code-testing-agent` for any language, or `writing-mstest-tests` for MSTest specifically)
- User wants direct implementation fixes rather than a diagnostic review (use the relevant write/edit skill)
- User asks to fix swapped `Assert.AreEqual` argument order in MSTest (use `writing-mstest-tests`)
- User asks to convert MSTest `DynamicData` from `IEnumerable<object[]>` to `ValueTuple` (use `writing-mstest-tests`)
- User wants to run or execute tests (use `run-tests` for .NET)
- User wants to migrate between test frameworks or versions (use migration skills)
- User wants to measure code coverage (out of scope)
- User wants a deep formal test smell audit with academic taxonomy and extended catalog (use `test-smell-detection`)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | One or more test files or classes to analyze |
| Production code | No | The code under test, for context on what tests should verify |
| Specific concern | No | A focused area like "flakiness" or "naming" to narrow the review |

## Workflow

### Step 1: Detect language and load extension

Identify the target codebase's language and test framework. Call the `test-analysis-extensions` skill and read the matching extension file. The extension file documents framework-specific anti-pattern markers — what counts as a sleep/wait, a test marker, a skip, a setup/teardown, a shared-state hot spot, and an integration boundary — so this skill stays language-neutral.

### Step 2: Gather the test code

Read the test files the user wants reviewed. If the user points to a directory or project, scan for all test files using the discovery markers in the loaded language extension file (e.g., `[TestClass]`/`[Fact]`/`[Test]` for .NET, `test_*.py` / `def test_*` for pytest, `*.test.ts` / `it()` for Jest, `*Test.java` / `@Test` for JUnit, `*_test.go` / `func TestXxx` for Go, `*_spec.rb` for RSpec, `#[test]` for Rust, `*.Tests.ps1` / `Describe` for Pester, `TEST(...)` for GoogleTest, `TEST_CASE(...)` for Catch2/doctest).

If production code is available, read it too -- this is critical for detecting tests that are coupled to implementation details rather than behavior.

### Step 3: Scan for anti-patterns

Check each test file against the anti-pattern catalog below. Report findings grouped by severity. The examples are .NET-centric but the patterns generalize — use the loaded language extension file to map each pattern to the framework you are auditing.

#### Critical -- Tests that give false confidence

| Anti-Pattern | What to Look For |
|---|---|
| **No assertions** | Test methods that execute code but never assert anything. A passing test without assertions proves nothing. In .NET look for missing `Assert.*`; in pytest a function with no `assert` and no `pytest.raises`; in Jest no `expect(...)`; in JUnit no `assert*`/`assertThat`; in Go a test that never calls `t.Error*`, `t.Fatal*`, or testify; in RSpec a block with no `expect`; in Pester no `Should`. Mock-call verifications (`verify(mock)`, `expect(mock).toHaveBeenCalled`, `Should -Invoke`) are real assertions. |
| **Missing await on async assertions (JS/TS, .NET, Python, Kotlin, Swift)** | `expect(promise).resolves.toBe(x)` without `await`/`return`, `pytest-asyncio` test with un-awaited coroutine, `async Task` xUnit test calling `Assert.ThrowsAsync` without `await`, Kotest suspending test without `runTest`, Swift Testing async test without `await`. These tests silently pass even when the underlying assertion would have failed. |
| **Coverage touching** | Test class that methodically calls every public member on a type — often in alphabetical or declaration order — without asserting meaningful outcomes. Each test typically does `var result = sut.MethodName(...)` (or `result = sut.method_name(...)`, `sut.methodName()`, `sut.MethodName(t)`) with no assertion, or only a trivial null/None/nil check. The intent is to inflate code-coverage metrics rather than verify behavior. Distinct from a single assertion-free test: the pattern is *systematic* coverage of the surface area with no real verification. |
| **Self-referential assertion** | Asserts that the output of an operation equals its input when the operation is expected to be an identity or no-op, e.g. `Assert.AreEqual(input, Parse(input.ToString()))`, `assert input == parse(str(input))`, `expect(parse(input.toString())).toBe(input)`, `assert.Equal(t, input, parse(input))`. Also flags `Assert.AreEqual(dto.Name, dto.Name)` / `assert dto.name == dto.name` / `expect(dto.name).toBe(dto.name)` (asserting a field against itself). The test is tautological — it can only fail if the round-trip is broken, but never verifies that a *transformation* actually happened. |
| **Swallowed exceptions** | `try { ... } catch { }`, `catch (Exception)` without rethrowing or asserting (.NET); bare `except:` or `except Exception:` with `pass` (Python); `try { ... } catch (e) {}` (JS/TS/Java); `defer recover()` without re-panic and no assertion (Go); `rescue StandardError` with no assertion (Ruby); `Result::unwrap_or(...)` swallowing errors in a test (Rust); empty `catch` block (Kotlin/Swift). |
| **Assert in catch block only** | `try { Act(); } catch (Exception ex) { Assert.Fail(ex.Message); }` (and equivalents in other languages) -- use `Assert.ThrowsException` / `pytest.raises` / `expect(fn).toThrow` / `assertThrows` / `assert.Error(t, err)` / `#[should_panic]` / `Should -Throw` / `EXPECT_THROW` instead. The test passes when no exception is thrown even if the result is wrong. |
| **Always-true assertions** | `Assert.IsTrue(true)`, `Assert.AreEqual(x, x)`, `assert True`, `expect(true).toBe(true)`, `assert.True(t, true)`, `assert!(true)`, or conditions that can never fail. |
| **Commented-out assertions** | Assertions that were disabled but the test still runs, giving the illusion of coverage. |

#### High -- Tests likely to cause pain

| Anti-Pattern | What to Look For |
|---|---|
| **Flakiness indicators** | Wall-clock sleeps/waits used for synchronization: `Thread.Sleep` / `Task.Delay` (.NET), `time.sleep` (Python), `setTimeout` / `await new Promise(r => setTimeout(...))` (JS/TS), `Thread.sleep` (Java/Kotlin), `time.Sleep` (Go), `sleep` (Ruby/Bash), `std::thread::sleep` (Rust), `Start-Sleep` (Pester), `std::this_thread::sleep_for` (C++). Wall-clock reads without abstraction: `DateTime.Now`/`UtcNow`, `datetime.now()`/`datetime.utcnow()`, `Date.now()` / `new Date()`, `System.currentTimeMillis()`, `time.Now()`, `Time.now`, `Instant::now()`, `Date()`/`Date.now`, `Get-Date`, `std::chrono::system_clock::now`. Unseeded randomness: `new Random()`, `random.random()`/`random.randint()`, `Math.random()`, `new Random()` (Java/Kotlin), `rand.Int()` without seed, `rand` (Ruby), `rand::random()` (Rust). Environment-dependent paths (hard-coded `C:\...`, `/tmp/...`, network hosts). |
| **Test ordering dependency** | Static/global mutable state modified across tests; setup that doesn't fully reset state (`[TestInitialize]`, `setUp`, `beforeEach`, `before(:each)`, `BeforeEach`, `t.Cleanup`); tests that fail when run individually but pass in suite (or vice versa). Examples per language: `static` fields (.NET/Java), module-level globals (Python), top-level `let`/`const` in test file (JS/TS), `var` package globals (Go), class variables (Ruby), `static mut`/`lazy_static!`/`OnceCell` (Rust), `$script:` variables (PowerShell). |
| **Over-mocking** | More mock setup lines than actual test logic. Verifying exact call sequences on mocks rather than outcomes. Mocking types the test owns. Per language: Moq/NSubstitute/FakeItEasy (.NET), `unittest.mock` / `pytest-mock` (Python), Jest auto-mocks / Sinon (JS/TS), Mockito/PowerMock (Java), gomock/testify mock (Go), RSpec mocks/mocha (Ruby), `mockall` (Rust), MockK (Kotlin), `Mock` cmdlet (Pester), gmock (C++). For a deep mock audit in .NET, use `exp-mock-usage-analysis`. |
| **Implementation coupling** | Testing private methods via reflection (`MethodInfo.Invoke`, `getattr` in Python, `(thing as any)` in TS, `Field.setAccessible(true)` in Java, `Object#send` in Ruby, internal `pub(crate)` access in Rust). Asserting on internal state instead of observable behavior. Verifying exact method call counts on collaborators instead of business outcomes. |
| **Broad exception assertions** | `Assert.ThrowsException<Exception>(...)` (.NET) / `pytest.raises(Exception)` / `expect(fn).toThrow(Error)` without a message matcher / `assertThrows(Exception.class, ...)` (Java) / `assert.Error(t, err)` without checking the kind / `expect { ... }.to raise_error` without class (RSpec) / `#[should_panic]` without `expected = "..."` / `Should -Throw` without `-ExpectedMessage` / `EXPECT_ANY_THROW` instead of `EXPECT_THROW(stmt, SpecificType)`. |

#### Medium -- Maintainability and clarity issues

| Anti-Pattern | What to Look For |
|---|---|
| **Poor naming** | Test names like `Test1`, `TestMethod`, `test`, names that don't describe the scenario or expected outcome. Good naming differs by language convention — see the loaded language extension file (e.g., `Add_NegativeNumber_ThrowsArgumentException` for .NET, `test_add_negative_number_raises_value_error` for pytest, `addNegativeNumber_throwsArgumentException` for Java, `'adds negative number throws'` for Jest descriptions, `TestAdd_NegativeNumber_ReturnsError` for Go). |
| **Magic values** | Unexplained numbers or strings in arrange/assert: `Assert.AreEqual(42, result)` / `assert result == 42` / `expect(result).toBe(42)` -- what does 42 mean? |
| **Duplicate tests** | Three or more test methods with near-identical bodies that differ only in a single input value. Should be parametrized: `[DataRow]`/`[Theory]`/`[TestCase]` (.NET), `@pytest.mark.parametrize` (pytest), `test.each` / `it.each` (Jest/Vitest), `@ParameterizedTest` + `@ValueSource` (JUnit 5), `@DataProvider` (TestNG), Go table-driven tests, `where` / shared examples (RSpec), `#[rstest]` (Rust), `@ParameterizedTest` + `@MethodSource` (Kotlin), `-ForEach` / `-TestCases` (Pester), `INSTANTIATE_TEST_SUITE_P` (GoogleTest), `SECTION` / `GENERATE` (Catch2), `TEST_CASE_TEMPLATE` (doctest). For a detailed duplication analysis in .NET, use `exp-test-maintainability`. Note: Two tests covering distinct boundary conditions (e.g., zero vs. negative) are NOT duplicates -- separate tests for different edge cases provide clearer failure diagnostics and are a valid practice. |
| **Giant tests** | Test methods exceeding ~30 lines or testing multiple behaviors at once. Hard to diagnose when they fail. |
| **Assertion messages that repeat the assertion** | `Assert.AreEqual(expected, actual, "Expected and actual are not equal")` / `assert x == y, "x is not equal to y"` / `assertEquals(x, y, "values not equal")` add no information. Messages should describe the business meaning. |
| **Missing AAA / Given-When-Then separation** | Arrange/Act/Assert (or Given/When/Then for BDD frameworks like RSpec, Kotest behavior specs, Pester) phases are interleaved or indistinguishable. |

#### Low -- Style and hygiene

| Anti-Pattern | What to Look For |
|---|---|
| **Unused test infrastructure** | Setup/teardown hooks that do nothing — `[TestInitialize]`/`[SetUp]`/`[BeforeEach]`, `setUp`/`@BeforeEach`/`@BeforeAll`, `beforeEach`/`beforeAll`, `before(:each)`/`before(:all)`, `BeforeEach`/`BeforeAll` (Pester), `setUpWithError` (XCTest) — and test helper methods that are never called. |
| **Unmanaged resources** | Test creates disposable/closeable resources without cleanup: `HttpClient`/`Stream` without `using` (.NET), file/connection without `with` block or `try/finally` (Python), `FileInputStream` without `try-with-resources` (Java), `defer file.Close()` missing (Go), connection without `ensure` (Ruby), `Drop` not relied on / forgotten `close` (Rust), missing teardown for temp files / DBs in any language. |
| **Print debugging** | Leftover `Console.WriteLine` / `Debug.WriteLine` / `print()` / `console.log` / `System.out.println` / `fmt.Println` / `puts` / `dbg!` / `Write-Host` / `std::cout` statements used during test development. |
| **Inconsistent naming convention** | Mix of naming styles in the same test class/module/file (e.g., some use `Method_Scenario_Expected`, others use `ShouldDoSomething`). |

### Step 4: Calibrate severity honestly

Before reporting, re-check each finding against these severity rules:

- **Critical/High**: Only for issues that cause tests to give false confidence or be unreliable. A test that always passes regardless of correctness is Critical. Flaky shared state is High. Missing-await on async assertions is Critical (silent pass).
- **Medium**: Only for issues that actively harm maintainability -- 5+ nearly-identical tests, truly meaningless names like `Test1` / `test` / `it1`.
- **Low**: Cosmetic naming mismatches, minor style preferences, assertion messages that could be better. When in doubt, rate Low.
- **Not an issue** (per-language nuance):
  - Go and Rust **table-driven loops** with sub-tests (`t.Run` / `for case in cases { ... }`) are *idiomatic*, not "Conditional Test Logic". Do NOT flag.
  - pytest **bare `assert`** is the canonical assertion form, not a missing assertion library. Do NOT flag.
  - Go tests use `if got != want { t.Errorf(...) }` as canonical equality. Do NOT flag as ad-hoc.
  - Separate tests for distinct boundary conditions (zero vs. negative vs. null). Do NOT flag as duplicates.
  - Explicit per-test setup instead of `[TestInitialize]` / `beforeEach` (this *improves* isolation).
  - Tests that are short and clear but could theoretically be consolidated.

IMPORTANT: If the tests are well-written, say so clearly up front. Do not inflate severity to justify the review. A review that finds zero Critical/High issues and only minor Low suggestions is a valid and valuable outcome. Lead with what the tests do well.

### Step 5: Report findings

Present findings in this structure:

1. **Summary** -- Total issues found, broken down by severity (Critical / High / Medium / Low). If tests are well-written, lead with that assessment.
2. **Critical and High findings** -- List each with:
   - The anti-pattern name
   - The specific location (file, method name, line)
   - A brief explanation of why it's a problem
   - A concrete fix (show before/after code when helpful)
3. **Medium and Low findings** -- Summarize in a table unless the user wants full detail
4. **Positive observations** -- Call out things the tests do well (sealed class, specific exception types, data-driven tests, clear AAA structure, proper use of fakes, good naming). Don't only report negatives.

### Step 6: Prioritize recommendations

If there are many findings, recommend which to fix first:

1. **Critical** -- Fix immediately, these tests may be giving false confidence
2. **High** -- Fix soon, these cause flakiness or maintenance burden
3. **Medium/Low** -- Fix opportunistically during related edits

## Validation

- [ ] Every finding includes a specific location (not just a general warning)
- [ ] Every Critical/High finding includes a concrete fix
- [ ] Report covers all categories (assertions, isolation, naming, structure)
- [ ] Positive observations are included alongside problems
- [ ] Recommendations are prioritized by severity

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Reporting style issues as critical | Naming and formatting are Medium/Low, never Critical |
| Suggesting rewrites instead of targeted fixes | Show minimal diffs -- change the assertion, not the whole test |
| Flagging intentional design choices | If `Thread.Sleep` / `time.sleep` / `time.Sleep` is in an integration test testing actual timing, that's not an anti-pattern. Consider context. |
| Inventing false positives on clean code | If tests follow best practices, say so. A review finding "0 Critical, 0 High, 1 Low" is perfectly valid. Don't inflate findings to justify the review. |
| Flagging separate boundary tests as duplicates | Two tests for zero and negative inputs test different edge cases. Only flag as duplicates when 3+ tests have truly identical bodies differing by a single value. |
| Rating cosmetic issues as Medium | Naming mismatches (e.g., method name says `ArgumentException` but asserts `ArgumentOutOfRangeException`) are Low, not Medium -- the test still works correctly. |
| Ignoring the test framework | Use correct terminology per the loaded language extension: xUnit `[Fact]`/`[Theory]`, NUnit `[Test]`/`[TestCase]`, MSTest `[TestMethod]`/`[DataRow]`, pytest `def test_*` / `@pytest.mark.parametrize`, Jest `it.each` / `describe`, JUnit `@Test` / `@ParameterizedTest`, Go `func TestXxx(t *testing.T)` + table-driven, RSpec `describe`/`it`, Pester `Describe`/`It`, Rust `#[test]` / `#[rstest]`, Catch2 `TEST_CASE`/`SECTION`. |
| Treating idiomatic patterns as smells | Go/Rust **table-driven loops** are idiomatic. Pytest **bare `assert`** is canonical. Go's `if got != want { t.Errorf(...) }` is canonical. JS/TS `expect(mock).toHaveBeenCalledWith(...)` is a real assertion, not an over-mock. Do NOT flag these. |
| Missing async-test pitfalls | A Jest test that calls `expect(promise).resolves.toBe(x)` without returning/awaiting the promise silently passes; a TUnit/xUnit `async Task` test calling `Assert.ThrowsAsync` without `await` silently passes; pytest-asyncio tests with un-awaited coroutines silently pass. Always flag as Critical. |
| Missing the forest for the trees | If 80% of tests have no assertions, lead with that systemic issue rather than listing every instance |
