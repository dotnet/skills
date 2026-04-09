---
name: exp-test-smell-detection
description: "Deep formal test smell audit based on academic research taxonomy (testsmells.org). Detects 19 categorized smell types — conditional logic, mystery guests, sensitive equality, eager tests, and more — with calibrated severity and research-backed remediation. Use for comprehensive test suite health assessments. For a quick pragmatic review, use test-anti-patterns instead. DO NOT USE FOR: writing new tests (use writing-mstest-tests), evaluating assertion quality specifically (use exp-assertion-quality), or finding test duplication and boilerplate (use exp-test-maintainability)."
---

# Test Smell Detection

Deep formal audit of test code using an academic test smell taxonomy. Detects symptoms of bad design or implementation decisions that make tests harder to understand, more fragile, less effective at catching bugs, or more expensive to maintain. Produces a severity-ranked report with specific locations and actionable fixes.

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
- User wants to evaluate assertion diversity specifically (use `exp-assertion-quality`)
- User wants to find duplicated boilerplate across tests (use `exp-test-maintainability`)
- User wants to write new tests from scratch (help them directly)
- User wants to fix a specific failing test (diagnose and fix directly)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | One or more test files or a test project directory to analyze |
| Production code | No | The code under test, for context on whether patterns are justified |

## Workflow

### Step 1: Gather the test code

Read all test files the user provides. If the user points to a directory or project, scan for all test files by looking for test framework markers — see the `exp-dotnet-test-frameworks` skill for .NET-specific markers.

For a thorough audit, also consult the [extended smell catalog](references/test-smell-catalog.md) which covers 9 additional smell types beyond the core 10 below.

### Step 2: Scan for test smells

For each test method and class, check for the following smell categories:

#### Smell 1: Conditional Test Logic

Test methods containing `if`, `else`, `switch`, ternary (`? :`), `for`, `foreach`, or `while` statements. Control flow in tests means some paths may never execute, hiding gaps.

**Severity:** High
**Detection:** Any control flow statement inside a test method body.
**Exception:** `foreach` used solely to assert every item in a known collection is acceptable when the assertion is the loop body.

#### Smell 2: Mystery Guest

Tests that depend on external resources — files on disk, databases, network endpoints, environment variables — without making the dependency explicit or using test doubles.

**Severity:** High
**Detection:** Test methods that read files, open database connections, make HTTP requests (without a test handler), read environment variables, or use hard-coded file paths.
**Exception:** In-memory fakes or test-specific handlers are fine.

#### Smell 3: Sleepy Test

Tests that call sleep or delay functions to wait for a condition. These introduce non-deterministic timing and slow down the suite.

**Severity:** High
**Detection:** Calls to sleep/delay functions inside test methods. See the `exp-dotnet-test-frameworks` skill for .NET-specific patterns.

#### Smell 4: Assertion-Free Test (Unknown Test)

Tests that execute code but never assert anything. Test frameworks report these as passing even if the code is completely broken, as long as no exception is thrown.

**Severity:** High
**Detection:** A test method with no assertion calls (framework-specific: `Assert.*`, `expect()`, `assert`, `Should*`, etc.) and no expected-exception annotation.
**Calibration:** A method named `*_DoesNotThrow` or `*_NoException` is implicitly asserting no exception — still flag it but note it may be intentional.

#### Smell 5: Eager Test

A test method that calls many different production methods, making it unclear what behavior is being tested. When it fails, diagnosis is difficult because the failure could stem from any of the calls.

**Severity:** Medium
**Detection:** A test method that calls 4+ distinct methods on the production object (excluding setup/construction). Count unique method names, not call count.
**Calibration:** Integration tests or workflow tests may legitimately call multiple methods — note this as a possible exception for end-to-end scenarios.

#### Smell 6: Magic Number Test

Assertions that contain unexplained numeric literals. The intent of `Assert.AreEqual(42, result)` is unclear without context — what does 42 represent?

**Severity:** Medium
**Detection:** Numeric literals (other than 0, 1, -1, and the literal used in the test name) appearing as `expected` parameters in assertion methods.
**Calibration:** Small integers in context (like count checks `Assert.AreEqual(3, list.Count)` where 3 items were just added) are acceptable — only flag when the number's meaning is genuinely unclear.

#### Smell 7: Sensitive Equality

Tests that use `ToString()` for comparison or assertion. If the `ToString()` implementation changes, the test breaks even though the actual behavior is correct.

**Severity:** Medium
**Detection:** `Assert.AreEqual(expected, obj.ToString())`, or `.ToString()` appearing inside an assertion parameter.

#### Smell 8: Exception Handling in Tests

Tests that contain `try`/`catch` blocks or `throw` statements. This typically means the test is manually managing exceptions rather than using the framework's built-in exception assertion facilities.

**Severity:** Medium
**Detection:** `try`/`catch` or `throw`/`raise` statements inside a test method.
**Exception:** `catch` blocks that capture an exception for further assertion are a lesser concern — note but don't flag as high severity.

#### Smell 9: General Fixture (Over-broad Setup)

The test setup method or constructor initializes fields that are not used by every test method. This means each test pays the cost of setting up objects it doesn't need.

**Severity:** Low
**Detection:** Fields initialized in setup that are referenced by fewer than half the test methods in the class.

#### Smell 10: Ignored/Disabled Test

Tests marked as skipped or disabled. These add overhead and clutter, and the underlying issue they were disabled for may never be addressed.

**Severity:** Low
**Detection:** Skip/ignore annotations or conditional compilation that disables a test. See the `exp-dotnet-test-frameworks` skill for framework-specific skip attributes.

### Step 3: Apply calibration rules

Before reporting, calibrate findings to avoid false positives:

- **Integration tests have different norms.** A test class clearly marked as integration (by name, annotation, or category) legitimately uses external resources, calls multiple methods, and may use delays for async coordination. Downgrade Mystery Guest, Eager Test, and Sleepy Test severity for integration tests — note them but don't flag as problems.
- **Simple loop-assert patterns are fine.** Iterating a collection to assert on every item is readable and correct. Only flag loops with complex branching logic.
- **Context matters for magic numbers.** A count assertion right after adding a known number of items is self-documenting. Only flag numbers whose meaning requires looking at production code to understand.
- **Inconclusive/pending markers are not assertion-free.** Tests explicitly marked as incomplete should be flagged as Ignored Test, not Assertion-Free.
- **Capture-and-assert exception patterns are borderline.** Try/catch patterns that capture an exception then assert on its properties are ugly but functional. Note as a smell and suggest the framework's built-in exception assertion instead of calling it broken.
- **If the test suite is clean, say so.** A report finding few or no smells is perfectly valid.

### Step 4: Report findings

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
| Flagging integration tests for using real resources | Check for integration test markers and adjust severity accordingly |
| Flagging loop-over-collection-assert as conditional logic | Only flag loops with branching or complex logic, not assertion iterations |
| Flagging obvious count assertions after adding N items | Consider the immediate context — self-documenting numbers are fine |
| Missing framework-specific assertion syntax | Consult the `exp-dotnet-test-frameworks` skill for .NET framework assertion and skip APIs |
| Over-flagging try/catch that captures for assertion | Distinguish swallowed exceptions from capture-and-assert patterns |
| Treating skip annotations with reasons same as bare skips | Note that reasoned skips are less concerning than unexplained ones |
| Flagging `DoesNotThrow`-style tests as assertion-free | These implicitly assert no exception — note but acknowledge the intent |
