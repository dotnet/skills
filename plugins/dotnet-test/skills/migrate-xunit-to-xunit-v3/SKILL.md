---
name: migrate-xunit-to-xunit-v3
description: >
  Migrates .NET test projects from xUnit.net v2 to xUnit.net v3.
  USE FOR: upgrading xunit to xunit.v3.
  DO NOT USE FOR: migrating between test frameworks (MSTest/NUnit to
  xUnit.net), migrating from VSTest to Microsoft.Testing.Platform
  (use migrate-vstest-to-mtp).
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

### Step 1: Identify xUnit.net projects

Search for test projects referencing xUnit.net v2 packages:

- `xunit`
- `xunit.abstractions`
- `xunit.assert`
- `xunit.core`
- `xunit.extensibility.core`
- `xunit.extensibility.execution`
- `xunit.runner.visualstudio`

Make sure to check the package references in project files, MSBuild props and targets files, like `Directory.Build.props`, `Directory.Build.targets`, and `Directory.Packages.props`.

### Step 2: Verify compatibility

1. Verify target framework compatibility: xUnit.net v3 requires **.NET 8+** or **.NET Framework 4.7.2+**. For test library projects, .NET Standard 2.0 is also supported.
2. If any of the test projects have non-compatible target frameworks, STOP here and DON'T do anything. Only tell the user to upgrade the target framework first before migrating xUnit.net.
3. Verify project compatibility: xUnit.net v3 only supports SDK-style projects. If any test projects are non-SDK-style, STOP here and DON'T do anything. Only tell the user to migrate to SDK-style projects first before migrating xUnit.net.

### Step 3: Establish a baseline

Run `dotnet test` to establish a baseline of test pass/fail counts. When running `dotnet test`, ensure that:

- You run `dotnet test` without any additional arguments (i.e., don't pass `--no-restore` or `--no-build`).
- Ensure you redirect the command output to a file and read the output from that file.

### Step 4: Update package references

1. Update any `PackageReference` or `PackageVersion` items for the new package names, based on the following mapping:

    - `xunit` → `xunit.v3`
    - `xunit.abstractions` → Remove entirely
    - `xunit.assert` → `xunit.v3.assert`
    - `xunit.core` → `xunit.v3.core`
    - `xunit.extensibility.core` and `xunit.extensibility.execution` → `xunit.v3.extensibility.core` (if both are referenced in a project consolidate to only a single entry as the two packages are merged)

2. Update all `xunit.v3.*` packages to the latest correct version available on NuGet. Also update `xunit.runner.visualstudio` to the latest version.

### Step 5: Set `OutputType` to `Exe`

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

### Step 6: Remove `Xunit.Abstractions` usings

Find any `using Xunit.Abstractions;` directives in C# files and remove them completely.

### Step 7: Address `async void` breaking change

In xUnit.net v3, `async void` test methods are no longer supported and will fail to compile. Search for any test methods declared with `async void` and change them to `async Task`. Test methods can be identified via the `[Fact]` or `[Theory]` attributes or other test attributes.

### Step 8: Address breaking change of attributes

In xUnit.net v3, some attributes were updated so that they accept a `System.Type` instead of two strings (fully qualified type name and assembly name). These attributes are:

- `CollectionBehaviorAttribute`
- `TestCaseOrdererAttribute`
- `TestCollectionOrdererAttribute`
- `TestFrameworkAttribute`

For example, `[assembly: CollectionBehavior("MyNamespace.MyCollectionFactory", "MyAssembly")]` must be converted to `[assembly: CollectionBehavior(typeof(MyNamespace.MyCollectionFactory))]`.

### Step 9: Inheriting from FactAttribute or TheoryAttribute

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

### Step 10: Inheriting from BeforeAfterTestAttribute

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

### Step 11: Address new xUnit analyzer warnings

xunit.v3 introduced new analyzer warnings. You should attempt to address them.

One of the most notable warnings is [xUnit1051: Calls to methods which accept CancellationToken should use TestContext.Current.CancellationToken](https://xunit.net/xunit.analyzers/rules/xUnit1051). Identify the calls to such methods, if any, and pass the cancellation token.

### Step 12: Test platform selection

You should keep the same test platform that was used with xunit 2.

Note that xunit 2 is always VSTest except if the user used YTest.MTP.XUnit2.

- If user had a reference to YTest.MTP.XUnit2:
    - Remove the reference to YTest.MTP.XUnit2 completely.
    - Add `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` to Directory.Build.props under an unconditional PropertyGroup.
- If user didn't have a reference to YTest.MTP.XUnit2:
    - Add `<IsTestingPlatformApplication>false</IsTestingPlatformApplication>` to Directory.Build.props under an unconditional PropertyGroup.

### Step 13: Migrate `Xunit.SkippableFact`

If there are any package references to `Xunit.SkippableFact`, remove all these package references entirely.

Then, follow these steps to eliminate usages of APIs coming from the removed package reference:

- Update any `SkippableFact` attribute to the regular `Fact` attribute.
- Update any `SkippableTheory` attribute to the regular `Theory` attribute.
- Change `Skip.If` method calls to `Assert.SkipWhen`.
- Change `Skip.IfNot` method calls to `Assert.SkipUnless`.

### Step 14: Update `Xunit.Combinatorial` NuGet package

Find package references of `Xunit.Combinatorial` and update them from 1.x to the latest 2.x version available.

### Step 15: Update `Xunit.StaFact` NuGet package

Find package references of `Xunit.StaFact` and update them from 1.x to the latest 3.x version available.

### Step 16: Build the solution

Now, build the solution to identify any remaining compilation errors that might not have been addressed by previous instructions.
Fix any straightforward errors that show up, and keep iterating and fixing more.

You can also look into https://xunit.net/docs/getting-started/v3/migration-extensibility and https://xunit.net/docs/getting-started/v3/migration to help with the remaining compilation errors.

You can fix as much as you can, and it's okay if not everything is fixed. Just tell the user that there are remaining errors that need to be manually addressed.
