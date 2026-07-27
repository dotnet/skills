---
name: migrate-mstest-v1v2-to-v3
description: >
  Migrate MSTest v1 or v2 test projects to MSTest v3. Use when the user asks
  to upgrade MSTest and the project has QualityTools assembly references,
  MSTest.TestFramework/TestAdapter 1.x-2.x, .testsettings, or migration errors
  after changing those packages to 3.x.
  USE FOR: upgrading from MSTest v1 assembly references
  (Microsoft.VisualStudio.QualityTools.UnitTestFramework) or MSTest v2 NuGet
  (MSTest.TestFramework 1.x-2.x) to MSTest v3, fixing assertion overload
  errors (AreEqual/AreNotEqual), updating DataRow constructors, replacing or
  migrating .testsettings to .runsettings, timeout behavior changes, target framework
  compatibility (.NET 5 dropped -- use .NET 6+; .NET Fx older than 4.6.2 dropped),
  adopting MSTest.Sdk while moving from v1/v2.
  First step toward MSTest v4 -- after this, use migrate-mstest-v3-to-v4.
  DO NOT USE FOR: migrating to MSTest v4 (use migrate-mstest-v3-to-v4),
  projects already on MSTest v3+, migrating between test frameworks, generic
  test modernization, or .NET upgrades unrelated to MSTest.
license: MIT
---

# MSTest v1/v2 -> v3 Migration

Migrate a test project from MSTest v1 (assembly references) or MSTest v2 (NuGet 1.x-2.x) to MSTest v3. MSTest v3 is **not binary compatible** with v1/v2 -- libraries compiled against v1/v2 must be recompiled.

## When to Use

- Project references `Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll` (MSTest v1)
- Project uses `MSTest.TestFramework` / `MSTest.TestAdapter` NuGet 1.x or 2.x
- Resolving build errors after updating MSTest packages from v1/v2 to v3
- Replacing `.testsettings` with `.runsettings`
- Adopting MSTest.Sdk or in-assembly parallel execution

## When Not to Use

- Project already on MSTest v3 with no migration-related build errors (fully migrated)
- Upgrading v3 to v4 -- use `migrate-mstest-v3-to-v4`
- Migrating between frameworks (MSTest to xUnit/NUnit)

## Boundary Gate

Check package versions before any edit. If all MSTest references are already 3.x
and no v1/v2-to-v3 error is reported, state that migration is complete and make
no changes. Do not consolidate working v3 packages into the metapackage. Run the
existing tests only if verification was requested. This overrides all steps below.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or `.slnx` entry point containing MSTest test projects |
| Build command | No | How to build (e.g., `dotnet build`, a repo build script). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |

## Breaking Changes Summary

MSTest v3 introduces these breaking changes from v1/v2. Address only the ones relevant to the project:

| Breaking Change | Impact | Fix |
|---|---|---|
| `Assert.AreEqual(object, object)` overload removed | Compile error on untyped assertions | Add generic type: `Assert.AreEqual<T>(expected, actual)`. Same for `AreNotEqual`, `AreSame`, `AreNotSame` |
| `DataRow` strict type matching | Runtime/compile errors when argument types don't match parameter types exactly | Change literals to exact types: `1` for int, `1L` for long, `1.0f` for float |
| `DataRow` max 16 constructor parameters (early v3) | Compile error if >16 args; fixed in later v3 versions | Update to latest 3.x, or refactor test / wrap extra params in array |
| `.testsettings` / `<LegacySettings>` no longer supported | Settings silently ignored | Delete `.testsettings`, create `.runsettings` with equivalent config |
| Timeout behavior unified across .NET Core / Framework | Tests with `[Timeout]` may behave differently | Verify timeout values; adjust if needed |
| Dropped target frameworks: .NET 5, .NET Fx < 4.6.2, netstandard1.0, UWP < 16299, WinUI < 18362 | Build error | Update TFM: .NET 5 -> net8.0 (LTS) or net6.0+, netfx -> net462+, netstandard1.0 -> netstandard2.0. Note: net6.0, net8.0, net9.0 are all supported |
| Not binary compatible with v1/v2 | Libraries compiled against v1/v2 must be recompiled | Recompile all dependencies against v3 |
| Test ID generation changed | Playlists, filters, or CI history keyed by test ID may reset | Re-baseline IDs and verify affected filters |
| `TargetInvocationException` is unwrapped | Tests or infrastructure expecting the wrapper observe the inner exception | Update exception handling to expect the underlying exception |
| Initialization/cleanup messages now attach to test results | The first/last test output may gain lifecycle messages that were previously absent | Update log processing and inspect the first/last test results |
| Deployment directory behavior is unified across TFMs | Tests with hard-coded deployment paths may fail | Use `TestContext.DeploymentDirectory` or deployed-item paths instead of assumptions |
| Nullable annotations were added | Nullable-enabled projects may gain warnings | Fix the warnings without suppressing unrelated diagnostics |

## Response Guidelines

- **Always identify the current version first**: Before recommending any migration steps, explicitly state the current MSTest version detected in the project (e.g., "Your project uses MSTest v2 (2.2.10)" or "This is an MSTest v1 project using QualityTools assembly references"). This grounds the migration advice and confirms you've read the project files.
- **Require project evidence**: Do not assume v1/v2 from the wording alone. Read project or central package files and classify the source as QualityTools/v1, NuGet 1.x, or NuGet 2.x. If the project is already on v3+, stop and route to the appropriate skill.
- **Preserve the test platform**: Keep VSTest or MTP unchanged during the framework upgrade unless the user separately requests a runner migration.
- **Execute full migrations**: When the user asks you to migrate or upgrade the project, edit the files, build, and run tests. Do not stop after listing breaking changes. Advice-only responses are appropriate only when the user asks what to expect.
- **Focused fix requests** (user has specific compilation errors after upgrading): Address only the relevant breaking change from the table above. Show a concise before/after fix. Do not walk through the full migration workflow.
- **DataRow fix requests**: Compare every supplied `DataRow` with its method signature. Mismatches can build with only `MSTEST0014` and fail during test execution. Preserve the method contract and normally fix the literal (`1L` -> `1` for `int`), then run the affected tests.
- **Specific feature migration** (user asks about one aspect like .testsettings, DataRow, or assertions): Address only that feature, but handle every active setting or affected usage in the supplied files. For `.testsettings`, put all MSTest settings under one `<MSTest>` element, map requested deployment, per-test timeout, data collector, and other active configuration, and do not add a session-wide timeout. Do not walk through unrelated breaking changes.
- **"What to expect" questions** (user asks about breaking changes before upgrading): First state the concrete package update needed to reach v3, then summarize every category in the Breaking Changes Summary, marking which ones directly apply to the visible project. Keep each item to one line and do not expand into release-note history.
- **Full migration requests** (user wants complete migration): Follow the complete workflow below.
- **Comparison questions** (user asks about v1 vs v2 differences): Explain concisely -- v1 uses assembly references and requires removing them first; v2 uses NuGet and just needs a version bump. Both converge on the same v3 packages and breaking changes.
- **Keep execution project-specific**: For fixes and full migrations, change only patterns found in the visible code/configuration. Broader coverage is reserved for explicit "what should I expect?" questions.

## Migration Paths

- **MSTest v1 (assembly reference to QualityTools)**: Remove the assembly reference (Step 2), add v3 NuGet packages (Step 3), fix breaking changes (Step 5).
- **MSTest v2 (NuGet packages 1.x-2.x)**: Update package versions to 3.x (Step 3), fix breaking changes (Step 5). No assembly reference removal needed.

Both paths converge at Step 3 -- the same v3 packages and breaking changes apply regardless of starting version.

## Workflow

### Step 1: Assess the project

1. In one discovery pass, batch-read project and central configuration files, search for affected APIs/settings, and identify which MSTest version is currently in use:
   - **Assembly reference**: Look for `Microsoft.VisualStudio.QualityTools.UnitTestFramework` in project references -> MSTest v1
   - **NuGet packages**: Check `MSTest.TestFramework` and `MSTest.TestAdapter` package versions -> v1 if 1.x, v2 if 2.x
2. Check whether the target framework is dropped in v3 (see Step 4).
3. Run the existing test command. Record discovered, passed, failed, and skipped counts as the parity baseline.

### Step 2: Remove v1 assembly references (if applicable)

If the project uses MSTest v1 via assembly references:

1. Remove the reference to `Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll`
   - In SDK-style projects, remove the `<Reference>` element from the `.csproj`
   - In non-SDK-style projects, remove via Visual Studio Solution Explorer -> References -> right-click -> Remove
2. Save the project file

### Step 3: Update packages to MSTest v3

Use one package model; do not leave duplicate framework/adapter references.

**Default -- install the MSTest metapackage:**

Remove individual `MSTest.TestFramework` and `MSTest.TestAdapter` package references and replace with the unified `MSTest` metapackage:

```xml
<PackageReference Include="MSTest" Version="3.8.0" />
```

Keep `Microsoft.NET.Test.Sdk` when the project remains on VSTest, but update it to a version compatible with the selected MSTest release. For example, `MSTest` 3.8.0 requires `Microsoft.NET.Test.Sdk` 17.13.0 or later; leaving an older explicit version causes `NU1605`. If package versions are centrally managed, update `Directory.Packages.props` rather than adding inline versions.

**Use MSTest.Sdk only when the user requests it or the repository already standardizes on it (SDK-style projects only):**

Change `<Project Sdk="Microsoft.NET.Sdk">` to `<Project Sdk="MSTest.Sdk/3.8.0">`. MSTest.Sdk automatically provides the MSTest framework, adapter, and analyzers.

> **Important**: MSTest.Sdk defaults to Microsoft.Testing.Platform (MTP). When preserving VSTest, set `<UseVSTest>true</UseVSTest>`; the SDK then supplies the required `Microsoft.NET.Test.Sdk` reference. Do not switch runners merely as a side effect of the framework upgrade.

When switching to MSTest.Sdk, remove these (SDK provides them automatically):

- **Packages**: `MSTest`, `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers`, `Microsoft.NET.Test.Sdk`
- **Properties**: `<EnableMSTestRunner>`, `<OutputType>Exe</OutputType>`, `<IsPackable>false</IsPackable>`, `<IsTestProject>true</IsTestProject>`

### Step 4: Update target frameworks if needed

MSTest v3 supports .NET 6+, .NET Core 3.1, .NET Framework 4.6.2+, .NET Standard 2.0, UWP 16299+, and WinUI 18362+. .NET Core 3.1 is end-of-life but remains supported by MSTest v3; preserve it during this framework-only migration and recommend a separate runtime upgrade. If the project targets a framework version dropped by MSTest v3, update to a supported one:

| Dropped | Recommended replacement |
|---------|------------------------|
| .NET 5 | .NET 8.0 (current LTS) or .NET 6+ |
| .NET Framework < 4.6.2 | .NET Framework 4.6.2 |
| .NET Standard 1.0 | .NET Standard 2.0 |
| UWP < 16299 | UWP 16299 |
| WinUI < 18362 | WinUI 18362 |

> **Note**: .NET 6, .NET 8, and .NET 9 are all supported by MSTest v3. Do not change TFMs that are already supported.

### Step 5: Resolve build errors and breaking changes

Search the supplied files first and fix only breaking changes that are present.
A successful build does not prove compatibility; some failures surface only as
analyzer warnings or during test execution.

**Assertion overloads** -- MSTest v3 removed `Assert.AreEqual(object, object)` and `Assert.AreNotEqual(object, object)`. Add explicit generic type parameters:

```csharp
// Before (v1/v2)                           // After (v3)
Assert.AreEqual(expected, actual);        -> Assert.AreEqual<MyType>(expected, actual);
Assert.AreNotEqual(a, b);                -> Assert.AreNotEqual<MyType>(a, b);
Assert.AreSame(expected, actual);         -> Assert.AreSame<MyType>(expected, actual);
```

**DataRow strict type matching** -- argument types must exactly match parameter types. Implicit conversions that worked in v2 fail in v3:

```csharp
// Error: 1L (long) won't convert to int parameter -> fix: use 1 (int)
// Error: 1.0 (double) won't convert to float parameter -> fix: use 1.0f (float)
```

Preserve method parameter types unless independently wrong. `dotnet build` may
succeed with `MSTEST0014`; run the test to prove each row binds and executes.

**Timeout behavior** -- unified across .NET Core and .NET Framework. Verify `[Timeout]` values still work.

### Step 6: Replace .testsettings with .runsettings

The `.testsettings` file and `<LegacySettings>` are no longer supported in MSTest v3. **Delete the `.testsettings` file** and create a `.runsettings` file -- do not keep both. Consolidate all MSTest configuration under one `<MSTest>` element; do not create an `<MSTestV2>` section.

Key mappings:

| .testsettings | .runsettings equivalent |
|---|---|
| `TestTimeout` property | `<MSTest><TestTimeout>30000</TestTimeout></MSTest>` |
| Deployment config | `<MSTest><DeploymentEnabled>true</DeploymentEnabled></MSTest>` or remove |
| Assembly resolution settings | Remove -- not needed in modern .NET |
| Data collectors | `<DataCollectionRunSettings><DataCollectors>` section |

> **Important**: Map timeout to `<MSTest><TestTimeout>` (per-test), **not** `<TestSessionTimeout>` (session-wide). Remove `<LegacySettings>` entirely.

### Step 7: Verify

1. Run the same test command, filter, and configuration used for the baseline. `dotnet test` builds by default; run a separate build only to isolate a compilation failure.
2. Compare discovered, passed, failed, and skipped counts to the pre-migration baseline.
3. Investigate every count difference; do not accept silently dropped tests or data rows.
4. Confirm no QualityTools reference, 1.x/2.x MSTest package, `.testsettings`, or `<LegacySettings>` remains.

## Validation

- [ ] MSTest v3 packages (or MSTest.Sdk) correctly referenced; v1/v2 references removed
- [ ] Project builds with zero errors
- [ ] All tests pass (`dotnet test`) -- compare pass/fail counts to pre-migration baseline
- [ ] `.testsettings` replaced with `.runsettings` (if applicable)

## Next Step

After v3 migration, use `migrate-mstest-v3-to-v4` for MSTest v4.

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Non-MSTest.Sdk VSTest project missing `Microsoft.NET.Test.Sdk` | Add the package reference for VSTest discovery |
| MSTest.Sdk tests not found by `vstest.console` | Set `<UseVSTest>true</UseVSTest>`; MSTest.Sdk then supplies `Microsoft.NET.Test.Sdk` |
