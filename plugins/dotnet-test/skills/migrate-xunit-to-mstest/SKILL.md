---
name: migrate-xunit-to-mstest
description: >
  Migrate .NET test projects from xUnit.net (v2 or v3) to MSTest v4.
  USE FOR: convert/migrate xUnit tests to MSTest, replace xunit/xunit.v3 packages,
  port [Fact]/[Theory]/[InlineData]/[MemberData]/[ClassData] to
  [TestMethod]/[DataRow]/[DynamicData], port Assert.Equal/True/Throws/ThrowsAsync
  to Assert.AreEqual/IsTrue/ThrowsExactly/ThrowsExactlyAsync, port IClassFixture/
  ICollectionFixture/IDisposable/IAsyncLifetime/ITestOutputHelper/[Trait]/[Fact(Skip)]
  to MSTest equivalents, preserve xUnit parallel-class default via
  [assembly: Parallelize(Scope = ClassLevel)], remove xunit.runner.json.
  DO NOT USE FOR: xUnit v2 -> v3 upgrade (use migrate-xunit-to-xunit-v3); MSTest ->
  xUnit, NUnit/TUnit -> MSTest (no skills exist); MSTest version upgrades (use
  migrate-mstest-v1v2-to-v3 or migrate-mstest-v3-to-v4); VSTest <-> MTP only
  (use migrate-vstest-to-mtp); general .NET upgrades.
license: MIT
---

# xUnit -> MSTest Migration

Migrate a .NET test project from xUnit.net (v2 or v3) to MSTest v4. The outcome is a project that:

- References MSTest v4 packages (or `MSTest.Sdk` 4.x) instead of `xunit*` / `xunit.v3.*`
- Has every `[Fact]`/`[Theory]` rewritten as `[TestMethod]` and every assertion mapped to the MSTest equivalent
- Builds cleanly with the same target framework
- Passes the same set of tests (modulo intentional changes documented below)
- Preserves the **current test platform** (VSTest stays on VSTest; MTP stays on MTP)

This is a **cross-framework** migration. Do not bundle it with a version upgrade or a platform switch in the same pass -- if both are needed, do this skill first, commit, then run `migrate-mstest-v3-to-v4` (if you stopped on v3) or `migrate-vstest-to-mtp`.

## When to Use

- The project references `xunit`, `xunit.assert`, `xunit.core`, `xunit.extensibility.core`/`execution`, `xunit.abstractions`, or any `xunit.v3.*` package, and you want to switch to MSTest
- You want a single .NET test framework across a solution that today mixes xUnit and MSTest

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or `.slnx` containing xUnit test projects |
| Build command | No | How to build (e.g., `dotnet build`). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |

## Response Guidelines

- **Always identify the current xUnit version first.** State whether the project is on xUnit v2 (`xunit` 2.x) or xUnit v3 (`xunit.v3` / `xunit.v3.*`) before recommending changes. This grounds the migration advice -- some breaking-change steps only apply to one version.
- **Always preserve the current test platform.** If the project runs on VSTest, keep VSTest. If it runs on MTP (e.g., xUnit v3 native MTP, or `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`), keep MTP. Recommend `migrate-vstest-to-mtp` as a separate follow-up only if the user asks for it.
- **Specific API mapping questions** (assertions, fixtures, output helper, etc.): jump to the relevant step. Do not run the full workflow.
- **Full migration requests**: follow the workflow end-to-end.
- **Focused fix requests** (specific compile error after a partial migration): address only that error using the mapping reference. Do not walk the full workflow.
- **Code samples**: show concrete before/after using the user's actual type/method names, not generic placeholders.

## Strategy

The conversion is mechanical for ~80% of code (attributes and simple assertions) and judgement-based for ~20% (collection fixtures, custom data attributes, exact-type-vs-derived exception assertions, parallelization semantics). Always do the mechanical pass first so build errors point you at the judgement areas.

## Mapping Reference

For the full attribute/assertion/fixture/lifecycle mapping tables -- including semantic traps (`Assert.Throws<T>` vs `Assert.ThrowsAny<T>`, `IClassFixture` vs `ICollectionFixture` scope), edge cases (`TheoryData<T...>`, `MemberType=`, custom `DataAttribute`, custom `FactAttribute`, `Record.Exception`), and copy-pasteable before/after snippets -- see [`references/mapping-cheatsheet.md`](references/mapping-cheatsheet.md). Load it whenever you need a specific xUnit -> MSTest equivalent.

For writing idiomatic MSTest code (modern assertion APIs, lifecycle patterns, data-driven conventions, `Assert.HasCount`/`IsEmpty`/`StartsWith`, etc.), see the `writing-mstest-tests` skill. **Do not re-derive idiomatic MSTest patterns here.** Apply this skill to *convert*; apply `writing-mstest-tests` to *polish*.

## Workflow

> **Commit strategy:** Commit after Step 2 (packages updated, builds broken), after Step 6 (attributes converted, asserts fixed), and after Step 8 (fixtures/lifecycle rewritten, tests pass). Commit before fixing follow-up cleanup so reviewers can bisect.

### Step 1: Assess the project

1. Locate every test project. Read `.csproj`, `Directory.Build.props`, `Directory.Packages.props`, and `global.json`.
2. Identify the **xUnit version**:
   - `xunit` 2.x (+ `xunit.assert` / `xunit.core` / `xunit.abstractions`) -> **xUnit v2**
   - `xunit.v3` / `xunit.v3.*` -> **xUnit v3**
3. Identify the **current test platform** (this dictates what to keep, not what to change):
   - `xunit.runner.visualstudio` (v2) or no MTP signals (v3) and no `<UseMicrosoftTestingPlatformRunner>` -> **VSTest**
   - xUnit v3 with `xunit.v3.mtp-v*` / `xunit.v3.core.mtp-v*`, or `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`, or `YTest.MTP.XUnit2` (v2 MTP shim) -> **MTP**
   - Use the `platform-detection` skill if you are unsure.
4. Verify the `TargetFramework` is supported by MSTest v4:
   - **Supported**: `net8.0`, `net9.0`, `net462`+, `netstandard2.0` (test library only), `uap10.0.16299`, `net8.0-windows10.0.18362.0` (WinUI), `net9.0-windows10.0.17763.0` (modern UWP).
   - **Unsupported**: .NET Core 3.1, `net5.0`-`net7.0`. **STOP** and ask the user to upgrade the TFM first, or migrate to MSTest v3 (then use `migrate-mstest-v3-to-v4` after a TFM bump).
5. Inventory high-risk patterns -- scan for these and flag them now so you can plan judgement steps later:
   - **Parallelization differences (Step 11)** -- xUnit parallelizes test classes by default; MSTest does not. This is the **single most common source of post-migration regressions**: tests that depended on isolation by parallel scheduling, on the lack of it, or on shared static state can pass differently. Decide the target parallelization model now -- do not leave it as the MSTest default by accident.
   - `ICollectionFixture<T>` / `[CollectionDefinition]` (scope concern -- see Step 8)
   - Custom `DataAttribute` / custom `FactAttribute` / custom `TheoryAttribute` subclasses (manual conversion to `ITestDataSource` / `TestMethodAttribute` -- see Step 5)
   - `Assert.Throws<T>` (xUnit semantics = exact type; maps to `Assert.ThrowsExactly<T>`, **not** `Assert.Throws<T>`)
   - `Record.Exception` / `Record.ExceptionAsync` (manual conversion)
   - `Assert.Raises*` / event assertions (no MSTest equivalent -- manual)
   - xUnit v3: `[assembly: CaptureConsole]` and other v3-only assembly attributes
6. **Inventory state shared between tests** -- static fields/properties, singletons, file paths, well-known ports, in-memory caches, database connection strings pointing at a single shared DB, environment variables. Whether parallelization is on or off, switching frameworks changes the *order* and *concurrency* in which these are touched. List them now so you can decide in Step 11 whether to enable parallelism, serialize specific classes with `[DoNotParallelize]`, or refactor the shared state.
7. Run a baseline build + test to record the current pass/fail count for parity check at Step 13. Re-run a second time -- if the xUnit run is **flaky** today, those flakes are almost certainly caused by parallel scheduling and will manifest differently after migration. Flag any flaky tests now.

### Step 2: Replace packages

> Choose the package option that matches what the project uses today. Default to the `MSTest` metapackage unless the user already uses `MSTest.Sdk` elsewhere in the solution. If you adopt `MSTest.Sdk`, you must preserve the platform from Step 1.

**Remove** every xUnit package reference (from `.csproj`, `Directory.Build.props`, `Directory.Packages.props`):

- `xunit`, `xunit.abstractions`, `xunit.assert`, `xunit.core`
- `xunit.extensibility.core`, `xunit.extensibility.execution`
- `xunit.runner.visualstudio`
- `xunit.v3`, `xunit.v3.assert`, `xunit.v3.core`, `xunit.v3.extensibility.core`
- `xunit.v3.mtp-v1`, `xunit.v3.mtp-v2`, `xunit.v3.core.mtp-v1`, `xunit.v3.core.mtp-v2`
- `YTest.MTP.XUnit2` (xUnit v2 MTP shim)
- Companion packages: `Xunit.SkippableFact`, `Xunit.Combinatorial`, `Xunit.StaFact` (see Step 10)

**Add** MSTest v4. Two options -- both correct.

**Option A -- `MSTest` metapackage (recommended for incremental migrations):**

```xml
<ItemGroup>
  <PackageReference Include="MSTest" Version="4.1.0" />
</ItemGroup>
```

The `MSTest` metapackage pulls in `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers`, and `Microsoft.NET.Test.Sdk` -- so VSTest discovery (`vstest.console`, classic `dotnet test`) still works.

**Option B -- `MSTest.Sdk`:**

```xml
<Project Sdk="MSTest.Sdk/4.1.0">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
```

`MSTest.Sdk` defaults to **MTP** and does *not* include `Microsoft.NET.Test.Sdk`. To preserve a VSTest project, you must opt back in:

```xml
<PropertyGroup>
  <UseVSTest>true</UseVSTest>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
</ItemGroup>
```

When switching to `MSTest.Sdk`, also remove now-redundant properties: `<OutputType>Exe</OutputType>`, `<IsPackable>false</IsPackable>`, `<IsTestProject>true</IsTestProject>`, `<EnableMSTestRunner>`.

### Step 3: Update project configuration

1. **Preserve the runner.** Confirm the platform decision from Step 1 still holds after Step 2. Common mistakes:
   - Switching to `MSTest.Sdk` without `UseVSTest=true` silently flips a VSTest project to MTP. Add `Microsoft.NET.Test.Sdk` back.
   - Leaving `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` in `Directory.Build.props` will keep MTP on -- that is correct only if the project was already on MTP.
2. Remove xUnit-only properties from `.csproj` / `Directory.Build.props`: anything referencing `xunit.runner.*`, `<CaptureConsoleOutput>`, `<UseRoslynCompilers>` set just for xUnit, etc.
3. Remove `using Xunit;` and `using Xunit.Abstractions;` from C# files (the rewriter will add `using Microsoft.VisualStudio.TestTools.UnitTesting;` instead in Step 4).

### Step 4: Convert test classes and methods

Apply these rewrites to every C# test file. Class-level first, then method-level.

**Class:**

- Add `[TestClass]` to every class that contained xUnit `[Fact]`/`[Theory]` methods (xUnit had no class-level requirement).
- Mark the class `sealed` (MSTest idiom -- see `writing-mstest-tests`).
- Replace `using Xunit;` / `using Xunit.Abstractions;` with `using Microsoft.VisualStudio.TestTools.UnitTesting;`.

**Methods:**

| xUnit | MSTest |
|---|---|
| `[Fact]` | `[TestMethod]` |
| `[Theory]` | `[TestMethod]` (parameterized; MSTest 3+ no longer needs `[DataTestMethod]`) |
| `[Fact(DisplayName = "x")]` | `[TestMethod("x")]` (v3 of MSTest) or `[TestMethod(DisplayName = "x")]` (v4) |
| `[Fact(Skip = "reason")]` | `[Ignore("reason")]` |
| `[Trait("Category", "Unit")]` | `[TestCategory("Unit")]` |
| `[Trait("Owner", "alice")]` | `[TestProperty("Owner", "alice")]` |

> Use `[TestCategory]` for the conventional category trait (it integrates with VSTest/MTP filter syntax); use `[TestProperty]` for arbitrary key/value metadata.

### Step 5: Convert data-driven tests

| xUnit | MSTest |
|---|---|
| `[InlineData(1, 2)]` | `[DataRow(1, 2)]` |
| `[InlineData(1, DisplayName = "case 1")]` | `[DataRow(1, DisplayName = "case 1")]` |
| `[MemberData(nameof(Cases))]` returning `IEnumerable<object[]>` | `[DynamicData(nameof(Cases))]` returning `IEnumerable<object[]>` |
| `[MemberData(nameof(Cases), MemberType = typeof(X))]` | `[DynamicData(nameof(Cases), typeof(X))]` |
| `[MemberData(nameof(Method), arg1, arg2)]` (parameterized member) | **Manual**: convert to a parameterless property or compute the inputs inside the test |
| `[ClassData(typeof(MyData))]` (class implementing `IEnumerable<object[]>`) | Add a static property `=> new MyData()` on the test class, then `[DynamicData(nameof(Cases))]` |
| `TheoryData<int, string>` | `IEnumerable<object[]>` or `IEnumerable<(int, string)>` (MSTest 3.7+ ValueTuple support) |
| Custom `DataAttribute` subclass | **Manual**: implement `ITestDataSource` (`GetData`, `GetDisplayName`) |

Prefer ValueTuple data sources for new MSTest tests (see `writing-mstest-tests`), but for migration keep `IEnumerable<object[]>` -- it minimizes diff churn and works in both MSTest 3 and 4.

### Step 6: Convert assertions

Most common cases inline. For the full table including string/collection/type/numeric and event/equivalence assertions, see [`references/mapping-cheatsheet.md`](references/mapping-cheatsheet.md) §3.

| xUnit | MSTest |
|---|---|
| `Assert.Equal(expected, actual)` | `Assert.AreEqual(expected, actual)` |
| `Assert.NotEqual(a, b)` | `Assert.AreNotEqual(a, b)` |
| `Assert.True(x)` / `Assert.False(x)` | `Assert.IsTrue(x)` / `Assert.IsFalse(x)` |
| `Assert.Null(x)` / `Assert.NotNull(x)` | `Assert.IsNull(x)` / `Assert.IsNotNull(x)` |
| `Assert.Same(a, b)` / `Assert.NotSame(a, b)` | `Assert.AreSame(a, b)` / `Assert.AreNotSame(a, b)` |
| `Assert.Throws<T>(() => ...)` | **`Assert.ThrowsExactly<T>(() => ...)`** (see trap below) |
| `Assert.ThrowsAny<T>(() => ...)` | **`Assert.Throws<T>(() => ...)`** |
| `await Assert.ThrowsAsync<T>(...)` | `await Assert.ThrowsExactlyAsync<T>(...)` |
| `Assert.IsType<T>(x)` / `Assert.IsAssignableFrom<T>(x)` | `Assert.IsInstanceOfType<T>(x)` (MSTest v4 returns the typed value) |
| `Assert.Empty(coll)` / `Assert.NotEmpty(coll)` | `Assert.IsEmpty(coll)` / `Assert.IsNotEmpty(coll)` |
| `Assert.Single(coll)` | `var item = Assert.ContainsSingle(coll);` |
| `Assert.Contains(item, coll)` / `Assert.DoesNotContain(...)` | Same -- `Assert.Contains` / `Assert.DoesNotContain` |
| `Assert.Contains("sub", str)` / `StartsWith` / `EndsWith` / `Matches` | Same (MSTest 3.8+) or `StringAssert.*` |
| `Assert.Skip("reason")` (v3 runtime) | `Assert.Inconclusive("reason")` |
| `Assert.SkipWhen(cond, "reason")` (v3) | `if (cond) Assert.Inconclusive("reason");` |
| `Assert.SkipUnless(cond, "reason")` (v3) | `if (!cond) Assert.Inconclusive("reason");` |

**Critical semantic trap -- exception assertions:**

- xUnit `Assert.Throws<T>` = **exact type match** -> MSTest `Assert.ThrowsExactly<T>`.
- xUnit `Assert.ThrowsAny<T>` = **derived types also match** -> MSTest `Assert.Throws<T>`.

Reversing these flips the assertion semantics silently. Verify by name, not by visual similarity.

**No-equivalent assertions** -- convert manually (see cheatsheet §3.11):

- `Assert.Collection(items, e1 => ..., e2 => ...)` -> assert count, then per-element
- `Assert.All(items, x => ...)` -> `foreach`
- `Assert.Equivalent(expected, actual)` -> deep equality manually, or a third-party library
- `Assert.Raises<T>` / `Assert.PropertyChanged` -> manual event subscription + flag check
- `Record.Exception` / `Record.ExceptionAsync` -> `try/catch` returning the exception (or `Assert.ThrowsExactly<T>` if you know the type)

### Step 7: Convert lifecycle

**Constructor / `IDisposable` / `IAsyncDisposable` / `IAsyncLifetime`:**

| xUnit | MSTest |
|---|---|
| Constructor (sync setup) | Keep constructor (MSTest also instantiates per test). Drop xUnit-only `ITestOutputHelper` param -- see Step 9 |
| `Dispose()` (sync teardown) | Keep `Dispose()` (MSTest supports `IDisposable`) **or** rewrite as `[TestCleanup] public void Cleanup() { ... }` |
| `DisposeAsync()` (async teardown) | Keep `IAsyncDisposable.DisposeAsync()` **or** rewrite as `[TestCleanup] public async Task CleanupAsync() { ... }` |
| `IAsyncLifetime.InitializeAsync` | `[TestInitialize] public async Task InitAsync() { ... }` |
| `IAsyncLifetime.DisposeAsync` | `[TestCleanup] public async Task CleanupAsync() { ... }` |

> Per `writing-mstest-tests`: prefer the constructor for sync init (it allows `readonly` fields). Use `[TestInitialize]` only for async setup or when you need `TestContext`.

### Step 8: Convert fixtures (high-risk -- read carefully)

**`IClassFixture<T>` -- class-level shared state (mechanical):**

```csharp
// xUnit v2/v3
public class DbFixture : IDisposable
{
    public string ConnectionString { get; } = "...";
    public void Dispose() { /* cleanup */ }
}

public class OrderTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;
    public OrderTests(DbFixture fixture) => _fixture = fixture;
}
```

```csharp
// MSTest equivalent
[TestClass]
public sealed class OrderTests
{
    private static DbFixture? s_fixture;

    [ClassInitialize]
    public static void ClassInit(TestContext context) => s_fixture = new DbFixture();

    [ClassCleanup]
    public static void ClassCleanup() => s_fixture?.Dispose();
}
```

**`ICollectionFixture<T>` / `[CollectionDefinition]` -- shared by tests in the same collection (judgement call):**

xUnit collections do two things simultaneously: (1) share a fixture instance across multiple test classes, and (2) serialize those classes (no parallel execution within a collection). MSTest does not have a built-in equivalent that preserves both semantics. **Pick one** -- do not silently map to `[AssemblyInitialize]`:

- **Few classes, narrow scope**: copy the fixture initialization into each class's `[ClassInitialize]`, OR introduce a static `Lazy<T>` shared helper. Add `[DoNotParallelize]` on each class to preserve serialization.
- **Many classes, fixture is genuinely assembly-wide** (e.g., process-wide TestServer): hoist to `[AssemblyInitialize]` / `[AssemblyCleanup]` in a dedicated `AssemblySetup` class **and** confirm with the user that widening the scope is acceptable. Note that this changes parallelization semantics.
- **Custom collection behavior or test-collection-orderer**: stop and flag for manual review.

**Always explain the scope change** -- silently widening fixture scope across the assembly is the most common way this migration regresses tests.

### Step 9: Convert output and TestContext

**`ITestOutputHelper` -> `TestContext`:**

```csharp
// xUnit (v2 and v3)
public class MyTests
{
    private readonly ITestOutputHelper _output;
    public MyTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Test() => _output.WriteLine("...");
}
```

```csharp
// MSTest (v3.6+ supports TestContext in constructor)
[TestClass]
public sealed class MyTests
{
    private readonly TestContext _testContext;
    public MyTests(TestContext testContext) => _testContext = testContext;

    [TestMethod]
    public void Test() => _testContext.WriteLine("...");
}
```

If the project pins MSTest < 3.6 (rare after Step 2), use property injection instead:

```csharp
public TestContext TestContext { get; set; } = null!;
```

**xUnit v3 `TestContext.Current`:**

- `TestContext.Current.CancellationToken` -> `TestContext.CancellationToken` (MSTest 3.6+, on the **instance** TestContext from constructor or property injection)
- `TestContext.Current.AddAttachment(name, path)` -> `TestContext.AddResultFile(path)`
- `TestContext.Current.TestOutputHelper.WriteLine(...)` -> `TestContext.WriteLine(...)`

### Step 10: Convert companion packages

| xUnit companion | MSTest equivalent |
|---|---|
| `Xunit.SkippableFact` (`[SkippableFact]`, `Skip.If`, `Skip.IfNot`) | `[Ignore]` (compile-time) or `Assert.Inconclusive("reason")` (runtime). Remove the package |
| `Xunit.Combinatorial` (`[CombinatorialData]`, `[CombinatorialValues]`) | No equivalent. Convert to explicit `[DataRow]`s or compute combinations with `[DynamicData]`. Remove the package |
| `Xunit.StaFact` (`[StaFact]`, `[WpfFact]`) | `[TestMethod]` + manual STA thread. No MSTest equivalent for `[WpfFact]`; flag for manual conversion |
| `Verify.Xunit` | `Verify.MSTest` -- swap the package; usage is similar |
| `FluentAssertions` / `Shouldly` / `AwesomeAssertions` | Keep -- assertion library is framework-agnostic |
| `Moq` / `NSubstitute` / `FakeItEasy` | Keep -- mocking library is framework-agnostic |

### Step 11: Handle parallelization (defaults differ -- read carefully)

> **This is the most common source of post-migration regressions.** xUnit and MSTest have **opposite defaults**. Do not skip this step even if Step 1 said tests passed cleanly.

#### How each framework parallelizes by default

| Framework | Across test classes | Within a test class | Test-class instance lifetime |
|---|---|---|---|
| **xUnit v2** | Parallel (one class per worker thread) | Serial (one test method at a time) | New instance per test method |
| **xUnit v3** | Parallel (same as v2) | Serial (same as v2) | New instance per test method |
| **MSTest (default)** | Serial (one class at a time) | Serial (one test method at a time) | New instance per test method |
| MSTest + `[assembly: Parallelize(Scope = ClassLevel)]` | Parallel | Serial | Same |
| MSTest + `[assembly: Parallelize(Scope = MethodLevel)]` | Parallel | **Parallel** -- more aggressive than xUnit | Same |

`Workers = 0` means "use all available logical cores" (MSTest's recommended default for parallel runs); any positive integer caps the worker count.

#### Pick a target model -- there are three reasonable choices

**Choice A -- Match xUnit's behaviour exactly (recommended default):**

```csharp
// Place in any .cs file at assembly scope (often AssemblyInfo.cs or GlobalUsings.cs)
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]
```

Use this when the suite was healthy on xUnit and you want zero behavioural change. It preserves "parallel across classes, serial within a class" exactly.

**Choice B -- Adopt MSTest's serial default:**

```csharp
// No [assembly: Parallelize] needed -- this is the default
```

Use this only when the suite has known shared-state issues (Step 1.6) that you intend to leave unfixed for now, or when wall-clock time is not a concern. Expect significantly slower CI.

**Choice C -- Selective parallelization:**

```csharp
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]
```

Plus per-class opt-out for the classes that genuinely cannot run concurrently:

```csharp
[TestClass]
[DoNotParallelize]
public sealed class DatabaseIntegrationTests { /* ... */ }
```

Use this when most of the suite is isolated but a few classes touch shared state (one DB, fixed ports, file system locations). This is usually the right answer when migrating from xUnit collections.

> **Do not pick `ExecutionScope.MethodLevel` to "match xUnit"** -- it parallelizes test methods *within* a single class, which xUnit never does. It is more aggressive than xUnit and will surface latent intra-class state issues.

#### Translate xUnit parallelization opt-outs

| xUnit pattern | MSTest equivalent |
|---|---|
| `[assembly: CollectionBehavior(DisableTestParallelization = true)]` | Omit `[assembly: Parallelize]` (or use Choice B above) |
| `[assembly: CollectionBehavior(MaxParallelThreads = N)]` | `[assembly: Parallelize(Workers = N, Scope = ExecutionScope.ClassLevel)]` |
| `[Collection("Db")]` on multiple classes (forces those classes to share a fixture **and** run serially) | `[DoNotParallelize]` on each of those classes (preserves serialization) + Step 8 fixture handling (preserves sharing) |
| `[CollectionDefinition("Db", DisableParallelization = true)]` | Same as above -- `[DoNotParallelize]` on each member class |
| `[Collection("Foo")]` used only for fixture sharing (no parallelization concern) | Step 8 fixture handling; **do not** add `[DoNotParallelize]` |

The distinction in the last two rows matters: xUnit collections conflate "share state" with "serialize". MSTest decouples them. Read the original `[CollectionDefinition]` carefully -- if `DisableParallelization` is `false` (or omitted), only the fixture sharing semantic needs to migrate, not the serialization.

#### Verify after Step 13

If pass/fail counts diverge from the baseline after migration, parallelization is the first place to look:

- **More failures than baseline**: tests are now running concurrently and stomping shared state. Either add `[DoNotParallelize]` to the offending classes, or fix the shared state.
- **Fewer failures than baseline** (tests previously flaky now green): probably means a race condition that xUnit's scheduling exposed is now hidden by serial execution. Note it in a follow-up issue -- do not declare victory.
- **Same count but tests take much longer**: you forgot `[assembly: Parallelize]`. Add Choice A.
- **Same count but tests take much less time and occasionally fail**: you picked `MethodLevel` instead of `ClassLevel`. Switch to `ClassLevel`.

#### Other runner config: `xunit.runner.json` migration

Delete `xunit.runner.json`. Port relevant settings:

| `xunit.runner.json` | MSTest equivalent |
|---|---|
| `"parallelizeAssembly": false` | Default in MSTest -- no action |
| `"parallelizeTestCollections": false` | Omit `[assembly: Parallelize]` (Choice B) |
| `"maxParallelThreads": N` | `[assembly: Parallelize(Workers = N, Scope = ExecutionScope.ClassLevel)]` |
| `"methodDisplay": "method"` / `"classAndMethod"` | No equivalent (MSTest always uses class + method) |
| `"diagnosticMessages": true` | Use `--diagnostic` on the CLI, or set verbosity in `.runsettings` |
| `"preEnumerateTheories": false` | No equivalent (MSTest enumerates `[DataRow]`/`[DynamicData]` eagerly) |
| `"longRunningTestSeconds": N` | Use `[Timeout(N * 1000)]` per test |
| `"appDomain": "denied"` / `"ifAvailable"` | No equivalent (MSTest uses no app domains on modern .NET) |

If the project uses xUnit traits in CI filter expressions (e.g., `--filter "Category=Unit"` with xUnit), the equivalent MSTest filter is `--filter "TestCategory=Unit"` (VSTest) or `--filter-trait "TestCategory=Unit"` (MTP). Update CI pipelines accordingly.

### Step 12: Remove xUnit assembly attributes

Delete these from `AssemblyInfo.cs` / any `[assembly: ...]` declaration:

- `[assembly: CollectionBehavior(...)]`
- `[assembly: TestCaseOrderer(...)]`
- `[assembly: TestCollectionOrderer(...)]`
- `[assembly: TestFramework(...)]`
- `[assembly: CaptureConsole]` (xUnit v3)
- `[assembly: Xunit.Trait("k", "v")]` (replace with `[TestCategory]`/`[TestProperty]` on test classes/methods)

Custom orderers/test framework hooks must be reimplemented against MSTest's extensibility model (`TestMethodAttribute` subclasses, `ITestDataSource`, etc.) -- stop and flag for manual conversion if present.

### Step 13: Build and verify parity

1. `dotnet build` -- must succeed with zero errors. Address remaining errors using the mapping reference.
2. `dotnet test` -- run with the **same** filter/runner combination as before migration.
3. **Compare pass/fail counts** to the baseline from Step 1.7. Investigate any deltas:
   - **New failures on shared-state tests** -- you enabled parallelization (Choice A/C in Step 11) and tests are now stomping each other. Add `[DoNotParallelize]` to the specific class(es), or fix the shared state.
   - **Tests previously parallel now serial (wall-clock much longer)** -- you forgot `[assembly: Parallelize]`. See Step 11 Choice A.
   - **Tests previously flaky now consistently green** -- almost certainly a race condition hidden by MSTest's serial default. Open a follow-up issue; do not declare victory.
   - Tests now skipped (`[Ignore]`) that used to run via `Assert.SkipWhen`? Convert to runtime `Assert.Inconclusive` if you want them to execute when the condition is false.
   - Theory cases dropped? Check `[DataRow]` literal types (`1` int vs `1L` long -- MSTest enforces exact match unlike xUnit).
   - Tests passing but executing 0 assertions? Likely an `Assert.Collection` or `Assert.All` was dropped -- restore manually.
4. After parity is confirmed, run the test-quality skills (`test-anti-patterns`, `assertion-quality`) to identify follow-up improvements -- e.g., replacing `Assert.IsTrue(x.Count() == 3)` with `Assert.HasCount(3, x)`.

## Validation

- [ ] No `xunit*`, `xunit.v3.*`, or `YTest.MTP.XUnit2` package references remain
- [ ] Every test class has `[TestClass]` and every test method has `[TestMethod]`
- [ ] `using Xunit;` and `using Xunit.Abstractions;` removed
- [ ] `xunit.runner.json` removed; equivalent config in `.runsettings` / `[assembly: Parallelize]`
- [ ] **Parallelization is explicit** -- either `[assembly: Parallelize(...)]` is present (Choice A/C, matches xUnit default) or the user accepted the serial default (Choice B). Not left unspecified by accident
- [ ] Project builds with zero errors
- [ ] Same number of tests discovered as before migration (-- not silently dropping data rows or skipped tests)
- [ ] Same pass/fail count as the pre-migration baseline
- [ ] Test platform unchanged (VSTest stayed VSTest, MTP stayed MTP) unless the user requested otherwise
- [ ] `TargetFramework` unchanged unless MSTest v4 forced an upgrade (and the user approved)

## Common Pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Leaving parallelization unspecified | Suite that ran in 30s on xUnit now takes minutes on MSTest; or new flakiness from inherited xUnit assumptions | Pick a target parallelization model explicitly in Step 11 (Choice A matches xUnit) -- do not leave it as the MSTest serial default by accident |
| Picking `ExecutionScope.MethodLevel` to "match xUnit" | New flakiness on tests sharing instance state within a class | Use `ExecutionScope.ClassLevel` -- it matches xUnit exactly |
| Mapping `Assert.Throws<T>` to `Assert.Throws<T>` | Tests pass for derived exception types they shouldn't | Map xUnit `Assert.Throws<T>` to MSTest `Assert.ThrowsExactly<T>` |
| Silently widening `ICollectionFixture` to assembly scope | State leak between unrelated tests; new flakiness | Step 8 -- pick scope explicitly and disclose to the user |
| `MSTest.Sdk` flipping VSTest project to MTP | `vstest.console` finds zero tests; CI breaks | Add `<UseVSTest>true</UseVSTest>` + `Microsoft.NET.Test.Sdk` package |
| `[DataRow]` type mismatch | Theory cases compile in xUnit but produce MSTest runtime errors | Use exact literal types: `1` int, `1L` long, `1.0f` float |
| `Assert.SkipUnless` becomes `[Ignore]` | Tests that *would* have run on this machine now silently skip everywhere | Use runtime `Assert.Inconclusive` instead |
| Dropping `Assert.Collection` / `Assert.All` without replacement | Test passes but verifies nothing | Restore as explicit `foreach` + per-element assertions |
| Leaving `xunit.runner.json` in the project | Build warning + dead config | Delete the file after porting settings |

## Next Steps

After this migration:

- Run `migrate-vstest-to-mtp` if you want to move to Microsoft.Testing.Platform (separate, committable migration).
- Run `writing-mstest-tests` to polish converted code: replace `Assert.IsTrue(x.Count() == 3)` with `Assert.HasCount(3, x)`, prefer ValueTuple data sources, mark classes `sealed`, etc.
- Run `test-anti-patterns` / `assertion-quality` to catch any quality regressions introduced by mechanical conversion.
