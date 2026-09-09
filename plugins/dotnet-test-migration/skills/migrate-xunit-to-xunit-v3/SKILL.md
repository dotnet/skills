---
name: migrate-xunit-to-xunit-v3
description: >
  Migrate .NET test projects from xUnit.net v2 to xunit.v3 and fix v3 breaks.
  Use for package/CPM conversion, OutputType=Exe, preserving the VSTest or MTP
  runner (including projects currently using YTest.MTP.XUnit2), incompatible
  TFMs, async void tests,
  string-to-Type attributes, custom Fact/Theory/BeforeAfterTest attributes,
  Xunit.SkippableFact, xunit.abstractions/extensibility consolidation, and
  Xunit.Combinatorial/StaFact compatibility. Do not use for framework
  conversion or a runner-only migration. For xUnit v3 MTP filter syntax, also
  use migrate-vstest-to-mtp.
license: MIT
---

# xunit.v3 Migration

Migrate .NET test projects from xUnit.net v2 to xUnit.net v3. The outcome is a solution where all test projects reference `xunit.v3.*` packages, compiles cleanly, and all tests pass with the same results as before migration.

## When to Use

- Upgrading test projects from `xunit` (v2) packages to `xunit.v3`
- Resolving compilation errors after updating xunit package references to v3

## When Not to Use

- Migrating between test frameworks (e.g., MSTest or NUnit to xUnit.net) — different effort entirely
- Migrating from VSTest to Microsoft.Testing.Platform — use `migrate-vstest-to-mtp`
- The projects already reference `xunit.v3` — migration is done

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test project or solution | No | Discover `.csproj`, `.sln`, `.slnx`, central props, and source in the current working directory; ask only if none are found or the target is ambiguous |

## Workspace and Completion Contract

- Skill activation is not completion. For migrate/fix/update requests, inspect
  the staged files, edit them, and run tests in the same task.
- The skill base directory contains only guidance. Search the current working
  directory and open paths exactly as returned. If a tool rejects a path just
  found by search, retry with another available reader/editor instead of
  concluding that files are missing.
- Do not ask the user to provide a path while workspace discovery can find it.
- Inventory project/central package files and all affected source in one pass.
  A package-only migration is incomplete when v2-only APIs remain.
- End with the detected source version and runner, exact package compatibility
  set, files changed, discovered/passed/failed/skipped counts, and any
  platform-specific result. A build without test discovery is not success.
- Package validity is empirical: record the configured feed query or resolved
  package graph and a successful restore. In the final result, say how the
  selected exact versions were proven available; do not merely name a version
  and leave a judge or user to infer whether it exists.

## Workflow

> **Commit strategy:** Do not create commits unless the user asks. Keep project
> configuration and source edits logically separable in the diff, but finish
> and verify the whole requested migration.

> **Prioritization:** Steps 1-5 are required for every migration. Steps 6-12 are conditional — only apply the ones relevant to the project's code patterns. Skip steps that don't apply.

### Decisions that change the result

Run this preflight before editing:

| Detected state | Required action |
|---|---|
| Project references `xunit.v3`, has the required executable/runner configuration, contains no remaining v2 packages or v2-only API patterns, and its existing test command passes | Stop: migration is already complete. Do not update versions, create props files, or modify source. Report the verified no-op result. If any required v3 adaptation remains, make only that repair instead of treating the package reference alone as completion. |
| xUnit v2 uses `YTest.MTP.XUnit2` | Preserve MTP: remove that shim, set `UseMicrosoftTestingPlatformRunner=true`, and do not add `xunit.runner.visualstudio` or `IsTestingPlatformApplication=false`. |
| xUnit v2 does not use the MTP shim | Preserve VSTest: keep/update `xunit.runner.visualstudio` and set `IsTestingPlatformApplication=false`. |
| A custom type derives from `BeforeAfterTestAttribute` | Preserve that inheritance and its behavior. Add the `IXunitTest` parameter to both overrides and pass it to `base.Before`/`base.After`; do not replace the subclass with a direct interface implementation. |
| A Type-based collection/orderer attribute points to a custom type | Migrate both the attribute syntax and the referenced type's v3 contract. For a collection factory, implement the required xUnit v3 `IXunitTestCollectionFactory` behavior; compiling the attribute while leaving an empty factory is not a complete migration. |
| Companion packages are present | Resolve `xunit.v3`, Xunit.Combinatorial, and Xunit.StaFact as one compatible set from configured feeds. If the newest xunit.v3 major has no compatible stable companion on those feeds, select the newest compatible xunit.v3 major and explain the pin. Validate discovery, not just compilation. |
| `OutputType=Exe` makes a `net*-windows` project fail on a non-Windows host | Add `EnableWindowsTargeting=true` when cross-building is intended, then rerun. Do not dismiss this migration-induced failure as pre-existing. |

Resolve package versions from the configured package source. Do not guess a
version from the product's "v3" name or update unrelated packages. Change only
files that contain a package, property, or source construct required by the
applicable rule.

After editing a Central Package Management project, read back both
`Directory.Packages.props` and the project file. Confirm that `PackageVersion`
owns the version, the renamed `PackageReference` is versionless, and
`OutputType=Exe` is effective.

### Step 1: Identify xUnit.net projects and verify compatibility

Search for test projects referencing xUnit.net v2 packages:

- `xunit`
- `xunit.abstractions`
- `xunit.assert`
- `xunit.core`
- `xunit.extensibility.core`
- `xunit.extensibility.execution`
- `xunit.runner.visualstudio`

Make sure to check the package references in project files, MSBuild props and targets files, like `Directory.Build.props`, `Directory.Build.targets`, and `Directory.Packages.props`.

Verify target framework compatibility: xUnit.net v3 requires **.NET 8+** or **.NET Framework 4.7.2+**. For test library projects, .NET Standard 2.0 is also supported. If any test projects have non-compatible target frameworks, STOP here — tell the user to upgrade the target framework first. Also verify the project uses SDK-style format.

### Step 2: Update package references

1. Update any `PackageReference` or `PackageVersion` items for the new package names, based on the following mapping:

    - `xunit` → `xunit.v3`
    - `xunit.abstractions` → Remove entirely
    - `xunit.assert` → `xunit.v3.assert`
    - `xunit.core` → `xunit.v3.core`
    - `xunit.extensibility.core` and `xunit.extensibility.execution` → `xunit.v3.extensibility.core` (if both are referenced in a project consolidate to only a single entry as the two packages are merged)

2. Query the configured package source and pin the latest stable versions that actually exist. Also update `xunit.runner.visualstudio` only for VSTest projects; do not add it to MTP projects.

### Step 3: Set `OutputType` to `Exe`

In each test project (excluding test library projects), set `OutputType` to `Exe` in the project file:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

Depending on the solution in hand, there might be a centralized place where this can be added. For example:

- If all test projects share (or can share) a common `Directory.Build.props`, add the `<OutputType>Exe</OutputType>` property there. Note that the OutputType should not be added to `Directory.Build.targets`.
- If all test projects share a name pattern (e.g., `*.Tests.csproj`), add a conditional property group in `Directory.Build.props` that applies only to those projects, like `<OutputType Condition="$(MSBuildProjectName.EndsWith('.Tests'))">Exe</OutputType>`. Adjust the condition as needed to target only test projects.
- Otherwise, add the `<OutputType>Exe</OutputType>` property to each test project file individually.

### Step 4: Configure test platform

Preserve the same test platform that was used with xUnit.net v2. xUnit.net v2 always uses VSTest except if the project used `YTest.MTP.XUnit2`.

- If the project had a reference to `YTest.MTP.XUnit2`:
  - Remove the reference to `YTest.MTP.XUnit2` completely.
  - Set `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` in an existing shared `Directory.Build.props`, or in the test project when no shared props file exists.
  - Do not add `xunit.runner.visualstudio`; it is a VSTest runner and weakens platform preservation.
- If the project did NOT reference `YTest.MTP.XUnit2` (the common case):
  - Set `<IsTestingPlatformApplication>false</IsTestingPlatformApplication>` in an existing shared `Directory.Build.props`, or directly in the test project when no shared props file exists. Do not create a repository-wide props file solely for one project. This keeps the project on VSTest.

### Step 5: Remove `Xunit.Abstractions` usings

Find any `using Xunit.Abstractions;` directives in C# files and remove them completely.

### Step 6: Address `async void` breaking change (if applicable)

In xUnit.net v3, `async void` test methods are no longer supported and will fail to compile. Search for any test methods declared with `async void` and change them to `async Task`. Test methods can be identified via the `[Fact]` or `[Theory]` attributes or other test attributes.

In the final result, state why the source changed: xUnit.net v3 rejects
`async void` tests, so each affected method now returns `Task`. Do not report
only the mechanical replacement. Also state how the exact package versions were
resolved from the configured feed.

### Step 7: Address breaking change of attributes (if applicable)

In xUnit.net v3, some attributes were updated so that they accept a `System.Type` instead of two strings (fully qualified type name and assembly name). These attributes are:

- `CollectionBehaviorAttribute`
- `TestCaseOrdererAttribute`
- `TestCollectionOrdererAttribute`
- `TestFrameworkAttribute`

For example, `[assembly: CollectionBehavior("MyNamespace.MyCollectionFactory", "MyAssembly")]` must be converted to `[assembly: CollectionBehavior(typeof(MyNamespace.MyCollectionFactory))]`.

### Step 8: Inheriting from FactAttribute or TheoryAttribute (if applicable)

Identify if there are any custom attributes that inherit from `FactAttribute` or `TheoryAttribute`. These custom user-defined attributes must now provide source information. For example, if the attribute looked like this:

```csharp
internal sealed class MyFactAttribute : FactAttribute
{
    public MyFactAttribute()
    {
    }
}
```

it must be changed to this:

```csharp
internal sealed class MyFactAttribute : FactAttribute
{
    public MyFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    ) : base(sourceFilePath, sourceLineNumber)
    {
    }
}
```

Before reporting completion, read back every affected `FactAttribute`- and
`TheoryAttribute`-derived constructor. Name each type and confirm that both
caller-info parameters and the corresponding `base(sourceFilePath,
sourceLineNumber)` forwarding are present. A passing test run alone does not
prove source information was propagated.

### Step 9: Inheriting from BeforeAfterTestAttribute (if applicable)

Identify if there are any custom attributes that inherit from `BeforeAfterTestAttribute`. These custom user-defined attributes must update their method signatures. Previously, they would have `Before`/`After` overrides that look like this:

```csharp
    public override void Before(MethodInfo methodUnderTest)
    {
        // Possibly some custom logic here
        base.Before(methodUnderTest);
        // Possibly some custom logic here
    }

    public override void After(MethodInfo methodUnderTest)
    {
        // Possibly some custom logic here
        base.After(methodUnderTest);
        // Possibly some custom logic here
    }
```

it must be changed to this:

```csharp
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        // Possibly some custom logic here
        base.Before(methodUnderTest, test);
        // Possibly some custom logic here
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        // Possibly some custom logic here
        base.After(methodUnderTest, test);
        // Possibly some custom logic here
    }
```

Keep the `BeforeAfterTestAttribute` base class, retain the override modifiers, and preserve the
existing base calls and their ordering relative to custom logic. Implementing
`IBeforeAfterTestAttribute` directly may compile, but it is not the mechanical v2-to-v3 migration
and can discard base-class behavior.

Before reporting completion, read the resulting attribute file and quote the
actual `Before(MethodInfo, IXunitTest)` and `After(MethodInfo, IXunitTest)`
signatures. Explicitly confirm that both `base.Before` and `base.After` receive
the same `IXunitTest` argument; a generic claim that the overrides were updated
is insufficient evidence.

### Step 10: Address new xUnit analyzer warnings (if applicable)

xunit.v3 introduced new analyzer warnings. The most notable is xUnit1051 (use `TestContext.Current.CancellationToken` for methods accepting `CancellationToken`). Address these if present.

### Step 11: Migrate `Xunit.SkippableFact` (if applicable)

If there are any package references to `Xunit.SkippableFact`, remove all these package references entirely.

Then, follow these steps to eliminate usages of APIs coming from the removed package reference:

- Update any `SkippableFact` attribute to the regular `Fact` attribute.
- Update any `SkippableTheory` attribute to the regular `Theory` attribute.
- Change `Skip.If` method calls to `Assert.SkipWhen`.
- Change `Skip.IfNot` method calls to `Assert.SkipUnless`.

Verify both branches when the fixture makes that practical: the default
condition should report the intended skip reason, and the enabled condition
should execute and pass. A grep plus a generic passing run does not prove the
runtime skip semantics were preserved.

Limit this conversion to the existing project/central-package file and the source files containing
these APIs. Do not create a new `Directory.Build.props` merely to perform this companion-package
migration; any required runner property belongs in the existing test project when no shared props
file already exists.

### Step 12: Update companion packages (if applicable)

- Query the configured feeds for a mutually compatible set instead of resolving
  each package independently. `Xunit.Combinatorial` 1.x moves to 2.x or later,
  and `Xunit.StaFact` 1.x moves to a line compatible with the selected
  `xunit.v3` major.
- Do not infer companion compatibility from the product name or matching major
  numbers. Use package dependency constraints and the versions available on the
  configured feeds, then prove the selected set through test discovery.
- On `net*-windows` projects built from Linux/macOS after switching to
  executable output, set `EnableWindowsTargeting=true` if cross-targeting is
  intended.
- Run tests and confirm expected platform skips (such as STA tests on Linux)
  separately from failures.

### Step 13: Build and verify

Build the solution and fix any remaining compilation errors. Run `dotnet test` to verify all tests pass with the same results as before migration.
