---
name: migrate-mstest-v1v2-to-v3
description: >
  Migrate an MSTest v1 or v2 test project to MSTest v3.
  USE FOR: upgrading from MSTest v1 assembly references
  (Microsoft.VisualStudio.QualityTools.UnitTestFramework) or MSTest v2 NuGet
  packages (MSTest.TestFramework 1.x–2.x) to MSTest v3, fixing assertion
  overload errors (AreEqual/AreNotEqual with object), updating DataRow
  constructors, replacing .testsettings with .runsettings, resolving timeout
  behavior changes, adopting MSTest.Sdk, and enabling in-assembly parallel
  execution. This is the first step toward MSTest v4 — after completing this
  migration, use migrate-mstest-v3-to-v4 to reach the latest version.
  DO NOT USE FOR: migrating directly to MSTest v4 (use migrate-mstest-v3-to-v4
  after this skill), migrating between test frameworks (MSTest to xUnit/NUnit),
  or .NET version upgrades (use migrate-dotnet*-to-dotnet* skills).
---

# MSTest v1/v2 → v3 Migration

Migrate a test project from MSTest v1 (assembly references) or MSTest v2 (NuGet packages 1.x–2.x) to MSTest v3. MSTest v3 is **not binary compatible** with v1 or v2 — any library compiled against v1/v2 must be recompiled against v3. The outcome is a project using MSTest v3 that builds cleanly, passes tests, and leverages the improved defaults, security fixes, and performance optimizations in MSTest v3.

## When to Use

- Project references `Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll` directly (MSTest v1 via assembly reference)
- Project uses `MSTest.TestFramework` NuGet package with version 1.x or 2.x
- Project uses `MSTest.TestAdapter` NuGet package with version 1.x or 2.x
- Resolving build errors after updating MSTest NuGet packages from v1/v2 to v3
- Replacing a `.testsettings` file with `.runsettings`
- Adopting modern MSTest features like in-assembly parallel execution or MSTest.Sdk

## When Not to Use

- The project already uses MSTest v3 (3.x packages) — migration is done
- Upgrading from MSTest v3 to v4 — use the `migrate-mstest-v3-to-v4` skill
- Migrating between test frameworks (e.g., MSTest to xUnit or NUnit) — different effort entirely
- The project does not use MSTest at all

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or `.slnx` entry point containing MSTest test projects |
| Build command | No | How to build (e.g., `dotnet build`, a repo build script). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |

## Workflow

### Step 1: Assess the project

1. Identify which MSTest version is currently in use:
   - **Assembly reference**: Look for `Microsoft.VisualStudio.QualityTools.UnitTestFramework` in project references → MSTest v1
   - **NuGet packages**: Check `MSTest.TestFramework` and `MSTest.TestAdapter` package versions → v1 if 1.x, v2 if 2.x
2. Check if the project uses a `.testsettings` file (indicated by `<LegacySettings>` in test configuration)
3. Record the current target framework(s) — MSTest v3 dropped support for:
   - .NET Framework below 4.6.2
   - .NET Standard 1.0 (use 2.0)
   - UWP before build 16299
   - WinUI before build 18362
   - .NET 5 (use .NET Core 3.1 or .NET 6+)
4. Run a clean build to establish a baseline of existing errors/warnings

### Step 2: Remove v1 assembly references (if applicable)

If the project uses MSTest v1 via assembly references:

1. Remove the reference to `Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll`
   - In SDK-style projects, remove the `<Reference>` element from the `.csproj`
   - In non-SDK-style projects, remove via Visual Studio Solution Explorer → References → right-click → Remove
2. Save the project file

### Step 3: Update packages to MSTest v3

Choose one of these approaches:

**Option A — Install the MSTest metapackage (recommended):**

Remove individual `MSTest.TestFramework` and `MSTest.TestAdapter` package references and replace with the unified `MSTest` metapackage:

```xml
<PackageReference Include="MSTest" Version="3.8.0" />
```

Also ensure `Microsoft.NET.Test.Sdk` is referenced:

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
```

**Option B — Update individual packages:**

```xml
<PackageReference Include="MSTest.TestFramework" Version="3.8.0" />
<PackageReference Include="MSTest.TestAdapter" Version="3.8.0" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
```

**Option C — Use MSTest.Sdk (SDK-style projects only):**

Change the project SDK to MSTest.Sdk, which handles all MSTest references automatically:

```xml
<Project Sdk="MSTest.Sdk/3.8.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

When using MSTest.Sdk, remove all explicit MSTest and Microsoft.NET.Test.Sdk package references — the SDK provides them.

### Step 4: Update target frameworks if needed

If the project targets a dropped framework version, update to a supported one:

| Dropped | Minimum supported |
|---------|-------------------|
| .NET Framework < 4.6.2 | .NET Framework 4.6.2 |
| .NET Standard 1.0 | .NET Standard 2.0 |
| UWP < 16299 | UWP 16299 |
| WinUI < 18362 | WinUI 18362 |
| .NET 5 | .NET Core 3.1 or .NET 6+ |

### Step 5: Resolve build errors and breaking changes

Run `dotnet build` and address errors. Common breaking changes:

#### Assertion overloads (AreEqual / AreNotEqual / AreSame / AreNotSame)

MSTest v3 removed the `Assert.AreEqual(object, object)` and `Assert.AreNotEqual(object, object)` overloads. If these assertions now fail to compile, add explicit generic type parameters:

```csharp
// Before (v1/v2) — compiled against object overload
Assert.AreEqual(expectedObject, actualObject);

// After (v3) — use explicit generic typing
Assert.AreEqual<MyType>(expectedObject, actualObject);
```

Similarly, `Assert.AreSame` and `Assert.AreNotSame` now use generic overloads:

```csharp
Assert.AreSame<MyType>(expected, actual);
Assert.AreNotSame<MyType>(expected, actual);
```

#### DataRow constructor changes

MSTest v3 simplified `DataRowAttribute` constructors to enforce strict type matching. DataRow values must precisely match method parameter types:

```csharp
// Correct: types match exactly
[DataRow(1, "test")]
public void MyTest(int number, string text) { }

// Error in v3: implicit conversions no longer accepted
[DataRow(1L, "test")]  // long won't auto-convert to int
public void MyTest(int number, string text) { }
```

Also, `DataRowAttribute` now supports a maximum of 16 constructor parameters. If you have more than 16, wrap parameters in an array or refactor the test.

#### Timeout behavior changes

MSTest v3 unified timeout handling across .NET Core and .NET Framework. Tests with `[Timeout]` attributes may behave differently — verify timeout values are still appropriate:

```csharp
[TestMethod]
[Timeout(2000)] // Verify this value still works under MSTest v3
public async Task TestMethod() { }
```

### Step 6: Replace .testsettings with .runsettings

The `.testsettings` file and `<LegacySettings>` are no longer supported in MSTest v3. Migrate to `.runsettings`:

```xml
<!-- .runsettings -->
<RunSettings>
  <RunConfiguration>
    <MaxCpuCount>-1</MaxCpuCount> <!-- Uses all available processors -->
  </RunConfiguration>
  <MSTest>
    <Parallelize>
      <Workers>0</Workers> <!-- 0 = number of processors -->
      <Scope>MethodLevel</Scope>
    </Parallelize>
  </MSTest>
</RunSettings>
```

### Step 7: Verify

1. Run `dotnet build` — confirm zero errors and review any new warnings
2. Run `dotnet test` — confirm all tests pass
3. Compare test results (pass/fail counts) to the pre-migration baseline
4. Check that no tests were silently dropped due to discovery changes

## Validation

- [ ] All MSTest v1/v2 assembly references or NuGet packages are removed
- [ ] MSTest v3 packages (or MSTest.Sdk) are correctly referenced
- [ ] Project builds with zero errors
- [ ] All tests pass with `dotnet test`
- [ ] `.testsettings` file replaced with `.runsettings` (if applicable)
- [ ] Timeout behavior verified for time-sensitive tests
- [ ] No tests were lost during migration (compare test counts)

## Next Step: Migrate to MSTest v4

After completing this migration to MSTest v3, proceed to the `migrate-mstest-v3-to-v4` skill to upgrade to MSTest v4 (the latest version). MSTest v4 introduces additional breaking changes (async TestMethodAttribute, ClassCleanupBehavior removal, Assert API changes, ExpectedException removal, dropped .NET < 8 support) that are covered in that skill.

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| `Assert.AreEqual(obj, obj)` no longer compiles | Add explicit generic type: `Assert.AreEqual<T>(expected, actual)` |
| DataRow with > 16 parameters fails | Refactor to use fewer parameters or wrap in an array |
| DataRow implicit type conversions fail | Match DataRow argument types exactly to method parameter types |
| `.testsettings` ignored silently | Migrate to `.runsettings` — legacy settings are not supported |
| Tests with tight timeouts fail | MSTest v3 unified timeout handling; adjust values if needed |
| Target framework no longer supported | Update to minimum supported version (e.g., net462, netstandard2.0) |
| Missing `Microsoft.NET.Test.Sdk` | Add package reference — required for test discovery with VSTest |
