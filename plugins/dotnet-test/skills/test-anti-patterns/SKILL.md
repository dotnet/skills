---
name: test-anti-patterns
description: "Quick pragmatic review of .NET test code for anti-patterns that undermine reliability and diagnostic value. Use when asked to review tests, find test problems, check test quality, or audit tests for common mistakes. Catches assertion gaps, flakiness indicators, over-mocking, naming issues, and structural problems with actionable fixes. Use for periodic test code reviews and PR feedback. For a deep formal audit based on academic test smell taxonomy, use exp-test-smell-detection instead. Works with MSTest, xUnit, NUnit, and TUnit."
---

# Test Anti-Pattern Detection

Quick, pragmatic analysis of .NET test code for anti-patterns and quality issues that undermine test reliability, maintainability, and diagnostic value.

## When to Use

- User asks to review test quality or find test smells
- User wants to know why tests are flaky or unreliable
- User asks "are my tests good?" or "what's wrong with my tests?"
- User requests a test audit or test code review
- User wants to improve existing test code

## When Not to Use

- User wants to write new tests from scratch (use `writing-mstest-tests`)
- User wants to run or execute tests (use `run-tests`)
- User wants to migrate between test frameworks or versions (use migration skills)
- User wants to measure code coverage (out of scope)
- User wants a deep formal test smell audit with academic taxonomy and extended catalog (use `exp-test-smell-detection`)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | One or more test files or classes to analyze |
| Production code | No | The code under test, for context on what tests should verify |
| Specific concern | No | A focused area like "flakiness" or "naming" to narrow the review |

## Workflow

### Step 1: Gather the test code

Read the test files the user wants reviewed. If the user points to a directory or project, scan for all test files using the framework-specific markers in the `dotnet-test-frameworks` skill (e.g., `[TestClass]`, `[Fact]`, `[Test]`).

If production code is available, read it too -- this is critical for detecting tests that are coupled to implementation details rather than behavior.

### Step 2: Scan for anti-patterns

Check each test file against the anti-pattern catalog below. Report findings grouped by severity.

#### Critical -- Tests that give false confidence

| Anti-Pattern | What to Look For |
|---|---|
| **No assertions** | Test methods that execute code but never assert anything. A passing test without assertions proves nothing. |
| **Swallowed exceptions** | `try { ... } catch { }` or `catch (Exception)` without rethrowing or asserting. Failures are silently hidden. |
| **Assert in catch block only** | `try { Act(); } catch (Exception ex) { Assert.Fail(ex.Message); }` -- use `Assert.ThrowsException` or equivalent instead. The test passes when no exception is thrown even if the result is wrong. |
| **Always-true assertions** | `Assert.IsTrue(true)`, `Assert.AreEqual(x, x)`, or conditions that can never fail. |
| **Commented-out assertions** | Assertions that were disabled but the test still runs, giving the illusion of coverage. |

#### High -- Tests likely to cause pain

| Anti-Pattern | What to Look For |
|---|---|
| **Flakiness indicators** | `Thread.Sleep(...)`, `Task.Delay(...)` for synchronization, `DateTime.Now`/`DateTime.UtcNow` without abstraction, `Random` without a seed, environment-dependent paths. |
| **Test ordering dependency** | Static mutable fields modified across tests, `[TestInitialize]` that doesn't fully reset state, tests that fail when run individually but pass in suite (or vice versa). |
| **Over-mocking** | More mock setup lines than actual test logic. Verifying exact call sequences on mocks rather than outcomes. Mocking types the test owns. For a deep mock audit, use `exp-mock-usage-analysis`. |
| **Implementation coupling** | Testing private methods via reflection, asserting on internal state, verifying exact method call counts on collaborators instead of observable behavior. |
| **Broad exception assertions** | `Assert.ThrowsException<Exception>(...)` instead of the specific exception type. Also: `[ExpectedException(typeof(Exception))]`. |

#### Medium -- Maintainability and clarity issues

| Anti-Pattern | What to Look For |
|---|---|
| **Poor naming** | Test names like `Test1`, `TestMethod`, names that don't describe the scenario or expected outcome. Good: `Add_NegativeNumber_ThrowsArgumentException`. |
| **Magic values** | Unexplained numbers or strings in arrange/assert: `Assert.AreEqual(42, result)` -- what does 42 mean? |
| **Duplicate tests** | Three or more test methods with near-identical bodies that differ only in a single input value. Should be data-driven (`[DataRow]`, `[Theory]`, `[TestCase]`). For a detailed duplication analysis, use `exp-test-maintainability`. Note: Two tests covering distinct boundary conditions (e.g., zero vs. negative) are NOT duplicates -- separate tests for different edge cases provide clearer failure diagnostics and are a valid practice. |
| **Giant tests** | Test methods exceeding ~30 lines or testing multiple behaviors at once. Hard to diagnose when they fail. |
| **Assertion messages that repeat the assertion** | `Assert.AreEqual(expected, actual, "Expected and actual are not equal")` adds no information. Messages should describe the business meaning. |
| **Missing AAA separation** | Arrange, Act, Assert phases are interleaved or indistinguishable. |

#### Low -- Style and hygiene

| Anti-Pattern | What to Look For |
|---|---|
| **Unused test infrastructure** | `[TestInitialize]`/`[SetUp]` that does nothing, test helper methods that are never called. |
| **IDisposable not disposed** | Test creates `HttpClient`, `Stream`, or other disposable objects without `using` or cleanup. |
| **Console.WriteLine debugging** | Leftover `Console.WriteLine` or `Debug.WriteLine` statements used during test development. |
| **Inconsistent naming convention** | Mix of naming styles in the same test class (e.g., some use `Method_Scenario_Expected`, others use `ShouldDoSomething`). |

### Step 3: Calibrate severity honestly

Before reporting, re-check each finding against these severity rules:

- **Critical/High**: Only for issues that cause tests to give false confidence or be unreliable. A test that always passes regardless of correctness is Critical. Flaky shared state is High.
- **Medium**: Only for issues that actively harm maintainability -- 5+ nearly-identical tests, truly meaningless names like `Test1`.
- **Low**: Cosmetic naming mismatches, minor style preferences, assertion messages that could be better. When in doubt, rate Low.
- **Not an issue**: Separate tests for distinct boundary conditions (zero vs. negative vs. null). Explicit per-test setup instead of `[TestInitialize]` (this *improves* isolation). Tests that are short and clear but could theoretically be consolidated.

IMPORTANT: If the tests are well-written, say so clearly up front. Do not inflate severity to justify the review. A review that finds zero Critical/High issues and only minor Low suggestions is a valid and valuable outcome. Lead with what the tests do well.

### Step 4: Report findings

Present findings in this structure:

1. **Summary** -- Total issues found, broken down by severity (Critical / High / Medium / Low). If tests are well-written, lead with that assessment.
2. **Critical and High findings** -- List each with:
   - The anti-pattern name
   - The specific location (file, method name, line)
   - A brief explanation of why it's a problem
   - A concrete fix (show before/after code when helpful)
3. **Medium and Low findings** -- Summarize in a table unless the user wants full detail
4. **Positive observations** -- Call out things the tests do well (sealed class, specific exception types, data-driven tests, clear AAA structure, proper use of fakes, good naming). Don't only report negatives.

### Step 5: Prioritize recommendations

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
| Flagging intentional design choices | If `Thread.Sleep` is in an integration test testing actual timing, that's not an anti-pattern. Consider context. |
| Inventing false positives on clean code | If tests follow best practices, say so. A review finding "0 Critical, 0 High, 1 Low" is perfectly valid. Don't inflate findings to justify the review. |
| Flagging separate boundary tests as duplicates | Two tests for zero and negative inputs test different edge cases. Only flag as duplicates when 3+ tests have truly identical bodies differing by a single value. |
| Rating cosmetic issues as Medium | Naming mismatches (e.g., method name says `ArgumentException` but asserts `ArgumentOutOfRangeException`) are Low, not Medium -- the test still works correctly. |
| Ignoring the test framework | xUnit uses `[Fact]`/`[Theory]`, NUnit uses `[Test]`/`[TestCase]`, MSTest uses `[TestMethod]`/`[DataRow]` -- use correct terminology |
| Missing the forest for the trees | If 80% of tests have no assertions, lead with that systemic issue rather than listing every instance |
