---
name: migrate-mstest-v3-to-v4
description: >
  Migrate an MSTest v3 test project to MSTest v4. Use when user says
  "upgrade to MSTest v4", "update to latest MSTest", "MSTest 4 migration",
  "MSTest v4 breaking changes", "MSTest v4 compatibility", or has build errors
  after updating MSTest packages from 3.x to 4.x. Also use for target
  framework compatibility (e.g. net6.0/net7.0 support with MSTest v4).
  USE FOR: upgrading MSTest packages from 3.x to 4.x, fixing source breaking
  changes (Execute -> ExecuteAsync, CallerInfo constructor, ClassCleanupBehavior
  removal, TestContext.Properties, Assert API changes, ExpectedExceptionAttribute
  removal, TestTimeout enum removal), resolving behavioral changes
  (TreatDiscoveryWarningsAsErrors, TestContext lifecycle, TestCase.Id changes,
  MSTest.Sdk MTP changes), handling dropped TFMs (net5.0-net7.0 dropped,
  only net8.0+, net462, uap10.0 supported).
  DO NOT USE FOR: migrating from MSTest v1/v2 to v3 (use migrate-mstest-v1v2-to-v3
  first), migrating between test frameworks, or general .NET upgrades unrelated
  to MSTest.
---

# MSTest v3 -> v4 Migration

Migrate a test project from MSTest v3 to MSTest v4. The outcome is a project using MSTest v4 that builds cleanly, passes tests, and accounts for every source-incompatible and behavioral change. MSTest v4 is **not binary compatible** with MSTest v3 -- any library compiled against v3 must be recompiled against v4.

## When to Use

- Upgrading `MSTest.TestFramework`, `MSTest.TestAdapter`, or `MSTest` metapackage from 3.x to 4.x
- Upgrading `MSTest.Sdk` from 3.x to 4.x
- Fixing build errors after updating to MSTest v4 packages
- Resolving behavioral changes in test execution after upgrading to MSTest v4
- Updating custom `TestMethodAttribute` or `ConditionBaseAttribute` implementations for v4

## When Not to Use

- The project already uses MSTest v4 and builds cleanly -- migration is done
- Upgrading from MSTest v1 or v2 -- use `migrate-mstest-v1v2-to-v3` first, then return here
- The project does not use MSTest
- Migrating between test frameworks (e.g., MSTest to xUnit or NUnit)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or `.slnx` entry point containing MSTest test projects |
| Build command | No | How to build (e.g., `dotnet build`, a repo build script). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |

## Response Guidelines

- **Always identify the current version first**: Before recommending any migration steps, explicitly state the current MSTest version detected in the project (e.g., "Your project uses MSTest v3 (3.8.0)"). This confirms you've read the project files and grounds the migration advice.
- **Focused fix requests** (user has specific compilation errors after upgrading): Address only the relevant breaking changes from Step 3. **Always provide concrete fixed code** using the user's actual types and method names — show a complete, copy-pasteable code snippet, not just a description of what to change. For custom `TestMethodAttribute` subclasses, show the full fixed class including CallerInfo propagation to the base constructor. Mention any related analyzer that could have caught this earlier (e.g., MSTEST0006 for ExpectedException). Do not walk through the entire migration workflow.
- **"What to expect" questions** (user asks about breaking changes before upgrading): Present ALL major breaking changes from the Step 3 quick-lookup table -- not just the ones visible in the current code. For each, provide a one-line fix summary. Also mention key behavioral changes from Step 4 (especially TestCase.Id history impact and TreatDiscoveryWarningsAsErrors default). If project code is available, highlight which changes apply directly.
- **Full migration requests** (user wants complete migration): Follow the complete workflow below.
- **Behavioral/runtime symptom reports** (user describes test execution differences without build errors): Match described symptoms to the behavioral changes table in Step 4. Provide targeted, symptom-specific advice. Mention other behavioral changes the user should watch for. Do not walk through source breaking changes unless the user also has build errors.
- **CI/test-discovery issues** (tests not discovered, vstest.console stopped working, CI pipeline failures after upgrading): Focus on 4.5 (MSTest.Sdk defaults to MTP mode, which does not include Microsoft.NET.Test.Sdk -- needed for vstest.console) and 4.4 (TreatDiscoveryWarningsAsErrors). Explain the root cause clearly and give both fix options (add Microsoft.NET.Test.Sdk package or switch to `dotnet test`). Do not walk through the full migration workflow.
- **Explanatory questions** (user asks "is this a known change?", "what else should I watch out for?"): Explain the relevant changes and advise. Mention related changes the user might encounter next. Do not prescribe a full migration procedure.

## Workflow

> **Commit strategy:** Commit at each logical boundary -- after updating packages (Step 2), after resolving source breaking changes (Step 3), after addressing behavioral changes (Step 4). This keeps each commit focused and reviewable.

### Step 1: Assess the project

1. Identify the current MSTest version by checking package references for `MSTest`, `MSTest.TestFramework`, `MSTest.TestAdapter`, or `MSTest.Sdk` in `.csproj`, `Directory.Build.props`, or `Directory.Packages.props`.
2. Confirm the project is on MSTest v3 (3.x). If on v1 or v2, use `migrate-mstest-v1v2-to-v3` first.
3. Check target framework(s) -- MSTest v4 drops support for .NET Core 3.1 through .NET 7. Supported target frameworks are: **net8.0**, **net9.0**, **net462** (.NET Framework 4.6.2+), **uap10.0.16299** (UWP), **net9.0-windows10.0.17763.0** (modern UWP), and **net8.0-windows10.0.18362.0** (WinUI).
4. Check for custom `TestMethodAttribute` subclasses -- these require changes in v4.
5. Check for usages of `ExpectedExceptionAttribute` -- removed in v4 (deprecated since v3 with analyzer MSTEST0006).
6. Check for usages of `Assert.ThrowsException` (deprecated) -- removed in v4.
7. Run a clean build to establish a baseline of existing errors/warnings.

### Step 2: Update packages to MSTest v4

**If using the MSTest metapackage:**

```xml
<PackageReference Include="MSTest" Version="4.1.0" />
```

**If using individual packages:**

```xml
<PackageReference Include="MSTest.TestFramework" Version="4.1.0" />
<PackageReference Include="MSTest.TestAdapter" Version="4.1.0" />
```

**If using MSTest.Sdk:**

```xml
<Project Sdk="MSTest.Sdk/4.1.0">
```

Run `dotnet restore`, then `dotnet build`. Collect all errors for Step 3.

### Step 3: Resolve source breaking changes

Work through compilation errors systematically. Use this quick-lookup table to identify all applicable changes, then apply each fix:

| Error / Pattern in code | Breaking change | Fix |
|---|---|---|
| Custom `TestMethodAttribute` overrides `Execute` | Execute removed | Change to `ExecuteAsync` returning `Task<TestResult[]>` (3.1) |
| `[TestMethod("name")]` or custom attribute constructor | CallerInfo params added | Use `DisplayName = "name"` named param; propagate CallerInfo in subclasses (3.2) |
| `ClassCleanupBehavior.EndOfClass` | Enum removed | Remove argument: just `[ClassCleanup]` (3.3) |
| `TestContext.Properties.Contains("key")` | `Properties` is `IDictionary<string, object>` | Change to `ContainsKey("key")` (3.4) |
| `[Timeout(TestTimeout.Infinite)]` | `TestTimeout` enum removed | Replace with `[Timeout(int.MaxValue)]` (3.5) |
| `TestContext.ManagedType` | Property removed | Use `FullyQualifiedTestClassName` (3.6) |
| `Assert.AreEqual(a, b, "msg {0}", arg)` | Message+params overloads removed | Use string interpolation: `$"msg {arg}"` (3.7) |
| `Assert.ThrowsException<T>(...)` | Renamed | Replace with `Assert.ThrowsExactly<T>(...)` or `Assert.Throws<T>(...)` (3.7) |
| `Assert.IsInstanceOfType<T>(obj, out var t)` | Out parameter removed | Use `var t = Assert.IsInstanceOfType<T>(obj)` (3.7) |
| `[ExpectedException(typeof(T))]` | Attribute removed | Move assertion into test body: `Assert.ThrowsExactly<T>(() => ...)` (3.8) |
| Project targets net5.0, net6.0, or net7.0 | TFM dropped | Change to net8.0 or net9.0 (3.9) |

> **Important**: Scan the entire project for ALL patterns above before starting fixes. Multiple breaking changes often coexist in the same project.

#### 3.1 TestMethodAttribute.Execute -> ExecuteAsync

If you have custom `TestMethodAttribute` subclasses that override `Execute`, change to `ExecuteAsync`. This change was made because the v3 synchronous `Execute` API caused deadlocks when test code used `async`/`await` internally -- the synchronous wrapper would block the thread while the async operation needed that same thread to complete.

```csharp
// Before (v3)
public sealed class MyTestMethodAttribute : TestMethodAttribute
{
    public override TestResult[] Execute(ITestMethod testMethod)
    {
        // custom logic
        return result;
    }
}

// After (v4) -- Option A: wrap synchronous logic with Task.FromResult
public sealed class MyTestMethodAttribute : TestMethodAttribute
{
    public override Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        // custom logic (synchronous)
        return Task.FromResult(result);
    }
}

// After (v4) -- Option B: make properly async
public sealed class MyTestMethodAttribute : TestMethodAttribute
{
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        // custom async logic
        return await base.ExecuteAsync(testMethod);
    }
}
```

Use `Task.FromResult` when your override logic is purely synchronous. Use `async`/`await` when you call `base.ExecuteAsync` or other async methods.

#### 3.2 TestMethodAttribute CallerInfo constructor

`TestMethodAttribute` now uses `[CallerFilePath]` and `[CallerLineNumber]` parameters in its constructor.

**If you inherit from TestMethodAttribute**, propagate caller info to the base class:

```csharp
public class MyTestMethodAttribute : TestMethodAttribute
{
    public MyTestMethodAttribute(
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber)
    {
    }
}
```

**If you use `[TestMethodAttribute("Custom display name")]`**, switch to the named parameter syntax:

```csharp
// Before (v3)
[TestMethodAttribute("Custom display name")]

// After (v4)
[TestMethodAttribute(DisplayName = "Custom display name")]
```

#### 3.3 ClassCleanupBehavior enum removed

The `ClassCleanupBehavior` enum is removed. In v3, this enum controlled whether class cleanup ran at end of class (`EndOfClass`) or end of assembly (`EndOfAssembly`). In v4, class cleanup always runs at end of class. Remove the enum argument:

```csharp
// Before (v3)
[ClassCleanup(ClassCleanupBehavior.EndOfClass)]
public static void ClassCleanup(TestContext testContext) { }

// After (v4)
[ClassCleanup]
public static void ClassCleanup(TestContext testContext) { }
```

If you previously used `ClassCleanupBehavior.EndOfAssembly`, move that cleanup logic to an `[AssemblyCleanup]` method instead.

#### 3.4 TestContext.Properties type change

`TestContext.Properties` changed from `IDictionary` to `IDictionary<string, object>`. Update any `Contains` calls to `ContainsKey`:

```csharp
// Before (v3)
testContext.Properties.Contains("key");

// After (v4)
testContext.Properties.ContainsKey("key");
```

#### 3.5 TestTimeout enum removed

The `TestTimeout` enum (with only `TestTimeout.Infinite`) is removed. Replace with `int.MaxValue`:

```csharp
// Before (v3)
[Timeout(TestTimeout.Infinite)]

// After (v4)
[Timeout(int.MaxValue)]
```

#### 3.6 TestContext.ManagedType removed

The `TestContext.ManagedType` property is removed. Use `TestContext.FullyQualifiedTestClassName` instead.

#### 3.7 Assert API signature changes

- **Message + params removed**: Assert methods that accepted both `message` and `object[]` parameters now accept only `message`. Use string interpolation instead of format strings:

```csharp
// Before (v3)
Assert.AreEqual(expected, actual, "Expected {0} but got {1}", expected, actual);

// After (v4)
Assert.AreEqual(expected, actual, $"Expected {expected} but got {actual}");
```

- **Assert.ThrowsException renamed**: The `Assert.ThrowsException` APIs are renamed. Use `Assert.ThrowsExactly` (strict type match) or `Assert.Throws` (accepts derived exception types):

```csharp
// Before (v3)
Assert.ThrowsException<InvalidOperationException>(() => DoSomething());

// After (v4) -- exact type match (same behavior as old ThrowsException)
Assert.ThrowsExactly<InvalidOperationException>(() => DoSomething());

// After (v4) -- also catches derived exception types
Assert.Throws<InvalidOperationException>(() => DoSomething());
```

- **Assert.IsInstanceOfType out parameter changed**: `Assert.IsInstanceOfType<T>(x, out var t)` changes to `var t = Assert.IsInstanceOfType<T>(x)`:

```csharp
// Before (v3)
Assert.IsInstanceOfType<MyType>(obj, out var typed);

// After (v4)
var typed = Assert.IsInstanceOfType<MyType>(obj);
```

- **Assert.AreEqual for IEquatable\<T\> removed**: If you get generic type inference errors, explicitly specify the type argument as `object`.

#### 3.8 ExpectedExceptionAttribute removed

The `[ExpectedException]` attribute is removed in v4. In MSTest 3.2, the `MSTEST0006` analyzer was introduced to flag `[ExpectedException]` usage and suggest migrating to `Assert.ThrowsExactly` while still on v3 (a non-breaking change). In v4, the attribute is gone entirely. Migrate to `Assert.ThrowsExactly`:

```csharp
// Before (v3)
[ExpectedException(typeof(InvalidOperationException))]
[TestMethod]
public void TestMethod()
{
    MyCall();
}

// After (v4)
[TestMethod]
public void TestMethod()
{
    Assert.ThrowsExactly<InvalidOperationException>(() => MyCall());
}
```

**When the test has setup code before the throwing call**, wrap only the throwing call in the lambda -- keep Arrange/Act separation clear:

```csharp
// Before (v3)
[ExpectedException(typeof(ArgumentNullException))]
[TestMethod]
public void Validate_NullInput_Throws()
{
    var service = new ValidationService();
    service.Validate(null);  // throws here
}

// After (v4)
[TestMethod]
public void Validate_NullInput_Throws()
{
    var service = new ValidationService();
    Assert.ThrowsExactly<ArgumentNullException>(() => service.Validate(null));
}
```

**For async test methods**, use `Assert.ThrowsExactlyAsync`:

```csharp
// Before (v3)
[ExpectedException(typeof(HttpRequestException))]
[TestMethod]
public async Task FetchData_BadUrl_Throws()
{
    await client.GetAsync("https://localhost:0");
}

// After (v4)
[TestMethod]
public async Task FetchData_BadUrl_Throws()
{
    await Assert.ThrowsExactlyAsync<HttpRequestException>(
        () => client.GetAsync("https://localhost:0"));
}
```

**If `[ExpectedException]` used the `AllowDerivedTypes` property**, use `Assert.ThrowsAsync<T>` (base type matching) instead of `Assert.ThrowsExactlyAsync<T>` (exact type matching).

#### 3.9 Dropped target frameworks

MSTest v4 supports: **net8.0**, **net9.0**, **net462** (.NET Framework 4.6.2+), **uap10.0.16299** (UWP), **net9.0-windows10.0.17763.0** (modern UWP), and **net8.0-windows10.0.18362.0** (WinUI). All other frameworks are dropped -- including net5.0, net6.0, net7.0, and netcoreapp3.1.

If the test project targets an unsupported framework, update `TargetFramework`:

```xml
<!-- Before -->
<TargetFramework>net6.0</TargetFramework>

<!-- After -->
<TargetFramework>net8.0</TargetFramework>
```

#### 3.10 Unfolding strategy moved to TestMethodAttribute

The `UnfoldingStrategy` property (introduced in MSTest 3.7) has moved from individual data source attributes (`DataRowAttribute`, `DynamicDataAttribute`) to `TestMethodAttribute`.

#### 3.11 ConditionBaseAttribute.ShouldRun renamed

The `ConditionBaseAttribute.ShouldRun` property is renamed to `IsConditionMet`.

#### 3.12 Internal/removed types

Several types previously public are now internal or removed:

- `MSTestDiscoverer`, `MSTestExecutor`, `AssemblyResolver`, `LogMessageListener`
- `TestExecutionManager`, `TestMethodInfo`, `TestResultExtensions`
- `UnitTestOutcomeExtensions`, `GenericParameterHelper`
- `ITestMethod` in PlatformServices assembly (the one in TestFramework is unchanged)

If your code references any of these, find alternative approaches or remove the dependency.

### Step 4: Address behavioral changes

These changes won't cause build errors but may affect test runtime behavior.

| Symptom | Cause | Fix |
|---|---|---|
| Tests show as new in Azure DevOps / test history lost | `TestCase.Id` generation changed (4.3) | No code fix; history will re-baseline |
| `TestContext.TestName` throws in `[ClassInitialize]` | v4 enforces lifecycle scope (4.2) | Move access to `[TestInitialize]` or test methods |
| Tests not discovered / discovery failures | `TreatDiscoveryWarningsAsErrors` now true (4.4) | Fix warnings, or set to false in .runsettings |
| Tests hang that didn't before | AppDomain disabled by default (4.1) | Set `DisableAppDomain` to false in .runsettings `RunConfiguration` |
| vstest.console can't find tests with MSTest.Sdk | MSTest.Sdk defaults to MTP; `Microsoft.NET.Test.Sdk` only added in VSTest mode (4.5) | Add explicit package reference or switch to `dotnet test` |
| New warnings from analyzers | Analyzer severities upgraded (4.6) | Fix warnings or suppress in .editorconfig |

#### 4.1 DisableAppDomain defaults to true

AppDomains are disabled by default. On .NET Framework, when running inside testhost (the default for `dotnet test` and VS), MSTest re-enables AppDomains automatically. If you need to explicitly control AppDomain isolation, set it via `.runsettings`:

```xml
<RunSettings>
  <RunConfiguration>
    <DisableAppDomain>false</DisableAppDomain>
  </RunConfiguration>
</RunSettings>
```

#### 4.2 TestContext throws when used incorrectly

MSTest v4 now throws when accessing test-specific properties in the wrong lifecycle stage:

- `TestContext.FullyQualifiedTestClassName` -- cannot be accessed in `[AssemblyInitialize]`
- `TestContext.TestName` -- cannot be accessed in `[AssemblyInitialize]` or `[ClassInitialize]`

**Fix**: Move any code that accesses `TestContext.TestName` from `[ClassInitialize]` to `[TestInitialize]` or individual test methods, where per-test context is available. Do not replace `TestName` with `FullyQualifiedTestClassName` as a workaround -- they have different semantics.

#### 4.3 TestCase.Id generation changed

The generation algorithm for `TestCase.Id` has changed to fix long-standing bugs. This may affect Azure DevOps test result tracking (e.g., test failure tracking over time). There is no code fix needed, but be aware of test result history discontinuity.

#### 4.4 TreatDiscoveryWarningsAsErrors defaults to true

v4 uses stricter defaults. Discovery warnings are now treated as errors, which means tests that previously ran despite discovery issues may now fail entirely. If you see unexpected test failures after upgrading (not build errors, but tests not being discovered), check for discovery warnings. To restore v3 behavior while you investigate:

```xml
<RunSettings>
  <MSTest>
    <TreatDiscoveryWarningsAsErrors>false</TreatDiscoveryWarningsAsErrors>
  </MSTest>
</RunSettings>
```

> **Recommended**: Fix the underlying discovery warnings rather than suppressing this setting.

#### 4.5 MSTest.Sdk and vstest.console compatibility

MSTest.Sdk defaults to Microsoft.Testing.Platform (MTP) mode. In MTP mode, MSTest.Sdk does **not** add a reference to `Microsoft.NET.Test.Sdk` -- it only adds it in VSTest mode. This is not a v4-specific change; it applies to MSTest.Sdk v3 as well. Without `Microsoft.NET.Test.Sdk`, `vstest.console` cannot discover or run tests and will silently find zero tests. This commonly surfaces during migration when a CI pipeline uses `vstest.console` but the project uses MSTest.Sdk in its default MTP mode.

**Option A -- Switch to VSTest mode**: Set the `UseVSTest` property. MSTest.Sdk will then automatically add `Microsoft.NET.Test.Sdk`:

```xml
<Project Sdk="MSTest.Sdk/4.1.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <UseVSTest>true</UseVSTest>
  </PropertyGroup>
</Project>
```

**Option B -- Switch CI to `dotnet test`**: Replace `vstest.console` invocations in your CI pipeline with `dotnet test`. This works natively with MTP and is the recommended long-term approach for MSTest.Sdk projects.

If you need VSTest during a transition period, Option A works without changing CI pipelines.

#### 4.6 Analyzer severity changes

Multiple analyzers have been upgraded from Info to Warning by default:

- MSTEST0001, MSTEST0007, MSTEST0017, MSTEST0023, MSTEST0024, MSTEST0025
- MSTEST0030, MSTEST0031, MSTEST0032, MSTEST0035, MSTEST0037, MSTEST0045

Review and fix any new warnings, or suppress them in `.editorconfig` if intentional.

### Step 5: Verify

1. Run `dotnet build` -- confirm zero errors and review any new warnings
2. Run `dotnet test` -- confirm all tests pass
3. Compare test results (pass/fail counts) to the pre-migration baseline
4. If using Azure DevOps test tracking, be aware that `TestCase.Id` changes may affect history continuity
5. Check that no tests were silently dropped due to stricter discovery

## Validation

- [ ] All MSTest packages updated to 4.x
- [ ] Project builds with zero errors
- [ ] All tests pass with `dotnet test`
- [ ] Custom `TestMethodAttribute` subclasses updated for `ExecuteAsync` and CallerInfo
- [ ] `ExpectedExceptionAttribute` replaced with `Assert.ThrowsExactly`
- [ ] `Assert.ThrowsException` replaced with `Assert.ThrowsExactly` (or `Assert.Throws`)
- [ ] `ClassCleanupBehavior` enum usages removed
- [ ] `TestContext.Properties.Contains` updated to `ContainsKey`
- [ ] All target frameworks are net8.0+, net9.0, net462+, uap10.0.16299, or WinUI
- [ ] Behavioral changes reviewed and addressed
- [ ] No tests were lost during migration (compare test counts)

## Related Skills

- `writing-mstest-tests` -- for modern MSTest v4 assertion APIs and test authoring best practices
- `run-tests` -- for running tests after migration

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Custom `TestMethodAttribute` still overrides `Execute` | Change to `ExecuteAsync` returning `Task<TestResult[]>` |
| `TestMethodAttribute("display name")` no longer compiles | Use `TestMethodAttribute(DisplayName = "display name")` |
| `ClassCleanupBehavior` enum not found | Remove the enum argument; `[ClassCleanup]` now always runs at end of class. For end-of-assembly cleanup, use `[AssemblyCleanup]` |
| `TestContext.Properties.Contains` missing | Use `ContainsKey` -- `Properties` is now `IDictionary<string, object>` |
| `ExpectedException` attribute not found | Replace with `Assert.ThrowsExactly<T>(() => ...)` inside the test body |
| `Assert.ThrowsException` not found | Replace with `Assert.ThrowsExactly` (or `Assert.Throws` for derived types) |
| `Assert.AreEqual` with format string args fails | Use string interpolation: `$"message {value}"` |
| Tests hang that didn't before | AppDomain is disabled by default; on .NET Fx in testhost it is re-enabled automatically |
| Azure DevOps test history breaks | Expected -- `TestCase.Id` generation changed; no code fix, results will re-baseline |
| Discovery warnings now fail the run | `TreatDiscoveryWarningsAsErrors` is true by default; fix the discovery warnings |
| Net6.0/net7.0 targets don't compile | Update to net8.0 -- MSTest v4 supports net8.0, net9.0, net462, uap10.0.16299, modern UWP, and WinUI |
