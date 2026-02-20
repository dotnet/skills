<!-- AUTO-GENERATED — DO NOT EDIT. Regenerate with: node src/dotnet-msbuild/build.js -->

---
name: msbuild-antipatterns
description: "Catalog of MSBuild anti-patterns with detection rules and fix recipes. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when reviewing, auditing, or cleaning up .csproj, .vbproj, .fsproj, .props, .targets, or .proj files. Each anti-pattern has a symptom, explanation, and concrete BAD→GOOD transformation. Complements the msbuild-style-guide skill (which teaches how to write good MSBuild) with a smell-detection approach. DO NOT use for non-MSBuild build systems (npm, Maven, CMake, etc.)."
---

# MSBuild Anti-Pattern Catalog

A numbered catalog of common MSBuild anti-patterns. Each entry follows the format:

- **Smell**: What to look for
- **Why it's bad**: Impact on builds, maintainability, or correctness
- **Fix**: Concrete transformation

Use this catalog when scanning project files for improvements. Cross-reference with `msbuild-style-guide` for the positive guidance.

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

See [`shared/private-assets.md`](../shared/private-assets.md) for BAD/GOOD examples and the full list of packages that need this.

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

**Fix:** Use Central Package Management. See [`shared/central-package-management.md`](../shared/central-package-management.md) for the setup pattern.

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

See [`shared/incremental-build-inputs-outputs.md`](../shared/incremental-build-inputs-outputs.md) for BAD/GOOD examples and the full pattern including FileWrites registration.

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

**Why it's bad**: `$(TargetFramework)` is only available during `.props` evaluation for multi-targeting projects. For single-targeting projects, the condition silently fails. This applies to both `<PropertyGroup Condition="...">` and individual `<Property Condition="...">` elements.

**⚠️ Item and Target conditions are NOT affected.** `<ItemGroup Condition="'$(TargetFramework)' == '...'">` and individual item conditions in `.props` files are safe — do NOT flag them. This includes `PackageVersion` items in `Directory.Packages.props`.

See [`shared/targetframework-props-evaluation.md`](../shared/targetframework-props-evaluation.md) for the full explanation, BAD/GOOD examples, and the item/target exception.

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

## msbuild-style-guide

---
name: msbuild-style-guide
description: "MSBuild best practices and style guide for writing clean, idiomatic project files. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when reviewing, creating, or refactoring .csproj, .vbproj, .fsproj, .props, .targets, or other MSBuild files. Covers property naming, conditions, target ordering, property functions, and modern SDK-style patterns. Invoke when asked to review, clean up, or improve MSBuild project files."
---

# MSBuild Style Guide & Best Practices

A reference for writing clean, idiomatic MSBuild project files. Every section shows concrete **BAD → GOOD** transformations.

---

## SDK-style Project Fundamentals

Always use SDK-style projects for new code.

### Minimal viable .csproj

```xml
<!-- GOOD: Minimal SDK-style project — this is all you need -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

```xml
<!-- BAD: Legacy verbose project file -->
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <AssemblyName>MyLib</AssemblyName>
    <RootNamespace>MyLib</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Class1.cs" />
    <Compile Include="Class2.cs" />
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

### Implicit defaults

SDK-style projects provide sensible defaults. Don't restate them unless you need to override:

| Default                  | SDK Behavior                    | Override when…                          |
|--------------------------|---------------------------------|-----------------------------------------|
| `DefaultItemExcludes`    | Globs include `**/*.cs` etc.    | You need to exclude specific patterns   |
| `ImplicitUsings`         | `enable` in .NET 6+             | You need to disable or customize usings |
| `Nullable`               | Not enabled by default          | You want nullable reference types       |
| `OutputType`             | `Library`                       | You're building an exe or test project  |

```xml
<!-- GOOD: Only specify what differs from defaults -->
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <Nullable>enable</Nullable>
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

```xml
<!-- BAD: Restating defaults that SDK already provides -->
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <Nullable>enable</Nullable>
  <OutputType>Library</OutputType>          <!-- default, remove -->
  <RootNamespace>MyLib</RootNamespace>      <!-- matches assembly name, remove -->
  <AssemblyName>MyLib</AssemblyName>        <!-- matches project filename, remove -->
  <EnableDefaultItems>true</EnableDefaultItems> <!-- default, remove -->
</PropertyGroup>
```

---

## Property Organization

Group related properties logically. Use a consistent ordering convention.

**Recommended order:** output settings → language settings → package settings → build behavior.

```xml
<!-- GOOD: Organized with labeled groups -->
<PropertyGroup Label="Output">
  <TargetFramework>net8.0</TargetFramework>
  <OutputType>Exe</OutputType>
</PropertyGroup>

<PropertyGroup Label="Language">
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <LangVersion>latest</LangVersion>
</PropertyGroup>

<PropertyGroup Label="Package">
  <PackageId>MyCompany.MyLib</PackageId>
  <Version>1.2.0</Version>
  <Authors>My Company</Authors>
</PropertyGroup>

<PropertyGroup Label="Build">
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <WarningLevel>9999</WarningLevel>
</PropertyGroup>
```

```xml
<!-- BAD: Properties scattered with no logical grouping -->
<PropertyGroup>
  <Authors>My Company</Authors>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <TargetFramework>net8.0</TargetFramework>
  <PackageId>MyCompany.MyLib</PackageId>
  <Nullable>enable</Nullable>
  <OutputType>Exe</OutputType>
  <Version>1.2.0</Version>
  <LangVersion>latest</LangVersion>
  <WarningLevel>9999</WarningLevel>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

---

## Property Naming

- **PascalCase** for all custom properties.
- Use meaningful, descriptive names.
- Avoid abbreviations unless universally understood (`TFM`, `OS`, `CI`).
- Prefix custom properties to avoid conflicts with built-in MSBuild properties.

```xml
<!-- GOOD -->
<MyCompany_EnableTelemetry>true</MyCompany_EnableTelemetry>
<MyProject_GeneratedCodeOutputPath>$(IntermediateOutputPath)Generated/</MyProject_GeneratedCodeOutputPath>

<!-- BAD -->
<enabletel>true</enabletel>            <!-- not PascalCase, unclear abbreviation -->
<GenOut>$(IntermediateOutputPath)Generated/</GenOut> <!-- too short, no prefix -->
```

---

## Conditions

### Attribute-level vs group-level conditions

```xml
<!-- GOOD: Attribute-level condition when only one property differs -->
<PropertyGroup>
  <DebugSymbols Condition="'$(Configuration)' == 'Debug'">true</DebugSymbols>
</PropertyGroup>

<!-- BAD: Entire group for a single property -->
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <DebugSymbols>true</DebugSymbols>
</PropertyGroup>
```

```xml
<!-- GOOD: Group-level condition when multiple properties share the same condition -->
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <Optimize>true</Optimize>
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
</PropertyGroup>
```

### Condition syntax rules

```xml
<!-- String comparison: always quote BOTH sides -->
<PropertyGroup Condition="'$(Configuration)' == 'Release'">

<!-- Boolean check: use lowercase 'true' -->
<RunAnalyzers Condition="'$(RunAnalyzers)' == 'true'">true</RunAnalyzers>

<!-- Existence check: compare against empty string -->
<MyProp Condition="'$(MyProp)' != ''">$(MyProp)</MyProp>

<!-- File/directory existence: use Exists() -->
<Import Project="local.props" Condition="Exists('local.props')" />

<!-- TFM condition -->
<DefineConstants Condition="'$(TargetFramework)' == 'net8.0'">$(DefineConstants);NET8_FEATURE</DefineConstants>

<!-- TFM compatibility (preferred for >= comparisons) -->
<DefineConstants Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net6.0'))">$(DefineConstants);NET6_OR_GREATER</DefineConstants>
```

```xml
<!-- BAD: Unquoted sides -->
<PropertyGroup Condition="$(Configuration) == Release">

<!-- BAD: Double negative -->
<RunTests Condition="'$(SkipTests)' != 'true'">false</RunTests>

<!-- GOOD: Positive condition instead of double negative -->
<RunTests Condition="'$(SkipTests)' == 'true'">false</RunTests>
```

---

## Item Groups

### Prefer implicit includes

```xml
<!-- GOOD: Let the SDK glob handle it. Only exclude what you must. -->
<ItemGroup>
  <Compile Remove="LegacyCode\**" />
</ItemGroup>

<!-- BAD: Manually listing every file -->
<ItemGroup>
  <Compile Include="Class1.cs" />
  <Compile Include="Class2.cs" />
  <Compile Include="Services\MyService.cs" />
</ItemGroup>
```

### Include vs Update vs Remove

| Operation | Use When |
|-----------|----------|
| `Include` | Adding new items not covered by default globs |
| `Update` | Modifying metadata on items already included (e.g., by default globs) |
| `Remove` | Excluding items from default globs |

```xml
<!-- GOOD: Update metadata on an already-included file -->
<ItemGroup>
  <EmbeddedResource Update="Strings.resx">
    <Generator>ResXFileCodeGenerator</Generator>
  </EmbeddedResource>
</ItemGroup>

<!-- BAD: Using Include on a file already picked up by globs (creates duplicates) -->
<ItemGroup>
  <EmbeddedResource Include="Strings.resx">
    <Generator>ResXFileCodeGenerator</Generator>
  </EmbeddedResource>
</ItemGroup>
```

### ItemDefinitionGroup for default metadata

```xml
<!-- GOOD: Set default metadata for all items of a type -->
<ItemDefinitionGroup>
  <EmbeddedResource>
    <Generator>ResXFileCodeGenerator</Generator>
  </EmbeddedResource>
</ItemDefinitionGroup>
```

### Don't mix Include and Update for the same type in one ItemGroup

```xml
<!-- BAD -->
<ItemGroup>
  <Compile Include="Extra.cs" />
  <Compile Update="Extra.cs" CopyToOutputDirectory="Always" />
</ItemGroup>

<!-- GOOD: Separate the operations -->
<ItemGroup>
  <Compile Include="Extra.cs" />
</ItemGroup>
<ItemGroup>
  <Compile Update="Extra.cs" CopyToOutputDirectory="Always" />
</ItemGroup>
```

---

## PackageReference Best Practices

### Always use PackageReference

```xml
<!-- GOOD -->
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
</ItemGroup>

<!-- BAD: Legacy packages.config style or raw assembly reference -->
<ItemGroup>
  <Reference Include="..\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll" />
</ItemGroup>
```

### Central Package Management

For multi-project repos, use `Directory.Packages.props` to centralize versions. See [`shared/central-package-management.md`](../shared/central-package-management.md) for the full setup pattern.

```xml
<!-- Individual .csproj — no Version attribute needed -->
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" />
</ItemGroup>
```

### Analyzers and build-only packages

Mark analyzers and build tools with `PrivateAssets="all"` so they don't flow to consumers. See [`shared/private-assets.md`](../shared/private-assets.md) for the full list of packages that need this.

### Fine-grained asset control

```xml
<!-- Only use compile and runtime assets (no build/analyzers) -->
<PackageReference Include="SomePackage" Version="1.0.0"
                  IncludeAssets="compile;runtime"
                  ExcludeAssets="build;analyzers" />
```

### Version ranges

```xml
<!-- Pin exact version for apps / deployables -->
<PackageReference Include="MyDep" Version="2.1.0" />

<!-- Allow patch updates for libraries -->
<PackageReference Include="MyDep" Version="[2.1.0, 2.2.0)" />

<!-- Prefer pinned versions in most cases; use ranges only when you have a clear need -->
```

---

## ProjectReference Best Practices

```xml
<!-- GOOD: In-repo dependency -->
<ItemGroup>
  <ProjectReference Include="..\MyLib\MyLib.csproj" />
</ItemGroup>

<!-- BAD: Referencing a built DLL from another project in the same repo -->
<ItemGroup>
  <Reference Include="..\MyLib\bin\Release\MyLib.dll" />
</ItemGroup>
```

### Build-only dependencies

```xml
<!-- Dependency for build ordering only — don't reference the output assembly -->
<ProjectReference Include="..\CodeGen\CodeGen.csproj"
                  ReferenceOutputAssembly="false" />

<!-- Don't flow transitive dependency to consumers -->
<ProjectReference Include="..\Internal\Internal.csproj"
                  PrivateAssets="all" />
```

### Keep the dependency graph slim

```xml
<!-- BAD: Every project references everything -->
<ItemGroup>
  <ProjectReference Include="..\Core\Core.csproj" />
  <ProjectReference Include="..\Data\Data.csproj" />
  <ProjectReference Include="..\Utils\Utils.csproj" />      <!-- only Data uses Utils -->
  <ProjectReference Include="..\Logging\Logging.csproj" />   <!-- already transitive via Core -->
</ItemGroup>

<!-- GOOD: Only direct dependencies; let transitive references flow -->
<ItemGroup>
  <ProjectReference Include="..\Core\Core.csproj" />
  <ProjectReference Include="..\Data\Data.csproj" />
</ItemGroup>
```

---

## Target Authoring

### BeforeTargets/AfterTargets vs DependsOnTargets

| Mechanism | Use When |
|-----------|----------|
| `BeforeTargets` / `AfterTargets` | Hooking into targets you don't own (SDK/NuGet targets) |
| `DependsOnTargets` | Declaring dependencies between your own targets |

```xml
<!-- GOOD: Hook into the build pipeline -->
<Target Name="GenerateVersionInfo" BeforeTargets="CoreCompile">
  <WriteLinesToFile File="$(IntermediateOutputPath)Version.g.cs"
                    Lines="[assembly: System.Reflection.AssemblyVersion(&quot;$(Version)&quot;)]"
                    Overwrite="true" />
  <ItemGroup>
    <Compile Include="$(IntermediateOutputPath)Version.g.cs" />
  </ItemGroup>
</Target>

<!-- GOOD: Chain your own targets with DependsOnTargets -->
<Target Name="PackageApp" DependsOnTargets="BuildApp;RunTests">
  <!-- packaging logic -->
</Target>
```

### Naming targets

Use **Verb + Noun** format:

```xml
<!-- GOOD -->
<Target Name="GenerateVersionInfo" />
<Target Name="CopyLicenseFile" />
<Target Name="ValidatePackageMetadata" />

<!-- BAD -->
<Target Name="DoStuff" />
<Target Name="Step2" />
<Target Name="MyTarget" />
```

### Incremental build support

Always specify `Inputs` and `Outputs` so the target is skipped when up-to-date. See [`shared/incremental-build-inputs-outputs.md`](../shared/incremental-build-inputs-outputs.md) for the full pattern with FileWrites.

```xml
<!-- GOOD: Incremental target -->
<Target Name="TransformTemplates"
        BeforeTargets="CoreCompile"
        Inputs="@(TemplateFile)"
        Outputs="@(TemplateFile->'$(IntermediateOutputPath)%(Filename).g.cs')">
  <!-- transform logic -->
</Target>

<!-- BAD: Runs every build even if nothing changed -->
<Target Name="TransformTemplates" BeforeTargets="CoreCompile">
  <!-- transform logic -->
</Target>
```

### Use Returns for inter-target communication

```xml
<Target Name="CollectDeployFiles" Returns="@(DeployFile)">
  <ItemGroup>
    <DeployFile Include="$(OutputPath)**\*.dll" />
    <DeployFile Include="$(OutputPath)**\*.json" />
  </ItemGroup>
</Target>
```

### Keep targets small and focused

```xml
<!-- BAD: Monolithic target doing everything -->
<Target Name="PrepareRelease" BeforeTargets="Build">
  <!-- generate version, copy license, sign assembly, validate metadata... 50 lines -->
</Target>

<!-- GOOD: Separate single-responsibility targets -->
<Target Name="GenerateVersionInfo" BeforeTargets="CoreCompile" />
<Target Name="CopyLicenseFile" AfterTargets="Build" />
<Target Name="SignAssembly" AfterTargets="Build" DependsOnTargets="CopyLicenseFile" />
```

---

## Property Functions

Use property functions for simple operations instead of shelling out.

```xml
<!-- GOOD: String operations via property functions -->
<PropertyGroup>
  <CleanVersion>$(Version.Replace('-preview', ''))</CleanVersion>
  <HasPrerelease>$(Version.Contains('-'))</HasPrerelease>
  <LowerName>$(AssemblyName.ToLowerInvariant())</LowerName>
</PropertyGroup>

<!-- GOOD: Path operations -->
<PropertyGroup>
  <ToolPath>$([System.IO.Path]::Combine($(MSBuildThisFileDirectory), 'tools', 'mytool.exe'))</ToolPath>
  <NormalizedOutput>$([MSBuild]::NormalizeDirectory($(OutputPath)))</NormalizedOutput>
</PropertyGroup>

<!-- GOOD: Common MSBuild intrinsic functions -->
<PropertyGroup>
  <RepoRoot>$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), '.gitignore'))</RepoRoot>
  <SafePath>$([MSBuild]::NormalizePath($(RepoRoot), 'src', 'MyLib'))</SafePath>
</PropertyGroup>

<!-- BAD: Shelling out for a simple string operation -->
<Target Name="GetCleanVersion">
  <Exec Command="echo $(Version) | sed 's/-preview//'" ConsoleToMsBuildProperty="CleanVersion" />
</Target>
```

### Don't call side-effecting functions during evaluation

```xml
<!-- BAD: Side effects during property evaluation -->
<PropertyGroup>
  <Timestamp>$([System.IO.File]::WriteAllText('stamp.txt', 'built'))</Timestamp>
</PropertyGroup>

<!-- GOOD: Side effects belong in targets -->
<Target Name="WriteTimestamp" BeforeTargets="Build">
  <WriteLinesToFile File="stamp.txt" Lines="built" Overwrite="true" />
</Target>
```

### Don't condition properties on TargetFramework in .props files

`$(TargetFramework)` is only available during `.props` evaluation for multi-targeting projects. For single-targeting projects, property conditions on it silently fail. See [`shared/targetframework-props-evaluation.md`](../shared/targetframework-props-evaluation.md) for the full explanation.

```xml
<!-- BAD: In Directory.Build.props — TargetFramework may be empty -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <DefineConstants>$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>

<!-- GOOD: In Directory.Build.targets — TargetFramework is always available -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <DefineConstants>$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>
```

---

## Paths

```xml
<!-- GOOD: Relative to current file (works in .props/.targets imported from anywhere) -->
<Import Project="$(MSBuildThisFileDirectory)shared\common.props" />

<!-- GOOD: Relative to project directory -->
<Content Include="$(MSBuildProjectDirectory)\assets\**" />

<!-- GOOD: Normalize paths to avoid mixed slashes -->
<PropertyGroup>
  <ToolDir>$([MSBuild]::NormalizePath($(MSBuildThisFileDirectory), 'tools'))</ToolDir>
</PropertyGroup>

<!-- GOOD: Forward slashes work cross-platform in MSBuild -->
<Import Project="$(RepoRoot)/eng/common.props" />

<!-- BAD: Hardcoded absolute paths -->
<Import Project="C:\repos\myrepo\eng\common.props" />

<!-- BAD: Backslashes break on non-Windows -->
<Import Project="$(RepoRoot)\eng\common.props" />
```

---

## Imports

```xml
<!-- GOOD: Use SDK import when an SDK exists -->
<Project Sdk="Microsoft.NET.Sdk">
  <!-- SDK provides implicit top and bottom imports -->
</Project>

<!-- GOOD: Optional import with Exists guard -->
<Import Project="local.overrides.props" Condition="Exists('local.overrides.props')" />

<!-- GOOD: Custom imports after SDK defaults so you can override -->
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$(RepoRoot)/eng/custom.targets" />
</Project>

<!-- BAD: Import without Exists guard — breaks build if file is missing -->
<Import Project="local.overrides.props" />

<!-- NOTE: Import order matters. Later imports override earlier ones.
     .props files = set defaults (imported early)
     .targets files = define build logic (imported late) -->
```

---

## Anti-patterns to Avoid

### 1. Hardcoded paths → use MSBuild properties

[truncated]