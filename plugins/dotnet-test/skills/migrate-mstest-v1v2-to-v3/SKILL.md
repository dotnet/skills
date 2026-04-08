---
name: migrate-mstest-v1v2-to-v3
description: >
  Migrate MSTest v1 or v2 test project to MSTest v3. Use when user says
  "upgrade MSTest", "upgrade to MSTest v3", "migrate to MSTest v3",
  "update test framework", "modernize tests", "MSTest v3 migration",
  "MSTest compatibility", "MSTest v2 to v3", or build errors after
  updating MSTest packages from 1.x/2.x to 3.x.
  USE FOR: upgrading from MSTest v1 assembly references
  (Microsoft.VisualStudio.QualityTools.UnitTestFramework) or MSTest v2 NuGet
  (MSTest.TestFramework 1.x-2.x) to MSTest v3, fixing assertion overload
  errors (AreEqual/AreNotEqual), updating DataRow constructors, replacing
  .testsettings with .runsettings, timeout behavior changes, target framework
  compatibility (.NET 5 dropped -- use .NET 6+; .NET Fx older than 4.6.2 dropped),
  adopting MSTest.Sdk.
  First step toward MSTest v4 -- after this, use migrate-mstest-v3-to-v4.
  DO NOT USE FOR: migrating to MSTest v4 (use migrate-mstest-v3-to-v4),
  migrating between frameworks (MSTest to xUnit/NUnit), or general .NET
  upgrades unrelated to MSTest.
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

- Project already uses MSTest v3 (3.x packages)
- Upgrading v3 to v4 -- use `migrate-mstest-v3-to-v4`
- Migrating between frameworks (MSTest to xUnit/NUnit)

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

## Response Guidelines

- **Always identify the current version first**: Before recommending any migration steps, explicitly state the current MSTest version detected in the project (e.g., "Your project uses MSTest v2 (2.2.10)" or "This is an MSTest v1 project using QualityTools assembly references"). This grounds the migration advice and confirms you've read the project files.
- **Focused fix requests** (user has specific compilation errors after upgrading): Address only the relevant breaking change from the table above. Show a concise before/after fix. Do not walk through the full migration workflow.
- **Specific feature migration** (user asks about one aspect like .testsettings, DataRow, or assertions): Address only that specific aspect with a concrete fix. Do not walk through the entire migration workflow or unrelated breaking changes.
- **"What to expect" questions** (user asks about breaking changes before upgrading): Present only the breaking changes that are clearly relevant to the user's visible code and configuration. For each, give a one-line fix summary. Do not include every possible breaking change -- only the ones that apply. Do not walk through the full workflow.
- **Full migration requests** (user wants complete migration): Follow the complete workflow below.
- **Comparison questions** (user asks about v1 vs v2 differences): Explain concisely -- v1 uses assembly references and requires removing them first; v2 uses NuGet and just needs a version bump. Both converge on the same v3 packages and breaking changes.

## Migration Paths

- **MSTest v1 (assembly reference to QualityTools)**: Remove the assembly reference (Step 2), add v3 NuGet packages (Step 3), fix breaking changes (Step 5).
- **MSTest v2 (NuGet packages 1.x-2.x)**: Update package versions to 3.x (Step 3), fix breaking changes (Step 5). No assembly reference removal needed.

Both paths converge at Step 3 -- the same v3 packages and breaking changes apply regardless of starting version.

## Workflow

### Step 1: Assess the project

1. Identify which MSTest version is currently in use:
   - **Assembly reference**: Look for `Microsoft.VisualStudio.QualityTools.UnitTestFramework` in project references -> MSTest v1
   - **NuGet packages**: Check `MSTest.TestFramework` and `MSTest.TestAdapter` package versions -> v1 if 1.x, v2 if 2.x
2. Check if the project uses a `.testsettings` file (indicated by `<LegacySettings>` in test configuration)
3. Check if the target framework is dropped in v3 (see Step 4)
4. Run a clean build to establish a baseline of existing errors/warnings

### Step 2: Remove v1 assembly references (if applicable)

If the project uses MSTest v1 via assembly references:

1. Remove the reference to `Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll`
   - In SDK-style projects, remove the `<Reference>` element from the `.csproj`
   - In non-SDK-style projects, remove via Visual Studio Solution Explorer -> References -> right-click -> Remove
2. Save the project file

### Step 3: Update packages to MSTest v3

Choose one of these approaches:

**Option A -- Install the MSTest metapackage (recommended):**

Remove individual `MSTest.TestFramework` and `MSTest.TestAdapter` package references and replace with the unified `MSTest` metapackage:

```xml
<PackageReference Include="MSTest" Version="3.8.0" />
```

Also ensure `Microsoft.NET.Test.Sdk` is referenced (or update individual `MSTest.TestFramework` + `MSTest.TestAdapter` packages to 3.8.0 if you prefer not using the metapackage).

**Option B -- Use MSTest.Sdk (SDK-style projects only):**

Change `<Project Sdk="Microsoft.NET.Sdk">` to `<Project Sdk="MSTest.Sdk/3.8.0">`. MSTest.Sdk automatically provides MSTest.TestFramework, MSTest.TestAdapter, MSTest.Analyzers, and Microsoft.NET.Test.Sdk.

> **Important**: MSTest.Sdk defaults to Microsoft.Testing.Platform (MTP) instead of VSTest. For VSTest compatibility (e.g., `vstest.console` in CI), add `<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />`.

When switching to MSTest.Sdk, remove these (SDK provides them automatically):

- **Packages**: `MSTest`, `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers`, `Microsoft.NET.Test.Sdk`
- **Properties**: `<EnableMSTestRunner>`, `<OutputType>Exe</OutputType>`, `<IsPackable>false</IsPackable>`, `<IsTestProject>true</IsTestProject>`

### Step 4: Update target frameworks if needed

MSTest v3 supports .NET 6+, .NET Core 3.1, .NET Framework 4.6.2+, .NET Standard 2.0, UWP 16299+, and WinUI 18362+. If the project targets a dropped framework version, update to a supported one:

| Dropped | Recommended replacement |
|---------|------------------------|
| .NET 5 | .NET 8.0 (current LTS) or .NET 6+ |
| .NET Framework < 4.6.2 | .NET Framework 4.6.2 |
| .NET Standard 1.0 | .NET Standard 2.0 |
| UWP < 16299 | UWP 16299 |
| WinUI < 18362 | WinUI 18362 |

> **Note**: .NET 6, .NET 8, and .NET 9 are all supported by MSTest v3. Do not change TFMs that are already supported.

### Step 5: Resolve build errors and breaking changes

Run `dotnet build` and fix errors using the Breaking Changes Summary above. Key fixes:

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

**Timeout behavior** -- unified across .NET Core and .NET Framework. Verify `[Timeout]` values still work.

### Step 6: Replace .testsettings with .runsettings

The `.testsettings` file and `<LegacySettings>` are no longer supported in MSTest v3. **Delete the `.testsettings` file** and create a `.runsettings` file -- do not keep both.

Key mappings:

| .testsettings | .runsettings equivalent |
|---|---|
| `TestTimeout` property | `<MSTest><TestTimeout>30000</TestTimeout></MSTest>` |
| Deployment config | `<MSTest><DeploymentEnabled>true</DeploymentEnabled></MSTest>` or remove |
| Assembly resolution settings | Remove -- not needed in modern .NET |
| Data collectors | `<DataCollectionRunSettings><DataCollectors>` section |

> **Important**: Map timeout to `<MSTest><TestTimeout>` (per-test), **not** `<TestSessionTimeout>` (session-wide). Remove `<LegacySettings>` entirely.

### Step 7: Verify

1. Run `dotnet build` -- confirm zero errors and review any new warnings
2. Run `dotnet test` -- confirm all tests pass
3. Compare test results (pass/fail counts) to the pre-migration baseline
4. Check that no tests were silently dropped due to discovery changes

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
| Missing `Microsoft.NET.Test.Sdk` | Add package reference -- required for test discovery with VSTest |
| MSTest.Sdk tests not found by `vstest.console` | MSTest.Sdk defaults to Microsoft.Testing.Platform; add explicit `Microsoft.NET.Test.Sdk` for VSTest compatibility |
