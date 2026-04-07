---
name: dotnet-test-frameworks
description: "Reference data for .NET test framework detection patterns, assertion APIs, skip annotations, setup/teardown methods, and common test smell indicators across MSTest, xUnit, NUnit, and TUnit. DO NOT USE directly — loaded by test analysis skills (test-anti-patterns, exp-test-smell-detection, exp-assertion-quality, exp-test-maintainability, exp-test-tagging) when they need framework-specific lookup tables."
user-invocable: false
---

# .NET Test Framework Reference

Language-specific detection patterns for .NET test frameworks (MSTest, xUnit, NUnit, TUnit).

## Test File Identification

| Framework | Test class markers | Test method markers |
| --------- | ------------------ | ------------------- |
| MSTest | `[TestClass]` | `[TestMethod]`, `[DataTestMethod]` |
| xUnit | _(none — convention-based)_ | `[Fact]`, `[Theory]` |
| NUnit | `[TestFixture]` | `[Test]`, `[TestCase]`, `[TestCaseSource]` |
| TUnit | `[ClassDataSource]` | `[Test]` |

## Assertion APIs by Framework

| Category | MSTest | xUnit | NUnit |
| -------- | ------ | ----- | ----- |
| Equality | `Assert.AreEqual` | `Assert.Equal` | `Assert.That(x, Is.EqualTo(y))` |
| Boolean | `Assert.IsTrue` / `Assert.IsFalse` | `Assert.True` / `Assert.False` | `Assert.That(x, Is.True)` |
| Null | `Assert.IsNull` / `Assert.IsNotNull` | `Assert.Null` / `Assert.NotNull` | `Assert.That(x, Is.Null)` |
| Exception | `Assert.Throws<T>()` / `Assert.ThrowsExactly<T>()` | `Assert.Throws<T>()` | `Assert.That(() => ..., Throws.TypeOf<T>())` |
| Collection | `CollectionAssert.Contains` | `Assert.Contains` | `Assert.That(col, Has.Member(x))` |
| String | `StringAssert.Contains` | `Assert.Contains(str, sub)` | `Assert.That(str, Does.Contain(sub))` |
| Type | `Assert.IsInstanceOfType` | `Assert.IsAssignableFrom` | `Assert.That(x, Is.InstanceOf<T>())` |
| Inconclusive | `Assert.Inconclusive()` | _skip via `[Fact(Skip)]`_ | `Assert.Inconclusive()` |
| Fail | `Assert.Fail()` | `Assert.Fail()` (.NET 10+) | `Assert.Fail()` |

Third-party assertion libraries: `Should*` (Shouldly), `.Should()` (FluentAssertions / AwesomeAssertions), `Verify()` (Verify).

## Sleep/Delay Patterns

| Pattern | Example |
| ------- | ------- |
| Thread sleep | `Thread.Sleep(2000)` |
| Task delay | `await Task.Delay(1000)` |
| SpinWait | `SpinWait.SpinUntil(() => condition, timeout)` |

## Skip/Ignore Annotations

| Framework | Annotation | With reason |
| --------- | ---------- | ----------- |
| MSTest | `[Ignore]` | `[Ignore("reason")]` |
| xUnit | `[Fact(Skip = "reason")]` | _(reason is required)_ |
| NUnit | `[Ignore("reason")]` | _(reason is required)_ |
| TUnit | `[Skip("reason")]` | _(reason is required)_ |
| Conditional | `#if false` / `#if NEVER` | _(no reason possible)_ |

## Exception Handling — Idiomatic Alternatives

When a test uses `try`/`catch` to verify exceptions, suggest the framework-native alternative:

**MSTest:**

```csharp
// Instead of try/catch (matches exact type):
var ex = Assert.ThrowsExactly<InvalidOperationException>(
    () => processor.ProcessOrder(emptyOrder));
Assert.AreEqual("Order must contain at least one item", ex.Message);

// Or (also matches derived types):
var ex = Assert.Throws<InvalidOperationException>(
    () => processor.ProcessOrder(emptyOrder));
Assert.AreEqual("Order must contain at least one item", ex.Message);
```

**xUnit:**

```csharp
var ex = Assert.Throws<InvalidOperationException>(
    () => processor.ProcessOrder(emptyOrder));
Assert.Equal("Order must contain at least one item", ex.Message);
```

**NUnit:**

```csharp
var ex = Assert.Throws<InvalidOperationException>(
    () => processor.ProcessOrder(emptyOrder));
Assert.That(ex.Message, Is.EqualTo("Order must contain at least one item"));
```

## Mystery Guest — Common .NET Patterns

| Smell indicator | What to look for |
| --------------- | ---------------- |
| File system | `File.ReadAllText`, `File.Exists`, `File.WriteAllBytes`, `Directory.GetFiles`, `Path.Combine` with hard-coded paths |
| Database | `SqlConnection`, `DbContext` (without in-memory provider), `SqlCommand` |
| Network | `HttpClient` without `HttpMessageHandler` override, `WebRequest`, `TcpClient` |
| Environment | `Environment.GetEnvironmentVariable`, `Environment.CurrentDirectory` |
| Acceptable | `MemoryStream`, `StringReader`, `InMemory` database providers, custom `DelegatingHandler` |

## Integration Test Markers

Recognize these as integration tests (adjust smell severity accordingly):

- Class name contains `Integration`, `E2E`, `EndToEnd`, or `Acceptance`
- `[TestCategory("Integration")]` (MSTest)
- `[Trait("Category", "Integration")]` (xUnit)
- `[Category("Integration")]` (NUnit)
- Project name ending in `.IntegrationTests` or `.E2ETests`

## Setup/Teardown Methods

| Framework | Setup | Teardown |
| --------- | ----- | -------- |
| MSTest | `[TestInitialize]` or constructor | `[TestCleanup]` or `IDisposable.Dispose` / `IAsyncDisposable.DisposeAsync` |
| xUnit | constructor | `IDisposable.Dispose` / `IAsyncDisposable.DisposeAsync` |
| NUnit | `[SetUp]` | `[TearDown]` |
| MSTest (class) | `[ClassInitialize]` | `[ClassCleanup]` |
| NUnit (class) | `[OneTimeSetUp]` | `[OneTimeTearDown]` |
| xUnit (class) | `IClassFixture<T>` | fixture's `Dispose` |
