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

## Coverage XML Parsing

If `.testagent/initial_coverage.xml` exists, it uses Cobertura/VS format:

- `module` elements with `line_coverage` attribute — identifies which assemblies have low coverage
- `function` elements with `line_coverage="0.00"` — identifies completely untested methods
- `range` elements with `covered="no"` — identifies specific uncovered lines

## Skip Coverage Tools

Do not configure or run code coverage measurement tools (coverlet, dotnet-coverage, XPlat Code Coverage). These tools have inconsistent cross-configuration behavior and waste significant time. Coverage is measured separately by the evaluation harness.
