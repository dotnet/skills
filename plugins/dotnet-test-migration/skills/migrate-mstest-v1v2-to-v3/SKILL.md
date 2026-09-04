---
name: migrate-mstest-v1v2-to-v3
description: >
  Use this skill before answering or editing whenever an MSTest v1/v2 project
  is being upgraded or repaired for v3. Triggers include QualityTools assembly
  references; MSTest.TestFramework/TestAdapter 1.x-2.x; "upgrade to MSTest
  v3"; comparing v1 and v2 migration paths; choosing MSTest or MSTest.Sdk;
  CS0411/CS1503 after a v3 package bump; DataRow type mismatch,
  MSTEST0014, or "Test data doesn't match method parameters";
  .testsettings/LegacySettings to .runsettings; timeout changes; and net5.0 or
  other dropped v3 TFMs. Still use it when packages say 3.x but migration
  source errors or legacy settings remain. Preserve VSTest/MTP. Do not use for
  a clean v3 project, v3-to-v4, framework conversion, or runner-only migration.
license: MIT
---

# MSTest v1/v2 -> v3 Migration

Migrate a test project from MSTest v1 (assembly references) or MSTest v2 (NuGet 1.x-2.x) to MSTest v3. MSTest v3 is **not binary compatible** with v1/v2 -- libraries compiled against v1/v2 must be recompiled.

## First Action

Inspect the supplied workspace and classify v1, v2, partially migrated v3, or
already-complete v3 before answering. Do not search the web or answer from
memory first. For an edit request, continue through the requested source
changes and validation; for an advice request, answer directly after the
classification.

## When to Use

- Project references `Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll` (MSTest v1)
- Project uses `MSTest.TestFramework` / `MSTest.TestAdapter` NuGet 1.x or 2.x
- Resolving build errors after updating MSTest packages from v1/v2 to v3 -- including when the packages already read 3.x and only the source or settings still need fixing
- Replacing `.testsettings` with `.runsettings`
- Adopting MSTest.Sdk or in-assembly parallel execution

## When Not to Use

- Project already on MSTest v3 with no migration-related build errors and no leftover `.testsettings` / `<LegacySettings>` (fully migrated)
- Upgrading v3 to v4 -- use `migrate-mstest-v3-to-v4`
- Migrating between frameworks (MSTest to xUnit/NUnit)

## Boundary Gate

Check package versions before any edit. If all MSTest references are already 3.x,
no v1/v2-to-v3 error is reported, and no `.testsettings` or `<LegacySettings>`
remains, state that migration is complete and make no changes. A 3.x package
version alone does not end the migration -- leftover v1/v2-era settings files or
breaking-change errors are still in scope. Do not consolidate working v3 packages
into the metapackage. Run the existing tests only if verification was requested.
This overrides all steps below.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | No | The `.csproj`, `.sln`, or `.slnx` entry point. Glob the working directory for it; ask only if nothing is found or several test projects make the target ambiguous |
| Build command | No | How to build (e.g., `dotnet build`, a repo build script). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |

> **Never open by asking for the project path.** A user describing their project
> in prose is asking a question, not withholding a file -- look on disk first. If
> there is genuinely no project file, answer for the setup they described rather
> than replying with only a question.
>
> **Open the paths exactly as the search returned them.** A glob that answers
> `./TestProject.csproj` means the file is there, relative to the working
> directory -- read it at that path. Do not rebuild it into an absolute path
> under this skill's own base directory: that directory holds `SKILL.md`, not the
> user's project, so the read fails and the project looks missing when it is not.
> If a file you just found fails to open, the path you constructed is wrong --
> retry with the literal result. Never conclude "there is no project on disk"
> while a search is still reporting project files, and never scaffold a
> substitute project from the prose description as a workaround.

## Execution Contract

- Skill activation is not a stopping point. Continue with workspace discovery and the requested work in the same task.
- The skill directory contains guidance, not the staged project. Search the current working directory, open the literal paths returned by the search, and retry with another available reader/editor if one tool rejects a valid path.
- Never ask for a path while a glob or directory search can discover it. Ask only after an exhaustive current-workspace search finds no project, or multiple projects make the target genuinely ambiguous.
- Classify the requested deliverable, not isolated verbs: "make the edits", "update this project", or "then build and run" means execute; "what do I need to change?", "what should I expect?", "are the steps the same?", or "show me" means answer, even if the prompt also says upgrade or migrate.
- After changing files, name the detected MSTest version and runner, every file changed, and the exact repaired calls/settings (for example, list each assertion changed to `AreEqual<object>`, `AreNotEqual<object>`, or `AreSame<object>`). Report the clean test counts. Do not claim VSTest preservation, a build, or passing tests without evidence from the project.

## Breaking Changes Summary

MSTest v3 introduces these breaking changes from v1/v2. Address only the ones relevant to the project:

| Breaking Change | Impact | Fix |
|---|---|---|
| `Assert.AreEqual(object, object)` overload removed; only `AreEqual<T>(T?, T?)` remains | **`CS0411`** (type arguments cannot be inferred) or `CS1503` -- but only where the two arguments have no common inferred type. Two `object`-typed arguments still infer `T = object` and **compile unchanged** | Add the explicit type argument on the failing call: `Assert.AreEqual<object>(expected, actual)`. Same for `AreNotEqual`, `AreSame`, `AreNotSame`. Leave assertions that already compile alone |
| `DataRow` strict type matching | **Not a compile error.** Builds with analyzer warning `MSTEST0014` and fails at run time with "Test data doesn't match method parameters". Widening conversions (`int` -> `long`) still bind; narrowing or unrelated types (`1L` -> `int`, `1.0` -> `float`) do not | Change literals to the exact parameter type: `1` for int, `1L` for long, `1.0f` for float. Run the tests -- a green build proves nothing here |
| `DataRow` limited to 16 arguments -- **3.0.1 and 3.0.2 only** | `CS1729` on those two versions; the limit was removed again in **3.0.3** | On 3.0.3+ (every current 3.x) a longer row is valid -- **leave it unchanged**. Do not wrap extras in an array, cast to `object`, or split the test. Only a project pinned to 3.0.1/3.0.2 needs action: update to 3.0.3+ |
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
- **Require project evidence, but gather it yourself**: Do not assume v1/v2 from the wording alone -- read the project or central package files and classify the source as QualityTools/v1, NuGet 1.x, or NuGet 2.x. Gather that evidence from the working directory rather than asking the user for it. If the project is already on v3+ with no v1/v2 leftovers, stop and route to the appropriate skill.
- **Preserve the test platform**: Keep VSTest or MTP unchanged during the framework upgrade unless the user separately requests a runner migration.
- **Execute full migrations**: When the user asks you to migrate or upgrade the project, edit the files, build, and run tests. Do not stop after listing breaking changes. Advice-only responses are appropriate only when the user asks what to expect.
- **Focused fix requests** (user has specific compilation errors after upgrading): Address only the relevant breaking change from the table above. Show a concise before/after fix. Do not walk through the full migration workflow.
- **DataRow fix requests**: Compare every supplied `DataRow` with its method signature. Mismatches can build with only `MSTEST0014` and fail during test execution. Preserve the method contract and normally fix the literal (`1L` -> `1` for `int`), then run the affected tests. **Change only the rows that are actually wrong.** Argument count is not itself a defect on 3.0.3+, so leave a long row alone unless the compiler rejects it.
- **Change nothing on suspicion -- confirm the error first**: When you believe a construct is unsupported, build and read the actual diagnostic before editing it. If it compiles, this version supports it and it needs no change. Rewriting valid code to dodge a limit the project is not subject to is a defect, not caution.
- **Specific feature migration** (user asks about one aspect like .testsettings, DataRow, or assertions): Address only that feature, but handle every active setting or affected usage in the supplied files. For `.testsettings`, put all MSTest settings under one `<MSTest>` element, map requested deployment, per-test timeout, data collector, and other active configuration, and do not add a session-wide timeout. Do not walk through unrelated breaking changes.
- **"What to expect" questions** (user asks about breaking changes before upgrading): First state the concrete package update needed to reach v3, then summarize every category in the Breaking Changes Summary, marking which ones directly apply to the visible project. Keep each item to one line and do not expand into release-note history.
- **Required shape for "what to expect"**: Use an `Applies / Watch / No change` table grounded in the visible project and cover every row in the Breaking Changes Summary. This completeness is the value of the skill; do not omit runtime-only categories merely to be concise.
- **Full migration requests** (user wants complete migration): Follow the complete workflow below.
- **Comparison questions** (user asks about v1 vs v2 differences): Explain concisely -- v1 uses assembly references and requires removing them first; v2 uses NuGet and just needs a version bump. Both converge on the same v3 packages and breaking changes.
- **Keep execution project-specific**: For fixes and full migrations, change only patterns found in the visible code/configuration. Broader coverage is reserved for explicit "what should I expect?" questions.

## Migration Paths

- **MSTest v1 (assembly reference to QualityTools)**: Remove the assembly reference (Step 2), add v3 NuGet packages (Step 3), fix breaking changes (Step 5).
- **MSTest v2 (NuGet packages 1.x-2.x)**: Update package versions to 3.x (Step 3), fix breaking changes (Step 5). No assembly reference removal needed.

Both paths converge at Step 3 -- the same v3 packages and breaking changes apply regardless of starting version.

## Workflow

### Step 1: Assess the project

1. Locate the project first: glob the working directory for `*.csproj`, `*.sln`,
   `*.slnx`, `Directory.Build.props`, `Directory.Packages.props`, and
   `*.testsettings`. Do this before asking the user anything, and open whatever
   it returns at exactly the path it reported (see the note under Inputs).
2. In one discovery pass, batch-read project and central configuration files, search for affected APIs/settings, and identify which MSTest version is currently in use:
   - **Assembly reference**: Look for `Microsoft.VisualStudio.QualityTools.UnitTestFramework` in project references -> MSTest v1
   - **NuGet packages**: Check `MSTest.TestFramework` and `MSTest.TestAdapter` package versions -> v1 if 1.x, v2 if 2.x
3. Check whether the target framework is dropped in v3 (see Step 4).
4. Run the existing test command. Record discovered, passed, failed, and skipped counts as the parity baseline.

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

> **Important**: MSTest.Sdk defaults to Microsoft.Testing.Platform (MTP). When the
> project itself must remain on VSTest, set `<UseVSTest>true</UseVSTest>`. MSTest.Sdk
> v3 also supplies `Microsoft.NET.Test.Sdk` in MTP mode, so a separate transitional
> `vstest.console` invocation does not by itself require changing the primary runner.
> Do not switch runners merely as a side effect of the framework upgrade.

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

**Assertion overloads** -- MSTest v3 replaced `Assert.AreEqual(object, object)` and `AreNotEqual(object, object)` with the generic `AreEqual<T>(T?, T?)`. This breaks **only** where `T` can no longer be inferred, which the compiler reports as `CS0411` (or `CS1503` for unrelated argument types):

```csharp
// Breaks -- string and int have no common inferred type:
Assert.AreEqual(referenceCode, numericId);   // CS0411
// Fix -- name the type argument explicitly:
Assert.AreEqual<object>(referenceCode, numericId);
```

Two `object`-typed arguments still infer `T = object` and compile untouched, as do
ordinary typed assertions like `Assert.AreEqual("A-3", order.Reference)`. **Fix only
the call sites the compiler rejects.** Widening every assertion in the file to
`<object>` also compiles, so nothing will flag it -- but it discards the type
checking v3 added, which is the entire point of the change.

**DataRow strict type matching** -- argument types must match parameter types
exactly. This is **not** a compile error: the row builds (with `MSTEST0014`) and
fails at run time with "Test data doesn't match method parameters".

```csharp
// Fails at run time: 1L (long) does not bind to an int parameter -> use 1
// Fails at run time: 1.0 (double) does not bind to a float parameter -> use 1.0f
// Still binds: 1 (int) to a long parameter -- widening conversions are accepted
```

Preserve method parameter types unless independently wrong. `dotnet build` may
succeed with `MSTEST0014`; run the test to prove each row binds and executes.

**Rows with more than 16 arguments** -- leave them alone unless the compiler
actually emits `CS1729`. The cap existed only in 3.0.1/3.0.2 (removed in 3.0.3),
so wrapping extras in an `object[]`, casting to `object`, or splitting the method
just rewrites a correct test.

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

Update every project, CI command, or IDE setting that explicitly selected the old
`.testsettings` path to select the new `.runsettings` path. When a VSTest project
must preserve behavior but the legacy file was never selected, make the new file
effective with `RunSettingsFilePath`. For MTP, use the framework-supported
`--settings` path or existing MTP configuration instead of assuming the VSTest
MSBuild property is honored.

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
| Replying with "which project?" when the workspace already holds one | Glob for `*.csproj`/`*.sln`/`*.slnx` and read what is there |
| "No project on disk" right after a search listed project files | The path was rebuilt under the skill's base directory. Reopen using the literal search result; never scaffold a replacement project |
| Rewriting a `DataRow` with more than 16 arguments | Valid on 3.0.3+, which is every current 3.x. Only 3.0.1/3.0.2 ever rejected it |
| Non-MSTest.Sdk VSTest project missing `Microsoft.NET.Test.Sdk` | Add the package reference for VSTest discovery |
| MSTest.Sdk v3 project must use VSTest as its primary runner | Set `<UseVSTest>true</UseVSTest>`; do not flip the runner merely because a transitional `vstest.console` job also exists |
