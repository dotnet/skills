---
name: writing-mstest-tests
description: >
  Write, create, modernize, or fix comprehensive MSTest unit tests with MSTest 3.x/4.x APIs.
  USE FOR: write or create MSTest unit tests, fix/modernize MSTest assertions,
  better MSTest assertion than Assert.IsTrue, replace hard cast with type check (IsInstanceOfType),
  MSTest assertion APIs (Contains, ContainsSingle, HasCount, IsEmpty, IsNotEmpty, DoesNotContain,
  AreSame, IsNull, StartsWith, EndsWith, MatchesRegex, IsGreaterThan, IsLessThan, IsInRange),
  swapped Assert.AreEqual args, replace ExpectedException with Assert.Throws,
  data-driven (DataRow, DynamicData, ValueTuples),
  lifecycle (TestInitialize, TestCleanup, TestContext),
  async tests and cancellation tokens, conditional execution/retry/cleanup (OSCondition, Retry),
  parallelization (Parallelize/DoNotParallelize), MSTest.Sdk setup, MSTESTxxxx analyzer fixes.
  DO NOT USE FOR: test quality audits (use test-anti-patterns),
  running tests (use run-tests), MSTest version migration (use the migrate-mstest skills),
  xUnit/NUnit/TUnit, or non-.NET languages.
license: MIT
---

# Writing MSTest Tests

Help users write effective, modern unit tests with MSTest 3.x/4.x using current APIs and best practices.

## When to Use

- User wants to write new MSTest unit tests
- User wants to improve or modernize existing MSTest tests by implementing concrete fixes
- User asks about MSTest assertion APIs, data-driven patterns, or test lifecycle
- User asks to replace `Assert.IsTrue` with more specific assertions (collections, nulls, types, comparisons)
- User asks to replace hard casts with type-checking assertions in tests
- User needs help fixing a specific MSTest test bug or failing assertion
- User asks to fix swapped `Assert.AreEqual` argument order (expected first, actual second)
- User asks to convert `DynamicData` from `IEnumerable<object[]>` to ValueTuple-based data
- User asks to fix or understand an MSTest analyzer diagnostic (an `MSTESTxxxx` warning/error)

## When Not to Use

- User needs a test quality audit, anti-pattern detection, or flaky-test investigation (use `test-anti-patterns`)
- User needs to run or execute tests (use the `run-tests` skill)
- User needs to upgrade from MSTest v1/v2 to v3 (use `migrate-mstest-v1v2-to-v3`)
- User needs to upgrade from MSTest v3 to v4 (use `migrate-mstest-v3-to-v4`)
- User needs CI/CD pipeline configuration
- User is using xUnit, NUnit, or TUnit (not MSTest)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Code under test | No | The production code to be tested |
| Existing test code | No | Current tests to fix, update, or modernize |
| Test scenario description | No | What behavior the user wants to test |

## Response Guidelines

- **Specific API or pattern questions** (assertions, data-driven, lifecycle): Jump directly to the relevant workflow step. Do not follow the full workflow.
- **Write new tests from scratch**: Follow the full workflow.
- **Review and fix existing tests**: Fix only the issues present. Do not add unrelated improvements.

## Workflow

### Step 1: Determine project setup

Check the test project for MSTest version and configuration:

- If using `MSTest.Sdk` (`<Sdk Name="MSTest.Sdk">`): modern setup, all features available
- If using `MSTest` metapackage: modern setup (MSTest 3.x+)
- If using `MSTest.TestFramework` + `MSTest.TestAdapter`: check version for feature availability

Recommend MSTest.Sdk or the MSTest metapackage for new projects:

```xml
<!-- Option 1: MSTest SDK (simplest, recommended for new projects) -->
<Project Sdk="MSTest.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
```

When using `MSTest.Sdk`, put the version in `global.json` instead of the project file so all test projects get bumped together:

```json
{
  "msbuild-sdks": {
    "MSTest.Sdk": "3.8.2"
  }
}
```

```xml
<!-- Option 2: MSTest metapackage -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MSTest" Version="3.8.2" />
  </ItemGroup>
</Project>
```

### Step 2: Write test classes following conventions

Apply these structural conventions:

- **Seal test classes** with `sealed` for performance and design clarity
- Use `[TestClass]` on the class and `[TestMethod]` on test methods
- Follow the **Arrange-Act-Assert** (AAA) pattern
- Name tests using `MethodName_Scenario_ExpectedBehavior`
- Use separate test projects with naming convention `[ProjectName].Tests`

```csharp
[TestClass]
public sealed class OrderServiceTests
{
    [TestMethod]
    public void CalculateTotal_WithDiscount_ReturnsReducedPrice()
    {
        // Arrange
        var service = new OrderService();
        var order = new Order { Price = 100m, DiscountPercent = 10 };

        // Act
        var total = service.CalculateTotal(order);

        // Assert
        Assert.AreEqual(90m, total);
    }
}
```

### Step 3: Use modern assertion APIs

Pick the most specific assertion for each test scenario. More specific assertions produce better failure messages and make the test's intent clear:

| What you are testing | Assertion |
|---|---|
| Two values are equal | `Assert.AreEqual(expected, actual)` |
| Same object instance (reference identity) | `Assert.AreSame(expected, actual)` |
| Value is null | `Assert.IsNull(value)` |
| Value is not null | `Assert.IsNotNull(value)` |
| Collection is empty | `Assert.IsEmpty(collection)` |
| Collection is not empty | `Assert.IsNotEmpty(collection)` |
| Collection has exactly N items | `Assert.HasCount(N, collection)` |
| Collection contains an item | `Assert.Contains(item, collection)` |
| Collection does not contain an item | `Assert.DoesNotContain(item, collection)` |
| Object is a specific type | `Assert.IsInstanceOfType<T>(value)` |
| Code throws an exception | `Assert.ThrowsExactly<T>(() => ...)` |

Prefer `Assert` class methods over `StringAssert` or `CollectionAssert` where both exist.

#### Equality, null, and reference checks

```csharp
Assert.AreEqual(expected, actual);      // Value equality
Assert.AreSame(expected, actual);       // Reference equality -- same object instance
Assert.IsNull(value);
Assert.IsNotNull(value);
```

#### Exception testing -- use `Assert.Throws` instead of `[ExpectedException]`

```csharp
// Synchronous
var ex = Assert.ThrowsExactly<ArgumentNullException>(() => service.Process(null));
Assert.AreEqual("input", ex.ParamName);

// Async
var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
    async () => await service.ProcessAsync(null));
```

- `Assert.Throws<T>` matches `T` or any derived type
- `Assert.ThrowsExactly<T>` matches only the exact type `T`

#### Collection assertions

```csharp
Assert.Contains(expectedItem, collection);
Assert.DoesNotContain(unexpectedItem, collection);
var single = Assert.ContainsSingle(collection);  // Returns the single element
Assert.HasCount(3, collection);
Assert.IsEmpty(collection);
Assert.IsNotEmpty(collection);
```

Replace generic `Assert.IsTrue` with specialized assertions -- they give better failure messages:

| Instead of | Use |
|---|---|
| `Assert.IsTrue(list.Count > 0)` | `Assert.IsNotEmpty(list)` |
| `Assert.IsTrue(list.Count == 0)` | `Assert.IsEmpty(list)` |
| `Assert.IsTrue(list.Count() == 3)` | `Assert.HasCount(3, list)` |
| `Assert.IsTrue(x != null)` | `Assert.IsNotNull(x)` |
| `Assert.IsTrue(x == null)` | `Assert.IsNull(x)` |
| `Assert.AreEqual(a, b)` for same instance | `Assert.AreSame(a, b)` -- reference identity |
| `Assert.IsTrue(!list.Contains(item))` | `Assert.DoesNotContain(item, list)` |
| `list.Single(predicate)` + `Assert.IsNotNull` | `Assert.ContainsSingle(list)` |
| `Assert.IsTrue(list.Contains(item))` | `Assert.Contains(item, list)` |

#### String assertions

```csharp
Assert.Contains("expected", actualString);
Assert.StartsWith("prefix", actualString);
Assert.EndsWith("suffix", actualString);
Assert.MatchesRegex(@"\d{3}-\d{4}", phoneNumber);
```

#### Type assertions

```csharp
// MSTest 3.x -- out parameter
Assert.IsInstanceOfType<MyHandler>(result, out var typed);
typed.Handle();

// MSTest 4.x -- returns directly
var typed = Assert.IsInstanceOfType<MyHandler>(result);
```

#### Comparison assertions

```csharp
Assert.IsGreaterThan(lowerBound, actual);
Assert.IsLessThan(upperBound, actual);
Assert.IsInRange(actual, low, high);
```

### Step 4: Use data-driven tests for multiple inputs

#### DataRow for inline values

```csharp
[TestMethod]
[DataRow(1, 2, 3)]
[DataRow(0, 0, 0, DisplayName = "Zeros")]
[DataRow(-1, 1, 0)]
public void Add_ReturnsExpectedSum(int a, int b, int expected)
{
    Assert.AreEqual(expected, Calculator.Add(a, b));
}
```

#### DynamicData with ValueTuples (preferred for complex data)

Prefer `ValueTuple` return types over `IEnumerable<object[]>` for type safety:

```csharp
[TestMethod]
[DynamicData(nameof(DiscountTestData))]
public void ApplyDiscount_ReturnsExpectedPrice(decimal price, int percent, decimal expected)
{
    var result = PriceCalculator.ApplyDiscount(price, percent);
    Assert.AreEqual(expected, result);
}

// ValueTuple -- preferred (MSTest 3.7+)
public static IEnumerable<(decimal price, int percent, decimal expected)> DiscountTestData =>
[
    (100m, 10, 90m),
    (200m, 25, 150m),
    (50m, 0, 50m),
];
```

When you need metadata per test case, use `TestDataRow<T>`:

```csharp
public static IEnumerable<TestDataRow<(decimal price, int percent, decimal expected)>> DiscountTestDataWithMetadata =>
[
    new((100m, 10, 90m)) { DisplayName = "10% discount" },
    new((200m, 25, 150m)) { DisplayName = "25% discount" },
    new((50m, 0, 50m)) { DisplayName = "No discount" },
];
```

### Step 5: Handle test lifecycle correctly

- **Always initialize in the constructor** -- this enables `readonly` fields and works correctly with nullability analyzers (fields are guaranteed non-null after construction)
- Use `[TestInitialize]` **only** for async initialization, combined with the constructor for sync parts
- Use `[TestCleanup]` for cleanup that must run even on failure
- Inject `TestContext` via constructor (MSTest 3.6+)

```csharp
[TestClass]
public sealed class RepositoryTests
{
    private readonly TestContext _testContext;
    private readonly FakeDatabase _db;  // readonly -- guaranteed by constructor

    public RepositoryTests(TestContext testContext)
    {
        _testContext = testContext;
        _db = new FakeDatabase();  // sync init in ctor
    }

    [TestInitialize]
    public async Task InitAsync()
    {
        // Use TestInitialize ONLY for async setup
        await _db.SeedAsync();
    }

    [TestCleanup]
    public void Cleanup() => _db.Reset();
}
```

#### Execution order

1. `[AssemblyInitialize]` -- once per assembly
2. `[ClassInitialize]` -- once per class
3. Per test:
   - With `TestContext` property injection: Constructor -> set `TestContext` property -> `[TestInitialize]`
   - With constructor injection of `TestContext`: Constructor (receives `TestContext`) -> `[TestInitialize]`
4. Test method
5. `[TestCleanup]` -> `DisposeAsync` -> `Dispose` -- per test
6. `[ClassCleanup]` -- once per class
7. `[AssemblyCleanup]` -- once per assembly

### Step 6: Apply cancellation and timeout patterns

Always use `TestContext.CancellationToken` with `[Timeout]`:

```csharp
[TestMethod]
[Timeout(5000)]
public async Task FetchData_ReturnsWithinTimeout()
{
    var result = await _client.GetDataAsync(_testContext.CancellationToken);
    Assert.IsNotNull(result);
}
```

### Step 7: Use advanced features where appropriate

#### Retry flaky tests (MSTest 3.9+)

Use only for genuinely flaky external dependencies (network, file system), not to paper over race conditions or shared state issues.

```csharp
[TestMethod]
[Retry(3)]
public void ExternalService_EventuallyResponds() { }
```

#### Conditional execution (MSTest 3.10+)

```csharp
[TestMethod]
[OSCondition(OperatingSystems.Windows)]
public void WindowsRegistry_ReadsValue() { }

[TestMethod]
[CICondition(ConditionMode.Exclude)]
public void LocalOnly_InteractiveTest() { }
```

#### Parallelization

```csharp
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]

[TestClass]
[DoNotParallelize]  // Opt out specific classes
public sealed class DatabaseIntegrationTests { }
```

### Step 8: Fix MSTest analyzer diagnostics (MSTESTxxxx)

The `MSTest.Analyzers` package reports `MSTESTxxxx` diagnostics during build and in the IDE. The analyzers come in automatically with the modern `MSTest` metapackage and `MSTest.Sdk` (and are bundled with `MSTest.TestFramework` 3.7+); for other setups, reference `MSTest.Analyzers` explicitly. Most rules have an automated code fix (light bulb) in Visual Studio. When fixing one by hand, apply the idiomatic change below rather than suppressing the rule.

When asked to "fix MSTESTxxxx", look it up in the table of common diagnostics below, apply the fix, and rebuild to confirm the diagnostic is gone. The table is not exhaustive — for any rule it does not list, consult the full reference and apply the documented guidance: <https://learn.microsoft.com/dotnet/core/testing/mstest-analyzers/overview>.

#### Common diagnostics and their fixes

| Rule | Problem | Fix |
|---|---|---|
| MSTEST0006 | `[ExpectedException]` used | Replace with `Assert.Throws<T>` / `Assert.ThrowsExactly<T>` (Step 3) |
| MSTEST0017 | `Assert.AreEqual` args swapped | Put `expected` first, `actual` second |
| MSTEST0023 | Negated boolean assertion (`Assert.IsTrue(!x)`) | Use `Assert.IsFalse(x)` |
| MSTEST0025 | Always-false condition asserted | Use `Assert.Fail("reason")` |
| MSTEST0032 | Always-true assert condition | Remove or correct the assertion |
| MSTEST0037 | Sub-optimal assert (`IsTrue(x == null)`) | Use the specific assert (`Assert.IsNull`, `HasCount`, etc.) (Step 3) |
| MSTEST0038 | `Assert.AreSame` on value types | Use `Assert.AreEqual` (value types box to distinct references) |
| MSTEST0039 | Legacy `Assert.ThrowsException` | Use `Assert.Throws` / `Assert.ThrowsExactly` (+ `Async` variants) |
| MSTEST0044 | `[DataTestMethod]` used | Replace with `[TestMethod]` (it now supports data rows) |
| MSTEST0046 | `StringAssert` used | Use the equivalent `Assert` method (`Assert.Contains`, `StartsWith`, ...) |
| MSTEST0052 | Explicit `DynamicDataSourceType` | Drop it — the source type is inferred |
| MSTEST0042 / MSTEST0060 | Duplicate `[DataRow]` / `[TestMethod]` | Remove the duplicate attribute |
| MSTEST0024 | Static `TestContext` field | Make it an instance member (Step 5) |
| MSTEST0045 / MSTEST0049 / MSTEST0054 | Timeout/token not cooperative | Flow `TestContext.CancellationToken` into the awaited call (Step 6) |
| MSTEST0036 | Member shadows a base test member | Rename or use `override` instead of `new` |
| MSTEST0061 | Runtime OS check inside a test | Use `[OSCondition(...)]` (Step 7) |
| MSTEST0002 / MSTEST0003 / MSTEST0005 / MSTEST0007–0014 | Invalid test class / method / fixture / `TestContext` / data-source layout | Correct the signature named by the rule (e.g. make it public, fix the return type and parameters, add `static` where required) |

#### Tuning which rules are enforced

Use the `MSTestAnalysisMode` MSBuild property (MSTest 3.8+) to control the rule set globally:

```xml
<PropertyGroup>
  <!-- None | Default | Recommended | All -->
  <MSTestAnalysisMode>Recommended</MSTestAnalysisMode>
</PropertyGroup>
```

- `Recommended` escalates info-level rules to warnings and is the mode most projects should adopt.
- A handful of rules are completely opt-in (e.g. MSTEST0015, MSTEST0019–0022); enable them per project via `.editorconfig` when you want their convention enforced.
- Prefer fixing the underlying code over suppressing a diagnostic. Suppress only with a documented justification.
