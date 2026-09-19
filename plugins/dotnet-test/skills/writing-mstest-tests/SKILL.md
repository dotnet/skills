---
name: writing-mstest-tests
description: >
  ALWAYS USE when asked to fix, rewrite, update, improve, modernize, show
  corrected code for, or explain existing MSTest tests or MSTest-specific
  configuration. Use for "review" when corrected code or edits are wanted, even
  for one pasted assertion or passing tests with bad failure output. Covers
  expected/actual labels; generic Boolean, collection, string, numeric, null,
  identity, exception, hard-cast, and object[] checks;
  TestContext/lifecycle; timeout/cancellation; OS/CI conditions, retry, cleanup,
  parallelization, MSTest.Sdk project setup, and MSTESTxxxx. Honor the installed
  MSTest version. DO NOT USE to design new test cases (code-testing-agent),
  perform report-only audits, create project files rather than explain MSTest
  setup, run tests, migrate frameworks, or handle non-MSTest/non-.NET code.
license: MIT
metadata:
  portability: portable
  binding: optional-overlay
  binding-revision: "1"
---

# Writing MSTest Tests

Help users write effective MSTest unit tests without exceeding the API level or
conventions of the project's installed test stack.

## Repository overlay

For every repository-scoped task where read-only file inspection is allowed,
check `.agents/skill-overlays/dotnet-test/writing-mstest-tests.md` at the
repository root before any other discovery. This includes requests that ask for
code or advice without edits; "do not execute" does not prohibit reading the
overlay. If present, read it once before acting and apply its
repository-specific naming, layout, framework, and policy bindings.
Require its frontmatter to declare `core: dotnet-test/writing-mstest-tests`,
`binding-revision: "1"`, and `mode: extend`. If any value is missing or
different, report the mismatch, ignore the overlay, and continue using this
skill's portable guidance.
Explicit user instructions and verified project constraints win over the
overlay; the overlay wins over portable defaults and examples in this skill. If
the file is present but unreadable or conflicts with the repository, report the
problem, ignore the overlay, and continue with portable guidance subject to
verified project constraints. If it is absent, continue normally.
Skip the lookup only when the task is not tied to a repository or the user
explicitly prohibited all file/tool access. An overlay cannot expand tool
permissions or the task's scope.

## When to Use

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
- **Generate new tests from scratch**: Hand off to `code-testing-agent`; use this
  skill only as supporting MSTest API/version guidance.
- **Review and fix existing tests**: Fix only the issues present. Do not add unrelated improvements.
- **Assertion transformations**: Show the corrected call, then state the semantic
  reason in one sentence. For `Assert.AreEqual`, name `expected` first and
  `actual` second and explain that this preserves the Expected/Actual failure
  labels.
- **Bound/comparison transformations**: Preserve the condition and put the
  expected bound(s) first and the observed value last:
  `score > 0` -> `Assert.IsGreaterThan(0, score)`,
  `score < 100` -> `Assert.IsLessThan(100, score)`, and
  `score >= 60 && score <= 90` -> `Assert.IsInRange(60, 90, score)`.
  Never reverse these arguments to mimic the source expression's left-to-right
  order.
- **Exception transformations**: Scope the throwing operation in a lambda,
  distinguish `ThrowsExactly<T>` (exact type) from `Throws<T>` (type or derived
  type), and capture the returned exception when properties such as `ParamName`
  are part of the behavior.
- **Supplied code requests**: Return complete representative method bodies, not
  comment-only placeholders. Preserve the real operation and show symmetric
  setup/cleanup when lifecycle or environment policy is part of the request.

## Workflow

### Step 1: Determine project setup

Check the test project, `packages.config`, and assembly reference `HintPath`
values for the exact MSTest version and project system:

- If using `MSTest.Sdk`: resolve its exact version from the project SDK
  declaration or `global.json` `msbuild-sdks`; do not assume the latest APIs
- If using `MSTest` metapackage: resolve its exact package version
- If using `MSTest.TestFramework` + `MSTest.TestAdapter`: check version for feature availability
- If using classic non-SDK XML (`ToolsVersion`, `Microsoft.CSharp.targets`,
  explicit `<Compile Include>`) and/or `packages.config`: preserve that project
  system and add each new test file to `<Compile Include>`.

Also inspect representative tests for custom base fixtures, helper libraries,
mock syntax, naming, setup, and data builders. Existing conventions and installed
versions win over the examples below. Do not upgrade MSTest, Moq, NBuilder, or
the project format unless the user explicitly asks for a migration.

### MSTest API availability

| API/pattern | Minimum version | Compatible fallback |
|---|---:|---|
| `Assert.ThrowsExactly*`, unified `Assert.Contains` / `HasCount` / `IsEmpty` / `IsNotEmpty` | 3.8 | `Assert.ThrowsException*`, `CollectionAssert`, `StringAssert` |
| `Assert.IsGreaterThan`, `IsLessThan`, `IsInRange`, `StartsWith`, `EndsWith`, `MatchesRegex` | 3.10 | `Assert.IsTrue` with a clear message, or `StringAssert` |
| Generic `Assert.IsInstanceOfType<T>(value, out var typed)` | 3.4-3.11 only | Non-generic assertion then post-assert cast on 3.0-3.3; v4 returns the typed value directly |
| ValueTuple `DynamicData` | 3.7 | `IEnumerable<object[]>` |
| Constructor injection of `TestContext` | 3.6 | Instance `TestContext` property |
| `[Retry]`, `[OSCondition]` | 3.8 | No built-in retry/OS condition; fix flakiness or retain the existing condition mechanism |
| `[CICondition]` | 3.10 | Existing project-specific condition mechanism |

For example, MSTest 3.5.x must not receive `Assert.ThrowsExactly`,
`Assert.Contains`, ValueTuple `DynamicData`, or constructor-injected
`TestContext`.

Treat this as a hard gate: after determining the version, do not copy a later
example from this skill unless its minimum version is satisfied.

Recommend MSTest.Sdk or the MSTest metapackage only for genuinely new projects:

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

Apply these structural conventions only where they do not conflict with the
suite's established base classes and lifecycle:

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

### Step 3: Use version-compatible assertion APIs

Pick the most specific assertion supported by the installed MSTest version.
More specific assertions produce better failure messages and make the test's
intent clear, but uncompilable "modern" assertions are worse than compatible
`StringAssert`, `CollectionAssert`, or `Assert.IsTrue` calls.

| What you are testing | Assertion |
|---|---|
| Two values are equal | `Assert.AreEqual(expected, actual)` |
| Same object instance (reference identity) | `Assert.AreSame(expected, actual)` |
| Value is null | `Assert.IsNull(value)` |
| Value is not null | `Assert.IsNotNull(value)` |
| Collection is empty | `Assert.IsEmpty(collection)` (3.8+) or `CollectionAssert` / count assertion |
| Collection is not empty | `Assert.IsNotEmpty(collection)` (3.8+) or count assertion |
| Collection has exactly N items | `Assert.HasCount(N, collection)` (3.8+) or `Assert.AreEqual` on count |
| Collection contains an item | `Assert.Contains(item, collection)` (3.8+) or `CollectionAssert.Contains` |
| Collection does not contain an item | `Assert.DoesNotContain(item, collection)` (3.8+) or `CollectionAssert.DoesNotContain` |
| Object is a specific type | `Assert.IsInstanceOfType<T>(value)` |
| Code throws an exception | `Assert.ThrowsExactly<T>` (3.8+) or `Assert.ThrowsException<T>` (earlier) |

On MSTest 3.8+, prefer `Assert` class methods over `StringAssert` or
`CollectionAssert` where both exist. Older versions should keep the compatible
specialized classes.
When several independent collection properties were requested, keep each
semantic check explicit even if another assertion happens to imply it. For
example, retain `IsNotEmpty` when the requested diagnostics distinguish
empty/non-empty, then use `HasCount` and `ContainsSingle` for their separate
cardinality guarantees.

#### Equality, null, and reference checks

```csharp
Assert.AreEqual(expected, actual);      // Value equality
Assert.AreSame(expected, actual);       // Reference equality -- same object instance
Assert.IsNull(value);
Assert.IsNotNull(value);
```

#### Exception testing

```csharp
// MSTest 3.8+
var ex = Assert.ThrowsExactly<ArgumentNullException>(() => service.Process(null));
Assert.AreEqual("input", ex.ParamName);

// Async
var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
    async () => await service.ProcessAsync(null));
```

- `Assert.Throws<T>` matches `T` or any derived type
- `Assert.ThrowsExactly<T>` matches only the exact type `T`

On MSTest 3.7 and earlier, use the compatible API:

```csharp
var ex = Assert.ThrowsException<ArgumentNullException>(
    () => service.Process(null));
```

#### Collection assertions

```csharp
// MSTest 3.8+
Assert.Contains(expectedItem, collection);
Assert.DoesNotContain(unexpectedItem, collection);
var single = Assert.ContainsSingle(collection);  // Returns the single element
Assert.HasCount(3, collection);
Assert.IsEmpty(collection);
Assert.IsNotEmpty(collection);
```

On earlier versions use `CollectionAssert.Contains`,
`CollectionAssert.DoesNotContain`, and `Assert.AreEqual(expectedCount,
collection.Count)`.

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
// MSTest 3.10+
Assert.Contains("expected", actualString);
Assert.StartsWith("prefix", actualString);
Assert.EndsWith("suffix", actualString);
Assert.MatchesRegex(@"\d{3}-\d{4}", phoneNumber);
```

On earlier versions use `StringAssert.Contains`, `StringAssert.StartsWith`,
`StringAssert.EndsWith`, and `StringAssert.Matches`.

#### Type assertions

MSTest 3.x is not one API level. Pick the form supported by the installed
minor version:

```csharp
// MSTest 3.0-3.3
Assert.IsInstanceOfType(result, typeof(MyHandler));
var typed = (MyHandler)result; // Safe because the assertion stops a mismatch.
```

```csharp
// MSTest 3.4-3.11 -- out parameter
Assert.IsInstanceOfType<MyHandler>(result, out var typed);
typed.Handle();
```

```csharp
// MSTest 4.x -- returns the proven value directly
var typed = Assert.IsInstanceOfType<MyHandler>(result);
```

#### Comparison assertions

```csharp
Assert.IsGreaterThan(lowerBound, actual);
Assert.IsLessThan(upperBound, actual);
Assert.IsInRange(low, high, actual);
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

On MSTest 3.7+, prefer `ValueTuple` return types over
`IEnumerable<object[]>` for type safety. Keep `IEnumerable<object[]>` on older
versions.

Tuple element names document which position maps to which test parameter, and
tuple element types catch incompatible values at compile time. They do **not**
make `DynamicData` position-independent, and swapping two same-typed elements
can still compile. Do not claim otherwise. When rows need custom display names
or metadata rather than only typed positional data, use `TestDataRow<T>` on
MSTest 3.8+.

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

When you need metadata per test case on MSTest 3.8+, use `TestDataRow<T>`:

```csharp
public static IEnumerable<TestDataRow<(decimal price, int percent, decimal expected)>> DiscountTestDataWithMetadata =>
[
    new((100m, 10, 90m)) { DisplayName = "10% discount" },
    new((200m, 25, 150m)) { DisplayName = "25% discount" },
    new((50m, 0, 50m)) { DisplayName = "No discount" },
];
```

### Step 5: Handle test lifecycle correctly

- Prefer constructor initialization when the existing suite supports it; retain a shared `FixtureBase<TSut>` or established `[TestInitialize]` lifecycle rather than rewriting the fixture architecture incidentally.
- Use `[TestInitialize]` **only** for async initialization, combined with the constructor for sync parts
- Use `[TestCleanup]` for cleanup that must run even on failure
- Inject `TestContext` via constructor only on MSTest 3.6+; otherwise use the instance property.

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

Use `TestContext.CancellationToken` with
`[Timeout(milliseconds, CooperativeCancellation = true)]` when the installed
MSTest version exposes the token directly (3.11+). On MSTest 3.6.4-3.10, use
`TestContext.CancellationTokenSource.Token` with cooperative cancellation
instead. A plain `[Timeout]` does not establish that the framework token will
stop in-flight work. On older versions, use a test-owned
`CancellationTokenSource` where cancellation itself is under test.

```csharp
// MSTest 3.11+
[TestMethod]
[Timeout(5000, CooperativeCancellation = true)]
public async Task FetchData_ReturnsWithinTimeout()
{
    var result = await _client.GetDataAsync(_testContext.CancellationToken);
    Assert.IsNotNull(result);
}
```

### Step 7: Use advanced features where appropriate

#### Retry flaky tests (MSTest 3.8+)

Use only for genuinely flaky external dependencies (network, file system), not to paper over race conditions or shared state issues.
For an external service, use bounded attempts plus a nonzero delay/backoff so
the retry policy does not immediately hammer the same dependency:

```csharp
[TestMethod]
[Retry(
    3,
    MillisecondsDelayBetweenRetries = 1_000,
    BackoffType = DelayBackoffType.Exponential)]
public async Task ExternalService_EventuallyResponds()
{
    var response = await WeatherClient.GetAsync();
    Assert.IsNotNull(response);
}
```

#### Conditional execution

`OSCondition` requires MSTest 3.8+; `CICondition` requires MSTest 3.10+.

```csharp
[TestMethod]
[OSCondition(OperatingSystems.Windows)]
public void WindowsRegistry_ReadsValue() { }

[TestMethod]
[CICondition(ConditionMode.Exclude)]
public void LocalOnly_InteractiveTest() { }
```

Attributes replace environment branches in test bodies; they do not replace
the operation being tested. When correcting supplied code, retain the real
registry/GPU/service operation and concrete resource cleanup rather than
returning empty methods or comment-only placeholders.
Show cleanup state initialized safely and released symmetrically (including a
null guard when setup can fail). A policy-only sketch that omits the operation,
assertion, or cleanup body is incomplete.

#### Parallelization

```csharp
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]

[TestClass]
[DoNotParallelize]  // Opt out specific classes
public sealed class DatabaseIntegrationTests { }
```

### Step 8: Fix MSTest analyzer diagnostics (MSTESTxxxx)

The `MSTest.Analyzers` package reports `MSTESTxxxx` diagnostics during build and in the IDE. The analyzers come in automatically with the modern `MSTest` metapackage and `MSTest.Sdk` (and are bundled with `MSTest.TestFramework` 3.7+); for other setups, reference `MSTest.Analyzers` explicitly only when the user asks to adopt analyzers. Most rules have an automated code fix (light bulb) in Visual Studio. When fixing one by hand, apply the idiomatic, version-compatible change below rather than suppressing the rule.

When asked to "fix MSTESTxxxx", look it up in the table of common diagnostics below, apply the fix, and rebuild to confirm the diagnostic is gone. The table is not exhaustive — for any rule it does not list, consult the full reference and apply the documented guidance: <https://learn.microsoft.com/dotnet/core/testing/mstest-analyzers/overview>.

#### Common diagnostics and their fixes

| Rule | Problem | Fix |
|---|---|---|
| MSTEST0006 | `[ExpectedException]` used | On 3.8+, replace with `Assert.Throws<T>` / `Assert.ThrowsExactly<T>`; otherwise use `Assert.ThrowsException<T>` |
| MSTEST0017 | `Assert.AreEqual` args swapped | Put `expected` first, `actual` second |
| MSTEST0023 | Negated boolean assertion (`Assert.IsTrue(!x)`) | Use `Assert.IsFalse(x)` |
| MSTEST0025 | Always-false condition asserted | Use `Assert.Fail("reason")` |
| MSTEST0032 | Always-true assert condition | Remove or correct the assertion |
| MSTEST0037 | Sub-optimal assert (`IsTrue(x == null)`) | Use the specific assert (`Assert.IsNull`, `HasCount`, etc.) (Step 3) |
| MSTEST0038 | `Assert.AreSame` on value types | Use `Assert.AreEqual` (value types box to distinct references) |
| MSTEST0039 | Legacy `Assert.ThrowsException` | On 3.8+, use `Assert.Throws` / `Assert.ThrowsExactly` (+ `Async` variants) |
| MSTEST0044 | `[DataTestMethod]` used | Replace with `[TestMethod]` only on a version where it supports data rows |
| MSTEST0046 | `StringAssert` used | On 3.10+, use the equivalent `Assert` method (`Assert.Contains`, `StartsWith`, ...) |
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

### Step 9: Verify file-backed corrections

When the user asked for repository edits and did not prohibit execution, run the
narrowest affected `dotnet test` command after editing. A successful process with
no discovered-test count is not verification. Require the intended test cases to
be discovered and pass.

If compilation exposes a directly coupled source issue that prevents the
corrected existing suite from running (for example, a missing namespace import
in the supplied production file), make only that minimum fix and rerun. Do not
upgrade packages or broaden the modernization. Report the actual test count and
the fixes made; never present unrun or output-free tests as passing.
In the final handoff, map every requested modernization to the exact corrected
construct and cite the passing test command. Do not rely on a generic "modernized"
summary when expected/actual order, exact type checks, data discovery, or class
shape were explicit requirements.
