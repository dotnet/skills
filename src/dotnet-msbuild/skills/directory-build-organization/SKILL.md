---
name: directory-build-organization
description: "Guide for organizing MSBuild infrastructure with Directory.Build.props, Directory.Build.targets, Directory.Packages.props, and Directory.Build.rsp. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when structuring multi-project repos, centralizing build settings, or implementing central package management. Invoke when asked about Directory.Build files, centralizing project properties, or organizing build infrastructure."
---

# Organizing Build Infrastructure with Directory.Build Files

## Directory.Build.props vs Directory.Build.targets

Understanding which file to use is critical. They differ in **when** they are imported during evaluation:

**Evaluation order:**

```
Directory.Build.props → SDK .props → YourProject.csproj → SDK .targets → Directory.Build.targets
```

| Use `.props` for | Use `.targets` for |
|---|---|
| Setting property defaults | Custom build targets |
| Common item definitions | Late-bound property overrides |
| Properties projects can override | Post-build steps |
| Assembly/package metadata | Conditional logic on final values |
| Analyzer PackageReferences | Targets that depend on SDK-defined properties |

**Rule of thumb:** Properties and items go in `.props`. Custom targets and late-bound logic go in `.targets`.

Because `.props` is imported before the project file, the project can override any value set there. Because `.targets` is imported after everything, it gets the final say—but projects cannot override `.targets` values.

### ⚠️ Critical: TargetFramework Availability in .props vs .targets

**Property conditions on `$(TargetFramework)` in `.props` files silently fail for single-targeting projects** — the property is empty during `.props` evaluation. Move TFM-conditional properties to `.targets` instead. ItemGroup and Target conditions are not affected.

See [`shared/targetframework-props-evaluation.md`](../shared/targetframework-props-evaluation.md) for the full explanation, BAD/GOOD examples, and the item/target exception.

## Directory.Build.props

### What to Put Here

**Output settings:**

```xml
<PropertyGroup>
  <!-- Use with caution — see bin/obj clash skill for risks -->
  <BaseOutputPath>$(MSBuildThisFileDirectory)artifacts\bin\</BaseOutputPath>
  <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)artifacts\obj\$(MSBuildProjectName)\</BaseIntermediateOutputPath>
</PropertyGroup>
```

**Language settings:**

```xml
<PropertyGroup>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
</PropertyGroup>
```

**Assembly and package metadata:**

```xml
<PropertyGroup>
  <Company>Contoso</Company>
  <Authors>Contoso Engineering</Authors>
  <Copyright>Copyright © Contoso $(CurrentYear)</Copyright>
  <Product>Contoso Platform</Product>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <RepositoryUrl>https://github.com/contoso/platform</RepositoryUrl>
  <PackageProjectUrl>https://github.com/contoso/platform</PackageProjectUrl>
</PropertyGroup>
```

**Build behavior and warnings:**

```xml
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <WarningsAsErrors />
  <NoWarn>$(NoWarn);CS1591</NoWarn>
</PropertyGroup>
```

**Code analysis:**

```xml
<PropertyGroup>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <EnableNETAnalyzers>true</EnableNETAnalyzers>
</PropertyGroup>
```

**Common analyzer PackageReferences (apply to all projects):**

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.3.4">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

### What NOT to Put Here

- **Project-specific TFMs** — each project should declare its own `<TargetFramework>` or `<TargetFrameworks>`
- **Project-specific PackageReferences** — unless truly universal (e.g., analyzers for all projects)
- **Targets or complex build logic** — use `Directory.Build.targets` instead
- **Properties that depend on SDK-defined values** — those won't be available yet during `.props` evaluation

## Directory.Build.targets

### What to Put Here

**Custom build targets:**

```xml
<Target Name="ValidateProjectSettings" BeforeTargets="Build">
  <Error Text="All libraries must target netstandard2.0 or higher"
         Condition="'$(OutputType)' == 'Library' AND '$(TargetFramework)' == 'net472'" />
</Target>
```

**Conditional targets based on project type:**

```xml
<Target Name="GenerateBuildInfo" BeforeTargets="CoreCompile"
        Condition="'$(GenerateBuildInfo)' == 'true'">
  <WriteLinesToFile File="$(IntermediateOutputPath)BuildInfo.g.cs"
                    Lines="[assembly: System.Reflection.AssemblyMetadata(&quot;BuildDate&quot;, &quot;$(Today)&quot;)]"
                    Overwrite="true" />
  <ItemGroup>
    <Compile Include="$(IntermediateOutputPath)BuildInfo.g.cs" />
  </ItemGroup>
</Target>
```

**Late-bound property overrides (values that depend on SDK properties):**

```xml
<PropertyGroup>
  <!-- DocumentationFile depends on OutputPath, which is set by the SDK -->
  <DocumentationFile Condition="'$(IsPackable)' == 'true'">$(OutputPath)$(AssemblyName).xml</DocumentationFile>
</PropertyGroup>
```

**Post-build validation:**

```xml
<Target Name="ValidatePackageOutput" AfterTargets="Pack"
        Condition="'$(IsPackable)' == 'true'">
  <Error Text="Package was not created at $(PackageOutputPath)$(PackageId).$(PackageVersion).nupkg"
         Condition="!Exists('$(PackageOutputPath)$(PackageId).$(PackageVersion).nupkg')" />
</Target>
```

## Directory.Packages.props (Central Package Management)

Central Package Management (CPM) provides a single source of truth for all NuGet package versions. See [`shared/central-package-management.md`](../shared/central-package-management.md) for the full setup pattern, including `GlobalPackageReference` and `VersionOverride`.

**Enable CPM in `Directory.Packages.props` at the repo root:**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="8.0.0" />
    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageVersion Include="xunit" Version="2.9.0" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <!-- GlobalPackageReference applies to ALL projects — great for analyzers -->
    <GlobalPackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" />
    <GlobalPackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.0.0" />
  </ItemGroup>
</Project>
```

## Directory.Build.rsp

Contains default MSBuild CLI arguments applied to all builds under the directory tree.

**Example `Directory.Build.rsp`:**

```
/maxcpucount
/nodeReuse:false
/consoleLoggerParameters:Summary;ForceNoAlign
/warnAsMessage:MSB3277
```

- Works with both `msbuild` and `dotnet` CLI in modern .NET versions
- Great for enforcing consistent CI and local build flags
- Each argument goes on its own line

## Multi-level Directory.Build Files

MSBuild only auto-imports the **first** `Directory.Build.props` (or `.targets`) it finds walking up from the project directory. To chain multiple levels, you must explicitly import the parent.

**Add this at the TOP of inner `Directory.Build.props` files:**

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))"
         Condition="Exists('$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))')" />

  <!-- Inner-level overrides go here -->
</Project>
```

**Example layout:**

```
repo/
  Directory.Build.props          ← repo-wide settings (lang version, company info, analyzers)
  Directory.Build.targets        ← repo-wide targets
  Directory.Packages.props       ← central package versions
  src/
    Directory.Build.props        ← src-specific (imports repo-level, sets IsPackable=true)
    MyLib/
      MyLib.csproj
    MyApp/
      MyApp.csproj
  test/
    Directory.Build.props        ← test-specific (imports repo-level, sets IsPackable=false)
    MyLib.Tests/
      MyLib.Tests.csproj
```

**Repo-level `Directory.Build.props`:**

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

**`src/Directory.Build.props`:**

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))"
         Condition="Exists('$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))')" />

  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

**`test/Directory.Build.props`:**

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))"
         Condition="Exists('$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))')" />

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NSubstitute" />
  </ItemGroup>
</Project>
```

## Common Patterns

### Pattern: Shared Analyzers via GlobalPackageReference

In `Directory.Packages.props`:

```xml
<ItemGroup>
  <GlobalPackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" />
  <GlobalPackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.3.4" />
</ItemGroup>
```

This ensures every project in the repo gets these analyzers without any per-project configuration.

### Pattern: Conditional Settings by Project Type

In `Directory.Build.props`:

```xml
<!-- Detect test projects by naming convention -->
<PropertyGroup Condition="$(MSBuildProjectName.EndsWith('.Tests')) OR $(MSBuildProjectName.EndsWith('.UnitTests'))">
  <IsPackable>false</IsPackable>
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

In `Directory.Build.targets`:

```xml
<!-- Detect project output type after SDK has set defaults -->
<PropertyGroup Condition="'$(OutputType)' == 'Exe'">
  <SelfContained>false</SelfContained>
</PropertyGroup>

<PropertyGroup Condition="'$(OutputType)' == 'Library' AND '$(IsTestProject)' != 'true'">
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

### Pattern: Before/After Repository Cleanup

**Before — duplicated settings in every .csproj:**

```xml
<!-- src/LibA/LibA.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Company>Contoso</Company>
    <Authors>Contoso Engineering</Authors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>

<!-- src/LibB/LibB.csproj — same boilerplate repeated -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Company>Contoso</Company>
    <Authors>Contoso Engineering</Authors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
  </ItemGroup>
</Project>
```

**After — centralized with Directory.Build files:**

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Company>Contoso</Company>
    <Authors>Contoso Engineering</Authors>
  </PropertyGroup>
</Project>

<!-- Directory.Packages.props -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <GlobalPackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" />
  </ItemGroup>
</Project>

<!-- src/LibA/LibA.csproj — clean and minimal -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" />
  </ItemGroup>
</Project>

<!-- src/LibB/LibB.csproj — clean and minimal -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>
</Project>
```

### Pattern: Artifact Output Layout (.NET 8+)

In `Directory.Build.props`:

```xml
<PropertyGroup>
  <ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
</PropertyGroup>
```

This produces a structured output layout:

```
artifacts/
  bin/
    MyLib/
      debug/
      release/
    MyApp/
      debug/
      release/
  obj/
    MyLib/
    MyApp/
  publish/
    MyApp/
```

The `ArtifactsPath` property (.NET 8+) automatically sets `BaseOutputPath`, `BaseIntermediateOutputPath`, and `PackageOutputPath` with project-name-separated directories, avoiding bin/obj clashes by default.

## Troubleshooting

| Problem | Cause | Fix |
|---|---|---|
| `Directory.Build.props` isn't picked up | File name casing wrong (exact match required on Linux/macOS) | Verify exact casing: `Directory.Build.props` (capital D, B) |
| Properties from `.props` are ignored by projects | Project sets the same property after the import | Move the property to `Directory.Build.targets` to set it after the project |
| Multi-level import doesn't work | Missing `GetPathOfFileAbove` import in inner file | Add the `<Import>` element at the top of the inner file (see Multi-level section) |
| Properties using SDK values are empty in `.props` | SDK properties aren't defined yet during `.props` evaluation | Move to `.targets` which is imported after the SDK |
| `Directory.Packages.props` not found | File not at repo root or not named exactly | Must be named `Directory.Packages.props` and at or above the project directory |
| Property condition on `$(TargetFramework)` doesn't match in `.props` | `TargetFramework` isn't set yet for single-targeting projects during `.props` evaluation | Move property to `.targets`, or use ItemGroup/Target conditions instead (which evaluate late) |

**Diagnosis:** Use the preprocessed project output to see all imports and final property values:

```bash
dotnet msbuild -pp:output.xml MyProject.csproj
```

This expands all imports inline so you can see exactly where each property is set and what the final evaluated value is.
