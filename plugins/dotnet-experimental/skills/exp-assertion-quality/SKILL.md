---
name: exp-assertion-quality
description: "Analyzes the variety and depth of assertions across .NET test suites. Use when the user asks to evaluate assertion quality, find shallow testing, identify tests with only trivial assertions, measure assertion coverage diversity, or audit whether tests verify different facets of correctness. Produces metrics and actionable recommendations. Works with MSTest, xUnit, NUnit, and TUnit. DO NOT USE FOR: writing new tests (use writing-mstest-tests), detecting anti-patterns (use test-anti-patterns), or fixing existing assertions."
---

# Assertion Diversity Analysis

Analyze .NET test code to measure how varied and meaningful the assertions are. Produce a metrics report that reveals whether tests verify different facets of correctness — not just "output equals X" but also structure, exceptions, state transitions, side effects, and invariants.

## Why Assertion Diversity Matters

Low assertion diversity signals shallow testing. Tests may pass while bugs hide in unasserted logic. Common symptoms:

| Problem | Symptom | Consequence |
|---------|---------|-------------|
| Trivial assertions | `Assert.IsNotNull(result)` only | Test passes but doesn't verify correctness |
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

## When Not to Use

- User wants to write new tests (use `writing-mstest-tests`)
- User wants to detect anti-patterns beyond assertions (use `test-anti-patterns`)
- User wants to fix or rewrite assertions (help them directly)
- User asks about code coverage percentages (out of scope — this analyzes assertion quality, not line coverage)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | One or more test files or a test project directory to analyze |
| Production code | No | The code under test, to evaluate whether assertions cover the important behaviors |

## Workflow

### Step 1: Gather the test code

Read all test files the user provides. If the user points to a directory or project, scan for all test files — see the `exp-dotnet-test-frameworks` skill for framework-specific markers.

### Step 2: Classify every assertion

For each test method, identify all assertions and classify them into these categories:

| Category | Examples | What it verifies |
|----------|---------|-----------------|
| **Equality** | `Assert.AreEqual`, `Assert.Equal`, `Is.EqualTo` | Return value matches expected |
| **Boolean** | `Assert.IsTrue`, `Assert.IsFalse`, `Assert.True` | Condition holds |
| **Null checks** | `Assert.IsNull`, `Assert.IsNotNull`, `Assert.NotNull` | Presence/absence of value |
| **Exception** | `Assert.ThrowsException`, `Assert.Throws`, `Assert.ThrowsAsync` | Error handling behavior |
| **Type checks** | `Assert.IsInstanceOfType`, `Assert.IsAssignableFrom` | Runtime type correctness |
| **String** | `StringAssert.Contains`, `StringAssert.StartsWith`, `Assert.Matches` | Text content and format |
| **Collection** | `CollectionAssert.Contains`, `Assert.Contains`, `Assert.All`, `Has.Member` | Collection contents and structure |
| **Comparison** | `Assert.IsTrue(x > y)`, `Assert.InRange`, `Is.GreaterThan` | Ordering and magnitude |
| **Approximate** | `Assert.AreEqual(expected, actual, delta)`, `Is.EqualTo().Within()` | Floating-point or tolerance-based |
| **Negative** | `Assert.AreNotEqual`, `Assert.DoesNotContain`, `Assert.DoesNotThrow` | What should NOT happen |
| **State/Side-effect** | Assertions on object properties after mutation, verifying mock calls | State transitions and side effects |
| **Structural/Deep** | Assertions on nested properties, serialized forms, complex objects | Deep object correctness |

A single assertion can belong to multiple categories (e.g., `Assert.AreNotEqual` is both Equality and Negative).

### Step 3: Compute metrics

Calculate these metrics for the test suite:

#### Per-test metrics
- **Assertion count**: Number of assertions in each test method
- **Assertion categories**: Which categories each test uses

#### Suite-wide metrics
- **Average assertions per test**: Total assertions / total test methods
- **Assertion type spread**: Number of distinct assertion categories used across the suite (out of 12)
- **Tests with zero assertions**: Count and percentage of test methods with no assertions at all
- **Tests with only trivial assertions**: Count and percentage of tests where every assertion is only a null check or `Assert.IsTrue(true)` — trivial means no meaningful value verification
- **Tests with negative assertions**: Count and percentage (target: at least 10% of tests should verify what should NOT happen)
- **Tests with exception assertions**: Count and percentage
- **Tests with state/side-effect assertions**: Count and percentage
- **Tests with structural/deep assertions**: Count and percentage
- **Single-category tests**: Count and percentage of tests that use only one assertion category

### Step 4: Apply calibration rules

Before reporting, calibrate findings:

- **Trivial means truly trivial.** `Assert.IsNotNull(result)` alone is trivial. But `Assert.IsNotNull(result)` followed by `Assert.AreEqual(expected, result.Value)` is not — the null check is a guard before the real assertion. Only flag a test as "trivial" if it has no meaningful value assertions.
- **Boolean assertions checking meaningful conditions are not trivial.** `Assert.IsTrue(result.IsValid)` checks a specific property — it's a Boolean assertion, not a trivial one. `Assert.IsTrue(true)` is trivial.
- **Consider the test's intent.** A test for a void method that verifies state change on a dependency is legitimate even if it only uses `Assert.IsTrue`.
- **Exception tests are inherently low-assertion-count.** `Assert.ThrowsException<T>(() => ...)` may be the only assertion — that's fine for exception-focused tests. Don't penalize them for low assertion count.
- **Don't conflate diversity with volume.** A test with 20 `Assert.AreEqual` calls has high volume but low diversity. A test with one equality, one null check, and one exception assertion has low volume but good diversity.
- **If assertions are well-diversified, say so.** A report concluding the suite has good diversity is perfectly valid.

### Step 5: Report findings

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
| Flagging null checks before value checks as trivial | Only flag tests where the null check is the ONLY assertion |
| Counting `Assert.IsTrue(condition)` as trivial | Only `Assert.IsTrue(true)` or always-true conditions are trivial |
| Ignoring framework differences | MSTest uses `Assert.AreEqual`, xUnit uses `Assert.Equal`, NUnit uses `Is.EqualTo` — classify all correctly |
| Recommending diversity for diversity's sake | Only suggest adding assertion types that would catch real bugs in the code under test |
| Missing implicit assertions | `Assert.ThrowsException` is both an exception assertion and a negative assertion (verifying that calling the method has a specific failure mode) |
