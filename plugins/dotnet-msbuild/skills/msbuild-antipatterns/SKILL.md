---
name: msbuild-antipatterns
description: "Catalog of MSBuild anti-patterns with detection rules and fix recipes. Only activate in MSBuild/.NET build context. USE FOR: reviewing, auditing, or cleaning up .csproj, .vbproj, .fsproj, .props, .targets, or .proj files. Each anti-pattern has a symptom, explanation, and concrete BAD→GOOD transformation. Covers Exec-instead-of-built-in-task, unquoted conditions, hardcoded paths, restating SDK defaults, scattered package versions, and more. DO NOT USE FOR: non-MSBuild build systems (npm, Maven, CMake, etc.), project migration to SDK-style (use msbuild-modernization)."
license: MIT
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

**Exception (F# / `.fsproj`)**: F# compilation is order-dependent — the compiler processes `<Compile Include>` items sequentially and a file can only reference types/modules declared in files listed above it. `.fsproj` files must therefore list every source file explicitly, in dependency order (utility/leaf modules at the top, the entry point such as `Program.fs` at the bottom). If a `.fsi` signature file is used, it must appear **immediately before** its companion `.fs` implementation file.

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
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>

<!-- GOOD: Define once in Directory.Build.props at the repo/src root -->
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
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

For additional anti-patterns (AP-16 through AP-21) and a quick-reference checklist, see [additional-antipatterns.md](references/additional-antipatterns.md).
