---
name: migrate-xunit-to-mstest
description: >
  Convert .NET test projects from xUnit.net v2 or v3 to MSTest v4. Use for
  replacing xunit packages, [Fact]/[Theory], xUnit assertions, fixtures,
  ITestOutputHelper, traits, skips, and xUnit parallelization with MSTest
  equivalents while preserving the current VSTest or MTP runner.
  DO NOT USE FOR: xUnit v2 to v3 upgrades, MSTest version upgrades, migrations
  from NUnit/TUnit, or runner-only VSTest to MTP migrations.
license: MIT
---

# xUnit -> MSTest Migration

Convert xUnit.net v2 or v3 tests to MSTest v4 without changing the target framework or test platform. A successful migration builds, discovers the same tests, and preserves pass/fail results and execution semantics.

## Scope

Use this skill only when the project contains xUnit packages or source and the user wants MSTest. If the project already uses MSTest and contains no xUnit tests, report that no framework migration is needed and make no changes.

Do not combine this framework conversion with a target-framework upgrade or VSTest/MTP migration. Complete and verify one migration before starting another.

## Response Mode

- **Full migration request:** inspect the project, make the edits, build, and run tests. Do not stop after giving a plan.
- **Focused compile error or API question:** inspect the relevant code and apply only that mapping. Do not narrate the entire workflow.
- **Unsupported target framework:** stop before changing packages. MSTest v4 requires .NET 8+ or .NET Framework 4.6.2+ for test applications; offer a separately approved TFM upgrade or MSTest v3 as the intermediate target.

For detailed mappings and examples, load [`references/mapping-cheatsheet.md`](references/mapping-cheatsheet.md) only for constructs actually present in the project. Do not reproduce the whole reference in the response.

## Workflow

### 1. Establish the baseline

1. Read the test projects plus `Directory.Build.props`, `Directory.Packages.props`, `global.json`, and runner configuration.
2. State the detected source version:
   - `xunit` 2.x and related packages -> xUnit v2
   - `xunit.v3` or `xunit.v3.*` -> xUnit v3
3. Use `platform-detection` to identify VSTest or MTP. Preserve that platform.
4. Record the target frameworks and stop if MSTest v4 does not support them.
5. Run the existing build and test command. Record discovered, passed, failed, and skipped counts.
6. Search for high-risk constructs before editing:
   - `IClassFixture`, `ICollectionFixture`, `CollectionDefinition`, custom `FactAttribute`/`TheoryAttribute`/`DataAttribute`
   - `Assert.Throws`, `ThrowsAny`, `IsType`, `Record.Exception`, event assertions
   - `ITestOutputHelper`, `TestContext.Current`, `IAsyncLifetime`
   - `CollectionBehavior`, `xunit.runner.json`, shared static or external state

### 2. Replace packages without switching runners

Remove xUnit packages from project files and central package files. This includes `xunit*`, `xunit.v3.*`, `xunit.runner.visualstudio`, `YTest.MTP.XUnit2`, and xUnit-specific companion packages that are being replaced.

Default to the MSTest v4 metapackage for an incremental conversion:

```xml
<PackageReference Include="MSTest" Version="4.1.0" />
```

This keeps VSTest available through `Microsoft.NET.Test.Sdk`. Use `MSTest.Sdk` only when the project already uses it elsewhere or the user explicitly requests it. `MSTest.Sdk` defaults to MTP, so add `<UseVSTest>true</UseVSTest>` when preserving VSTest.

Do not change `TargetFramework`. Remove `xunit.runner.json` only after porting its relevant settings.

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
- xUnit `Assert.IsType<T>` is exact-type and maps to `Assert.IsExactInstanceOfType<T>`; `Assert.IsAssignableFrom<T>` maps to `Assert.IsInstanceOfType<T>`.
- `[Ignore]` and `[Timeout]` are modifiers; keep `[TestMethod]` so the test is discovered.
- `[DataRow]` values must exactly match parameter types.
- `TestContext.Current.CancellationToken` maps to an injected MSTest `TestContext.CancellationToken`; never replace it with `CancellationToken.None` or a new `CancellationTokenSource`.
- Assertions with no MSTest equivalent (`Assert.Collection`, `Assert.All`, `Assert.Equivalent`, `Record.Exception`, event assertions) require an explicit manual rewrite. Never delete an assertion without replacing its verification.

Build after the mechanical pass. Use compiler errors plus the inventory to drive only the remaining conversions.

### 5. Preserve lifecycle, fixture scope, and parallelization

- Keep constructor setup and `IDisposable`/`IAsyncDisposable` when valid. Map `IAsyncLifetime` to `[TestInitialize]`/`[TestCleanup]`.
- Map `IClassFixture<T>` to class-scoped initialization and cleanup.
- For `ICollectionFixture<T>`, preserve both sharing and serialization. Prefer a static `Lazy<T>` helper used by each member class; add `[DoNotParallelize]` only when the source collection disabled parallelization. Use assembly initialization only when the fixture is genuinely assembly-wide.
- Replace `ITestOutputHelper` with injected or property-based MSTest `TestContext`.

xUnit runs classes in parallel by default; MSTest runs them serially. Unless the source disabled parallelism, preserve xUnit behavior with:

```csharp
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]
```

Never use `ExecutionScope.MethodLevel` to emulate xUnit. Before applying a fixture-scope or parallelization decision, state what the source shared or serialized and how the target preserves it.

### 6. Verify parity

1. Run the build; it must complete with zero errors.
2. Run tests with the same platform, filter, and configuration used for the baseline.
3. Compare discovered, passed, failed, and skipped counts.
4. Investigate every difference before declaring completion:
   - missing cases -> discovery attributes, `DynamicData`, or `DataRow` literal types
   - changed exception behavior -> exact-vs-derived assertion mapping
   - shared-state failures or large duration changes -> fixture scope and parallelization
   - silently skipped tests -> missing `[TestMethod]` or incorrect runtime-skip conversion
5. Confirm no xUnit package, namespace, attribute, runner configuration, or fixture interface remains unless explicitly documented for manual follow-up.

## Completion Criteria

- Current xUnit version and test platform were identified
- xUnit packages and source constructs were converted
- Target framework and test platform stayed unchanged
- Fixture scope and parallelization decisions are explicit
- Build succeeds
- Test discovery and result counts match the baseline
- Any unsupported custom extension point is called out rather than approximated

## Follow-up

Run `migrate-vstest-to-mtp` separately if the user also wants MTP. Use `writing-mstest-tests` only after parity is established to polish the converted MSTest code.
