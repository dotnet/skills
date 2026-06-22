# .NET Extension

Language-specific guidance for .NET (C#/F#/VB) test generation.

## Build Commands

| Scope | Command |
|-------|---------|
| Specific test project | `dotnet build MyProject.Tests.csproj` |
| Full solution (final validation) | `dotnet build MySolution.sln --no-incremental` |
| From repo root (no .sln) | `dotnet build --no-incremental` |

- Use `--no-restore` if dependencies are already restored
- Use `-v:q` (quiet) to reduce output noise
- Always use `--no-incremental` for the final validation build — incremental builds hide errors like CS7036

## Test Commands

| Scope | Command |
|-------|---------|
| All tests | `dotnet test` |
| Filtered | `dotnet test --filter "FullyQualifiedName~ClassName"` |
| After build | `dotnet test --no-build` |

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

### Registering a new test project (MANDATORY when `dotnet new` was used)

A new `.csproj` is **invisible** to `dotnet test <solution>`, to `dotnet test` run from the repo root, and to any CI/benchmark harness until it is added to the solution. Run `dotnet sln add` *immediately* after creating the project as part of Step 3 ("Register Test Project with Build System") — do not defer it to a later step.

1. Use the exact solution or solution-filter target identified in `.testagent/research.md` or `.testagent/plan.md` — do not search for or substitute a different `.sln`, `.slnx`, or `.slnf` target.
2. If that target is a `.sln` or `.slnx`, run `dotnet sln <solution> add <test-project.csproj>`.
3. If the target is a `.slnf` (solution filter), also ensure the new project is included in the filter; adding only to the underlying `.sln` may not be enough for test discovery.
4. Skip this if the project is already included in the solution or solution filter used for testing.
5. Prefer the researched test command. If you need to run the solution directly, use `dotnet test --solution <solution>` only for repos on .NET SDK 10+ with MTP-style syntax; otherwise use the standard positional form `dotnet test <solution>`.

### Harness Discovery Check

Before reporting success, run the **harness-equivalent** discovery command from the repo root and confirm the test count went up by at least the number of tests you generated. The harness (CI, msbench, coverage tools) does not know which `.csproj` you targeted — it runs the solution-level command, so a test that passes via `dotnet test MyProject.Tests.csproj` is still worthless if `dotnet test <solution> --list-tests` doesn't enumerate it.

```bash
# From repo root, against the solution identified in .testagent/research.md
dotnet test <solution> --list-tests --no-build 2>&1 | grep -c '^    [A-Za-z]'
```

If the delta is `0`, the new project isn't in the solution. Run `dotnet sln <solution> add <test-project.csproj>` and re-run the check. Do **not** report success until the harness command sees your new tests.

## Test Framework Detection

Detect the framework from the test project's `.csproj` package references and match its conventions:

| Package Reference | Framework | Attributes | Assertion Style |
|-------------------|-----------|------------|-----------------|
| `MSTest.Sdk` or `MSTest.TestFramework` | MSTest | `[TestClass]`, `[TestMethod]`, `[DataRow]` | `Assert.AreEqual(expected, actual)` |
| `xunit` | xUnit | `[Fact]`, `[Theory]`, `[InlineData]` | `Assert.Equal(expected, actual)` |
| `NUnit` | NUnit | `[TestFixture]`, `[Test]`, `[TestCase]` | `Assert.That(actual, Is.EqualTo(expected))` |

Use the repo's existing framework — do not introduce a different one.

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

**Exception**: if the user or evaluation harness explicitly requires a Cobertura/XML coverage artifact (e.g., they ask for `coverlet.collector` or a `--collect:"XPlat Code Coverage"` run), add the `coverlet.collector` PackageReference to the generated .NET test csproj so the harness's coverage command can produce output. Do not run the coverage command yourself; leave that to the validation step.
