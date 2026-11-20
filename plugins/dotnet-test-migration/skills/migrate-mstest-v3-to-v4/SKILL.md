---
name: migrate-mstest-v3-to-v4
description: >
  Use this skill before answering, planning, or editing any MSTest 3.x-to-4.x
  upgrade or post-upgrade failure. Triggers include "MSTest v4 breaking
  changes"; CS0507/CS0103/CS1061/CS1615; ExecuteAsync, CallerInfo, DisplayName,
  or custom TestMethodAttribute; ClassCleanupBehavior; ContainsKey;
  ThrowsExactly or ExpectedException; IsInstanceOfType out parameters;
  TestTimeout.Infinite; removed TestContext properties; ITestMethod changes;
  StringAssert overloads; MSTEST0056/MSTEST0057; renamed assemblies or
  executor URI; net6/net7 compatibility; TestCase.Id history; TestName in
  ClassInitialize; TreatDiscoveryWarningsAsErrors; ReflectionTypeLoadException
  during discovery; inherited test attributes; custom data-source display names;
  and MSTest.Sdk/MTP or vstest.console discovery changes. Do not use for
  v1/v2-to-v3 leftovers, framework conversion, runner-only migration, or a
  general .NET upgrade.
license: MIT
---

# MSTest v3 -> v4 Migration

Migrate a test project from MSTest v3 to MSTest v4. The outcome is a project using MSTest v4 that builds cleanly, passes tests, and accounts for every source-incompatible and behavioral change. MSTest v4 is **not binary compatible** with MSTest v3 -- any library compiled against v3 must be recompiled against v4.

## First Action

Inspect the supplied project and source before searching the web or answering
from memory. Classify the request as a focused source fix, runtime behavior
change, CI discovery issue, compatibility question, or full migration, then
follow the matching row below. A clean compile does not exclude this skill:
discovery failures, `TestContext` lifecycle exceptions, and test-history
changes are runtime migration failures.

## When to Use

- Upgrading `MSTest.TestFramework`, `MSTest.TestAdapter`, or `MSTest` metapackage from 3.x to 4.x
- Upgrading `MSTest.Sdk` from 3.x to 4.x
- Fixing build errors after updating to MSTest v4 packages
- Resolving behavioral changes in test execution after upgrading to MSTest v4
- Updating custom `TestMethodAttribute` or `ConditionBaseAttribute` implementations for v4

## When Not to Use

- The project already uses MSTest v4 with verified discovery and test execution, and has no remaining migration-related issues
- Upgrading from MSTest v1 or v2 -- use `migrate-mstest-v1v2-to-v3` first, then return here
- The project does not use MSTest
- Migrating between test frameworks (e.g., MSTest to xUnit or NUnit)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | No | The `.csproj`, `.sln`, or `.slnx` entry point containing MSTest test projects. **Discover it yourself** by globbing the working directory; ask only when nothing is found or the choice is genuinely ambiguous |
| Build command | No | How to build (e.g., `dotnet build`, a repo build script). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |

## Decisions That Change the Result

| Detected request or state | Required action |
|---|---|
| Files are supplied in the current workspace | Search there and open the literal returned paths. The skill directory is not the project directory. If one tool rejects a valid path, retry with another available reader/editor; do not ask the user for a path you can discover. |
| User asks to apply changes to supplied files: "fix my project/files", "please update this source", "make the changes", or "then build and run" | Edit every affected occurrence. Run the narrowest meaningful build/test command against the actual package version; skill activation is not a reason to stop at advice. |
| User asks "what should I expect?", "how do I fix these changes?", for compatibility advice, or for a plan | Answer directly from the actual project state even when source is visible. Keep a single-symptom answer focused; include only adjacent risks that change the decision. |
| Unsupported TFM in a full migration | Update the TFM first, then update MSTest packages, then fix source breaks. Do not bury this order in a release-note inventory. |
| Custom `TestMethodAttribute` subclass | Treat `ExecuteAsync`, CallerInfo propagation, display-name handling, and the subclass's retry/result semantics as one coupled migration. Fix the actual class, not a placeholder example. |
| `MSTest.Sdk` v4 source/API errors (`ManagedType`, `TestTimeout`, `Contains`) | Give the exact source replacements, then add the adjacent runner warning: MTP mode no longer supplies `Microsoft.NET.Test.Sdk`; add it only if VSTest discovery is still required. |
| `MSTest.Sdk` v4 plus `vstest.console` | MTP mode no longer includes `Microsoft.NET.Test.Sdk` in v4. Add it to retain transitional VSTest discovery while keeping MTP, opt into `UseVSTest`, or migrate CI to `dotnet test`. Native MTP requires every project/TFM governed by `global.json` to support it (4.5). |

## Response Guidelines

- **Always identify the current version first**: Before recommending any migration steps, explicitly state the current MSTest version detected in the project (e.g., "Your project uses MSTest v3 (3.8.0)"). This confirms you've read the project files and grounds the migration advice.
- **Resolve, do not assume, the target version**: When the user asks for "latest", query the project's configured package source and select the latest stable MSTest v4 version available at execution time. Never copy the example version from this skill into the result without checking it. Keep all MSTest packages on the same resolved version.
- **Focused fix requests**: Address only matching Step 3 changes with concrete code using the user's types and names. Edit only when a source change is requested; "how do I fix these?" remains an answer request. A green v3 build does not verify v4: update packages when requested or state that limitation. For custom attributes, show the full fixed class with CallerInfo propagation and preserved retry/result semantics. Mention relevant analyzers (e.g., MSTEST0006 for ExpectedException). For `MSTest.Sdk`, explain whether losing the `Microsoft.NET.Test.Sdk` reference affects the visible runner. Skip the full migration workflow.
- **"What to expect" questions** (user asks about breaking changes before upgrading): Present ALL major breaking changes from the Step 3 quick-lookup table -- not just the ones visible in the current code. For each, provide a one-line fix summary. Also mention key behavioral changes from Step 4 (especially TestCase.Id history impact and TreatDiscoveryWarningsAsErrors default). If project code is available, highlight which changes apply directly.
- **Before-upgrade analyzer guidance**: Recommend fixing MSTEST0006, MSTEST0039, and MSTEST0053 warnings while still on v3 because those analyzers are not available in v4. Mention MSTEST0034 when class-cleanup timing is relevant. For `TestMethodAttribute` constructor issues, include MSTEST0056 for display-name arguments and MSTEST0057 for custom attributes that fail to propagate caller info.
- **Full migration requests** (user wants complete migration): Follow the complete workflow below.
- **Behavioral/runtime symptoms**: Match symptoms to Step 4 and give targeted advice, including adjacent behavioral risks. Skip source-breaking changes unless there are also build errors.
- **CI/test-discovery issues**: Match the failure first: resolve loader errors (4.7) rather than suppressing warnings (4.4). For `MSTest.Sdk` v4 plus failing `vstest.console` discovery, explain the missing `Microsoft.NET.Test.Sdk` reference (4.5) and all three paths: add it while preserving MTP, set `UseVSTest`, or switch CI to MTP-native `dotnet test`. Skip the full migration workflow.
- **Explanatory questions** (user asks "is this a known change?", "what else should I watch out for?"): Explain the relevant changes and advise. Mention related changes the user might encounter next. Do not prescribe a full migration procedure.
- **Result proof**: End implementation work with the detected v3 version, resolved v4 version, runner choice, files changed, and actual build/test counts. Never report a build, VSTest compatibility, discovery, or passing tests from inference.

## Workflow

> **Commit strategy:** Do not create commits unless the user asks. Keep package,
> source, and behavioral changes logically separable in the diff, but finish and
> verify the requested migration.

### Step 1: Assess the project

1. Identify the current MSTest version by checking package references for `MSTest`, `MSTest.TestFramework`, `MSTest.TestAdapter`, or `MSTest.Sdk` in `.csproj`, `Directory.Build.props`, or `Directory.Packages.props`.
2. Confirm the starting state: v3 for an upgrade, or v4 for a post-upgrade fix. If already on v4, skip Step 2 unless package changes are needed for the reported issue. If on v1 or v2, use `migrate-mstest-v1v2-to-v3` first.
3. Check target framework(s) -- MSTest v4 drops support for .NET Core 3.1 through .NET 7. Supported target frameworks are: **net8.0**, **net9.0**, **net462** (.NET Framework 4.6.2+), **uap10.0.16299** (UWP), **net9.0-windows10.0.17763.0** (modern UWP), and **net8.0-windows10.0.18362.0** (WinUI).
4. Check for custom `TestMethodAttribute` subclasses and uses of `DataTestMethodAttribute`, `STATestMethodAttribute`, and `UITestMethodAttribute` constructors -- these require changes in v4.
5. Check for usages of `ExpectedExceptionAttribute` -- removed in v4 (deprecated since v3 with analyzer MSTEST0006).
6. Check for usages of `Assert.ThrowsException` (deprecated) -- removed in v4.
7. Search for removed `TestContext` properties, changed `ITestMethod` members, removed cleanup settings, and custom data-source interfaces or overrides listed in Step 3.
8. If code or tools reference MSTest assemblies by name/version or the VSTest executor URI, inventory those references; check whether the test adapter relies on its former `netstandard2.0` asset.
9. Run a clean build to establish a baseline of existing errors/warnings.

### Step 2: Update packages to MSTest v4

First resolve the latest stable v4 version from the configured package source. Pin that exact
version consistently in the metapackage, individual packages, `MSTest.Sdk`, and central package
management.

- For the `MSTest` metapackage, update its `PackageReference` to the resolved exact version.
- For individual packages, update `MSTest.TestFramework` and `MSTest.TestAdapter` to that same version.
- For `MSTest.Sdk`, update the SDK version in the project or `global.json` pin to that same version.

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
| `UnfoldingStrategy` on `DataRow` or `DynamicData` | Property moved | Set it on `TestMethodAttribute` instead (3.10) |
| `ConditionBaseAttribute.ShouldRun` | Property renamed | Use `IsConditionMet` (3.11) |
| Public adapter or PlatformServices types | Types internalized or removed | Remove dependencies on those implementation APIs; distinguish the two `ITestMethod` interfaces (3.12) |
| `DataTestMethod`, `STATestMethod`, or `UITestMethod` display-name constructor | `displayName` constructor removed | Set the `DisplayName` property instead (3.13) |
| `ITestMethod.Invoke` or `GetAllAttributes(..., inherit)` | TestFramework interface changed | Use `InvokeAsync`; remove the `inherit` argument because inherited attributes are always included (3.14) |
| Removed `TestContext.TestDir`, `TestDeploymentDir`, `TestLogsDir`, or `ManagedMethod` | Deprecated properties removed | Use the appropriate replacement in 3.15 |
| `ClassCleanupExecutionAttribute`, runsettings `ClassCleanupLifecycle`, or JSON `classCleanupLifecycle` | Assembly attribute and settings removed | Remove the attribute/settings; v4 always uses end-of-class cleanup (3.16) |
| `StringAssert` message and comparison arguments, or renamed named arguments | Assertion signatures changed | Reorder `StringComparison` before `message`; update renamed parameters (3.17) |
| `Assert.ReplaceNullChars`, `ExpectedExceptionBaseAttribute`, `TestIdGenerationStrategy`, CSS attributes, or `InternalTestFailureException` | API/type removed | Remove or replace the usage as described in 3.17 |
| Assembly-name or executor-URI lookup fails | MSTest assemblies and executor URI renamed | Update references to the v4 names and `executor://MSTestAdapter/v4` (3.18) |
| `ITestDataSourceUnfoldingCapability` or custom `IgnoreMessage` override | Interface removed / property no longer virtual | Set `UnfoldingStrategy` on `TestMethodAttribute`; set `IgnoreMessage` without overriding (3.19) |

> **Important**: Scan the entire project for ALL patterns above before starting fixes. Multiple breaking changes often coexist in the same project.

Before applying a matching fix, read the relevant numbered section in the
[breaking-change reference](references/mstest-v3-to-v4-breaking-changes.md).
Resolve this path relative to this `SKILL.md`, not the user's project. If it is
missing, list the known `references` directory at most once and report the
missing guidance rather than silently proceeding with reduced coverage.

For custom test-method attributes, update both `ExecuteAsync` and caller-info
propagation (3.1-3.2), preserving the existing retry and result semantics.
Do not replace a retry implementation with a pass-through call to the base method.

For display names, replace positional strings with the named property:

```csharp
[TestMethod(DisplayName = "custom name")]
```

### Step 4: Address behavioral changes

These changes won't cause build errors but may affect test runtime behavior.

| Symptom | Cause | Fix |
|---|---|---|
| Tests show as new in Azure DevOps / test history lost | `TestCase.Id` generation changed (4.3) | No code fix; history will re-baseline |
| `TestContext.TestName` throws in `[ClassInitialize]` | v4 enforces lifecycle scope (4.2) | Move access to `[TestInitialize]` or test methods |
| Tests not discovered / discovery failures | `TreatDiscoveryWarningsAsErrors` now true (4.4) | Fix warnings, or set to false in .runsettings |
| .NET Framework tests hang under MTP | AppDomain unloading no longer aborts foreground threads by default (4.1) | Stop lingering threads, or enable required isolation with `DisableAppDomain = false` under .runsettings `RunConfiguration` |
| vstest.console can't find tests with MSTest.Sdk after the v4 upgrade | MSTest.Sdk defaults to MTP; v4 stopped adding `Microsoft.NET.Test.Sdk` in MTP mode (4.5) | Add an explicit package while preserving MTP, set `UseVSTest`, or switch CI to `dotnet test` |
| New warnings from analyzers | Analyzer severities upgraded (4.6) | Fix warnings or suppress in .editorconfig |
| Discovery fails with `ReflectionTypeLoadException` | v4 fails discovery when any types in the test assembly cannot load | Fix the underlying loader error, often a missing or mismatched dependency (4.7) |
| A custom inherited data-source attribute now applies to overrides | v4 always includes inherited attributes | Set `Inherited = false` in the custom attribute's `AttributeUsage` if that is not intended (4.8) |
| Custom data rows have a different display name | A null `ITestDataSource.GetDisplayName` result now falls back to a name containing row arguments | Return an explicit display name if the old method-name fallback is required (4.9) |

Read the [breaking-change reference](references/mstest-v3-to-v4-breaking-changes.md) for the relevant runtime
configuration, discovery, attribute-inheritance, and custom data-source details.

### Step 5: Verify

1. Run `dotnet build` -- confirm zero errors and review any new warnings
2. Run `dotnet test` -- confirm all tests pass
3. Compare test results (pass/fail counts) to the pre-migration baseline
4. If using Azure DevOps test tracking, be aware that `TestCase.Id` changes may affect history continuity
5. Check that no tests were silently dropped due to stricter discovery
6. Check custom data-source inheritance and display-name behavior, and ensure the new discovery failure mode does not hide a loader error

## Validation

- [ ] All MSTest packages updated to 4.x
- [ ] Project builds with zero errors
- [ ] All tests pass with `dotnet test`
- [ ] Custom `TestMethodAttribute` subclasses updated for `ExecuteAsync` and CallerInfo
- [ ] `ExpectedExceptionAttribute` replaced with `Assert.ThrowsExactly`
- [ ] `Assert.ThrowsException` replaced with `Assert.ThrowsExactly` (or `Assert.Throws`)
- [ ] `ClassCleanupBehavior` enum usages removed
- [ ] `TestContext.Properties.Contains` updated to `ContainsKey`
- [ ] Removed `TestContext` properties replaced with the correct directory/name property
- [ ] TestFramework `ITestMethod` callers use `InvokeAsync` and updated attribute lookup signatures
- [ ] Removed cleanup attributes/settings, assertion APIs, and custom data-source interfaces addressed
- [ ] Assembly names, executor URI, and adapter target compatibility checked where referenced
- [ ] All target frameworks are net8.0+, net9.0, net462+, uap10.0.16299, or WinUI
- [ ] Behavioral changes reviewed and addressed
- [ ] Discovery loader failures, inherited custom attributes, and custom data-source names reviewed
- [ ] No tests were lost during migration (compare test counts)

## Related Skills

- `writing-mstest-tests` -- for modern MSTest v4 assertion APIs and test authoring best practices
- `run-tests` -- for running tests after migration

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Custom `TestMethodAttribute` still overrides `Execute` | Change to `ExecuteAsync` returning `Task<TestResult[]>` |
| `TestMethodAttribute("display name")` is silently ignored | Use `TestMethodAttribute(DisplayName = "display name")` |
| `ClassCleanupBehavior` enum not found | Remove the enum argument; `[ClassCleanup]` now always runs at end of class. For end-of-assembly cleanup, use `[AssemblyCleanup]` |
| `TestContext.Properties.Contains` missing | Use `ContainsKey` -- `Properties` is now `IDictionary<string, object>` |
| `ExpectedException` attribute not found | Replace with `Assert.ThrowsExactly<T>(() => ...)` inside the test body |
| `Assert.ThrowsException` not found | Replace with `Assert.ThrowsExactly` (or `Assert.Throws` for derived types) |
| `Assert.AreEqual` with format string args fails | Use string interpolation: `$"message {value}"` |
| `TestContext.TestDeploymentDir` or `TestContext.ManagedMethod` is missing | Use `DeploymentDirectory` or `TestName`; verify that the replacement preserves the required semantics |
| `ITestMethod.Invoke` is missing | Use `InvokeAsync`; update attribute lookup calls to omit the `inherit` parameter |
| `vstest.console` or assembly lookup stops matching after upgrade | Update renamed assembly references and the executor URI to v4 |
| Custom `ITestDataSource` now applies to an override | Set `Inherited = false` in the attribute's `AttributeUsage` if inheritance is not intended |
| Discovery fails after a loader warning in v3 | Resolve the `ReflectionTypeLoadException`, commonly by fixing a missing or mismatched dependency |
| A custom data-source test gets a different display name | Return a non-null name from `GetDisplayName` when row arguments should not appear |
| .NET Framework tests hang under MTP | Stop lingering foreground threads or enable required AppDomain isolation; this default change is specific to MTP |
| Azure DevOps test history breaks | Expected -- `TestCase.Id` generation changed; no code fix, results will re-baseline |
| Discovery warnings now fail the run | `TreatDiscoveryWarningsAsErrors` is true by default; fix the discovery warnings |
| Net6.0/net7.0 targets don't compile | Update to net8.0 -- MSTest v4 supports net8.0, net9.0, net462, uap10.0.16299, modern UWP, and WinUI |
