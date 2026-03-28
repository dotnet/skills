---
name: exp-test-maintainability
description: "Assesses maintainability of .NET test suites and recommends structural improvements. Use when the user asks to reduce test duplication, improve test readability, centralize test data, introduce builders or helpers, or clean up test boilerplate. Covers test size, data-driven patterns, shared setup, helper extraction, and display name quality. Works with MSTest, xUnit, NUnit, and TUnit."
---

# Test Maintainability Assessment

Analyze .NET test code for maintainability issues and recommend targeted refactorings. Read the test files (and production code if available), then assess and report findings.

## Calibration Rules

These judgment rules override default instincts. Apply before reporting:

- **Only recommend extraction at 3+ occurrences.** Two similar setups aren't worth extracting.
- **Don't recommend builders for simple objects.** `new Calculator()` or `new User(1, "Alice")` doesn't need a factory.
- **Respect intentional verbosity.** Explicit per-test setup is valid if each test reads clearly on its own.
- **Display names matter most for non-obvious values.** `[DataRow("Gold", 100.0, 90.0)]` is self-explanatory. `[DataRow(3, 7, 42)]` is not — add `DisplayName`.
- **Prefer `[DataRow]` with `DisplayName` over `[DynamicData]`** when all values are compile-time constants. `[DataRow]` is simpler. Reserve `[DynamicData]` for computed or complex values.
- **If tests are already well-maintained, say so.** A review finding only minor polish is perfectly valid. Acknowledge what's already good.
