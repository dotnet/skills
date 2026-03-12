---
name: migrate-mstest-v3-to-v4
description: >
  Migrate an MSTest v3 test project to MSTest v4.
  USE FOR: upgrading MSTest.TestFramework / MSTest.TestAdapter / MSTest packages
  from 3.x to 4.x, fixing source breaking changes (TestMethodAttribute.Execute
  → ExecuteAsync, CallerInfo constructor changes, ClassCleanupBehavior removal,
  TestContext.Properties generic IDictionary, Assert API signature changes,
  ExpectedExceptionAttribute removal, TestTimeout enum removal,
  TestContext.ManagedType removal), resolving behavioral changes (DisableAppDomain
  default, TestContext incorrect usage exceptions, TestCase.Id changes,
  TreatDiscoveryWarningsAsErrors default, MSTest.Sdk Microsoft.NET.Test.Sdk
  removal), dropping unsupported target frameworks (< net8.0), and updating
  custom TestMethodAttribute / ConditionBaseAttribute implementations.
  DO NOT USE FOR: migrating from MSTest v1/v2 to v3 (use migrate-mstest-v1v2-to-v3
  first, then return here), migrating between test frameworks (MSTest to
  xUnit/NUnit), or .NET version upgrades (use migrate-dotnet*-to-dotnet* skills).
---

# MSTest v3 → v4 Migration

Migrate a test project from MSTest v3 to MSTest v4. The outcome is a project using MSTest v4 that builds cleanly, passes tests, and accounts for every source-incompatible and behavioral change. MSTest v4 is **not binary compatible** with MSTest v3 — any library compiled against v3 must be recompiled against v4.

## When to Use

- Upgrading `MSTest.TestFramework`, `MSTest.TestAdapter`, or `MSTest` metapackage from 3.x to 4.x
- Upgrading `MSTest.Sdk` from 3.x to 4.x
- Fixing build errors after updating to MSTest v4 packages
- Resolving behavioral changes in test execution after upgrading to MSTest v4
- Updating custom `TestMethodAttribute` or `ConditionBaseAttribute` implementations for v4

## When Not to Use

- The project already uses MSTest v4 and builds cleanly — migration is done
- Upgrading from MSTest v1 or v2 — use `migrate-mstest-v1v2-to-v3` first, then return here
- The project does not use MSTest
- Migrating between test frameworks (e.g., MSTest to xUnit or NUnit)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or `.slnx` entry point containing MSTest test projects |
| Build command | No | How to build (e.g., `dotnet build`, a repo build script). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |

## Workflow

> **Commit strategy:** Commit at each logical boundary — after updating packages (Step 2), after resolving source breaking changes (Step 3), after addressing behavioral changes (Step 4). This keeps each commit focused and reviewable.

### Step 1: Assess the project

1. Identify the current MSTest version by checking package references for `MSTest`, `MSTest.TestFramework`, `MSTest.TestAdapter`, or `MSTest.Sdk` in `.csproj`, `Directory.Build.props`, or `Directory.Packages.props`.
2. Confirm the project is on MSTest v3 (3.x). If on v1 or v2, use `migrate-mstest-v1v2-to-v3` first.
3. Check target framework(s) — MSTest v4 drops support for .NET Core 3.1 through .NET 7. Minimum supported .NET is **net8.0**. .NET Framework 4.6.2+ continues to be supported.
4. Check for custom `TestMethodAttribute` subclasses — these require changes in v4.
5. Check for usages of `ExpectedExceptionAttribute` — removed in v4 (deprecated since v3 with analyzer MSTEST0006).
6. Check for usages of `Assert.ThrowsException` (deprecated) — removed in v4.
7. Run a clean build to establish a baseline of existing errors/warnings.

### Step 2: Update packages to MSTest v4

**If using the MSTest metapackage:**

```xml
<PackageReference Include="MSTest" Version="4.0.2" />
```

**If using individual packages:**

```xml
<PackageReference Include="MSTest.TestFramework" Version="4.0.2" />
<PackageReference Include="MSTest.TestAdapter" Version="4.0.2" />
```

**If using MSTest.Sdk:**

```xml
<Project Sdk="MSTest.Sdk/4.0.2">
```

Run `dotnet restore`, then `dotnet build`. Collect all errors for Step 3.

### Step 3: Resolve source breaking changes

Work through compilation errors systematically. The following sections cover each breaking change.

#### 3.1 TestMethodAttribute.Execute → ExecuteAsync

If you have custom `TestMethodAttribute` subclasses that override `Execute`, change to `ExecuteAsync`:

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

// After (v4)
public sealed class MyTestMethodAttribute : TestMethodAttribute
{
    public override Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        // custom logic
        return Task.FromResult(result);
    }
}
```

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

The `ClassCleanupBehavior` enum is removed. Class cleanup now always runs at end of class (the behavior most users expected). Remove the enum argument:

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

- **Assert.ThrowsException removed**: The deprecated `Assert.ThrowsException` APIs are removed. Use `Assert.ThrowsExactly` instead:

```csharp
// Before (v3)
Assert.ThrowsException<InvalidOperationException>(() => DoSomething());

// After (v4)
Assert.ThrowsExactly<InvalidOperationException>(() => DoSomething());
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

The `[ExpectedException]` attribute is removed (deprecated since MSTest 3.2 with analyzer MSTEST0006). Migrate to `Assert.ThrowsExactly`:

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

#### 3.9 Dropped target frameworks

MSTest v4 drops support for .NET Core 3.1 through .NET 7. The minimum supported .NET version is **.NET 8**. .NET Framework 4.6.2+ continues to be supported.

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

#### 4.1 DisableAppDomain defaults to true (MTP only)

When running under Microsoft.Testing.Platform, AppDomains are disabled by default in v4 (up to 30% faster). If you need AppDomain isolation, add to `.runsettings`:

```xml
<RunSettings>
  <MSTest>
    <DisableAppDomain>false</DisableAppDomain>
  </MSTest>
</RunSettings>
```

> **Warning**: When AppDomain isolation is enabled, MSTest unloads the AppDomain after tests finish, aborting associated threads. If you have foreground threads that ran forever in v3, they will now cause hangs in v4.

#### 4.2 TestContext throws when used incorrectly

MSTest v4 now throws when accessing test-specific properties in the wrong lifecycle stage:
- `TestContext.FullyQualifiedTestClassName` — cannot be accessed in `[AssemblyInitialize]`
- `TestContext.TestName` — cannot be accessed in `[AssemblyInitialize]` or `[ClassInitialize]`

#### 4.3 TestCase.Id generation changed

The generation algorithm for `TestCase.Id` has changed to fix long-standing bugs. This may affect Azure DevOps test result tracking (e.g., test failure tracking over time). There is no code fix needed, but be aware of test result history discontinuity.

#### 4.4 TreatDiscoveryWarningsAsErrors defaults to true

v4 uses stricter defaults. Discovery warnings are now treated as errors. This should be transparent for most projects, but may surface hidden issues in test discovery.

#### 4.5 MSTest.Sdk no longer adds Microsoft.NET.Test.Sdk for MTP

If using MSTest.Sdk with Microsoft.Testing.Platform (the default), the SDK no longer adds a reference to `Microsoft.NET.Test.Sdk`. If you still need VSTest support (e.g., for `vstest.console`), manually add:

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
```

#### 4.6 Analyzer severity changes

Multiple analyzers have been upgraded from Info to Warning by default:
- MSTEST0001, MSTEST0007, MSTEST0017, MSTEST0023, MSTEST0024, MSTEST0025
- MSTEST0030, MSTEST0031, MSTEST0032, MSTEST0035, MSTEST0037, MSTEST0045

Review and fix any new warnings, or suppress them in `.editorconfig` if intentional.

### Step 5: Verify

1. Run `dotnet build` — confirm zero errors and review any new warnings
2. Run `dotnet test` — confirm all tests pass
3. Compare test results (pass/fail counts) to the pre-migration baseline
4. If using Azure DevOps test tracking, be aware that `TestCase.Id` changes may affect history continuity
5. Check that no tests were silently dropped due to stricter discovery

## Validation

- [ ] All MSTest packages updated to 4.x
- [ ] Project builds with zero errors
- [ ] All tests pass with `dotnet test`
- [ ] Custom `TestMethodAttribute` subclasses updated for `ExecuteAsync` and CallerInfo
- [ ] `ExpectedExceptionAttribute` replaced with `Assert.ThrowsExactly`
- [ ] `Assert.ThrowsException` replaced with `Assert.ThrowsExactly`
- [ ] `ClassCleanupBehavior` enum usages removed
- [ ] `TestContext.Properties.Contains` updated to `ContainsKey`
- [ ] All target frameworks are net8.0+ (or net462+ for .NET Framework)
- [ ] Behavioral changes reviewed and addressed
- [ ] No tests were lost during migration (compare test counts)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Custom `TestMethodAttribute` still overrides `Execute` | Change to `ExecuteAsync` returning `Task<TestResult[]>` |
| `TestMethodAttribute("display name")` no longer compiles | Use `TestMethodAttribute(DisplayName = "display name")` |
| `ClassCleanupBehavior` enum not found | Remove the enum argument; `[ClassCleanup]` now always runs at end of class |
| `TestContext.Properties.Contains` missing | Use `ContainsKey` — `Properties` is now `IDictionary<string, object>` |
| `ExpectedException` attribute not found | Replace with `Assert.ThrowsExactly<T>(() => ...)` inside the test body |
| `Assert.ThrowsException` not found | Replace with `Assert.ThrowsExactly` |
| `Assert.AreEqual` with format string args fails | Use string interpolation: `$"message {value}"` |
| Tests hang that didn't before | AppDomain is disabled by default in MTP; foreground threads no longer aborted |
| Azure DevOps test history breaks | Expected — `TestCase.Id` generation changed; no code fix, results will re-baseline |
| Discovery warnings now fail the run | `TreatDiscoveryWarningsAsErrors` is true by default; fix the discovery warnings |
| Net6.0/net7.0 targets don't compile | Update to net8.0 — MSTest v4 dropped support for .NET < 8 |
