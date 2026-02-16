---
name: writing-xunit-tests
description: Write and structure xUnit test projects for .NET applications, including unit tests, integration tests, theory data, fixtures, and mocking. Use when creating new test projects, writing test cases, or improving test coverage for .NET code.
---

# Writing xUnit Tests

## When to Use

- Creating a new test project for an existing .NET solution
- Writing unit tests or integration tests for .NET code
- Structuring test classes with fixtures and shared context
- Adding parameterized tests with `[Theory]` and data sources

## When Not to Use

- The project already uses NUnit or MSTest and the user wants to keep it
- The user needs load testing or performance benchmarks (use BenchmarkDotNet instead)
- The user wants to test non-.NET code

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Code to test | Yes | The classes, methods, or APIs to cover |
| Test project | No | Existing test project path; created if absent |

## Workflow

### Step 1: Create or verify the test project

If no test project exists:

```bash
dotnet new xunit -o tests/MyProject.Tests
dotnet sln add tests/MyProject.Tests
dotnet add tests/MyProject.Tests reference src/MyProject
```

Verify packages are current:

```bash
dotnet list tests/MyProject.Tests package
```

Ensure these packages are present: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`.

### Step 2: Write test classes

Follow the Arrange-Act-Assert pattern. One test class per class under test.

```csharp
using Xunit;

public class CalculatorTests
{
    [Fact]
    public void Add_TwoPositiveNumbers_ReturnsSum()
    {
        // Arrange
        var calculator = new Calculator();

        // Act
        var result = calculator.Add(2, 3);

        // Assert
        Assert.Equal(5, result);
    }
}
```

**Naming convention:** `MethodName_Scenario_ExpectedBehavior`

### Step 3: Add parameterized tests for multiple inputs

Use `[Theory]` with `[InlineData]` for simple cases:

```csharp
[Theory]
[InlineData(1, 2, 3)]
[InlineData(-1, 1, 0)]
[InlineData(0, 0, 0)]
public void Add_VariousInputs_ReturnsExpectedSum(int a, int b, int expected)
{
    var calculator = new Calculator();
    Assert.Equal(expected, calculator.Add(a, b));
}
```

Use `[MemberData]` for complex data:

```csharp
public static IEnumerable<object[]> ComplexTestData =>
    new List<object[]>
    {
        new object[] { new Order { Total = 100 }, 10m },
        new object[] { new Order { Total = 0 }, 0m },
    };

[Theory]
[MemberData(nameof(ComplexTestData))]
public void CalculateDiscount_ReturnsExpected(Order order, decimal expected)
{
    var service = new PricingService();
    Assert.Equal(expected, service.CalculateDiscount(order));
}
```

### Step 4: Add mocking for dependencies

Use `Moq` or `NSubstitute`. Install if missing:

```bash
dotnet add tests/MyProject.Tests package Moq
```

```csharp
using Moq;
using Xunit;

public class OrderServiceTests
{
    [Fact]
    public async Task PlaceOrder_ValidOrder_SavesAndNotifies()
    {
        // Arrange
        var mockRepo = new Mock<IOrderRepository>();
        var mockNotifier = new Mock<INotificationService>();
        var service = new OrderService(mockRepo.Object, mockNotifier.Object);
        var order = new Order { Id = 1, Total = 50 };

        // Act
        await service.PlaceOrderAsync(order);

        // Assert
        mockRepo.Verify(r => r.SaveAsync(order), Times.Once);
        mockNotifier.Verify(n => n.SendAsync(It.IsAny<string>()), Times.Once);
    }
}
```

### Step 5: Use fixtures for shared expensive setup

**Class fixture** (shared across tests in one class):

```csharp
public class DatabaseFixture : IDisposable
{
    public DatabaseFixture()
    {
        Connection = new SqlConnection("...");
        Connection.Open();
    }

    public SqlConnection Connection { get; }

    public void Dispose() => Connection.Dispose();
}

public class DatabaseTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public DatabaseTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void Query_ReturnsData()
    {
        // Use _fixture.Connection
    }
}
```

**Collection fixture** (shared across multiple test classes):

```csharp
[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

[Collection("Database")]
public class UserRepositoryTests
{
    // DatabaseFixture injected via constructor
}

[Collection("Database")]
public class OrderRepositoryTests
{
    // Same DatabaseFixture instance shared
}
```

### Step 6: Run and validate

```bash
dotnet test --verbosity normal
```

For coverage:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Validation

- [ ] `dotnet test` passes with zero failures
- [ ] Test names clearly describe the scenario
- [ ] Each public method has at least one test
- [ ] Edge cases (null, empty, boundary values) are covered
- [ ] No test depends on execution order

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Tests depend on each other | xUnit creates a new class instance per test; don't rely on shared state |
| Async tests return `void` | Always return `Task` from async tests, never `async void` |
| Hard-coded file paths in tests | Use `Path.Combine` with temp directories or embedded resources |
| Flaky time-dependent tests | Inject `TimeProvider` or `ISystemClock` instead of using `DateTime.Now` |
| Missing `[Fact]` or `[Theory]` | Tests without attributes are not discovered by the runner |
