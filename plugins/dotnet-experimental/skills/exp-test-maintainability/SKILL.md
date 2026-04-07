---
name: exp-test-maintainability
description: "Detects duplicate boilerplate, copy-paste tests, and structural maintainability issues across .NET test suites. Use when the user asks to reduce repetition, consolidate similar test methods, convert copy-paste tests to data-driven parameterized tests, suggest a better test structure, or identify refactoring opportunities. Identifies repeated construction, assertion patterns, copy-paste methods convertible to DataRow/Theory/TestCase, redundant setup/teardown, and shared infrastructure. Produces an analysis report with concrete before/after suggestions. Works with MSTest, xUnit, NUnit, and TUnit. DO NOT USE FOR: writing new tests (use writing-mstest-tests), reviewing test quality or anti-patterns (use test-anti-patterns), or deep mock auditing (use exp-mock-usage-analysis)."
---

# Test Maintainability Assessment

Analyze .NET test code for maintainability issues: duplicated boilerplate, copy-paste test methods, and structural repetition across test methods and classes. Produce a report of refactoring opportunities with concrete before/after suggestions. The goal is analysis only — do not modify any files.

## When to Use

- User asks to find duplicated code or boilerplate in tests
- User wants to know where test code can be DRY-ed up
- User asks to reduce test duplication, improve test readability, or clean up test boilerplate
- User asks for refactoring opportunities in a test suite
- User wants to identify shared setup or teardown candidates
- User asks "what patterns repeat across my tests?"
- User wants to centralize test data, introduce builders or helpers

## When Not to Use

- User wants to write new tests from scratch (use `writing-mstest-tests`)
- User wants to detect anti-patterns or code smells (use `test-anti-patterns`)
- User wants to actually perform the refactoring (help them directly, this skill only analyzes)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test code | Yes | One or more test files or a test project directory to analyze |
| Production code | No | The code under test, for context on what abstractions might help |
| Scope | No | Whether to analyze within a single class or across multiple classes |

## Workflow

### Step 1: Gather the test code

Read all test files the user provides or references. If the user points to a directory or project, scan for all test files — see the `exp-dotnet-test-frameworks` skill for framework-specific markers.

### Step 2: Identify maintainability issues

Scan for these categories:

#### Category 1: Repeated object construction

Look for the same object being constructed in 3+ test methods with identical or near-identical parameters.

**Indicators:**
- `new ClassName(...)` appearing with identical arguments in multiple tests
- Multiple tests creating the same "system under test" with similar configuration
- Repeated mock/fake/stub creation with the same setup

**Potential refactorings:**
- Extract a factory method or test helper (e.g., `CreateSut()`, `CreateDefaultOrder()`)
- Use `[TestInitialize]`/constructor/`[SetUp]` for shared construction
- Introduce a builder pattern for complex objects with many variations

**Example — before:**
```csharp
[TestMethod]
public void Process_ValidOrder_Succeeds()
{
    var logger = new FakeLogger();
    var email = new FakeEmailService();
    var inventory = new FakeInventory(stock: 100);
    var processor = new OrderProcessor(logger, email, inventory);
    // ...
}

[TestMethod]
public void Process_EmptyItems_Fails()
{
    var logger = new FakeLogger();
    var email = new FakeEmailService();
    var inventory = new FakeInventory(stock: 100);
    var processor = new OrderProcessor(logger, email, inventory);
    // ...
}
```

**After — extract factory:**
```csharp
private static OrderProcessor CreateProcessor(int stock = 100)
{
    return new OrderProcessor(new FakeLogger(), new FakeEmailService(), new FakeInventory(stock));
}
```

#### Category 2: Repeated assertion patterns

Look for the same sequence of assertions appearing in 3+ test methods.

**Indicators:**
- Multiple tests asserting the same set of properties on a result object
- Repeated null-check-then-value-check sequences
- Same collection of `Assert.AreEqual` calls across methods

**Potential refactorings:**
- Extract a custom assertion helper (e.g., `AssertValidOrder(order, expectedTotal, expectedStatus)`)
- Use framework-specific assertion extensions
- Introduce a `Verify` method that checks a standard set of properties

#### Category 3: Copy-paste test methods

Look for test methods with near-identical bodies differing only in input values or a single parameter.

**Indicators:**
- 3+ methods with the same structure but different literal values
- Methods that could be collapsed into `[DataRow]`/`[Theory]`/`[TestCase]`
- Test names that follow a pattern like `Method_Input1_Result`, `Method_Input2_Result`

**Potential refactorings:**
- Convert to parameterized tests with `[DataRow]`/`[InlineData]`/`[TestCase]`
- Use `[DynamicData]`/`[MemberData]`/`[TestCaseSource]` for complex inputs
- Prefer `[DataRow]` with `DisplayName` over `[DynamicData]` when all values are compile-time constants. Reserve `[DynamicData]` for computed or complex values.
- Add `DisplayName` for non-obvious parameter values. `[DataRow("Gold", 100.0, 90.0)]` is self-explanatory; `[DataRow(3, 7, 42)]` is not.

#### Category 4: Duplicated setup/teardown logic

Look for initialization or cleanup code repeated across test classes.

**Indicators:**
- Multiple `[TestInitialize]`/`[SetUp]` methods with similar bodies
- Repeated database seeding, file creation, or HTTP client configuration
- Same `using`/`IDisposable` cleanup pattern across classes

**Potential refactorings:**
- Extract a shared test base class or fixture
- Use composition with a shared helper class
- Create a test context factory

#### Category 5: Repeated test infrastructure

Look for structural patterns shared across test classes.

**Indicators:**
- Same mock interfaces configured identically in multiple classes
- Repeated `HttpClient` setup with similar `DelegatingHandler` patterns
- Same logging/configuration scaffolding across test classes

**Potential refactorings:**
- Extract a shared test fixture or helper library
- Create reusable fake implementations
- Introduce a test harness class

### Step 3: Apply calibration rules

Before reporting, filter findings through these rules:

- **Only report at 3+ occurrences.** Two similar setups are not boilerplate — they may be intentional clarity.
- **Don't flag simple constructors.** `new Calculator()` or `new List<int>()` is not meaningful boilerplate. Don't recommend builders for `new User(1, "Alice")` either.
- **Respect intentional verbosity.** If each test is self-contained and reads clearly on its own, explicit setup per test is a valid choice. Note it but don't flag it as a problem.
- **Distinguish structural similarity from true duplication.** Tests that follow AAA (Arrange-Act-Assert) will look similar by nature. Only flag when the actual code (not just the structure) is duplicated.
- **Consider the blast radius of refactoring.** A helper shared across 20 tests creates coupling. Note the trade-off.
- **If tests are already well-maintained, say so.** A report finding only minor opportunities is perfectly valid. Acknowledge what's already good.

### Step 4: Report findings

Present findings in this structure:

1. **Summary** — How many patterns found, broken down by category. If the test suite is clean, lead with that.
2. **Findings by category** — For each pattern found:
   - Category name and description
   - Locations: list the specific test methods and files involved
   - The duplicated code pattern (show a representative sample)
   - Suggested refactoring with a concrete before/after example
   - Estimated impact: how many lines/methods would be simplified
3. **Refactoring priority** — Rank findings by:
   - Occurrence count (more occurrences = higher value)
   - Complexity of the duplicated code (complex setup > simple construction)
   - Risk (low-risk extractions first)
4. **Trade-offs** — For each suggestion, note:
   - What readability is gained
   - What locality/independence is lost
   - Whether it's worth it given the occurrence count

## Validation

- [ ] Every finding includes specific file and method locations
- [ ] Every finding shows the actual duplicated code, not just a description
- [ ] Every suggestion includes a concrete before/after example
- [ ] Findings are filtered through the 3+ occurrence threshold
- [ ] Simple constructors are not flagged
- [ ] Trade-offs are acknowledged for each suggestion
- [ ] If tests are clean, the report says so upfront

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Flagging AAA structure as duplication | The Arrange-Act-Assert pattern is not boilerplate — flag only when the actual code repeats |
| Suggesting extraction for 2 occurrences | Wait for 3+ before recommending extraction |
| Recommending base classes for everything | Prefer composition (helpers, factories) over inheritance |
| Ignoring the readability cost | Every extraction adds indirection — note the trade-off |
| Flagging simple `new X()` as boilerplate | Only flag complex construction with multiple parameters or configuration |
| Recommending DRY at the expense of test isolation | Tests that share mutable state through helpers become coupled — warn about this |
