---
name: dotnet-test-frameworks
description: "Reference data for .NET test framework detection patterns, assertion APIs, skip annotations, setup/teardown methods, and common test smell indicators across MSTest, xUnit, NUnit, and TUnit. Loaded by test analysis skills (test-anti-patterns) as framework-specific lookup tables."
user-invocable: false
disable-model-invocation: true
license: MIT
---

# .NET Test Framework Reference

Language-specific detection patterns for .NET test frameworks (MSTest, xUnit, NUnit, TUnit).

## Test File Identification

| Framework | Test class markers | Test method markers |
| --------- | ------------------ | ------------------- |
| MSTest | `[TestClass]` | `[TestMethod]`, `[DataTestMethod]` |
| xUnit | *(none — convention-based)* | `[Fact]`, `[Theory]` |
| NUnit | `[TestFixture]` | `[Test]`, `[TestCase]`, `[TestCaseSource]` |
| TUnit | *(none — convention-based)* | `[Test]` |

## Assertion APIs by Framework

| Category | MSTest | xUnit | NUnit | TUnit |
| -------- | ------ | ----- | ----- | ----- |
| Equality | `Assert.AreEqual` | `Assert.Equal` | `Assert.That(x, Is.EqualTo(y))` | `await Assert.That(x).IsEqualTo(y)` |
| Boolean | `Assert.IsTrue` / `Assert.IsFalse` | `Assert.True` / `Assert.False` | `Assert.That(x, Is.True)` | `await Assert.That(x).IsTrue()` / `await Assert.That(x).IsFalse()` |
| Null | `Assert.IsNull` / `Assert.IsNotNull` | `Assert.Null` / `Assert.NotNull` | `Assert.That(x, Is.Null)` | `await Assert.That(x).IsNull()` / `await Assert.That(x).IsNotNull()` |
| Exception | `Assert.Throws<T>()` / `Assert.ThrowsExactly<T>()` | `Assert.Throws<T>()` | `Assert.That(() => ..., Throws.TypeOf<T>())` | `await Assert.That(() => ...).Throws<T>()` / `await Assert.That(() => ...).ThrowsExactly<T>()` |
| Collection | `CollectionAssert.Contains` | `Assert.Contains` | `Assert.That(col, Has.Member(x))` | `await Assert.That(col).Contains(x)` |
| String | `StringAssert.Contains` | `Assert.Contains(str, sub)` | `Assert.That(str, Does.Contain(sub))` | `await Assert.That(str).Contains(sub)` |
| Type | `Assert.IsInstanceOfType` | `Assert.IsAssignableFrom` | `Assert.That(x, Is.InstanceOf<T>())` | `await Assert.That(x).IsAssignableTo<T>()` (use `await Assert.That(x).IsTypeOf<T>()` for exact-type check) |
| Inconclusive | `Assert.Inconclusive()` | *skip via `[Fact(Skip)]`* | `Assert.Inconclusive()` | `Skip.Test("reason")` (no true inconclusive state) |
| Fail | `Assert.Fail()` | `Assert.Fail()` (.NET 10+) | `Assert.Fail()` | `Assert.Fail()` |

**TUnit-specific:** assertions are async and **must be awaited** — a forgotten `await` causes the assertion to never run, and the test passes silently. A built-in analyzer warns when `await` is missing. Multiple assertions can be combined with `.And` / `.Or` chaining or grouped via `Assert.Multiple()`.

Third-party assertion libraries: `Should*` (Shouldly), `.Should()` (FluentAssertions / AwesomeAssertions), `Verify()` (Verify). TUnit also ships an optional `TUnit.Assertions.Should` package providing FluentAssertions-style `value.Should().BeEqualTo(...)` on top of the same infrastructure.

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
| xUnit | `[Fact(Skip = "reason")]` | *(reason is required)* |
| NUnit | `[Ignore("reason")]` | *(reason is required)* |
| TUnit | `[Skip("reason")]` | *(reason is required; also valid at class and assembly scope, e.g. `[assembly: Skip("…")]`. Dynamic in-test skipping via `Skip.Test("reason")`.)* |
| Conditional | `#if false` / `#if NEVER` | *(no reason possible)* |

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

**TUnit:**

```csharp
await Assert.That(() => processor.ProcessOrder(emptyOrder))
    .Throws<InvalidOperationException>()
    .WithMessage("Order must contain at least one item");

// Or, for exact-type matching (no derived types):
await Assert.That(() => processor.ProcessOrder(emptyOrder))
    .ThrowsExactly<InvalidOperationException>();
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
- `[Category("Integration")]` (NUnit, TUnit)
- Project name ending in `.IntegrationTests` or `.E2ETests`

## Setup/Teardown Methods

| Framework | Setup | Teardown |
| --------- | ----- | -------- |
| MSTest | `[TestInitialize]` or constructor | `[TestCleanup]` or `IDisposable.Dispose` / `IAsyncDisposable.DisposeAsync` |
| xUnit | constructor | `IDisposable.Dispose` / `IAsyncDisposable.DisposeAsync` |
| NUnit | `[SetUp]` | `[TearDown]` |
| TUnit | `[Before(Test)]` or constructor | `[After(Test)]` or `IDisposable.Dispose` / `IAsyncDisposable.DisposeAsync` |
| MSTest (class) | `[ClassInitialize]` | `[ClassCleanup]` |
| NUnit (class) | `[OneTimeSetUp]` | `[OneTimeTearDown]` |
| xUnit (class) | `IClassFixture<T>` | fixture's `Dispose` |
| TUnit (class) | `[Before(Class)]` | `[After(Class)]` |
| TUnit (assembly) | `[Before(Assembly)]` | `[After(Assembly)]` |
| TUnit (session) | `[Before(TestSession)]` | `[After(TestSession)]` |

**TUnit-specific:** `[BeforeEvery(Test)]` / `[AfterEvery(Test)]` (and the `Class` / `Assembly` variants) run for every test/class/assembly across the whole test run — useful for global cross-cutting hooks. Hooks may optionally accept a context object (`TestContext`, `ClassHookContext`, etc.) and/or a `CancellationToken`.
