---
name: migrate-nunit-to-mstest
description: >
  Convert .NET tests from NUnit 3/4 to MSTest v4 while preserving VSTest or
  MTP. Use for replacing NUnit/NUnit3TestAdapter packages, Test/TestCase/
  TestCaseSource/Values attributes, constraint assertions, SetUp/TearDown,
  OneTimeSetUp, SetUpFixture, FixtureLifeCycle, TestContext, categories,
  retries, timeouts, and NUnit parallelization. Also use when a "convert NUnit
  to MSTest" request may already be migrated: inspect and report the no-op. Do
  not use for NUnit version upgrades, xUnit/TUnit conversion, MSTest upgrades,
  or runner-only VSTest-to-MTP migration.
license: MIT
---

# NUnit -> MSTest Migration

Convert NUnit 3 or 4 tests to MSTest v4 without changing the target framework or test platform. A successful migration builds, discovers the same test cases, and preserves pass/fail, lifecycle, data, filtering, and concurrency semantics.

## Scope

Use this skill only when the project contains NUnit packages or source and the user wants MSTest. If the project already uses MSTest and contains no NUnit tests, report that no framework migration is needed and make no changes.

Do not combine this framework conversion with a target-framework upgrade or VSTest/MTP migration. Complete and verify one migration before starting another.

## Workspace Contract

- Search the current working directory for projects and source; never look for user files under this skill's directory.
- Classify the deliverable: "convert this project" means edit, build, and test; "give me a plan" means answer without editing.
- Preserve the user's requested project scope. Shared props may be changed only when they own NUnit package or runner configuration for that scope.
- The final response must state the source NUnit version, preserved runner, changed files, high-risk semantic mappings, and actual test counts.

## Response Mode

- **Full migration request:** inspect, edit, build, and run tests. Do not stop after a plan.
- **Focused compile error or API question:** apply only the relevant mapping.
- **Unsupported target framework:** stop before changing packages. MSTest v4 requires .NET 8+ or .NET Framework 4.6.2+ for test applications; offer a separately approved TFM upgrade or MSTest v3.

## Decisions That Change the Result

| Detected state | Required action |
|---|---|
| No NUnit package, namespace, attribute, or constraint remains | Stop, make no changes, and run the existing test command once to prove the already-MSTest project is healthy. |
| Source uses VSTest | Preserve VSTest. Retain and update an explicit `Microsoft.NET.Test.Sdk` pin when the repository owns one; do not introduce MTP properties. |
| Source uses MTP | Replace NUnit-specific MTP configuration with MSTest MTP configuration. Prefer `MSTest.Sdk`; with the metapackage set `EnableMSTestRunner=true` and `OutputType=Exe`. |
| NUnit uses its default fixture lifecycle | NUnit normally shares one fixture instance across all test cases; MSTest creates a new class instance per test. Move state that must remain class-shared behind static fields initialized by `[ClassInitialize]`, or prove the state is per-test before leaving it as an instance field. |
| NUnit has no `Parallelizable` configuration | Preserve serial execution. Do not add `[assembly: Parallelize]`; NUnit and MSTest are both serial by default. |
| NUnit explicitly enables parallel execution | Translate the effective scope and worker count. Never infer method-level parallelism from fixture-level settings. |

For detailed mappings, search [`references/mapping-cheatsheet.md`](references/mapping-cheatsheet.md) for constructs present in the project and read only those sections.

## Fast Path

For a routine migration, converge in four phases: one batched discovery pass, one edit pass, one `dotnet test`, and one concise result.

- Use an existing CI/test result as the baseline when available.
- Run a pre-edit baseline when counts are unavailable and the project contains parameterized tests, fixture state, namespace setup, retries, custom attributes, or explicit parallelization.
- Do not run separate restore, build, and test commands when `dotnet test` is sufficient.
- Do not delete NUnit runner configuration until its behavior has been translated.

## Workflow

### 1. Establish the baseline

1. Batch-read test projects, central package files, `global.json`, `.runsettings`, `testconfig.json`, and NUnit configuration.
2. Detect NUnit from `NUnit`, `NUnit3TestAdapter`, `NUnit.Analyzers`, `NUnit.Framework`, `NUnit.Framework.Legacy`, and `using NUnit.Framework`.
3. State whether the source is NUnit 3 or 4 from the resolved package version.
4. Detect VSTest or MTP and preserve it. Use `platform-detection` only when ambiguous.
5. Record target frameworks and stop if MSTest v4 does not support them.
6. Inventory high-risk constructs:
   - `[TestFixture(...)]`, `[TestFixtureSource]`, `[FixtureLifeCycle]`, constructors with parameters
   - `[TestCase]`, `[TestCaseSource]`, `TestCaseData`, `[Values]`, `[Range]`, `[Random]`, `[Sequential]`, `[Pairwise]`, custom data attributes
   - `[Theory]`, `[Datapoint]`, `[DatapointSource]`, automatic bool/enum datapoints, `Assume.That`
   - `[OneTimeSetUp]`, `[OneTimeTearDown]`, `[SetUpFixture]`, inheritance-based setup
   - `Assert.That`, `Assert.Multiple`, `Assert.Throws`, `Assert.Catch`, collection constraints
   - `[Parallelizable]`, `[NonParallelizable]`, `[LevelOfParallelism]`, `[Order]`, `[SingleThreaded]`
   - `[Apartment]`, `[RequiresThread]`, `[CancelAfter]`, `[Timeout]`
   - `[Culture]`, `[Platform]`, `[SetCulture]`, `[SetUICulture]`
   - `[Explicit]`, `[Repeat]`, `[Retry]`, `[MaxTime]`, categories, properties, `[TestOf]`
   - `[DefaultFloatingPointTolerance]`, `[NonTestAssembly]`, and deprecated fixture lifecycle aliases

### 2. Replace packages without switching runners

Remove NUnit-specific packages being replaced, including `NUnit`, `NUnit3TestAdapter`, `NUnit.Analyzers`, NUnit console runner packages, and NUnit-specific MTP adapters.

Default to the current stable MSTest v4 metapackage resolved from the configured package source:

```xml
<PackageReference Include="MSTest" Version="4.4.0" />
```

The pin is illustrative for the current release; during a real migration resolve and pin the current stable version. Preserve an explicit `Microsoft.NET.Test.Sdk` dependency for VSTest when the source project owns one. When preserving MTP, prefer `MSTest.Sdk`; otherwise use `EnableMSTestRunner=true` and `OutputType=Exe`.

Do not change `TargetFramework`. Remove NUnit `.runsettings` adapter settings only after translating relevant behavior.

### 3. Perform the mechanical conversion

| NUnit | MSTest |
|---|---|
| `[TestFixture]` or fixture with tests | `[TestClass]` |
| `[Test]` | `[TestMethod]` |
| `[TestCase(...)]` | `[TestMethod]` + `[DataRow(...)]` |
| `[TestCaseSource(nameof(Cases))]` | `[TestMethod]` + `[DynamicData(nameof(Cases))]` |
| `[SetUp]` / `[TearDown]` | `[TestInitialize]` / `[TestCleanup]` |
| `[OneTimeSetUp]` / `[OneTimeTearDown]` | static `[ClassInitialize]` / `[ClassCleanup]` |
| `[Category(value)]` | `[TestCategory(value)]` |
| `[Property(key, value)]` | `[TestProperty(key, value)]` |
| `[Ignore("reason")]` | `[Ignore("reason")]` plus `[TestMethod]` |
| `[Timeout(ms)]` | `[Timeout(ms)]` plus `[TestMethod]` |
| `[Retry(n)]` | `[Retry(n - 1)]` plus `[TestMethod]` when `n > 1`; remove it when `n = 1` |

Remove `using NUnit.Framework;` and `using NUnit.Framework.Legacy;`. Add `using Microsoft.VisualStudio.TestTools.UnitTesting;` when using the metapackage; `MSTest.Sdk` supplies an implicit global using.

Do not mechanically seal classes or flatten inherited setup methods.

### 4. Resolve semantic mappings

Load the mapping cheatsheet for every high-risk construct found in Step 1. These rules are mandatory:

- NUnit's default `LifeCycle.SingleInstance` differs from MSTest's per-test class instances. Preserve class-shared fields explicitly.
- `[OneTimeSetUp]` and `[OneTimeTearDown]` become static MSTest methods. Move any instance state they use to a static holder rather than merely adding `static`.
- NUnit `Assert.Throws<T>` and `Throws.TypeOf<T>` require an exact exception type and map to `Assert.ThrowsExactly<T>`. NUnit `Assert.Catch<T>` and `Throws.InstanceOf<T>` permit derived types and map to `Assert.Throws<T>`.
- NUnit `Is.TypeOf<T>` maps to `Assert.IsExactInstanceOfType<T>`; `Is.InstanceOf<T>` maps to `Assert.IsInstanceOfType<T>`.
- NUnit equality constraints compare sequences element-by-element. Use `Assert.AreSequenceEqual` on MSTest 4.3+ or `CollectionAssert.AreEqual` with materialized lists; never use reference-based `Assert.AreEqual` for sequences.
- `TestCase(ExpectedResult=...)` and `TestCaseData.Returns(...)` require rewriting the target test to assert the expected result.
- Preserve per-row names, categories, and ignores by returning `TestDataRow<T>` from `DynamicData` when needed. NUnit row properties have no direct `TestDataRow<T>` equivalent and require a custom data source or an explicit metadata decision.
- MSTest 4.4 combinatorial attributes can map NUnit `[Combinatorial]`, `[Values]`, `[Range]`, and `[Random]`; add `using Microsoft.VisualStudio.TestTools.UnitTesting.Combinatorial;`. Constructor semantics differ, so translate NUnit ranges and random bounds explicitly. `[Sequential]` still requires explicit rows or a custom data source.
- NUnit `[Pairwise]` has no MSTest built-in. Preserve the exact generated rows in `DynamicData` or use a verified pairwise `ITestDataSource`; full Cartesian combinations change test counts and execution cost.
- Convert `[Theory]`, `[Datapoint]`, and `[DatapointSource]` into explicit `DynamicData` or combinatorial sources. Preserve automatic bool/enum values and the rule that a theory fails when every row violates its assumptions.
- `[Explicit]`, `[Repeat]`, parameterized fixtures, fixture sources, and `Assert.Multiple` have no behavior-identical mechanical mapping. Rewrite deliberately or report manual follow-up; never approximate silently.
- NUnit `[Retry(n)]` counts the initial attempt, while MSTest `[Retry(n)]` counts retries after the initial attempt. Subtract one and review NUnit `RetryExceptions` filters separately.
- `[CancelAfter(ms)]` maps to `[Timeout(ms, CooperativeCancellation = true)]`. Under VSTest, remove NUnit's injected `CancellationToken` parameter and use an injected or property-based `TestContext.CancellationToken`; VSTest otherwise treats it as missing data. Retain a method token only when the preserved runner is proven to support injection. Expand fixture-level defaults to every affected test and lifecycle method.
- `[Apartment(ApartmentState.STA)]` maps to `[STATestClass]` or `[STATestMethod]`. Set `UseSTASynchronizationContext = true` when async continuations must remain on the STA thread. MTA is the MSTest default. `[RequiresThread]` and `[SingleThreaded]` guarantee thread identity that MSTest attributes do not generally preserve; use a custom executor or report manual follow-up.
- Map NUnit platform and culture gates to `OSCondition`, `ArchitectureCondition`, or a `MemberCondition` helper. Map `SetCulture` and `SetUICulture` to setup/cleanup that saves and restores the original culture.
- Expand `[DefaultFloatingPointTolerance]` into explicit deltas on every affected assertion. Preserve method-over-fixture-over-assembly precedence.

### 5. Preserve lifecycle and namespace setup

- `[SetUp]` and `[TearDown]` remain per-test lifecycle methods.
- For NUnit's default single fixture instance, audit every mutable instance field. If tests depend on sharing, use static state created by `[ClassInitialize]` and released by `[ClassCleanup]`.
- `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]` already matches MSTest class instantiation; remove the attribute and keep per-test instance state.
- `[SetUpFixture]` is namespace-scoped in NUnit. MSTest assembly initialization is assembly-wide. Use `[AssemblyInitialize]` only when the NUnit setup already covers the whole assembly; otherwise move setup into the affected classes or a shared helper without widening scope.
- Preserve setup/cleanup inheritance order. Do not merge base and derived methods unless the resulting order is proven equivalent.
- Map NUnit `TestContext.WriteLine` to an injected or property-based MSTest `TestContext.WriteLine`. Translate directory, test-name, and attachment APIs individually.

### 6. Preserve parallelization and ordering

NUnit and MSTest both run serially by default. Do not add parallelization for an unconfigured NUnit project.

When NUnit explicitly opts in:

- assembly `[Parallelizable(ParallelScope.Fixtures)]` -> `[assembly: Parallelize(Workers = N, Scope = ExecutionScope.ClassLevel)]`
- fixture `[Parallelizable(ParallelScope.Children)]` or method-level parallelism -> method-level MSTest parallelization only after confirming shared instance state is safe
- `[NonParallelizable]` -> `[DoNotParallelize]` when parallelization is enabled
- `[LevelOfParallelism(N)]` -> `Workers = N`
- resource-specific serialization can use MSTest 4.4 `[ResourceLock("key")]` when it preserves a narrower lock than `[DoNotParallelize]`

NUnit `[Order]` is not general dependency semantics. Use MSTest 4.4 `[DependsOn]` only when the source truly expresses a prerequisite; otherwise remove ordering by making tests independent or report manual follow-up.

### 7. Verify parity

1. Run tests with the same platform, filter, and configuration used for the baseline.
2. Compare discovered, passed, failed, and skipped counts.
3. Investigate every difference:
   - missing rows -> `DataRow`, `DynamicData`, combinatorial, or row metadata conversion
   - changed exceptions/types -> exact-vs-derived mapping
   - state failures -> NUnit single-instance fixture semantics or setup order
   - concurrency failures -> `Parallelize`, `DoNotParallelize`, worker count, or resource locks
   - changed skips -> `Ignore`, `Explicit`, row-level ignore, or retry behavior
4. Confirm no NUnit package, namespace, attribute, constraint, adapter setting, or custom NUnit extension remains unless documented for follow-up.
5. Read back high-risk changed files and name the exact target APIs in the result.

Use this final response shape:

- **Changed:** files and exact high-risk mappings.
- **Verified:** final command and discovered/passed/failed/skipped counts.
- **Preserved:** target framework, test platform, fixture scope, and concurrency choice.
- **Remaining:** manual follow-up, or none.

## Completion Criteria

- NUnit version and test platform were identified
- NUnit packages and source constructs were converted
- Target framework and test platform stayed unchanged
- Default single-instance fixture semantics were audited and preserved where observable
- Data-row metadata and assertion semantics were preserved
- Namespace setup and parallelization scope were not silently widened
- Every attribute on NUnit's official attribute index was classified as direct, rewritten, removed, or manual
- Build succeeds and test results match the baseline
- Unsupported custom extensions or behavior are called out

## Follow-up

Run `migrate-vstest-to-mtp` separately if the user also wants MTP. Use `writing-mstest-tests` only after parity is established.
