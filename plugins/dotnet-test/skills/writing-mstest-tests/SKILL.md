---
name: writing-mstest-tests
description: "Best practices for writing MSTest 3.x/4.x unit tests. Use when the user needs to write, improve, fix, or review MSTest tests, including modern assertions, data-driven tests, test lifecycle, and common anti-patterns. Also use when fixing test issues like swapped Assert.AreEqual arguments, incorrect assertion usage, or modernizing legacy test code. Covers MSTest.Sdk, sealed classes, Assert.Throws, DynamicData with ValueTuples, TestContext, and conditional execution."
---

# Writing MSTest Tests

Help users write effective, modern unit tests with MSTest 3.x/4.x using current APIs and best practices.

## When to Use

- User wants to write new MSTest unit tests
- User wants to improve or modernize existing MSTest tests
- User asks about MSTest assertion APIs, data-driven patterns, or test lifecycle
- User needs targeted help fixing or modernizing MSTest tests

## When Not to Use

- User needs to run or execute tests (use the `run-tests` skill)
- User needs to upgrade from MSTest v1/v2 to v3 (use `migrate-mstest-v1v2-to-v3`)
- User needs to upgrade from MSTest v3 to v4 (use `migrate-mstest-v3-to-v4`)
- User needs to review or audit existing tests for anti-patterns or test quality (use `test-anti-patterns`)
- User needs CI/CD pipeline configuration
- User is using xUnit, NUnit, or TUnit (not MSTest)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Code under test | No | The production code to be tested |
| Existing test code | No | Current tests to improve or modernize |
| Test scenario description | No | What behavior the user wants to test |

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

Use the correct assertion for each scenario. Prefer `Assert` class methods over `StringAssert` or `CollectionAssert` where both exist.

#### Equality and null checks

```csharp
Assert.AreEqual(expected, actual);      // Value equality
Assert.AreSame(expected, actual);       // Reference equality
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
| `Assert.IsTrue(list.Count() == 3)` | `Assert.HasCount(3, list)` |
| `Assert.IsTrue(x != null)` | `Assert.IsNotNull(x)` |
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

## Validation

- [ ] Test classes are `sealed`
- [ ] Test methods follow `MethodName_Scenario_ExpectedBehavior` naming
- [ ] `Assert.ThrowsExactly<T>` used instead of `[ExpectedException]`
- [ ] Specialized assertions used instead of `Assert.IsTrue` (e.g., `Assert.IsNotNull`, `Assert.AreEqual`)
- [ ] DynamicData uses ValueTuple return types instead of `IEnumerable<object[]>`
- [ ] Sync initialization done in the constructor, not `[TestInitialize]`
- [ ] `TestContext.CancellationToken` passed to async calls in tests with `[Timeout]`
- [ ] Project builds with zero errors and all tests pass

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| `Assert.AreEqual(actual, expected)` -- swapped arguments | Always put expected first: `Assert.AreEqual(expected, actual)`. Failure messages show "Expected: X, Actual: Y" so wrong order makes messages confusing |
| `[ExpectedException]` -- obsolete, cannot assert message | Use `Assert.Throws<T>` or `Assert.ThrowsExactly<T>` |
| `items.Single()` -- unclear exception on failure | Use `Assert.ContainsSingle(items)` for better failure messages |
| Hard cast `(MyType)result` -- unclear exception | Use `Assert.IsInstanceOfType<MyType>(result)` |
| `IEnumerable<object[]>` for DynamicData | Use `IEnumerable<(T1, T2, ...)>` ValueTuples for type safety |
| Sync setup in `[TestInitialize]` | Initialize in the constructor instead -- enables `readonly` fields and satisfies nullability analyzers |
| `CancellationToken.None` in async tests | Use `TestContext.CancellationToken` for cooperative timeout |
| `public TestContext? TestContext { get; set; }` | Drop the `?` -- MSTest suppresses CS8618 for this property |
| `TestContext TestContext { get; set; } = null!` | Remove `= null!` -- unnecessary, MSTest handles assignment |
| Non-sealed test classes | Seal test classes by default for performance |
