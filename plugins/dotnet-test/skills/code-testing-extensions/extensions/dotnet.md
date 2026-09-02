# .NET Extension

Language-specific guidance for .NET (C#/F#/VB) test generation.

## Project System Detection

Determine the project system before choosing any command or editing a manifest.

| Signal | Project system | Consequence |
|---|---|---|
| Root `<Project Sdk="...">` or an `Sdk` attribute | SDK-style | `dotnet build` / `dotnet test` are normally valid; new `*.cs` files are usually included by glob |
| `ToolsVersion`, `Microsoft.Common.props` / `Microsoft.CSharp.targets` imports, explicit `<Reference>` and `<Compile Include>` items | Classic non-SDK | Preserve the repository's MSBuild / test-runner commands; every new source or test file must be added to the project |
| `packages.config` beside the project | Classic NuGet dependency management | Preserve `packages.config` and assembly references; do not run `dotnet add package` or introduce `PackageReference` unless the user explicitly requested a migration |

For classic projects, inspect repository scripts, CI configuration, `README*`, and
`AGENTS.md` for the authoritative build and test commands. Common commands are
`MSBuild.exe` followed by `vstest.console.exe` or `MSTest.exe`, but the checked-in
command wins. If no compatible runner is installed, report that blocker instead of
migrating the project or claiming `dotnet test` succeeded.

## Build Commands

| Scope | Command |
|-------|---------|
| SDK-style test project | `dotnet build MyProject.Tests.csproj` |
| SDK-style solution (final validation) | `dotnet build MySolution.sln --no-incremental` |
| Classic non-SDK project | Use the repository's existing MSBuild command (often `MSBuild.exe MySolution.sln /t:Build`) |

- Use `--no-restore` if dependencies are already restored
- Use `-v:q` (quiet) to reduce output noise
- Always use `--no-incremental` for the final validation build — incremental builds hide errors like CS7036

## Test Commands

| Scope | Command |
|-------|---------|
| SDK-style all tests | `dotnet test` |
| SDK-style filtered | `dotnet test --filter "FullyQualifiedName~ClassName"` |
| SDK-style after build | `dotnet test --no-build` |
| Classic non-SDK | Use the checked-in runner command; commonly `vstest.console.exe <test.dll>` after MSBuild |

- Use `--no-build` if already built
- Use `-v:q` for quieter output

## Lint Command

```bash
dotnet format --include path/to/file.cs
dotnet format MySolution.sln         # full solution
```

## Project Reference Validation

Before writing test code, read the test project's `.csproj` to verify it has `<ProjectReference>` entries for the assemblies your tests will use. If a reference is missing, add it:

```xml
<ItemGroup>
    <ProjectReference Include="../SourceProject/SourceProject.csproj" />
</ItemGroup>
```

This prevents CS0234 ("namespace not found") and CS0246 ("type not found") errors.
In a classic project, preserve its existing `<ProjectReference>` metadata and
configuration mappings instead of replacing them with the SDK-style shorthand.

## Common CS Error Codes

| Error | Meaning | Fix |
|-------|---------|-----|
| CS0234 | Namespace not found | Add `<ProjectReference>` to the source project in the test `.csproj` |
| CS0246 | Type not found | Add `using Namespace;` or add missing `<ProjectReference>` |
| CS0103 | Name not found | Check spelling, add `using` statement |
| CS1061 | Missing member | Verify method/property name matches the source code exactly |
| CS0029 | Type mismatch | Cast or change the type to match the expected signature |
| CS7036 | Missing required parameter | Read the constructor/method signature and pass all required arguments |

## `.csproj` / `.sln` Handling

- During phase implementation, build only the specific test `.csproj` for speed
- For the final validation, build the full `.sln` with `--no-incremental`
- Full-solution builds catch cross-project reference errors invisible in scoped builds

### Registering test code with the build (MANDATORY)

Before writing a new C# test file, inspect the test project's compile items.

- SDK-style projects normally include `*.cs` by glob. Do not add a redundant
  `<Compile Include>` unless default compile items are disabled.
- Classic non-SDK projects require an explicit item for every new file. Add a
  path relative to the project, preserving its path separator and ordering:

```xml
<Compile Include="Services\OrderServiceTests.cs" />
```

After editing, re-open the project and verify the exact new test path appears
once. A file on disk that is missing from a classic project's compile items is
not part of the test assembly and must never be reported as generated coverage.

### Registering a new test project (MANDATORY when `dotnet new` was used)

A new `.csproj` is **invisible** to `dotnet test <solution>`, to `dotnet test` run from the repo root, and to any CI/benchmark harness until it is added to the solution. Run `dotnet sln add` *immediately* after creating the project as part of Step 3 ("Register Test Project with Build System") — do not defer it to a later step.

1. Use the exact solution or solution-filter target identified in the research or plan document under `<TESTAGENT_DIR>` — do not search for or substitute a different `.sln`, `.slnx`, or `.slnf` target.
2. If that target is a `.sln` or `.slnx`, run `dotnet sln <solution> add <test-project.csproj>`.
3. If the target is a `.slnf` (solution filter), also ensure the new project is included in the filter; adding only to the underlying `.sln` may not be enough for test discovery.
4. Skip this if the project is already included in the solution or solution filter used for testing.
5. Prefer the researched test command. If you need to run the solution directly, use `dotnet test --solution <solution>` only for repos on .NET SDK 10+ with MTP-style syntax; otherwise use the standard positional form `dotnet test <solution>`.

### Harness Discovery Check

Before reporting success, run the **harness-equivalent** discovery command from the repo root and confirm the test count went up by at least the number of tests you generated. The harness (CI, msbench, coverage tools) does not know which `.csproj` you targeted — it runs the solution-level command, so a test that passes via `dotnet test MyProject.Tests.csproj` is still worthless if `dotnet test <solution> --list-tests` doesn't enumerate it.

```bash
# From repo root, against the solution identified in <TESTAGENT_DIR>/research.md
dotnet test <solution> --list-tests --no-build 2>&1 | grep -c '^    [A-Za-z]'
```

If the delta is `0`, the new project isn't in the solution. Run `dotnet sln <solution> add <test-project.csproj>` and re-run the check. Do **not** report success until the harness command sees your new tests.

For a classic non-SDK project, use the repository's normal build and discovery
command instead of the example above. The minimum acceptable check is:

1. the new file is present exactly once as `<Compile Include="...">`;
2. the classic project builds with its documented MSBuild command; and
3. the repository's test runner discovers the new test(s).

If the environment lacks the required Visual Studio/MSBuild/test-runner toolchain,
verify item registration, report that execution is blocked, and do not substitute
`dotnet test` or modernize the project.

## Test Framework Detection

Detect the framework and installed version from the test project's `.csproj`,
`packages.config`, and referenced assembly `HintPath` values. Match the existing
framework, mocking library, base fixtures, and API level:

| Package Reference | Framework | Attributes | Assertion Style |
|-------------------|-----------|------------|-----------------|
| `MSTest.Sdk` or `MSTest.TestFramework` | MSTest | `[TestClass]`, `[TestMethod]`, `[DataRow]` | `Assert.AreEqual(expected, actual)` |
| `xunit` | xUnit | `[Fact]`, `[Theory]`, `[InlineData]` | `Assert.Equal(expected, actual)` |
| `NUnit` | NUnit | `[TestFixture]`, `[Test]`, `[TestCase]` | `Assert.That(actual, Is.EqualTo(expected))` |

Use the repo's existing framework — do not introduce a different one.

For MSTest, load `writing-mstest-tests` only for APIs supported by the installed
version. In particular, `Assert.ThrowsExactly` and the unified collection
assertions require MSTest 3.8+, while older suites should keep compatible
`Assert.ThrowsException`, `StringAssert`, and `CollectionAssert` patterns. Never
upgrade MSTest, Moq, NBuilder, or another test dependency merely to use a newer
example.

## MSTest Template

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProjectName.Tests;

[TestClass]
public sealed class ClassNameTests
{
    [TestMethod]
    public void MethodName_Scenario_ExpectedResult()
    {
        // Arrange
        var sut = new ClassName();

        // Act
        var result = sut.MethodName(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DataRow(2, 3, 5, DisplayName = "Positive numbers")]
    [DataRow(-1, 1, 0, DisplayName = "Negative and positive")]
    public void Add_ValidInputs_ReturnsSum(int a, int b, int expected)
    {
        // Act
        var result = _sut.Add(a, b);

        // Assert
        Assert.AreEqual(expected, result);
    }
}
```

## Skip Coverage Tools

Do not configure or run code coverage measurement tools (coverlet, dotnet-coverage, XPlat Code Coverage) by default. These tools have inconsistent cross-configuration behavior and waste significant time. Coverage is measured separately by the evaluation harness.

**SDK-style exception**: if the user or evaluation harness explicitly requires a
Cobertura/XML artifact, add `coverlet.collector` as a `PackageReference` so the
harness can produce it. For classic non-SDK projects, preserve `packages.config`
and use only the repository's existing coverage workflow; never inject a
`PackageReference`. Do not run the coverage command yourself.
