# MSTest v3 to v4 breaking-change details

Use this reference when a migration matches one of the source-breaking or runtime-behavior changes listed in the parent skill. Apply the specific replacement to the user's actual code and verify the resulting project; examples here are patterns, not substitutes for inspecting the project.

Source: [MSTest v3-to-v4 migration guide](https://learn.microsoft.com/dotnet/core/testing/unit-testing-mstest-migration-v3-v4),
incorporating dotnet/docs#56941 at revision `897da0c7f276f12b184beadf4b7d758eb2eff699`.

## Source-breaking changes

### 3.1 TestMethodAttribute.Execute -> ExecuteAsync

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

### 3.2 TestMethodAttribute CallerInfo constructor

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

If the subclass has its own display-name constructor, do not pass that string to
the v4 base constructor. Propagate only caller information and assign the
`DisplayName` property:

```csharp
public sealed class NamedTestMethodAttribute : TestMethodAttribute
{
    public NamedTestMethodAttribute(
        string displayName,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber)
    {
        DisplayName = displayName;
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

### 3.3 ClassCleanupBehavior enum removed

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

### 3.4 TestContext.Properties type change

`TestContext.Properties` changed from `IDictionary` to `IDictionary<string, object>`. Update any `Contains` calls to `ContainsKey`:

```csharp
// Before (v3)
testContext.Properties.Contains("key");

// After (v4)
testContext.Properties.ContainsKey("key");
```

### 3.5 TestTimeout enum removed

The `TestTimeout` enum (with only `TestTimeout.Infinite`) is removed. Replace with `int.MaxValue`:

```csharp
// Before (v3)
[Timeout(TestTimeout.Infinite)]

// After (v4)
[Timeout(int.MaxValue)]
```

### 3.6 TestContext.ManagedType removed

The `TestContext.ManagedType` property is removed. Use `TestContext.FullyQualifiedTestClassName` instead.

### 3.7 Assert API signature changes

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

Apply this assignment rewrite to every occurrence, preserving the concrete asserted type and all
later uses of the typed variable. When source is available, show or edit the actual method rather
than substituting a generic `MyType` example, then verify that the project compiles.

- **Assert.AreEqual for IEquatable\<T\> removed**: If you get generic type inference errors, explicitly specify the type argument as `object`.

### 3.8 ExpectedExceptionAttribute removed

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

For a focused migration, convert every attributed method in the supplied source, wrap only the
statement expected to throw, preserve arrange/setup statements outside the lambda, and run the
affected tests. A prose-only API substitution is incomplete when editable project files are present.

### 3.9 Dropped target frameworks

MSTest v4 supports **.NET 8 and later** and **.NET Framework 4.6.2 and later**. Platform-specific supported targets also include **uap10.0.16299** (UWP), with modern UWP and WinUI using their corresponding supported Windows-specific .NET TFMs. .NET Core 3.1 through .NET 7 are dropped.

If the test project targets an unsupported framework, update `TargetFramework`:

```xml
<!-- Before -->
<TargetFramework>net6.0</TargetFramework>

<!-- After -->
<TargetFramework>net8.0</TargetFramework>
```

### 3.10 Unfolding strategy moved to TestMethodAttribute

The `UnfoldingStrategy` property (introduced in MSTest 3.7) has moved from individual data source attributes (`DataRowAttribute`, `DynamicDataAttribute`) to `TestMethodAttribute`.

### 3.11 ConditionBaseAttribute.ShouldRun renamed

The `ConditionBaseAttribute.ShouldRun` property is renamed to `IsConditionMet`.

### 3.12 Internal/removed types

Several types previously public are now internal or removed:

- `MSTestDiscoverer`, `MSTestExecutor`, `AssemblyResolver`, `LogMessageListener`
- `TestExecutionManager`, `TestMethodInfo`, `TestResultExtensions`
- `UnitTestOutcomeExtensions`, `GenericParameterHelper`
- `ITestMethod` in the PlatformServices assembly is now internal. The separate
  TestFramework `ITestMethod` remains public but has API changes; see 3.14.

If your code references any of these, find alternative approaches or remove the dependency.

### 3.13 Test-method attribute display names

The constructors that accept a display name are removed from
`DataTestMethodAttribute`, `STATestMethodAttribute`, and `UITestMethodAttribute`.
Set `DisplayName` instead. For example, change
`[DataTestMethod("custom name")]` to `[DataTestMethod(DisplayName = "custom name")]`.
The same applies to the STA and UI attributes.

The `TestMethodAttribute` caller-info constructor has a subtle failure mode:
`[TestMethod("custom name")]` still compiles because the string binds to
`callerFilePath`, but it is then ignored as a display name. Always use
`[TestMethod(DisplayName = "custom name")]`. When deriving custom attributes,
propagate `[CallerFilePath]` and `[CallerLineNumber]` to the base constructor.
`UITestMethodAttribute` overrides `ExecuteAsync` instead of `Execute`; update
subclasses accordingly. MSTEST0056 and MSTEST0057 include code fixes for these
constructor migration problems.

### 3.14 TestFramework ITestMethod changes

The `Microsoft.VisualStudio.TestTools.UnitTesting.ITestMethod` interface in
TestFramework changed: `Invoke` was removed in favor of `InvokeAsync`, and the
`inherit` parameter was removed from `GetAllAttributes` and
`GetAttributes<TAttributeType>`. MSTest now always includes inherited
attributes; see 4.8 for how custom attributes can opt out. Do not confuse this
interface with the separate PlatformServices `ITestMethod`, which was made
internal.

### 3.15 TestContext property replacements

The deprecated `TestContext` properties were removed. Choose the replacement
that matches what the old path represented:

| Removed property | Replacement |
|------------------|-------------|
| `TestContext.TestDir` | `TestContext.TestRunDirectory` |
| `TestContext.TestDeploymentDir` | `TestContext.DeploymentDirectory` |
| `TestContext.TestLogsDir` | `TestContext.TestRunResultsDirectory` for run-level result files, or `TestContext.TestResultsDirectory` for test-specific result files |
| `TestContext.ManagedType` | `TestContext.FullyQualifiedTestClassName` |
| `TestContext.ManagedMethod` | `TestContext.TestName` |

`TestName` contains only the method name; `ManagedMethod` also included generic
arity and parameter types. `FullyQualifiedTestClassName` and `TestName` are
non-nullable in v4, but remain subject to the lifecycle restrictions in 4.2.

### 3.16 Class-cleanup settings removed

In addition to `ClassCleanupBehavior`, v4 removes the assembly-level
`ClassCleanupExecutionAttribute`. Remove usages such as
`[assembly: ClassCleanupExecution(...)]`; they no longer compile.

MSTest v4 also no longer reads `ClassCleanupLifecycle` from runsettings or
`classCleanupLifecycle` from the `mstest` section of `testconfig.json`.
Remove these settings; they have no effect in v4. Class cleanup always runs
at the end of each class. If cleanup must run once after the whole assembly,
move that work to `[AssemblyCleanup]`.

### 3.17 Other assertion and API removals

- The `StringAssert.Contains`, `StartsWith`, and `EndsWith` overloads that take
  both `message` and `StringComparison` now take `StringComparison` first.
  For example, change `StringAssert.Contains(value, part, "message",
  StringComparison.Ordinal)` to
  `StringAssert.Contains(value, part, StringComparison.Ordinal, "message")`.
- Named-argument callers must update renamed parameters: `expected` becomes
  `notExpected` in `Assert.DoesNotContain` and `CollectionAssert.AreNotEquivalent`;
  `substring` becomes `expectedPrefix`/`expectedSuffix` in `Assert.StartsWith`/
  `EndsWith`, and `notExpectedPrefix`/`notExpectedSuffix` in
  `DoesNotStartWith`/`DoesNotEndWith`.
- `Assert.ReplaceNullChars` is removed. Remove the call or implement the
  required transformation in application code.
- `ExpectedExceptionBaseAttribute` is removed along with
  `ExpectedExceptionAttribute`. Replace custom derived attributes with
  `Assert.Throws`, `Assert.ThrowsExactly`, or async counterparts inside tests.
- `TestIdGenerationStrategy`, `TestIdGenerationStrategyAttribute`, and
  `DataRowAttribute.TestIdGenerationStrategy` are removed. Remove attempts to
  configure test IDs; see 4.3 for the resulting ID/history behavior.
- `CssIterationAttribute` and `CssProjectStructureAttribute` are removed;
  remove them from tests. `InternalTestFailureException` is also removed; use
  another appropriate exception type.

### 3.18 Assembly, executor, and adapter compatibility

MSTest assemblies were renamed to match their package names. Their namespaces
did not change:

| v3 assembly | v4 assembly |
|-------------|-------------|
| `Microsoft.VisualStudio.TestPlatform.TestFramework.dll` | `MSTest.TestFramework.dll` |
| `Microsoft.VisualStudio.TestPlatform.TestFramework.Extensions.dll` | `MSTest.TestFramework.Extensions.dll` |
| `Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.dll` | `MSTest.TestAdapter.dll` |
| `Microsoft.VisualStudio.TestPlatform.MSTestAdapter.PlatformServices.dll` | `MSTestAdapter.PlatformServices.dll` |

The assembly version is no longer fixed at `14.0.0.0`, and the executor URI
changed from `executor://MSTestAdapter/v2` to `executor://MSTestAdapter/v4`.
Update binding redirects, assembly-qualified type names, code coverage filters,
and any tools that match assembly names, versions, or executor URIs.

`MSTest.TestAdapter` no longer ships a `netstandard2.0` build. The
`MSTest.TestFramework` package still targets `netstandard2.0`; do not infer
that the framework package was dropped along with the adapter asset.

### 3.19 Other extension API changes

- `ITestDataSourceUnfoldingCapability` is removed. Remove its implementation
  from custom data sources and set `UnfoldingStrategy` on `TestMethodAttribute`.
- `ConditionBaseAttribute.IgnoreMessage` is no longer virtual. Remove
  overrides and set the property directly, such as in the custom attribute's
  constructor.

## Runtime behavior changes

### 4.1 DisableAppDomain defaults to true under MTP

When running with Microsoft.Testing.Platform (MTP), MSTest v4 disables
AppDomain isolation by default. This is relevant to .NET Framework; do not
infer the runner from `dotnet test` alone or apply this MTP default to VSTest.
If AppDomain isolation is required, explicitly enable it via `.runsettings`:

```xml
<RunSettings>
  <RunConfiguration>
    <DisableAppDomain>false</DisableAppDomain>
  </RunConfiguration>
</RunSettings>
```

MSTest v4 reads `DisableAppDomain` only from
`RunSettings/RunConfiguration/DisableAppDomain`; v3 used the first element with
that name anywhere in the file. Move misplaced elements under `RunConfiguration`.
The equivalent `testconfig.json` setting is:

```json
{
  "mstest": {
    "execution": {
      "disableAppDomain": false
    }
  }
}
```

AppDomain unloading aborts its threads, including foreground threads. A
foreground thread that ran forever but was terminated by unloading in v3 can
therefore keep an MTP run alive in v4. Stop such threads as part of test cleanup;
enable AppDomain isolation only when the scenario requires it.

### 4.2 TestContext throws when used incorrectly

MSTest v4 now throws when accessing test-specific properties in the wrong lifecycle stage:

- `TestContext.FullyQualifiedTestClassName` -- cannot be accessed in `[AssemblyInitialize]`
- `TestContext.TestName` -- cannot be accessed in `[AssemblyInitialize]` or `[ClassInitialize]`

**Fix**: Move any code that accesses `TestContext.TestName` from `[ClassInitialize]` to `[TestInitialize]` or individual test methods, where per-test context is available. Do not replace `TestName` with `FullyQualifiedTestClassName` as a workaround -- they have different semantics.

### 4.3 TestCase.Id generation changed

The generation algorithm for `TestCase.Id` has changed to fix long-standing bugs. This may affect Azure DevOps test result tracking (e.g., test failure tracking over time). There is no code fix needed, but be aware of test result history discontinuity.

### 4.4 TreatDiscoveryWarningsAsErrors defaults to true

v4 uses stricter defaults. Discovery warnings are now treated as errors, which means tests that previously ran despite discovery issues may now fail entirely. If you see unexpected test failures after upgrading (not build errors, but tests not being discovered), check for discovery warnings. To restore v3 behavior while you investigate:

```xml
<RunSettings>
  <MSTest>
    <TreatDiscoveryWarningsAsErrors>false</TreatDiscoveryWarningsAsErrors>
  </MSTest>
</RunSettings>
```

> **Recommended**: Fix the underlying discovery warnings rather than suppressing this setting.

### 4.5 MSTest.Sdk and vstest.console compatibility

MSTest.Sdk defaults to Microsoft.Testing.Platform (MTP) mode. MSTest.Sdk v3
still added `Microsoft.NET.Test.Sdk` in that mode; v4 removes the unnecessary
reference. A CI pipeline that separately invokes `vstest.console` can therefore
drop to zero discovered tests immediately after the v4 upgrade.

**Option A -- Preserve MTP and transitional VSTest discovery**: Add the exact
compatible `Microsoft.NET.Test.Sdk` package explicitly. This is the least
disruptive fix when MTP remains the primary runner but an existing
`vstest.console` job cannot be removed yet:

Use a direct `PackageReference` with the exact compatible version resolved from
the configured feed. Under Central Package Management, add or update the
`Microsoft.NET.Test.Sdk` `PackageVersion` in `Directory.Packages.props` and keep
the project reference versionless. Do not copy a fixed example version.

Verify with the actual `vstest.console` command; a passing `dotnet test` MTP run
does not prove VSTest discovery.

**Option B -- Switch the project to VSTest mode**: Set the `UseVSTest` property.
MSTest.Sdk then adds `Microsoft.NET.Test.Sdk`:

```xml
<PropertyGroup>
  <UseVSTest>true</UseVSTest>
</PropertyGroup>
```

Keep the resolved exact `MSTest.Sdk` v4 pin from Step 2; this option changes the runner, not the
selected MSTest version or target framework.

**Option C -- Switch CI to `dotnet test`**: Replace `vstest.console` invocations
only after configuring the SDK's MTP integration. Choose the mode from the
repository/CI SDK version, not the test project's target framework:

- **.NET 8/9 SDKs**: Ensure the effective property below is set for the test
  project (in its project file or applicable `Directory.Build.props`):

  ```xml
  <PropertyGroup>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
  </PropertyGroup>
  ```

  Keep build arguments before `--` and pass MTP arguments after it.
- **.NET 10+ SDK**: Before changing `global.json`, inventory every `dotnet test`
  invocation governed by that file, including solution-level CI commands.
  Check every affected test project and target framework for MTP support,
  including the effective `UseVSTest` value after conditional/imported properties
  are evaluated. Native MTP cannot run VSTest-only projects; an `MSTest.Sdk`
  project with effective `UseVSTest=true` is not ready for this mode.

  Only when the affected test projects and target frameworks all support MTP,
  merge this `test` section into `global.json`, preserving existing SDK pins and
  other settings:

  ```json
  {
    "test": {
      "runner": "Microsoft.Testing.Platform"
    }
  }
  ```

  Pass MTP arguments directly, without `--`; the compatibility property above
  is not needed in this mode.

If any affected project or target framework must remain on VSTest, keep Option A
or B for that CI path instead of changing its shared `global.json`. Alternatively,
use a separately scoped `global.json` and CI invocation that selects only
MTP-compatible projects/target frameworks. Run that invocation from the intended
configuration's working directory; passing `--project` alone does not select a
different `global.json`. Keep the VSTest jobs in their original scope, and verify
both paths rather than dropping incompatible projects from CI.

Without the matching integration configuration, `dotnet test` may still use
VSTest and fail to discover the tests. Translate the existing CI arguments
rather than dropping filters, settings, or result publishing:

| Existing VSTest argument | MSTest on MTP |
|---|---|
| `/TestCaseFilter:<expression>` or `--filter <expression>` | `--filter <expression>`; preserve the MSTest filter expression unchanged |
| `/Logger:trx` or `--logger trx` | `--report-trx`; requires `Microsoft.Testing.Extensions.TrxReport` |
| TRX logger's `LogFileName=<name>` | `--report-trx-filename <name>`; retain unique filenames per project/TFM when sharing a results directory |
| `/ResultsDirectory:<dir>` or `--results-directory <dir>` | `--results-directory <dir>`; preserve the directory consumed by CI artifact publishing |
| `/Settings:<file>` or `--settings <file>` | `--settings <file>`; MSTest still supports `.runsettings` |
| `/ListTests` or `--list-tests` | `--list-tests` |
| `--collect "Code Coverage;Format=cobertura"` | `--coverage --coverage-output-format cobertura`; requires `Microsoft.Testing.Extensions.CodeCoverage` |
| `--blame-crash` | `--crashdump`; requires `Microsoft.Testing.Extensions.CrashDump` |
| `--blame-hang` | `--hangdump`; requires `Microsoft.Testing.Extensions.HangDump` |

Ensure each required extension is included by the `MSTest.Sdk` profile or an
explicit compatible package reference, following the exact-version and Central
Package Management rules above. Preserve existing build arguments such as
`--configuration` and `--framework`. For any other VSTest option or logger
parameter, confirm its replacement with the MTP test application's `--help`;
do not silently discard unsupported CI behavior.

For example, after configuring the selected mode and TRX extension:

```shell
# .NET 8/9 SDKs
dotnet test --configuration Release -- --filter "TestCategory=Smoke" --report-trx --results-directory TestResults

# .NET 10+ SDK in native MTP mode
dotnet test --configuration Release --filter "TestCategory=Smoke" --report-trx --results-directory TestResults
```

Run the actual translated CI command, compare selected test counts with the
baseline, and verify that the expected TRX/coverage/dump artifacts exist where
CI publishes them. A zero-test run is not proof of a successful migration.

Do not say this behavior predates v4: removal of the transitive
`Microsoft.NET.Test.Sdk` reference in MTP mode is one of the v4 behavioral
breaking changes.

### 4.6 Analyzer severity changes

Multiple analyzers have been upgraded from Info to Warning by default:

- MSTEST0001, MSTEST0007, MSTEST0017, MSTEST0023, MSTEST0024, MSTEST0025
- MSTEST0030, MSTEST0031, MSTEST0032, MSTEST0035, MSTEST0037, MSTEST0045

Review and fix any new warnings, or suppress them in `.editorconfig` if intentional.

### 4.7 Discovery fails when test-assembly types cannot load

In v3, a `ReflectionTypeLoadException` during discovery produced a warning and
MSTest continued with the types that loaded. In v4, discovery of that assembly
fails. When there is one loader exception, MSTest reports it directly. Fix the
underlying load failure, commonly a missing or mismatched dependency, rather
than treating the change as a test-discovery setting.

### 4.8 Inherited attributes are always read

MSTest v4 always includes inherited attributes in lookups, including custom
`ITestDataSource` attributes applied to an overridden method. Built-in
attributes that should not inherit declare `Inherited = false`. If a custom
attribute should not apply to derived classes or overrides, add
`Inherited = false` to its `AttributeUsage`.

### 4.9 Default display name for custom data sources

If a custom `ITestDataSource.GetDisplayName` returns `null`, v4 generates a
default display name that includes the data-row arguments. V3 used the test
method's display name instead. Return an explicit name from custom data sources
when the desired display name should not include row arguments.
