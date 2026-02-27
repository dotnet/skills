---
name: directory-build-organization
description: "Guide for organizing MSBuild infrastructure with Directory.Build.props, Directory.Build.targets, Directory.Packages.props, and Directory.Build.rsp. Only activate in MSBuild/.NET build context. Use when structuring multi-project repos, centralizing build settings, or implementing central package management. Invoke when asked about Directory.Build files, centralizing project properties, or organizing build infrastructure."
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

See the AP-21 section in the [msbuild-antipatterns skill](../msbuild-antipatterns/SKILL.md) for the full explanation.

## Directory.Build.props

Good candidates: language settings, assembly/package metadata, build warnings, code analysis, common analyzers.

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Company>Contoso</Company>
    <Authors>Contoso Engineering</Authors>
  </PropertyGroup>
</Project>
```

**Do NOT put here:** project-specific TFMs, project-specific PackageReferences, targets/build logic, or properties depending on SDK-defined values (not available during `.props` evaluation).

## Directory.Build.targets

Good candidates: custom build targets, late-bound property overrides (values depending on SDK properties), post-build validation.

```xml
<Project>
  <Target Name="ValidateProjectSettings" BeforeTargets="Build">
    <Error Text="All libraries must target netstandard2.0 or higher"
           Condition="'$(OutputType)' == 'Library' AND '$(TargetFramework)' == 'net472'" />
  </Target>

  <PropertyGroup>
    <!-- DocumentationFile depends on OutputPath, which is set by the SDK -->
    <DocumentationFile Condition="'$(IsPackable)' == 'true'">$(OutputPath)$(AssemblyName).xml</DocumentationFile>
  </PropertyGroup>
</Project>
```

## Directory.Packages.props (Central Package Management)

Central Package Management (CPM) provides a single source of truth for all NuGet package versions. See [https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) for details.

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

MSBuild only auto-imports the **first** `Directory.Build.props` (or `.targets`) it finds walking up from the project directory. To chain multiple levels, explicitly import the parent at the **top** of the inner file. See [multi-level-examples](references/multi-level-examples.md) for full file examples.

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
  Directory.Build.props          ← repo-wide (lang version, company info, analyzers)
  Directory.Build.targets        ← repo-wide targets
  Directory.Packages.props       ← central package versions
  src/
    Directory.Build.props        ← src-specific (imports repo-level, sets IsPackable=true)
  test/
    Directory.Build.props        ← test-specific (imports repo-level, sets IsPackable=false, adds test packages)
```

## Artifact Output Layout (.NET 8+)

Set `<ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>` in `Directory.Build.props` to automatically produce project-name-separated `bin/`, `obj/`, and `publish/` directories under a single `artifacts/` folder, avoiding bin/obj clashes by default. See [common-patterns](references/common-patterns.md) for the directory layout and additional patterns (conditional settings by project type, post-pack validation).

## Workflow: Organizing Build Infrastructure

1. **Audit all `.csproj` files** — Catalog every `<PropertyGroup>`, `<ItemGroup>`, and custom `<Target>` across the solution. Note which settings repeat and which are project-specific.
2. **Create root `Directory.Build.props`** — Move shared property defaults (LangVersion, Nullable, TreatWarningsAsErrors, metadata) here. These are imported before the project file so projects can override them.
3. **Create root `Directory.Build.targets`** — Move custom build targets, post-build validation, and any properties that depend on SDK-defined values (e.g., `OutputPath`, `TargetFramework` for single-targeting projects) here. These are imported after the SDK so all properties are available.
4. **Create `Directory.Packages.props`** — Enable Central Package Management (`ManagePackageVersionsCentrally`), list all `PackageVersion` entries, and remove `Version=` from `PackageReference` items in `.csproj` files.
5. **Set up multi-level hierarchy** — Create inner `Directory.Build.props` files for `src/` and `test/` folders with distinct settings. Use `GetPathOfFileAbove` to chain to the parent.
6. **Simplify `.csproj` files** — Remove all centralized properties, version attributes, and duplicated targets. Each project should only contain what is unique to it.
7. **Validate** — Run `dotnet restore && dotnet build` and verify no regressions. Use `dotnet msbuild -pp:output.xml` to inspect the final merged view if needed.

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
