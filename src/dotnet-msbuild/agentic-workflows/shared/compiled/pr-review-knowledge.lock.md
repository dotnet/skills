<!-- AUTO-GENERATED — DO NOT EDIT. Regenerate with: node src/dotnet-msbuild/build.js -->

---
name: msbuild-antipatterns
description: "Catalog of MSBuild anti-patterns with detection rules and fix recipes. Only activate in MSBuild/.NET build context. Use when reviewing, auditing, or cleaning up .csproj, .vbproj, .fsproj, .props, .targets, or .proj files. Each anti-pattern has a symptom, explanation, and concrete BAD→GOOD transformation. DO NOT use for non-MSBuild build systems (npm, Maven, CMake, etc.)."
---

# MSBuild Anti-Pattern Catalog

A numbered catalog of common MSBuild anti-patterns. Each entry follows the format:

- **Smell**: What to look for
- **Why it's bad**: Impact on builds, maintainability, or correctness
- **Fix**: Concrete transformation

Use this catalog when scanning project files for improvements.

---

## AP-01: `<Exec>` for Operations That Have Built-in Tasks

**Smell**: `<Exec Command="mkdir ..." />`, `<Exec Command="copy ..." />`, `<Exec Command="del ..." />`

**Why it's bad**: Built-in tasks are cross-platform, support incremental build, emit structured logging, and handle errors consistently. `<Exec>` is opaque to MSBuild.

```xml
<!-- BAD -->
<Target Name="PrepareOutput">
  <Exec Command="mkdir $(OutputPath)logs" />
  <Exec Command="copy config.json $(OutputPath)" />
  <Exec Command="del $(IntermediateOutputPath)*.tmp" />
</Target>

<!-- GOOD -->
<Target Name="PrepareOutput">
  <MakeDir Directories="$(OutputPath)logs" />
  <Copy SourceFiles="config.json" DestinationFolder="$(OutputPath)" />
  <Delete Files="@(TempFiles)" />
</Target>
```

**Built-in task alternatives:**

| Shell Command | MSBuild Task |
|--------------|--------------|
| `mkdir` | `<MakeDir>` |
| `copy` / `cp` | `<Copy>` |
| `del` / `rm` | `<Delete>` |
| `move` / `mv` | `<Move>` |
| `echo text > file` | `<WriteLinesToFile>` |
| `touch` | `<Touch>` |
| `xcopy /s` | `<Copy>` with item globs |

---

## AP-02: Unquoted Condition Expressions

**Smell**: `Condition="$(Foo) == Bar"` — either side of a comparison is unquoted.

**Why it's bad**: If the property is empty or contains spaces/special characters, the condition evaluates incorrectly or throws a parse error. MSBuild requires single-quoted strings for reliable comparisons.

```xml
<!-- BAD -->
<PropertyGroup Condition="$(Configuration) == Release">
  <Optimize>true</Optimize>
</PropertyGroup>

<!-- GOOD -->
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <Optimize>true</Optimize>
</PropertyGroup>
```

**Rule**: Always quote **both** sides of `==` and `!=` comparisons with single quotes.

---

## AP-03: Hardcoded Absolute Paths

**Smell**: Paths like `C:\tools\`, `D:\packages\`, `/usr/local/bin/` in project files.

**Why it's bad**: Breaks on other machines, CI environments, and other operating systems. Not relocatable.

```xml
<!-- BAD -->
<PropertyGroup>
  <ToolPath>C:\tools\mytool\mytool.exe</ToolPath>
</PropertyGroup>
<Import Project="C:\repos\shared\common.props" />

<!-- GOOD -->
<PropertyGroup>
  <ToolPath>$(MSBuildThisFileDirectory)tools\mytool\mytool.exe</ToolPath>
</PropertyGroup>
<Import Project="$(RepoRoot)eng\common.props" />
```

**Preferred path properties:**

| Property | Meaning |
|----------|---------|
| `$(MSBuildThisFileDirectory)` | Directory of the current .props/.targets file |
| `$(MSBuildProjectDirectory)` | Directory of the .csproj |
| `$([MSBuild]::GetDirectoryNameOfFileAbove(...))` | Walk up to find a marker file |
| `$([MSBuild]::NormalizePath(...))` | Combine and normalize path segments |

---

## AP-04: Restating SDK Defaults

**Smell**: Properties set to values that the .NET SDK already provides by default.

**Why it's bad**: Adds noise, hides intentional overrides, and makes it harder to identify what's actually customized. When defaults change in newer SDKs, the redundant properties may silently pin old behavior.

```xml
<!-- BAD: All of these are already the default -->
<PropertyGroup>
  <OutputType>Library</OutputType>
  <EnableDefaultItems>true</EnableDefaultItems>
  <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
  <RootNamespace>MyLib</RootNamespace>       <!-- matches project name -->
  <AssemblyName>MyLib</AssemblyName>         <!-- matches project name -->
  <AppendTargetFrameworkToOutputPath>true</AppendTargetFrameworkToOutputPath>
</PropertyGroup>

<!-- GOOD: Only non-default values -->
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
</PropertyGroup>
```

---

## AP-05: Manual File Listing in SDK-Style Projects

**Smell**: `<Compile Include="File1.cs" />`, `<Compile Include="File2.cs" />` in SDK-style projects.

**Why it's bad**: SDK-style projects automatically glob `**/*.cs` (and other file types). Explicit listing is redundant, creates merge conflicts, and new files may be accidentally missed if not added to the list.

```xml
<!-- BAD -->
<ItemGroup>
  <Compile Include="Program.cs" />
  <Compile Include="Services\MyService.cs" />
  <Compile Include="Models\User.cs" />
</ItemGroup>

<!-- GOOD: Remove entirely — SDK includes all .cs files by default.
     Only use Remove/Exclude when you need to opt out: -->
<ItemGroup>
  <Compile Remove="LegacyCode\**" />
</ItemGroup>
```

**Exception**: Non-SDK-style (legacy) projects require explicit file includes. If migrating, see `msbuild-modernization` skill.

---

## AP-06: Using `<Reference>` with HintPath for NuGet Packages

**Smell**: `<Reference Include="..." HintPath="..\packages\SomePackage\lib\..." />`

**Why it's bad**: This is the legacy `packages.config` pattern. It doesn't support transitive dependencies, version conflict resolution, or automatic restore. The `packages/` folder must be committed or restored separately.

```xml
<!-- BAD -->
<ItemGroup>
  <Reference Include="Newtonsoft.Json">
    <HintPath>..\packages\Newtonsoft.Json.13.0.3\lib\netstandard2.0\Newtonsoft.Json.dll</HintPath>
  </Reference>
</ItemGroup>

<!-- GOOD -->
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
</ItemGroup>
```

**Note**: `<Reference>` without HintPath is still valid for .NET Framework GAC assemblies like `WindowsBase`, `PresentationCore`, etc.

---

## AP-07: Missing `PrivateAssets="all"` on Analyzer/Tool Packages

**Smell**: `<PackageReference Include="StyleCop.Analyzers" Version="..." />` without `PrivateAssets="all"`.

**Why it's bad**: Without `PrivateAssets="all"`, analyzer and build-tool packages flow as transitive dependencies to consumers of your library. Consumers get unwanted analyzers or build-time tools they didn't ask for.

See [`references/private-assets.md`](references/private-assets.md) for BAD/GOOD examples and the full list of packages that need this.

---

## AP-08: Copy-Pasted Properties Across Multiple .csproj Files

**Smell**: The same `<PropertyGroup>` block appears in 3+ project files.

**Why it's bad**: Maintenance burden — a change must be made in every file. Inconsistencies creep in over time.

```xml
<!-- BAD: Repeated in every .csproj -->
<!-- ProjectA.csproj, ProjectB.csproj, ProjectC.csproj all have: -->
<PropertyGroup>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>

<!-- GOOD: Define once in Directory.Build.props at the repo/src root -->
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

See `directory-build-organization` skill for full guidance on structuring `Directory.Build.props` / `Directory.Build.targets`.

---

## AP-09: Scattered Package Versions Without Central Package Management

**Smell**: `<PackageReference Include="X" Version="1.2.3" />` with different versions of the same package across projects.

**Why it's bad**: Version drift — different projects use different versions of the same package, leading to runtime mismatches, unexpected behavior, or diamond dependency conflicts.

```xml
<!-- BAD: Version specified in each project, can drift -->
<!-- ProjectA.csproj -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
<!-- ProjectB.csproj -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

**Fix:** Use Central Package Management. See [https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) for details.

---

## AP-10: Monolithic Targets (Too Much in One Target)

**Smell**: A single `<Target>` with 50+ lines doing multiple unrelated things.

**Why it's bad**: Can't skip individual steps via incremental build, hard to debug, hard to extend, and the target name becomes meaningless.

```xml
<!-- BAD -->
<Target Name="PrepareRelease" BeforeTargets="Build">
  <WriteLinesToFile File="version.txt" Lines="$(Version)" Overwrite="true" />
  <Copy SourceFiles="LICENSE" DestinationFolder="$(OutputPath)" />
  <Exec Command="signtool sign /f cert.pfx $(OutputPath)*.dll" />
  <MakeDir Directories="$(OutputPath)docs" />
  <Copy SourceFiles="@(DocFiles)" DestinationFolder="$(OutputPath)docs" />
  <!-- ... 30 more lines ... -->
</Target>

<!-- GOOD: Single-responsibility targets -->
<Target Name="WriteVersionFile" BeforeTargets="CoreCompile"
        Inputs="$(MSBuildProjectFile)" Outputs="$(IntermediateOutputPath)version.txt">
  <WriteLinesToFile File="$(IntermediateOutputPath)version.txt" Lines="$(Version)" Overwrite="true" />
</Target>

<Target Name="CopyLicense" AfterTargets="Build">
  <Copy SourceFiles="LICENSE" DestinationFolder="$(OutputPath)" SkipUnchangedFiles="true" />
</Target>

<Target Name="SignAssemblies" AfterTargets="Build" DependsOnTargets="CopyLicense"
        Condition="'$(SignAssemblies)' == 'true'">
  <Exec Command="signtool sign /f cert.pfx %(AssemblyFiles.Identity)" />
</Target>
```

---

## AP-11: Custom Targets Missing `Inputs` and `Outputs`

**Smell**: `<Target Name="MyTarget" BeforeTargets="Build">` with no `Inputs` / `Outputs` attributes.

**Why it's bad**: The target runs on every build, even when nothing changed. This defeats incremental build and slows down no-op builds.

See [`references/incremental-build-inputs-outputs.md`](references/incremental-build-inputs-outputs.md) for BAD/GOOD examples and the full pattern including FileWrites registration.

See `incremental-build` skill for deep guidance on Inputs/Outputs, FileWrites, and up-to-date checks.

---

## AP-12: Setting Defaults in .targets Instead of .props

**Smell**: `<PropertyGroup>` with default values inside a `.targets` file.

**Why it's bad**: `.targets` files are imported late (after project files). By the time they set defaults, other `.targets` files may have already used the empty/undefined value. `.props` files are imported early and are the correct place for defaults.

```xml
<!-- BAD: custom.targets -->
<PropertyGroup>
  <MyToolVersion>2.0</MyToolVersion>
</PropertyGroup>
<Target Name="RunMyTool">
  <Exec Command="mytool --version $(MyToolVersion)" />
</Target>

<!-- GOOD: Split into .props (defaults) + .targets (logic) -->
<!-- custom.props (imported early) -->
<PropertyGroup>
  <MyToolVersion Condition="'$(MyToolVersion)' == ''">2.0</MyToolVersion>
</PropertyGroup>

<!-- custom.targets (imported late) -->
<Target Name="RunMyTool">
  <Exec Command="mytool --version $(MyToolVersion)" />
</Target>
```

**Rule**: `.props` = defaults and settings (evaluated early). `.targets` = build logic and targets (evaluated late).

---

## AP-13: Import Without `Exists()` Guard

**Smell**: `<Import Project="some-file.props" />` without a `Condition="Exists('...')"` check.

**Why it's bad**: If the file doesn't exist (not yet created, wrong path, deleted), the build fails with a confusing error. Optional imports should always be guarded.

```xml
<!-- BAD -->
<Import Project="$(RepoRoot)eng\custom.props" />

<!-- GOOD: Guard optional imports -->
<Import Project="$(RepoRoot)eng\custom.props" Condition="Exists('$(RepoRoot)eng\custom.props')" />

<!-- ALSO GOOD: Sdk attribute imports don't need guards (they're required by design) -->
<Project Sdk="Microsoft.NET.Sdk">
```

**Exception**: Imports that are *required* for the build to work correctly should fail fast — don't guard those. Guard imports that are optional or environment-specific (e.g., local developer overrides, CI-specific settings).

---

## AP-14: Using Backslashes in Paths (Cross-Platform Issue)

**Smell**: `<Import Project="$(RepoRoot)\eng\common.props" />` with backslash separators in `.props`/`.targets` files meant to be cross-platform.

**Why it's bad**: Backslashes work on Windows but fail on Linux/macOS. MSBuild normalizes forward slashes on all platforms.

```xml
<!-- BAD: Breaks on Linux/macOS -->
<Import Project="$(RepoRoot)\eng\common.props" />
<Content Include="assets\images\**" />

<!-- GOOD: Forward slashes work everywhere -->
<Import Project="$(RepoRoot)/eng/common.props" />
<Content Include="assets/images/**" />
```

**Note**: `$(MSBuildThisFileDirectory)` already ends with a platform-appropriate separator, so `$(MSBuildThisFileDirectory)tools/mytool` works on both platforms.

---

## AP-15: Unconditional Property Override in Multiple Scopes

**Smell**: A property set unconditionally in both `Directory.Build.props` and a `.csproj` — last write wins silently.

**Why it's bad**: Hard to trace which value is actually used. Makes the build fragile and confusing for anyone reading the project files.

```xml
<!-- BAD: Directory.Build.props sets it, csproj silently overrides -->
<!-- Directory.Build.props -->
<PropertyGroup>
  <OutputPath>bin\custom\</OutputPath>
</PropertyGroup>
<!-- MyProject.csproj -->
<PropertyGroup>
  <OutputPath>bin\other\</OutputPath>
</PropertyGroup>

<!-- GOOD: Use a condition so overrides are intentional -->
<!-- Directory.Build.props -->
<PropertyGroup>
  <OutputPath Condition="'$(OutputPath)' == ''">bin\custom\</OutputPath>
</PropertyGroup>
<!-- MyProject.csproj can now intentionally override or leave the default -->
```

---

## AP-16: Using `<Exec>` for String/Path Operations

**Smell**: `<Exec Command="echo $(Var) | sed ..." />` or `<Exec Command="powershell -c ..." />` for simple string manipulation.

**Why it's bad**: Shell-dependent, not cross-platform, slower than property functions, and the result is hard to capture back into MSBuild properties.

```xml
<!-- BAD -->
<Target Name="GetCleanVersion">
  <Exec Command="echo $(Version) | sed 's/-preview//'" ConsoleToMSBuildProperty="CleanVersion" />
</Target>

<!-- GOOD: Property function -->
<PropertyGroup>
  <CleanVersion>$(Version.Replace('-preview', ''))</CleanVersion>
  <HasPrerelease>$(Version.Contains('-'))</HasPrerelease>
  <LowerName>$(AssemblyName.ToLowerInvariant())</LowerName>
</PropertyGroup>

<!-- GOOD: Path operations -->
<PropertyGroup>
  <NormalizedOutput>$([MSBuild]::NormalizeDirectory($(OutputPath)))</NormalizedOutput>
  <ToolPath>$([System.IO.Path]::Combine($(MSBuildThisFileDirectory), 'tools', 'mytool.exe'))</ToolPath>
</PropertyGroup>
```

---

## AP-17: Mixing `Include` and `Update` for the Same Item Type in One ItemGroup

**Smell**: Same `<ItemGroup>` has both `<Compile Include="...">` and `<Compile Update="...">`.

**Why it's bad**: `Update` acts on items already in the set. If `Include` hasn't been processed yet (evaluation order), `Update` may not find the item. Separating them avoids subtle ordering bugs.

```xml
<!-- BAD -->
<ItemGroup>
  <Compile Include="Generated\Extra.cs" />
  <Compile Update="Generated\Extra.cs" CopyToOutputDirectory="Always" />
</ItemGroup>

<!-- GOOD -->
<ItemGroup>
  <Compile Include="Generated\Extra.cs" />
</ItemGroup>
<ItemGroup>
  <Compile Update="Generated\Extra.cs" CopyToOutputDirectory="Always" />
</ItemGroup>
```

---

## AP-18: Redundant `<ProjectReference>` to Transitively-Referenced Projects

**Smell**: A project references both `Core` and `Utils`, but `Core` already depends on `Utils`.

**Why it's bad**: Adds unnecessary coupling, makes the dependency graph harder to understand, and can cause ordering issues in large builds. MSBuild resolves transitive references automatically.

```xml
<!-- BAD -->
<ItemGroup>
  <ProjectReference Include="..\Core\Core.csproj" />
  <ProjectReference Include="..\Utils\Utils.csproj" />  <!-- Core already references Utils -->
</ItemGroup>

<!-- GOOD: Only direct dependencies -->
<ItemGroup>
  <ProjectReference Include="..\Core\Core.csproj" />
</ItemGroup>
```

**Caveat**: If you need to use types from `Utils` directly (not just transitively), the explicit reference is appropriate. But verify whether the direct dependency is actually needed.

---

## AP-19: Side Effects During Property Evaluation

**Smell**: Property functions that write files, make network calls, or modify state during `<PropertyGroup>` evaluation.

**Why it's bad**: Property evaluation happens during the evaluation phase, which can run multiple times (e.g., during design-time builds in Visual Studio). Side effects are unpredictable and can corrupt state.

```xml
<!-- BAD: File write during evaluation -->
<PropertyGroup>
  <Timestamp>$([System.IO.File]::WriteAllText('stamp.txt', 'built'))</Timestamp>
</PropertyGroup>

<!-- GOOD: Side effects belong in targets -->
<Target Name="WriteTimestamp" BeforeTargets="Build">
  <WriteLinesToFile File="stamp.txt" Lines="built" Overwrite="true" />
</Target>
```

---

## AP-20: Platform-Specific Exec Without OS Condition

**Smell**: `<Exec Command="chmod +x ..." />` or `<Exec Command="cmd /c ..." />` without an OS condition.

**Why it's bad**: Fails on the wrong platform. If the project is cross-platform, guard platform-specific commands.

```xml
<!-- BAD: Fails on Windows -->
<Target Name="MakeExecutable" AfterTargets="Build">
  <Exec Command="chmod +x $(OutputPath)mytool" />
</Target>

<!-- GOOD: OS-guarded -->
<Target Name="MakeExecutable" AfterTargets="Build"
        Condition="!$([MSBuild]::IsOSPlatform('Windows'))">
  <Exec Command="chmod +x $(OutputPath)mytool" />
</Target>
```

---

## AP-21: Property Conditioned on TargetFramework in .props Files

**Smell**: `<PropertyGroup Condition="'$(TargetFramework)' == '...'">` or `<Property Condition="'$(TargetFramework)' == '...'">` in `Directory.Build.props` or any `.props` file imported before the project body.

**Why it's bad**: `$(TargetFramework)` is NOT reliably available in `Directory.Build.props` or any `.props` file imported before the project body. It is only set that early for multi-targeting projects, which receive `TargetFramework` as a global property from the outer build. Single-targeting projects (using singular `<TargetFramework>`) set it in the project body, which is evaluated *after* `.props`. This means property conditions on `$(TargetFramework)` in `.props` files silently fail for single-targeting projects — the condition never matches because the property is empty. This applies to both `<PropertyGroup Condition="...">` and individual `<Property Condition="...">` elements.

For a detailed explanation of MSBuild's evaluation and execution phases, see [Build process overview](https://learn.microsoft.com/en-us/visualstudio/msbuild/build-process-overview).

```xml
<!-- BAD: In Directory.Build.props — TargetFramework may be empty here -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <DefineConstants>$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>

<!-- ALSO BAD: Condition on the property itself has the same problem -->
<PropertyGroup>
  <DefineConstants Condition="'$(TargetFramework)' == 'net8.0'">$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>

<!-- GOOD: In Directory.Build.targets — TargetFramework is always available -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <DefineConstants>$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>

<!-- ALSO GOOD: In the project file itself -->
<!-- MyProject.csproj -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <DefineConstants>$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>
```

**⚠️ Item and Target conditions are NOT affected.** This restriction applies ONLY to property conditions (`<PropertyGroup Condition="...">` and `<Property Condition="...">`). Item conditions (`<ItemGroup Condition="...">`) and Target conditions in `.props` files are SAFE because items and targets evaluate after all properties (including those set in the project body) have been evaluated. This includes `PackageVersion` items in `Directory.Packages.props`, `PackageReference` items in `Directory.Build.props`, and any other item types.

**Do NOT flag the following patterns — they are correct:**

```xml
<!-- OK in Directory.Build.props — ItemGroup conditions evaluate late -->
<ItemGroup Condition="'$(TargetFramework)' == 'net472'">
  <PackageReference Include="System.Memory" />
</ItemGroup>

<!-- OK in Directory.Packages.props — PackageVersion items evaluate late -->
<ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
</ItemGroup>
<ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
  <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
</ItemGroup>

<!-- OK — Individual item conditions also evaluate late -->
<ItemGroup>
  <PackageReference Include="System.Memory" Condition="'$(TargetFramework)' == 'net472'" />
</ItemGroup>
```

---

## Quick-Reference Checklist

When reviewing an MSBuild file, scan for these in order:

| # | Check | Severity |
|---|-------|----------|
| AP-02 | Unquoted conditions | 🔴 Error-prone |
| AP-19 | Side effects in evaluation | 🔴 Dangerous |
| AP-21 | Property conditioned on TargetFramework in .props | 🔴 Silent failure |
| AP-03 | Hardcoded absolute paths | 🔴 Broken on other machines |
| AP-06 | `<Reference>` with HintPath for NuGet | 🟡 Legacy |
| AP-07 | Missing `PrivateAssets="all"` on tools | 🟡 Leaks to consumers |
| AP-11 | Missing Inputs/Outputs on targets | 🟡 Perf regression |
| AP-13 | Import without Exists guard | 🟡 Fragile |
| AP-05 | Manual file listing in SDK-style | 🔵 Noise |
| AP-04 | Restating SDK defaults | 🔵 Noise |
| AP-08 | Copy-paste across csproj files | 🔵 Maintainability |
| AP-09 | Scattered package versions | 🔵 Version drift |
| AP-01 | `<Exec>` for built-in tasks | 🔵 Cross-platform |
| AP-14 | Backslashes in cross-platform paths | 🔵 Cross-platform |
| AP-10 | Monolithic targets | 🔵 Maintainability |
| AP-12 | Defaults in .targets instead of .props | 🔵 Ordering issue |
| AP-15 | Unconditional property override | 🔵 Confusing |
| AP-16 | `<Exec>` for string operations | 🔵 Preference |
| AP-17 | Mixed Include/Update in one ItemGroup | 🔵 Subtle bugs |
| AP-18 | Redundant transitive ProjectReferences | 🔵 Graph noise |
| AP-20 | Platform-specific Exec without guard | 🔵 Cross-platform |

---

## msbuild-modernization

---
name: msbuild-modernization
description: "Guide for modernizing and migrating MSBuild project files to SDK-style format. Only activate in MSBuild/.NET build context. Use when encountering legacy .csproj/.vbproj files with verbose XML, packages.config, or AssemblyInfo.cs patterns. Covers legacy-to-SDK migration, removing boilerplate, PackageReference migration, and Directory.Build consolidation. Invoke when asked to modernize, migrate, or clean up project files."
---

# MSBuild Modernization: Legacy to SDK-style Migration

## Identifying Legacy vs SDK-style Projects

**Legacy indicators:**

- `<Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />`
- Explicit file lists (`<Compile Include="..." />` for every `.cs` file)
- `ToolsVersion` attribute on `<Project>` element
- `packages.config` file present
- `Properties\AssemblyInfo.cs` with assembly-level attributes

**SDK-style indicators:**

- `<Project Sdk="Microsoft.NET.Sdk">` attribute on root element
- Minimal content — a simple project may be 10–15 lines
- No explicit file includes (implicit globbing)
- `<PackageReference>` items instead of `packages.config`

**Quick check:** if a `.csproj` is more than 50 lines for a simple class library or console app, it is likely legacy format.

```xml
<!-- Legacy: ~80+ lines for a simple library -->
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <OutputType>Library</OutputType>
    <RootNamespace>MyLibrary</RootNamespace>
    <AssemblyName>MyLibrary</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <!-- ... 60+ more lines ... -->
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

```xml
<!-- SDK-style: ~8 lines for the same library -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
  </PropertyGroup>
</Project>
```

## Migration Checklist: Legacy → SDK-style

### Step 1: Replace Project Root Element

**BEFORE:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props"
          Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <!-- ... project content ... -->
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

**AFTER:**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- ... project content ... -->
</Project>
```

Remove the XML declaration, `ToolsVersion`, `xmlns`, and both `<Import>` lines. The `Sdk` attribute replaces all of them.

### Step 2: Set TargetFramework

**BEFORE:**

```xml
<PropertyGroup>
  <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
</PropertyGroup>
```

**AFTER:**

```xml
<PropertyGroup>
  <TargetFramework>net472</TargetFramework>
</PropertyGroup>
```

**TFM mapping table:**

| Legacy `TargetFrameworkVersion` | SDK-style `TargetFramework` |
|---------------------------------|-----------------------------|
| `v4.6.1`                        | `net461`                    |
| `v4.7.2`                        | `net472`                    |
| `v4.8`                          | `net48`                     |
| (migrating to .NET 6)           | `net6.0`                    |
| (migrating to .NET 8)           | `net8.0`                    |

### Step 3: Remove Explicit File Includes

**BEFORE:**

```xml
<ItemGroup>
  <Compile Include="Controllers\HomeController.cs" />
  <Compile Include="Models\User.cs" />
  <Compile Include="Models\Order.cs" />
  <Compile Include="Services\AuthService.cs" />
  <Compile Include="Services\OrderService.cs" />
  <Compile Include="Properties\AssemblyInfo.cs" />
  <!-- ... 50+ more lines ... -->
</ItemGroup>
<ItemGroup>
  <Content Include="Views\Home\Index.cshtml" />
  <Content Include="Views\Shared\_Layout.cshtml" />
  <!-- ... more content files ... -->
</ItemGroup>
```

**AFTER:**

Delete all of these `<Compile>` and `<Content>` item groups entirely. SDK-style projects include them automatically via implicit globbing.

**Exception:** keep explicit entries only for files that need special metadata or reside outside the project directory:

```xml
<ItemGroup>
  <Content Include="..\shared\config.json" Link="config.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

### Step 4: Remove AssemblyInfo.cs

**BEFORE** (`Properties\AssemblyInfo.cs`):

```csharp
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("MyLibrary")]
[assembly: AssemblyDescription("A useful library")]
[assembly: AssemblyCompany("Contoso")]
[assembly: AssemblyProduct("MyLibrary")]
[assembly: AssemblyCopyright("Copyright © Contoso 2024")]
[assembly: ComVisible(false)]
[assembly: Guid("...")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
```

**AFTER** (in `.csproj`):

```xml
<PropertyGroup>
  <AssemblyTitle>MyLibrary</AssemblyTitle>
  <Description>A useful library</Description>
  <Company>Contoso</Company>
  <Product>MyLibrary</Product>
  <Copyright>Copyright © Contoso 2024</Copyright>
  <Version>1.2.0</Version>
</PropertyGroup>
```

Delete `Properties\AssemblyInfo.cs` — the SDK auto-generates assembly attributes from these properties.

**Alternative:** if you prefer to keep `AssemblyInfo.cs`, disable auto-generation:

```xml
<PropertyGroup>
  <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
</PropertyGroup>
```

### Step 5: Migrate packages.config → PackageReference

**BEFORE** (`packages.config`):

```xml
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net472" />
  <package id="Serilog" version="3.1.1" targetFramework="net472" />
  <package id="Microsoft.Extensions.DependencyInjection" version="8.0.0" targetFramework="net472" />
</packages>
```

**AFTER** (in `.csproj`):

```xml
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  <PackageReference Include="Serilog" Version="3.1.1" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
</ItemGroup>
```

Delete `packages.config` after migration.

**Migration options:**

- **Visual Studio:** right-click `packages.config` → *Migrate packages.config to PackageReference*
- **CLI:** `dotnet migrate-packages-config` or manual conversion
- **Binding redirects:** SDK-style projects auto-generate binding redirects — remove the `<runtime>` section from `app.config` if present

### Step 6: Remove Unnecessary Boilerplate

Delete all of the following — the SDK provides sensible defaults:

```xml
<!-- DELETE: SDK imports (replaced by Sdk attribute) -->
<Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" ... />
<Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />

<!-- DELETE: default Configuration/Platform (SDK provides these) -->
<PropertyGroup>
  <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
  <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
  <ProjectGuid>{...}</ProjectGuid>
  <OutputType>Library</OutputType>  <!-- keep only if not Library -->
  <AppDesignerFolder>Properties</AppDesignerFolder>
  <FileAlignment>512</FileAlignment>
  <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
  <Deterministic>true</Deterministic>
</PropertyGroup>

<!-- DELETE: standard Debug/Release configurations (SDK defaults match) -->
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
  <DebugSymbols>true</DebugSymbols>
  <DebugType>full</DebugType>
  <Optimize>false</Optimize>
  <OutputPath>bin\Debug\</OutputPath>
  <DefineConstants>DEBUG;TRACE</DefineConstants>
  <ErrorReport>prompt</ErrorReport>
  <WarningLevel>4</WarningLevel>
</PropertyGroup>
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
  <DebugType>pdbonly</DebugType>
  <Optimize>true</Optimize>
  <OutputPath>bin\Release\</OutputPath>
  <DefineConstants>TRACE</DefineConstants>
  <ErrorReport>prompt</ErrorReport>
  <WarningLevel>4</WarningLevel>
</PropertyGroup>

<!-- DELETE: framework assembly references (implicit in SDK) -->
<ItemGroup>
  <Reference Include="System" />
  <Reference Include="System.Core" />
  <Reference Include="System.Data" />
  <Reference Include="System.Xml" />
  <Reference Include="System.Xml.Linq" />
  <Reference Include="Microsoft.CSharp" />
</ItemGroup>

<!-- DELETE: packages.config reference -->
<None Include="packages.config" />

<!-- DELETE: designer service entries -->
<Service Include="{508349B6-6B84-11D3-8410-00C04F8EF8E0}" />
```

**Keep** only properties that differ from SDK defaults (e.g., `<OutputType>Exe</OutputType>`, `<RootNamespace>` if it differs from the assembly name, custom `<DefineConstants>`).

### Step 7: Enable Modern Features

After migration, consider enabling modern C# features:

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <LangVersion>latest</LangVersion>
</PropertyGroup>
```

- `<Nullable>enable</Nullable>` — enables nullable reference type analysis
- `<ImplicitUsings>enable</ImplicitUsings>` — auto-imports common namespaces (.NET 6+)
- `<LangVersion>latest</LangVersion>` — uses the latest C# language version (or specify e.g. `12.0`)

## Complete Before/After Example

**BEFORE** (legacy — 65 lines):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props"
          Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectGuid>{12345678-1234-1234-1234-123456789ABC}</ProjectGuid>
    <OutputType>Library</OutputType>
    <AppDesignerFolder>Properties</AppDesignerFolder>
    <RootNamespace>MyLibrary</RootNamespace>
    <AssemblyName>MyLibrary</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>bin\Debug\</OutputPath>
    <DefineConstants>DEBUG;TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>bin\Release\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System" />
    <Reference Include="System.Core" />
    <Reference Include="System.Xml.Linq" />
    <Reference Include="Microsoft.CSharp" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="Models\User.cs" />
    <Compile Include="Models\Order.cs" />
    <Compile Include="Services\UserService.cs" />
    <Compile Include="Services\OrderService.cs" />
    <Compile Include="Helpers\StringExtensions.cs" />
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>
  <ItemGroup>
    <None Include="packages.config" />
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

**AFTER** (SDK-style — 11 lines):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Serilog" Version="3.1.1" />
  </ItemGroup>
</Project>
```

## Common Migration Issues

**Embedded resources:** files not in a standard location may need explicit includes:

```xml
<ItemGroup>
  <EmbeddedResource Include="..\shared\Schemas\*.xsd" LinkBase="Schemas" />
</ItemGroup>
```

**Content files with CopyToOutputDirectory:** these still need explicit entries:

```xml
<ItemGroup>
  <Content Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  <None Include="scripts\*.sql" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

**Multi-targeting:** change the element name from singular to plural:

```xml
<!-- Single target -->
<TargetFramework>net8.0</TargetFramework>

<!-- Multiple targets -->
<TargetFrameworks>net472;net8.0</TargetFrameworks>
```

**WPF/WinForms projects:** use the appropriate SDK or properties:

```xml
<!-- Option A: WindowsDesktop SDK -->
<Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">

<!-- Option B: properties in standard SDK (preferred for .NET 5+) -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <UseWPF>true</UseWPF>
    <!-- or -->
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
```

**Test projects:** use the standard SDK with test framework packages:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.7.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
  </ItemGroup>
</Project>
```

## Central Package Management Migration

Centralizes NuGet version management across a multi-project solution. See [https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) for details.

**Step 1:** Create `Directory.Packages.props` at the repository root with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` and `<PackageVersion>` items for all packages.

**Step 2:** Remove `Version` from each project's `PackageReference`:

```xml
<!-- BEFORE -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />

<!-- AFTER -->
<PackageReference Include="Newtonsoft.Json" />
```

## Directory.Build Consolidation

Identify properties repeated across multiple `.csproj` files and move them to shared files.

**`Directory.Build.props`** (for properties — placed at repo or src root):

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Company>Contoso</Company>
    <Copyright>Copyright © Contoso 2024</Copyright>
  </PropertyGroup>
</Project>
```

**`Directory.Build.targets`** (for targets/tasks — placed at repo or src root):

```xml
<Project>
  <Target Name="PrintBuildInfo" AfterTargets="Build">
    <Message Importance="High" Text="Built $(AssemblyName) → $(TargetPath)" />
  </Target>
</Project>
```

**Keep in individual `.csproj` files** only what is project-specific:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>MyApp</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Serilog" />
    <ProjectReferen

[truncated]