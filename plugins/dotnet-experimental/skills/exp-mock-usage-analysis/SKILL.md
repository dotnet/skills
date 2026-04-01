---
name: exp-mock-usage-analysis
description: "Audits .NET test mock usage by tracing each mock setup through the production code's execution path to find dead, unreachable, redundant, or replaceable mocks. Use when the user asks to audit mock usage, find unused or unnecessary mock setups, check if mocks are needed, reduce mock duplication or over-mocking, simplify test setup, or review whether mock configurations like ILogger/IOptions should use real implementations instead. Supports Moq, NSubstitute, and FakeItEasy."
---

# Mock Usage Analysis

Trace each mock setup through the production code's execution path to determine which setups are actually exercised at runtime and which are dead, unreachable, redundant, or replaceable with real implementations.

## When to Use

- User asks to audit, review, or analyze mock usage in .NET tests
- User wants to find unused, unnecessary, or redundant mock setups
- User wants to simplify test setup or reduce over-mocking
- User asks whether mocks of ILogger, IOptions, or similar types are needed

## When Not to Use

- User wants to write new mocks or tests (general testing guidance)
- User wants to detect non-mock test anti-patterns (use `test-anti-patterns`)
- User wants to migrate between mock frameworks (out of scope)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | Test files to analyze |
| Production code | Yes | Code under test — essential for tracing execution paths |

## Workflow

### Step 1: Read all provided code

Read the test files and **always** read the production code. You cannot determine whether a mock setup is necessary without understanding the production method's control flow.

Identify the mock framework by scanning for its patterns:
- **Moq**: `new Mock<T>()`, `.Setup(...)`, `.Verify(...)`
- **NSubstitute**: `Substitute.For<T>()`, `.Returns(...)`, `.Received(...)`
- **FakeItEasy**: `A.Fake<T>()`, `A.CallTo(...)`, `.MustHaveHappened()`

Use the correct framework's terminology throughout your analysis.

### Step 2: Trace each mock setup through the production code

For **each test method**, do the following:

1. Identify every mock setup line (`.Setup`, `.Returns`, `A.CallTo`, etc.)
2. Read the production method being tested and trace its execution path for the specific inputs used in that test
3. Determine which mock setups are actually reached during execution
4. Classify each setup:

| Classification | Meaning | Example |
|---------------|---------|---------|
| **Used** | The production code calls this mock during the test's execution path | `GetStock` setup when `Reserve` is called and stock is sufficient |
| **Unreachable** | The production code returns early, throws, or branches away before reaching this mock call | `UpdateStock` setup when the test expects the method to throw `ArgumentOutOfRangeException` on the first line |
| **Unused** | The mock method is never called by the production method under test at all, regardless of inputs | `GetLowStockProducts` setup when testing `Reserve`, which never calls that method |
| **Redundant** | Identical mock configurations are duplicated across multiple tests instead of being shared | Five tests each creating `new Mock<IPaymentGateway>()` with the same default setup |

Pay special attention to:
- **Early returns and guard clauses** — setups for mocks called after a guard clause are unreachable when the guard triggers
- **Exception throws** — if the method throws before using dependencies, all setups for those dependencies are unnecessary
- **Branch-specific logic** — if a method dispatches by channel/type, setups for other channels are unused
- **Verify-only tests** — tests that only call `.Verify`/`.Received`/`.MustHaveHappened` without asserting on the method's return value

### Step 3: Check for replaceable mocks

Flag mocks of stable framework types that should use real implementations:
- `Mock<ILogger<T>>` → `NullLogger<T>.Instance` (unless log output is asserted)
- `Mock<IOptions<T>>` → `Options.Create(new T { ... })`
- Mocks of DTOs, records, or value objects → use `new T { ... }` directly

Explicitly confirm which mocks are **correctly placed** — external boundaries (databases, HTTP clients, message queues, third-party APIs) and security-sensitive types should remain mocked.

### Step 4: Report findings

For each finding, state:
1. The specific test method and mock setup line
2. Why the setup is unnecessary (trace the production code path to explain)
3. A concrete fix — which lines to remove, what to replace them with, or how to extract shared setup

When multiple tests duplicate mock configurations, provide a before/after example showing how to extract shared setup into a fixture or helper method.

## Validation

- [ ] Production code was read and execution paths were traced (not just test code reviewed)
- [ ] Every finding references a specific test method and setup line
- [ ] Unreachable setups include an explanation of which production code path makes them unreachable
- [ ] Correctly-placed mocks (external boundaries) are explicitly noted as appropriate
- [ ] Correct framework terminology is used throughout (not mixing Moq/NSubstitute/FakeItEasy terms)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Analyzing test code without reading production code | Always read the production method to trace which mocks are actually called |
| Flagging mocks for external boundaries (HTTP, DB) | These are valid isolation boundaries — keep them mocked |
| Flagging `ILogger` mock when log output is asserted | Only flag when the mock is set up but log output is never verified |
| Using wrong framework terminology | Match the framework in the code: Moq (`Setup`/`Verify`), NSubstitute (`Returns`/`Received`), FakeItEasy (`A.CallTo`/`MustHaveHappened`) |
