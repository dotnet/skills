---
name: migrate-xunit-to-xunit-v3
description: >
  Migrates .NET test projects from xUnit.net v2 to xUnit.net v3.
  USE FOR: upgrading xunit to xunit.v3.
  DO NOT USE FOR: migrating between test frameworks (MSTest/NUnit to
  xUnit.net), migrating from VSTest to Microsoft.Testing.Platform
  (use migrate-vstest-to-mtp). For xUnit v3 MTP filter syntax
  (--filter-class, --filter-trait, --filter-query), also load
  migrate-vstest-to-mtp.
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
| Test project or solution | Yes | The .NET project or solution containing xUnit.net v2 test projects |

## Workflow

> **Commit strategy:** Commit after each major step so the migration is reviewable and bisectable. Separate project file changes from code changes.

> **Prioritization:** Steps 1-5 are required for every migration. Steps 6-12 are conditional — only apply the ones relevant to the project's code patterns. Skip steps that don't apply.

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

2. Update all `xunit.v3.*` packages to the latest correct version available on NuGet. Also update `xunit.runner.visualstudio` to the latest version.

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
  - Add `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` to `Directory.Build.props` under an unconditional `PropertyGroup`.
- If the project did NOT reference `YTest.MTP.XUnit2` (the common case):
  - Add `<IsTestingPlatformApplication>false</IsTestingPlatformApplication>` to `Directory.Build.props` under an unconditional `PropertyGroup`. If `Directory.Build.props` doesn't exist, create it. This keeps the project on VSTest.

### Step 5: Remove `Xunit.Abstractions` usings

Find any `using Xunit.Abstractions;` directives in C# files and remove them completely.

### Step 6: Address `async void` breaking change (if applicable)

In xUnit.net v3, `async void` test methods are no longer supported and will fail to compile. Search for any test methods declared with `async void` and change them to `async Task`. Test methods can be identified via the `[Fact]` or `[Theory]` attributes or other test attributes.

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

### Step 10: Address new xUnit analyzer warnings (if applicable)

xunit.v3 introduced new analyzer warnings. The most notable is xUnit1051 (use `TestContext.Current.CancellationToken` for methods accepting `CancellationToken`). Address these if present.

### Step 11: Migrate `Xunit.SkippableFact` (if applicable)

If there are any package references to `Xunit.SkippableFact`, remove all these package references entirely.

Then, follow these steps to eliminate usages of APIs coming from the removed package reference:

- Update any `SkippableFact` attribute to the regular `Fact` attribute.
- Update any `SkippableTheory` attribute to the regular `Theory` attribute.
- Change `Skip.If` method calls to `Assert.SkipWhen`.
- Change `Skip.IfNot` method calls to `Assert.SkipUnless`.

### Step 12: Update companion packages (if applicable)

- `Xunit.Combinatorial` 1.x → latest 2.x
- `Xunit.StaFact` 1.x → latest 3.x

### Step 13: Build and verify

Build the solution and fix any remaining compilation errors. Run `dotnet test` to verify all tests pass with the same results as before migration.
