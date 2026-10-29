---
name: migrate-xunit-to-mstest
description: >
  Convert .NET tests from xUnit.net v2/v3 to MSTest v4 while preserving VSTest
  or MTP. Use for replacing xunit packages, Fact/Theory/InlineData/MemberData,
  assertions, IClassFixture/ICollectionFixture, ITestOutputHelper, TestContext
  cancellation, traits/Owner, skips, timeouts, and xUnit parallelization. Also
  use when a "convert xUnit to MSTest" request may already be migrated: inspect
  and report the no-op. Do not use for xUnit v2-to-v3, MSTest upgrades,
  NUnit/TUnit conversion, or runner-only VSTest-to-MTP migration.
license: MIT
---

# xUnit -> MSTest Migration

Convert xUnit.net v2 or v3 tests to MSTest v4 without changing the target framework or test platform. A successful migration builds, discovers the same tests, and preserves pass/fail results and execution semantics.

## Scope

Use this skill only when the project contains xUnit packages or source and the user wants MSTest. If the project already uses MSTest and contains no xUnit tests, report that no framework migration is needed and make no changes.

Do not combine this framework conversion with a target-framework upgrade or VSTest/MTP migration. Complete and verify one migration before starting another.

## Workspace Contract

- Continue after skill activation. Search the current working directory for the
  staged project and source; never look for user files under this skill's base
  directory.
- Open the literal paths returned by glob/search. If one reader or patch tool
  rejects a path that was just found, retry with another available tool. Do not
  ask the user for a path until current-workspace discovery is exhausted.
- Classify by the requested deliverable: "convert this project" means edit,
  build, and test; "give me a plan" or "how would I convert it?" means answer.
  Do not replace execution with "please provide the files" when files are present.
- The final response must state the source xUnit version, preserved runner,
  changed files, each high-risk semantic mapping applied, and actual test
  counts. Assertions about fixture lifetime, Owner mapping, cancellation, or
  parallelization must be visible in the resulting source, not only prose.

## Response Mode

- **Full migration request:** inspect the project, make the edits, build, and run tests. Do not stop after giving a plan.
- **Focused compile error or API question:** inspect the relevant code and apply only that mapping. Do not narrate the entire workflow.
- **Unsupported target framework:** stop before changing packages. MSTest v4 requires .NET 8+ or .NET Framework 4.6.2+ for test applications; offer a separately approved TFM upgrade or MSTest v3 as the intermediate target.

## Decisions That Change the Result

Apply these before the mechanical mapping:

| Detected state | Required action |
|---|---|
| No xUnit package, namespace, attribute, or fixture remains | Stop. Make no file changes, report that migration is unnecessary, and run the existing `dotnet test` command once to prove the already-MSTest project is healthy. |
| Source uses VSTest | Keep the existing VSTest property/configuration. Prefer retaining and updating a source project's explicit `Microsoft.NET.Test.Sdk` pin; a repository that intentionally relies on the MSTest metapackage's transitive dependency may keep that convention. Do not introduce MTP properties. |
| Source uses MTP | Replace xUnit-specific MTP selection with MSTest MTP configuration. Prefer `MSTest.Sdk`; with the metapackage, set `EnableMSTestRunner=true` and `OutputType=Exe`. Preserve native-versus-bridged integration only when the target package/SDK combination supports it. MTP 2+ cannot bridge on SDK 10+; report the required separately authorized command-mode alignment instead of adding a bypass or silently switching platform. |
| Source relies on xUnit's default collections mode | Use class-level MSTest parallelization for independently runnable classes, but preserve serialization between every member of a shared collection even without `DisableParallelization=true`. A one-class project with no explicit setting needs no assembly policy. Translate explicit settings and disclose any coarser target scheduling; inspect the compiled result. |
| Source explicitly uses a newer parallel mode, custom orderer or runner-wide scheduling | Inspect the pinned framework/runner version and effective settings. Do not apply default-collection rules blindly or promise exact parity for unsupported scheduling. |

For detailed mappings and examples, search [`references/mapping-cheatsheet.md`](references/mapping-cheatsheet.md) for constructs actually present in the project and read only the matching sections. Do not load or reproduce the whole reference.

## Fast Path

For a routine project migration, converge in four phases: one batched discovery read/search, one edit pass, one `dotnet test`, and one concise result. Do not:

- list a directory and then reread the same files through another tool
- copy project files into the loaded skill or its `references/` directory; those files are read-only guidance, not an editing workspace
- try `dotnet test --no-restore` unless restore is already known to be current
- run separate restore, build, and test commands when `dotnet test` is sufficient
- rerun a passing test command or inspect unchanged files for confirmation

Use an existing CI/test result as the parity baseline when available. Run a new pre-edit baseline only when counts are unavailable and the migration contains data-driven tests, fixtures, skips, custom extensions, shared state, or other behavior whose parity cannot be established from source alone.

## Workflow

### 1. Establish the baseline

1. In one discovery pass, batch-read the test projects plus `Directory.Build.props`, `Directory.Packages.props`, `global.json`, and runner configuration, and search the source for the high-risk constructs below.
2. State the detected source version:
   - `xunit` 2.x and related packages -> xUnit v2
   - `xunit.v3` or `xunit.v3.*` -> xUnit v3
3. Identify VSTest or MTP from the project and repository configuration. Use `platform-detection` only when the platform is ambiguous, and preserve the detected platform.
4. Record the target frameworks and stop if MSTest v4 does not support them.
5. If the Fast Path requires a new baseline, run the existing test command once and record discovered, passed, failed, and skipped counts.
6. Inventory high-risk constructs before editing:
   - `IClassFixture`, `ICollectionFixture`, `CollectionDefinition`, custom `FactAttribute`/`TheoryAttribute`/`DataAttribute`
   - `Assert.Throws`, `ThrowsAny`, `IsType`, `Record.Exception`, event assertions
   - `ITestOutputHelper`, `TestContext.Current`, `IAsyncLifetime`
   - `CollectionBehavior`, `xunit.runner.json`, shared static or external state

### 2. Replace packages without switching runners

Remove xUnit packages from project files and central package files. This includes `xunit*`, `xunit.v3.*`, `xunit.runner.visualstudio`, `YTest.MTP.XUnit2`, and xUnit-specific companion packages that are being replaced.

Default to the MSTest v4 metapackage for an incremental conversion:

```xml
<!-- Example pin: replace with the exact stable v4 version resolved from the configured package source. -->
<PackageReference Include="MSTest" Version="4.1.0" />
```

The metapackage includes `Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, `MSTest.TestFramework`, and `MSTest.Analyzers`. When preserving VSTest, prefer retaining a source project's explicit `Microsoft.NET.Test.Sdk` pin and update it to a version compatible with the chosen MSTest version; this keeps runner/version compatibility reviewable. A repository that intentionally relies on the metapackage's transitive dependency may preserve that convention instead. For the example pin above, MSTest 4.1.0 requires Microsoft.NET.Test.Sdk 18.0.1+; incompatible older pins can cause `NU1605`.

When preserving MTP, do not carry xUnit's `UseMicrosoftTestingPlatformRunner` property into the MSTest project. Prefer `MSTest.Sdk` at the resolved version. If repository conventions require the metapackage route, set `<EnableMSTestRunner>true</EnableMSTestRunner>` and `<OutputType>Exe</OutputType>`. Retain `TestingPlatformDotnetTestSupport=true` only for repositories that continue to invoke MTP applications through VSTest command mode; native .NET 10+ MTP mode does not require it. When preserving VSTest with `MSTest.Sdk`, set `<UseVSTest>true</UseVSTest>`.

Do not change `TargetFramework`. Remove `xunit.runner.json` only after porting its relevant settings.
Resolve transitive MTP/MSBuild versions before promising command-mode parity.
For example, an MSTest upgrade that introduces MTP 2+ cannot retain a legacy
SDK 10 bridge. Native mode requires MTP 1.7+; use `run-tests` when available
for command selection, or the verified repository runner and official support
matrix. Do not downgrade or switch command modes implicitly.

### 3. Perform the mechanical conversion

Apply the common rewrites first:

| xUnit | MSTest |
|---|---|
| no class attribute | `[TestClass]` |
| `[Fact]` | `[TestMethod]` |
| `[Theory]` + `[InlineData]` | `[TestMethod]` + `[DataRow]` |
| `[MemberData]` | `[DynamicData]` |
| `[Fact(Skip = "...")]` | `[TestMethod]` + `[Ignore("...")]` |
| `[Trait("Category", value)]` | `[TestCategory(value)]` |
| `[Trait("Owner", value)]` | `[Owner(value)]` |
| other `[Trait(key, value)]` | `[TestProperty(key, value)]` |
| `Assert.Equal` / `NotEqual` | `Assert.AreEqual` / `AreNotEqual` |
| `Assert.True` / `False` | `Assert.IsTrue` / `IsFalse` |
| `Assert.Null` / `NotNull` | `Assert.IsNull` / `IsNotNull` |

Remove `using Xunit;` and `using Xunit.Abstractions;`. Add `using Microsoft.VisualStudio.TestTools.UnitTesting;` for the metapackage option; `MSTest.Sdk` supplies it as an implicit global using.

Preserve existing class inheritance. Do not mechanically seal classes.

### 4. Resolve semantic mappings

Load the mapping cheatsheet for every high-risk construct found in Step 1. These rules are mandatory:

- xUnit `Assert.Throws<T>` is exact-type and maps to MSTest `Assert.ThrowsExactly<T>`.
- xUnit `Assert.ThrowsAny<T>` permits derived types and maps to MSTest `Assert.Throws<T>`.
- xUnit `Assert.IsType<T>` is exact-type and maps to the generic `Assert.IsExactInstanceOfType<T>`; `Assert.IsAssignableFrom<T>` maps to the generic `Assert.IsInstanceOfType<T>`. When the xUnit assertion's typed return value is assigned, preserve that assignment and use the generic MSTest overload rather than a non-generic `Type` overload.
- xUnit `Assert.Equal` on sequences compares elements. Use `Assert.AreSequenceEqual` on MSTest 4.3+ or `CollectionAssert.AreEqual` with materialized lists on earlier v4; never replace sequence equality with reference-based `Assert.AreEqual`.
- `[Ignore]` and `[Timeout]` are modifiers; keep `[TestMethod]` so the test is discovered.
- `[DataRow]` values must exactly match parameter types.
- `TestContext.Current.CancellationToken` maps to an injected MSTest `TestContext.CancellationToken`; never replace it with `CancellationToken.None` or a new `CancellationTokenSource`.
- `Owner` is a reserved VSTest property. Map `[Trait("Owner", value)]` to `[Owner(value)]`, not `[TestProperty("Owner", value)]`.
- Assertions with no MSTest equivalent (`Assert.Collection`, `Assert.All`, `Assert.Equivalent`, `Record.Exception`, event assertions) require an explicit manual rewrite. Never delete an assertion without replacing its verification.

Apply the mechanical and semantic rewrites in one edit pass when the inventory makes the required mappings clear. Do not run an intermediate build by default; use compiler errors from final verification to drive only unresolved conversions.

### 5. Preserve lifecycle, fixture scope, and parallelization

- Keep constructor setup and `IDisposable`/`IAsyncDisposable` when valid. Map `IAsyncLifetime` to `[TestInitialize]`/`[TestCleanup]`.
- `IClassFixture<T>` means one fixture instance per test class, shared by all methods in that class. Map it to a static `T` field created once by a static `[ClassInitialize]` method that accepts `TestContext`, and dispose it once from static `[ClassCleanup]`. Never use `[TestInitialize]`/`[TestCleanup]` for this mapping because that creates one fixture per test method.
- For `ICollectionFixture<T>` in default collections mode, preserve both sharing
  and member serialization, including when `DisableParallelization` is absent.
  A static `Lazy<T>` shares an instance but does not serialize tests or dispose
  it. Never make cleanup depend on every declared member class running: filters
  may select only one. Verify disposal in full and single-member filtered runs.
  A lazy run-end cleanup fallback is valid only for an explicitly accepted
  sole-collection assembly; otherwise report the unsupported lifetime mapping.
  Preserve one initialization and one disposal after the last selected member,
  including async lifecycle. Under class-level MSTest parallelization,
  `[DoNotParallelize]` on member classes is a conservative serialization option,
  not exact collection scheduling: disclose that it also prevents overlap with
  unrelated classes. Where the installed MSTest version supports it, a common
  exclusive `[ResourceLock]` can serialize members while allowing unrelated
  classes to overlap; it does not manage fixture lifetime.
  If exact grouping/lifetime cannot be preserved, flag that
  decision rather than silently widening fixture scope. Use assembly lifecycle
  only for a genuinely assembly-wide fixture or explicitly accepted scope change.
- Preserve diagnostic-versus-enforcement semantics. `longRunningTestSeconds`
  reports a diagnostic warning, not a failing timeout; do not convert it to
  `[Timeout]`. Document an unavailable diagnostic equivalent.
- Replace `ITestOutputHelper` with constructor-injected or property-based MSTest `TestContext`, and replace each `_output.WriteLine(...)` call with the corresponding `TestContext.WriteLine(...)`. In the final result, name both the `TestContext` injection/property and the `WriteLine` mapping explicitly; "migrated output" is not enough to demonstrate parity.

xUnit's default collections mode runs different collections in parallel and
serializes members within a collection; by default each class is its own
collection. MSTest runs serially unless configured otherwise. For independently
runnable classes, use:

```csharp
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]
```

For a one-class project with no explicit setting, omit an unnecessary assembly
policy. Always inspect explicit `CollectionBehavior`, runner JSON and scheduling
overrides. Do not use `ExecutionScope.MethodLevel` to emulate default collection
semantics. xUnit v3 4.0+ can explicitly select `parallelMode: all`; treat that as
a separate versioned case, not evidence that every xUnit project runs methods
concurrently. State the source sharing/serialization and target constraints,
including any accepted loss of unrelated-class concurrency.

### 6. Verify parity

1. Run tests once with the same platform, filter, and configuration used for the baseline. `dotnet test` builds by default; run a separate build only when needed to isolate a compilation failure.
2. Compare discovered, passed, failed, and skipped counts.
3. Investigate every difference before declaring completion:
   - missing cases -> discovery attributes, `DynamicData`, or `DataRow` literal types
   - changed exception behavior -> exact-vs-derived assertion mapping
   - shared-state failures or large duration changes -> fixture scope and parallelization
   - silently skipped tests -> missing `[TestMethod]` or incorrect runtime-skip conversion
4. Confirm no xUnit package, namespace, attribute, runner configuration, or fixture interface remains unless explicitly documented for manual follow-up.
5. After the final passing test, read back each changed file that implements a high-risk mapping, plus any runner or parallelization configuration. In the result, name the file and the exact target APIs that implement its lifecycle, data, skip, output, assertion, fixture-scope, or parallelization behavior. Preserve type arguments and member names such as `[ClassInitialize]`, `TestContext.WriteLine`, and `Assert.IsExactInstanceOfType<T>`; generic claims such as "converted attributes" or "migrated output" are not evidence of parity.

Keep the final response concise and outcome-focused:

- **Changed:** name the files and exact high-risk mappings applied.
- **Verified:** give the final test command and discovered/passed/failed/skipped counts.
- **Preserved:** state the unchanged target framework and test platform, plus any fixture or parallelization scope decision.
- **Remaining:** identify manual follow-up, or say none.

## Completion Criteria

- Current xUnit version and test platform were identified
- xUnit packages and source constructs were converted
- Target framework and test platform stayed unchanged
- Fixture scope and effective parallelization decisions are explicit, including justified omission when concurrency is not observable
- Build succeeds
- Test discovery and result counts match the baseline
- Any unsupported custom extension point is called out rather than approximated

## Follow-up

Run `migrate-vstest-to-mtp` separately if the user also wants MTP. Use `writing-mstest-tests` only after parity is established to polish the converted MSTest code.
