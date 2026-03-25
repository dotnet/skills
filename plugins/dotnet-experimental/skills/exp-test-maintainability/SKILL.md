---
name: exp-test-maintainability
description: "Assesses maintainability of .NET test suites and recommends structural improvements. Use when the user asks to reduce test duplication, improve test readability, centralize test data, introduce builders or helpers, or clean up test boilerplate. Covers test size, data-driven patterns, shared setup, helper extraction, and display name quality. Works with MSTest, xUnit, NUnit, and TUnit."
---

# Test Maintainability Assessment

Analyze .NET test code for maintainability problems — duplication, excessive test size, scattered test data construction, missing helpers, and unclear data-driven tests — and recommend targeted refactorings.

## When to Use

- User asks to reduce test duplication or boilerplate
- User asks "how can I make my tests easier to maintain?"
- User wants to centralize test data or introduce test builders
- User has large test files and wants to improve readability
- User asks to refactor or clean up test code structure

## When Not to Use

- User wants to find bugs or anti-patterns in tests (use `exp-test-anti-patterns`)
- User wants to write new tests from scratch (use `writing-mstest-tests`)
- User wants to run or execute tests (use `run-tests`)
- User wants to migrate between test frameworks or versions (use migration skills)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | One or more test files or classes to analyze |
| Production code | No | The code under test, for context on what the tests exercise |
| Specific concern | No | A focus area like "duplication" or "readability" to narrow the review |

## Workflow

### Step 1: Gather the test code

Read the test files the user wants reviewed. If the user points to a directory or project, scan for all test files. Read production code too if available — it helps assess whether test setup complexity is proportionate to the code under test.

### Step 2: Assess maintainability dimensions

Evaluate each test file against these dimensions:

#### Test Size and Focus

| Signal | What to Look For |
|---|---|
| **Oversized tests** | Test methods exceeding ~20 lines. Each test should be understandable at a glance. |
| **Multi-concern tests** | A single test method that arranges, acts, and asserts on multiple independent behaviors. Should be split. |
| **Appropriate size** | Short, focused tests (5-15 lines) that test one thing. Don't flag these — acknowledge them as good. |

#### Duplication and Data-Driven Patterns

| Signal | What to Look For |
|---|---|
| **Copy-pasted test bodies** | Three or more test methods with near-identical structure differing only in input values. Should use `[DataRow]`, `[Theory]`/`[InlineData]`, or `[TestCase]`. |
| **Copy-pasted arrangement** | The same multi-line object construction repeated across many tests. Should extract a builder method or shared factory. |
| **Missing display names** | `[DataRow]` / `[InlineData]` / `[TestCase]` without `DisplayName` parameters. When values are not self-explanatory (e.g., magic numbers, enum values, edge cases), display names help identify which case failed. |
| **Good data-driven usage** | Tests that already use parameterization effectively. Acknowledge this. |

#### Test Data Construction

| Signal | What to Look For |
|---|---|
| **Scattered object construction** | Complex `new Entity { Prop1 = ..., Prop2 = ..., Prop3 = ... }` blocks repeated across tests with slight variations. Should use a builder or factory method. |
| **Builder or factory pattern already in use** | Helper methods like `CreateTestOrder()`, `BuildUser()`, or a test builder class. Acknowledge as good practice. |
| **Appropriate inline construction** | Simple `new` calls with 1-2 properties. Don't flag these — extracting a builder for trivial objects adds unnecessary indirection. |

#### Shared Setup and Helpers

| Signal | What to Look For |
|---|---|
| **Repeated setup code** | Same 3+ lines of arrangement appearing in most tests in a class. Consider `[TestInitialize]`/constructor or a helper method. |
| **Over-centralized setup** | `[TestInitialize]` that builds complex state used by only some tests, making others harder to understand. Sometimes explicit per-test setup is clearer. |
| **Missing assertion helpers** | Complex assertion logic (multi-line comparisons, collection checks with specific predicates) repeated across tests. Should extract a domain-specific assertion method. |

### Step 3: Calibrate findings honestly

Apply these judgment rules before reporting:

- **Only recommend extraction when there are 3+ occurrences.** Two similar setups are not worth extracting — the cure is worse than the disease.
- **Don't recommend builders for simple objects.** `new Calculator()` or `new User(1, "Alice")` doesn't need a factory.
- **Respect intentional verbosity.** Some teams prefer explicit per-test setup over shared helpers for readability. If each test reads clearly on its own, that's valid.
- **Display names matter most for non-obvious values.** `[DataRow("Gold", 100.0, 90.0)]` is self-explanatory. `[DataRow(3, 7, 42)]` is not.
- **Prefer `[DataRow]` with `DisplayName` over `[DynamicData]`** when all values are compile-time constants. `[DataRow]` is simpler and `DisplayName` keeps test output readable. Reserve `[DynamicData]` for cases that genuinely need computed or complex values.
- **If the tests are already well-maintained, say so.** A review that finds only minor polish opportunities is perfectly valid.

### Step 4: Report findings

For each finding, show where it occurs, what to change, and concrete before/after code. Lead with high-duplication items. Include positive observations about what the tests already do well. Quantify benefits (e.g., "eliminates N lines", "new test cases become M-line methods").
