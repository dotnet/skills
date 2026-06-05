# .NET Test Frameworks Reference (MSTest, xUnit, NUnit, TUnit)

Reference data for analyzing .NET test code. Used by the polyglot test analysis skills (`assertion-quality`, `test-anti-patterns`, `test-gap-analysis`, `test-smell-detection`, `test-tagging`).

> See also: the standalone `dotnet-test-frameworks` skill, which carries the same data and is loaded by .NET-only skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — markers and conventions are well-defined |
| Assertion detection | Strong — framework-specific APIs plus FluentAssertions/Shouldly/Verify |
| Sleep/delay detection | Strong |
| Skip/ignore detection | Strong |
| Setup/teardown detection | Strong |
| Tag support | **auto-edit** — `[TestCategory]`, `[Trait]`, `[Category]`, `[Property]` |

## Test File Identification

| Framework | Test class markers | Test method markers |
|-----------|-------------------|---------------------|
| MSTest | `[TestClass]` | `[TestMethod]`, `[DataTestMethod]` |
| xUnit | *(none — convention-based)* | `[Fact]`, `[Theory]` |
| NUnit | `[TestFixture]` | `[Test]`, `[TestCase]`, `[TestCaseSource]` |
| TUnit | *(none — convention-based)* | `[Test]` |

## Assertion APIs by Framework

| Category | MSTest | xUnit | NUnit | TUnit |
|----------|--------|-------|-------|-------|
| Equality | `Assert.AreEqual` | `Assert.Equal` | `Assert.That(x, Is.EqualTo(y))` | `await Assert.That(x).IsEqualTo(y)` |
| Boolean | `Assert.IsTrue` / `Assert.IsFalse` | `Assert.True` / `Assert.False` | `Assert.That(x, Is.True)` | `await Assert.That(x).IsTrue()` |
| Null | `Assert.IsNull` / `Assert.IsNotNull` | `Assert.Null` / `Assert.NotNull` | `Assert.That(x, Is.Null)` | `await Assert.That(x).IsNull()` |
| Exception | `Assert.Throws<T>()` / `Assert.ThrowsExactly<T>()` | `Assert.Throws<T>()` | `Assert.That(() => ..., Throws.TypeOf<T>())` | `await Assert.That(() => ...).Throws<T>()` |
| Collection | `CollectionAssert.Contains` | `Assert.Contains` | `Assert.That(col, Has.Member(x))` | `await Assert.That(col).Contains(x)` |
| String | `StringAssert.Contains` | `Assert.Contains(str, sub)` | `Assert.That(str, Does.Contain(sub))` | `await Assert.That(str).Contains(sub)` |
| Type | `Assert.IsInstanceOfType` | `Assert.IsAssignableFrom` | `Assert.That(x, Is.InstanceOf<T>())` | `await Assert.That(x).IsAssignableTo<T>()` |
| Inconclusive | `Assert.Inconclusive()` | `[Fact(Skip)]` | `Assert.Inconclusive()` | `Skip.Test("reason")` |
| Fail | `Assert.Fail()` | `Assert.Fail()` (.NET 10+) | `Assert.Fail()` | `Assert.Fail()` |

**TUnit-specific:** assertions are async and must be awaited — a forgotten `await` causes the assertion to never run and the test to pass silently. Multiple assertions chainable via `.And` / `.Or` or grouped via `Assert.Multiple()`.

Third-party assertion libraries: `Should*` (Shouldly), `.Should()` (FluentAssertions / AwesomeAssertions), `Verify()` (Verify). TUnit also ships `TUnit.Assertions.Should`.

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| Thread sleep | `Thread.Sleep(2000)` |
| Task delay | `await Task.Delay(1000)` |
| SpinWait | `SpinWait.SpinUntil(() => condition, timeout)` |

## Skip/Ignore Annotations

| Framework | Annotation | With reason |
|-----------|------------|-------------|
| MSTest | `[Ignore]` | `[Ignore("reason")]` |
| xUnit | `[Fact(Skip = "reason")]` | *(reason required)* |
| NUnit | `[Ignore("reason")]` | *(reason required)* |
| TUnit | `[Skip("reason")]` | *(reason required; valid at class/assembly scope; dynamic via `Skip.Test("reason")`)* |
| Conditional | `#if false` / `#if NEVER` | *(no reason)* |

## Exception Handling — Idiomatic Alternatives

When a test uses `try`/`catch` to verify exceptions, prefer the framework-native form:

```csharp
// MSTest (exact type):
var ex = Assert.ThrowsExactly<InvalidOperationException>(() => sut.Do());
Assert.AreEqual("expected message", ex.Message);

// xUnit:
var ex = Assert.Throws<InvalidOperationException>(() => sut.Do());

// NUnit:
var ex = Assert.Throws<InvalidOperationException>(() => sut.Do());

// TUnit:
await Assert.That(() => sut.Do()).Throws<InvalidOperationException>();
```

## Mystery Guest — Common .NET Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `File.ReadAllText`, `File.Exists`, `Directory.GetFiles`, `Path.Combine` with hard-coded paths |
| Database | `SqlConnection`, `DbContext` (without in-memory provider), `SqlCommand` |
| Network | `HttpClient` without `HttpMessageHandler` override, `WebRequest`, `TcpClient` |
| Environment | `Environment.GetEnvironmentVariable`, `Environment.CurrentDirectory` |
| Acceptable | `MemoryStream`, `StringReader`, in-memory database providers, custom `DelegatingHandler` |

## Integration Test Markers

Recognize these as integration tests (adjust smell severity accordingly):

- Class name contains `Integration`, `E2E`, `EndToEnd`, or `Acceptance`
- `[TestCategory("Integration")]` (MSTest)
- `[Trait("Category", "Integration")]` (xUnit)
- `[Category("Integration")]` (NUnit, TUnit)
- Project name ending in `.IntegrationTests` or `.E2ETests`

## Setup/Teardown Methods

| Framework | Setup | Teardown |
|-----------|-------|----------|
| MSTest | `[TestInitialize]` or constructor | `[TestCleanup]` or `IDisposable.Dispose` |
| xUnit | constructor | `IDisposable.Dispose` / `IAsyncDisposable.DisposeAsync` |
| NUnit | `[SetUp]` | `[TearDown]` |
| TUnit | `[Before(Test)]` or constructor | `[After(Test)]` or `IDisposable.Dispose` |
| MSTest (class) | `[ClassInitialize]` | `[ClassCleanup]` |
| NUnit (class) | `[OneTimeSetUp]` | `[OneTimeTearDown]` |
| xUnit (class) | `IClassFixture<T>` | fixture's `Dispose` |
| TUnit (class) | `[Before(Class)]` | `[After(Class)]` |

## Tag/Trait Attributes (for `test-tagging`)

| Framework | Existing Attribute | Example |
|-----------|--------------------|---------|
| MSTest | `[TestCategory("...")]` | `[TestCategory("positive")]` |
| xUnit | `[Trait("Category", "...")]` | `[Trait("Category", "positive")]` |
| NUnit | `[Category("...")]` | `[Category("positive")]` |
| TUnit | `[Category("...")]` or `[Property("Category", "...")]` | `[Category("positive")]` |

Place trait attributes on the line directly above or below the existing test attribute. Multiple traits on the same test are allowed.

## Language-specific calibration notes

- **Sealed test classes (MSTest 4)** that lock down class layout are intentional, not a smell.
- **xUnit per-test instances** mean fields initialized in the constructor are reset between tests — General Fixture (over-broad setup) detection should still flag fields used by < 50% of tests.
- **TUnit's `await` requirement** is itself a fertile source of assertion-free smells; flag any TUnit assertion line that lacks `await` as a critical anti-pattern.
- **Data-driven tests** (`[DataRow]`, `[Theory]/[InlineData]`, `[TestCase]`, `[Arguments]`) are *not* duplicate tests; treat them as the consolidated form.
