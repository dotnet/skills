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
- **Explicitly communicate every judgement-call decision** before applying it -- otherwise the user cannot tell what changed semantically. In particular:
  - **Fixture scope changes** (Step 8): state the source scope (class / collection / assembly) and the target scope you chose, plus what gets shared and what gets serialized. A silent widening from collection to assembly is the most common way this migration regresses tests.
  - **Parallelization** (Step 11): state that **MSTest defaults to serial execution** (xUnit parallelizes classes by default), so an explicit `[assembly: Parallelize(...)]` is **required** to match xUnit's behaviour -- omitting it silently halves CI throughput.
  - **`Assert.Throws<T>` -> `Assert.ThrowsExactly<T>`** (Step 6): mention the exact-type-vs-any-derived semantic flip so reviewers know the assertion was deliberately renamed, not just translated.
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
3. Identify the **current test platform** (this dictates what to keep, not what to change) by invoking the `platform-detection` skill. The xUnit/MTP matrix is nuanced -- xunit.v3 inside Test Explorer is MTP by default unless opted out, while xunit.v3 inside `dotnet test` depends on the `xunit.v3.mtp-v*` packages -- so do not try to inline a shortcut here. Quick signals to feed into that skill: `xunit.runner.visualstudio` (v2) usually means VSTest; `xunit.v3.mtp-v*` / `xunit.v3.core.mtp-v*` packages or `YTest.MTP.XUnit2` (v2 MTP shim) usually mean MTP. `<UseMicrosoftTestingPlatformRunner>` only affects `dotnet run` and is **not** a reliable VSTest-vs-MTP signal on its own.
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

> Choose the package option that matches what the project uses today. **When the user says "preserve VSTest" -- or the existing project uses explicit `PackageReference`s -- default to Option A (`MSTest` metapackage).** Reach for Option B (`MSTest.Sdk`) only when the user explicitly asks to modernize the SDK or already uses `MSTest.Sdk` elsewhere in the solution; if you adopt it, you must preserve the platform from Step 1.

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

> **MTP code-coverage caveat for Option A:** `Microsoft.NET.Test.Sdk` pulls VSTest's `Microsoft.CodeCoverage` transitively. If the project from Step 1 is on **MTP** and uses code coverage, that transitive dependency can interfere with MTP's collector (`Microsoft.Testing.Extensions.CodeCoverage`). Prefer **Option B** (`MSTest.Sdk` without `UseVSTest`) for MTP projects -- the SDK omits `Microsoft.NET.Test.Sdk` and wires the MTP coverage collector instead. If you must stay on Option A for an MTP project, verify coverage works on a representative test run before merging.

**Option B -- `MSTest.Sdk`:**

```xml
<Project Sdk="MSTest.Sdk/4.1.0">
  <PropertyGroup>
    <!-- Keep the project's existing TargetFramework -- do NOT retarget during migration. -->
    <TargetFramework>$(ExistingTargetFramework)</TargetFramework>
  </PropertyGroup>
</Project>
```

`MSTest.Sdk` defaults to **MTP**. To preserve a VSTest project, opt back in with `<UseVSTest>true</UseVSTest>` -- the SDK then pulls in `Microsoft.NET.Test.Sdk` automatically (no extra `PackageReference` needed):

```xml
<PropertyGroup>
  <UseVSTest>true</UseVSTest>
</PropertyGroup>
```

For solutions with several test projects, prefer pinning the `MSTest.Sdk` version in `global.json` so it lives in one place:

```json
{
  "msbuild-sdks": {
    "MSTest.Sdk": "4.1.0"
  }
}
```

With the pin in `global.json`, the project line simplifies to `<Project Sdk="MSTest.Sdk">`.

When switching to `MSTest.Sdk`, also remove now-redundant properties: `<OutputType>Exe</OutputType>`, `<IsPackable>false</IsPackable>`, `<IsTestProject>true</IsTestProject>`, `<EnableMSTestRunner>`.

`MSTest.Sdk` also adds `Microsoft.VisualStudio.TestTools.UnitTesting` as an **implicit global using**. Do **not** add `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />` to the project (it's noise) and skip the per-file `using Microsoft.VisualStudio.TestTools.UnitTesting;` in Step 4 -- you only need it for projects on Option A (the `MSTest` metapackage).

### Step 3: Update project configuration

1. **Preserve the runner.** Confirm the platform decision from Step 1 still holds after Step 2. Common mistakes:
   - Switching to `MSTest.Sdk` without `UseVSTest=true` silently flips a VSTest project to MTP. Add `<UseVSTest>true</UseVSTest>` to the project (the SDK pulls in `Microsoft.NET.Test.Sdk` automatically -- no manual `PackageReference` needed).
   - `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` only affects the `dotnet run` entry point and is **not** a runner switch in Test Explorer or `dotnet test`. Do not infer the platform from this property in either direction -- defer to the `platform-detection` skill (see Step 1).
2. Delete `xunit.runner.json` and port any settings you need (parallelization, `[CollectionBehavior]`, `appDomain`) per Step 11's "xunit.runner.json -> MSTest" sub-table. The settings have no direct MSBuild-property mapping.
3. Remove `using Xunit;` and `using Xunit.Abstractions;` from C# files. For **Option A** (`MSTest` metapackage), add `using Microsoft.VisualStudio.TestTools.UnitTesting;` per file (Step 4 covers this alongside the other rewrites). For **Option B** (`MSTest.Sdk`), skip the per-file using -- the SDK provides it as an implicit global using.

### Step 4: Convert test classes and methods

Apply these rewrites to every C# test file. Class-level first, then method-level.

**Class:**

- Add `[TestClass]` to every class that contained xUnit `[Fact]`/`[Theory]` methods (xUnit had no class-level requirement).
- **Preserve the original class hierarchy.** xUnit projects often use base/derived test classes (shared setup, helper assertions, generic base fixtures); marking classes `sealed` would break that pattern. Sealing is an optional follow-up handled by `writing-mstest-tests`, not part of the mechanical migration.
- Replace `using Xunit;` / `using Xunit.Abstractions;` with `using Microsoft.VisualStudio.TestTools.UnitTesting;`. **On Option B (`MSTest.Sdk`), skip adding the MSTest using** -- the SDK provides it as an implicit global using, so just remove the `using Xunit;` / `using Xunit.Abstractions;` lines (Step 2 and Step 3 cover this).

**Methods:**

> **`[Ignore]` and `[Timeout]` are modifiers, not discovery attributes.** Always emit `[TestMethod]` *alongside* them -- a method with `[Ignore]` but no `[TestMethod]` is silently skipped by the test runner (no error, no skip count). Same for `[Timeout]`.

| xUnit | MSTest |
|---|---|
| `[Fact]` | `[TestMethod]` |
| `[Theory]` | `[TestMethod]` (parameterized; MSTest 3+ no longer needs `[DataTestMethod]`) |
| `[Fact(DisplayName = "x")]` | `[TestMethod("x")]` (v3 of MSTest) or `[TestMethod(DisplayName = "x")]` (v4) |
| `[Fact(Skip = "reason")]` | `[TestMethod]` + `[Ignore("reason")]` (both attributes required) |
| `[Fact(Timeout = 5000)]` | `[TestMethod]` + `[Timeout(5000)]` (both attributes required) |
| `[Trait("Category", "Unit")]` | `[TestCategory("Unit")]` |
| `[Trait("Owner", "alice")]` | `[TestProperty("Owner", "alice")]` |

> Both `[TestCategory]` and `[TestProperty]` are filterable at runtime (`--filter "TestCategory=Unit"` / `--filter "Owner=alice"`). `[TestCategory]` targets `Assembly`, `Class`, and `Method`, so an xUnit `[assembly: Trait("Category", ...)]` keeps its assembly scope under MSTest as `[assembly: TestCategory(...)]`. **`[TestProperty]` targets only `Class` and `Method`** — there is no `AttributeTargets.Assembly`, so an assembly-level xUnit trait with an arbitrary key must collapse to `[assembly: TestCategory(...)]` (or be pushed down to every class). Use `[TestCategory]` for the conventional category trait; use `[TestProperty]` for arbitrary key/value metadata at class/method scope. For environmental skips (OS-specific, CI-only), MSTest 3.10+'s `[OSCondition]` / `[CICondition]` are usually a better fit than overloading a trait -- see Step 6 / cheatsheet §3.9.

### Step 5: Convert data-driven tests

| xUnit | MSTest |
|---|---|
| `[InlineData(1, 2)]` | `[DataRow(1, 2)]` |
| `[InlineData(1, DisplayName = "case 1")]` | `[DataRow(1, DisplayName = "case 1")]` |
| `[MemberData(nameof(Cases))]` returning `IEnumerable<object[]>` | `[DynamicData(nameof(Cases))]` returning `IEnumerable<object[]>` |
| `[MemberData(nameof(Cases), MemberType = typeof(X))]` | `[DynamicData(nameof(Cases), typeof(X))]` |
| `[MemberData(nameof(Method), arg1, arg2)]` (parameterized member) | **Manual**: convert to a parameterless property or compute the inputs inside the test |
| `[ClassData(typeof(MyData))]` (class implementing `IEnumerable<object[]>`) | Add a static property `=> new MyData()` on the test class, then `[DynamicData(nameof(Cases))]` |
| `TheoryData<int, string>` | `IEnumerable<object[]>`, `IEnumerable<(int, string)>` (MSTest 3.7+ ValueTuple), or `IEnumerable<TestDataRow<(int, string)>>` (strongly-typed with per-row metadata) |
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
| `Assert.IsType<T>(x)` (exact-type check, returns `T`) | `Assert.IsExactInstanceOfType<T>(x)` (MSTest 4.1+, returns `T`) -- **not** `Assert.IsInstanceOfType<T>`, which is assignable/is-a and silently weakens the assertion |
| `Assert.IsNotType<T>(x)` (exact-type check) | `Assert.IsNotExactInstanceOfType<T>(x)` (MSTest 4.1+) |
| `Assert.IsAssignableFrom<T>(x)` | `Assert.IsInstanceOfType<T>(x)` (MSTest v4 returns the typed value) |
| `Assert.Empty(coll)` / `Assert.NotEmpty(coll)` | `Assert.IsEmpty(coll)` / `Assert.IsNotEmpty(coll)` |
| `Assert.Single(coll)` | `var item = Assert.ContainsSingle(coll);` |
| `Assert.Contains(item, coll)` / `Assert.DoesNotContain(...)` | Same -- `Assert.Contains` / `Assert.DoesNotContain` |
| `Assert.Contains("sub", str)` / `StartsWith` / `EndsWith` / `Matches` | Same (MSTest 3.8+) or `StringAssert.*` |
| `Assert.Skip("reason")` (v3 runtime) | `Assert.Inconclusive("reason")` |
| `Assert.SkipWhen(cond, "reason")` (v3) | If `cond` is environmental: `[OSCondition]` / `[CICondition]` (MSTest 3.10+); otherwise `if (cond) Assert.Inconclusive("reason");` |
| `Assert.SkipUnless(cond, "reason")` (v3) | Same -- prefer a condition attribute when the predicate is environmental; otherwise `if (!cond) Assert.Inconclusive("reason");` |

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

> **REQUIRED -- communicate the scope decision before applying it.** Silently widening fixture scope across the assembly is the most common way this migration regresses tests. Use this template (replace bracketed text):
>
> "The xUnit `[Collection(\"<name>\")]` shared a `<Fixture>` between **\<N\> classes** and serialized them. I am mapping that to: a static `Lazy<<Fixture>>` shared by each class's `[ClassInitialize]` (scope: **per-class, shared via static** -- not widened to assembly), plus `[DoNotParallelize]` on `<ClassA>` and `<ClassB>` to preserve the serialization. The alternative -- `[AssemblyInitialize]` -- would widen the fixture to every test in the assembly, which I rejected because \<reason\>."

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

**xUnit v3 `TestContext.Current`** (`TestContext.Current` is **static** in xUnit v3; in MSTest you must use the **instance** `TestContext` obtained via the same constructor or property injection shown above):

- `TestContext.Current.CancellationToken` -> `_testContext.CancellationToken` (MSTest 3.6+)
- `TestContext.Current.AddAttachment(name, path)` -> `_testContext.AddResultFile(path)`
- `TestContext.Current.TestOutputHelper.WriteLine(...)` -> `_testContext.WriteLine(...)`

> **REQUIRED for CancellationToken:** Add the constructor injection from above (or property injection if pinned to MSTest < 3.6) even if the class only uses `TestContext.Current.CancellationToken` (no `ITestOutputHelper`). Do **NOT** replace `TestContext.Current.CancellationToken` with `CancellationToken.None` or a freshly-constructed `CancellationTokenSource` -- both lose the test-host's cancellation linkage and change behavior under timeouts.

```csharp
// xUnit v3
[Fact]
public async Task WorkRespectsCancellation()
{
    var ct = TestContext.Current.CancellationToken;
    await Task.Delay(1, ct);
    Assert.False(ct.IsCancellationRequested);
}

// MSTest (note: Assert.False -> Assert.IsFalse from Step 6)
[TestClass]
public sealed class MyTests
{
    private readonly TestContext _testContext;
    public MyTests(TestContext testContext) => _testContext = testContext;

    [TestMethod]
    public async Task WorkRespectsCancellation()
    {
        var ct = _testContext.CancellationToken;
        await Task.Delay(1, ct);
        Assert.IsFalse(ct.IsCancellationRequested);
    }
}
```

### Step 10: Convert companion packages

| xUnit companion | MSTest equivalent |
|---|---|
| `Xunit.SkippableFact` (`[SkippableFact]`, `Skip.If`, `Skip.IfNot`) | For environmental predicates (OS/CI/arch): MSTest 3.10+ condition attributes (`[OSCondition]`, `[CICondition]`, etc.). Otherwise: `[Ignore]` (compile-time) or `Assert.Inconclusive("reason")` (runtime). Remove the package |
| `Xunit.Combinatorial` (`[CombinatorialData]`, `[CombinatorialValues]`) | [`Combinatorial.MSTest`](https://github.com/Youssef1313/Combinatorial.MSTest) (community port; attribute surface matches xUnit.Combinatorial). Or expand combinations into explicit `[DataRow]`s / `[DynamicData]` |
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

> **REQUIRED -- explicitly tell the user why this attribute is needed.** When applying Choice A, include this sentence (verbatim or near-verbatim) in your final summary:
>
> "MSTest defaults to **serial** execution across classes (unlike xUnit, which parallelizes classes by default), so this `[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]` is **required** to match the project's previous xUnit parallel-class behaviour. Without it, the suite would still pass but run roughly one-class-at-a-time and CI throughput would drop."
>
> The user must understand this is **opt-in** under MSTest -- a silent omission looks like a no-op but is actually a behavioural regression.

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

### Step 12: Convert xUnit assembly attributes

Some xUnit assembly attributes have direct MSTest equivalents at assembly scope; others must be removed (and re-applied per class/method) or reimplemented against MSTest extensibility.

**Convert (assembly scope preserved):**

- `[assembly: Xunit.Trait("Category", "v")]` -> `[assembly: TestCategory("v")]` -- `TestCategoryAttribute` targets `Assembly`, `Class`, and `Method`; assembly application propagates to every test.

**Convert (assembly scope NOT preserved):**

- `[assembly: Xunit.Trait("k", "v")]` (non-category key) -> **collapse to** `[assembly: TestCategory("v")]` if the value alone is sufficient as a filter, or move the trait down to every test class as `[TestProperty("k", "v")]`. `TestPropertyAttribute` only targets `Class` and `Method` (no `AttributeTargets.Assembly`) -- `[assembly: TestProperty(...)]` will not compile.

**Delete (no MSTest equivalent or now handled elsewhere):**

- `[assembly: CollectionBehavior(...)]` -- replaced by `[assembly: Parallelize(...)]` (Step 11)
- `[assembly: TestCaseOrderer(...)]` -- reimplement against MSTest extensibility; flag for manual conversion
- `[assembly: TestCollectionOrderer(...)]` -- flag for manual conversion
- `[assembly: TestFramework(...)]`
- `[assembly: CaptureConsole]` (xUnit v3) -- MSTest does not capture console by default

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
| `MSTest.Sdk` flipping VSTest project to MTP | `vstest.console` finds zero tests; CI breaks | Add `<UseVSTest>true</UseVSTest>` (no separate `Microsoft.NET.Test.Sdk` package needed -- the SDK pulls it in) |
| `[DataRow]` type mismatch | Theory cases compile in xUnit but produce MSTest runtime errors | Use exact literal types: `1` int, `1L` long, `1.0f` float |
| `Assert.SkipUnless` becomes `[Ignore]` | Tests that *would* have run on this machine now silently skip everywhere | Use a condition attribute (`[OSCondition]`/`[CICondition]`, MSTest 3.10+) when the predicate is environmental; otherwise runtime `Assert.Inconclusive` |
| Dropping `Assert.Collection` / `Assert.All` without replacement | Test passes but verifies nothing | Restore as explicit `foreach` + per-element assertions |
| Leaving `xunit.runner.json` in the project | Build warning + dead config | Delete the file after porting settings |

## Next Steps

After this migration:

- Run `migrate-vstest-to-mtp` if you want to move to Microsoft.Testing.Platform (separate, committable migration).
- Run `writing-mstest-tests` to polish converted code: replace `Assert.IsTrue(x.Count() == 3)` with `Assert.HasCount(3, x)`, prefer ValueTuple data sources, mark classes `sealed`, etc.
- Run `test-anti-patterns` / `assertion-quality` to catch any quality regressions introduced by mechanical conversion.
