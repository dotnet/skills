<!-- AUTO-GENERATED — DO NOT EDIT. Regenerate with: node src/dotnet-msbuild/build.js -->

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

---

## check-bin-obj-clash

# Detecting OutputPath and IntermediateOutputPath Clashes

## Overview

This skill helps identify when multiple MSBuild project evaluations share the same `OutputPath` or `IntermediateOutputPath`. This is a common source of build failures including:

- File access conflicts during parallel builds
- Missing or overwritten output files
- Intermittent build failures
- "File in use" errors
- **NuGet restore errors like `Cannot create a file when that file already exists`** - this strongly indicates multiple projects share the same `IntermediateOutputPath` where `project.assets.json` is written

Clashes can occur between:
- **Different projects** sharing the same output directory
- **Multi-targeting builds** (e.g., `TargetFrameworks=net8.0;net9.0`) where the path doesn't include the target framework
- **Multiple solution builds** where the same project is built from different solutions in a single build

**Note:** Project instances with `BuildProjectReferences=false` should be **ignored** when analyzing clashes - these are P2P reference resolution builds that only query metadata (via `GetTargetPath`) and do not actually write to output directories.

## When to Use This Skill

**Invoke this skill immediately when you see:**
- `Cannot create a file when that file already exists` during NuGet restore
- `The process cannot access the file because it is being used by another process`
- Intermittent build failures that succeed on retry
- Missing output files or unexpected overwriting

## Step 1: Generate a Binary Log

Use the `binlog-generation` skill to generate a binary log with the correct naming convention.

## Step 2: Load the Binary Log

```
load_binlog with path: "<absolute-path-to-build.binlog>"
```

## Step 3: List All Projects

```
list_projects with binlog_file: "<path>"
```

This returns all projects with their IDs and file paths.

## Step 4: Get Evaluations for Each Project

For each unique project file path, list its evaluations:

```
list_evaluations with:
  - binlog_file: "<path>"
  - projectFilePath: "<project-file-path>"
```

Multiple evaluations for the same project indicate multi-targeting or multiple build configurations.

## Step 5: Check Global Properties for Each Evaluation

For each evaluation, get the global properties to understand the build configuration:

```
get_evaluation_global_properties with:
  - binlog_file: "<path>"
  - evaluationId: <evaluation-id>
```

Look for properties like `TargetFramework`, `Configuration`, `Platform`, and `RuntimeIdentifier` that should differentiate output paths.

Also check **solution-related properties** to identify multi-solution builds:
- `SolutionFileName`, `SolutionName`, `SolutionPath`, `SolutionDir`, `SolutionExt` — differ when a project is built from multiple solutions
- `CurrentSolutionConfigurationContents` — the number of project entries reveals which solution an evaluation belongs to (e.g., 1 project vs ~49 projects)

Look for **extra global properties that don't affect output paths** but create distinct MSBuild project instances:
- `PublishReadyToRun` — a publish setting that doesn't change `OutputPath` or `IntermediateOutputPath`, but MSBuild treats it as a distinct project instance, preventing result caching and causing redundant target execution (e.g., `CopyFilesToOutputDirectory` running again)
- Any other global property that differs between evaluations but doesn't contribute to path differentiation

### Filter Out Non-Build Evaluations

When analyzing clashes, filter evaluations based on the type of clash you're investigating:

1. **For OutputPath clashes**: Exclude restore-phase evaluations (where `MSBuildRestoreSessionId` global property is set). These don't write to output directories.

2. **For IntermediateOutputPath clashes**: Include restore-phase evaluations, as NuGet restore writes `project.assets.json` to the intermediate output path.

3. **Always exclude `BuildProjectReferences=false`**: These are P2P metadata queries, not actual builds that write files.

## Step 6: Get Output Paths for Each Evaluation

For each evaluation, retrieve the `OutputPath` and `IntermediateOutputPath`:

```
get_evaluation_properties_by_name with:
  - binlog_file: "<path>"
  - evaluationId: <evaluation-id>
  - propertyNames: ["OutputPath", "IntermediateOutputPath", "BaseOutputPath", "BaseIntermediateOutputPath", "TargetFramework", "Configuration", "Platform"]
```

## Step 7: Identify Clashes

Compare the `OutputPath` and `IntermediateOutputPath` values across all evaluations:

1. **Normalize paths** - Convert to absolute paths and normalize separators
2. **Group by path** - Find evaluations that share the same OutputPath or IntermediateOutputPath
3. **Report clashes** - Any group with more than one evaluation indicates a clash

## Step 8: Verify Clashes via CopyFilesToOutputDirectory (Optional)

As additional evidence for OutputPath clashes, check if multiple project builds execute the `CopyFilesToOutputDirectory` target to the same path. Note that not all clashes manifest here - compilation outputs and other targets may also conflict.

```
search_binlog with:
  - binlog_file: "<path>"
  - query: "$target CopyFilesToOutputDirectory project(<project-name>.csproj)"
```

Then for each project ID that ran this target, examine the Copy task messages:

```
list_tasks_in_target with:
  - binlog_file: "<path>"
  - projectId: <project-id>
  - targetId: <target-id-of-CopyFilesToOutputDirectory>
```

Look for evidence of clashes in the messages:
- `Copying file from "..." to "..."` - Active file writes
- `Did not copy from file "..." to file "..." because the "SkipUnchangedFiles" parameter was set to "true"` - Indicates a second build attempted to write to the same location

The `SkipUnchangedFiles` skip message often masks clashes - the build succeeds but is vulnerable to race conditions in parallel builds.

## Step 9: Check CoreCompile Execution Patterns (Optional)

To understand which project instance did the actual compilation vs redundant work, check `CoreCompile`:

```
search_binlog with:
  - binlog_file: "<path>"
  - query: "$target CoreCompile project(<project-name>.csproj)"
```

Compare the durations:
- The instance with a long `CoreCompile` duration (e.g., seconds) is the **primary build** that did the actual compilation
- Instances where `CoreCompile` was skipped (duration ~0-10ms) are **redundant builds** — they didn't recompile but may still run other targets like `CopyFilesToOutputDirectory` that write to the same output directory

This helps distinguish the "real" build from redundant instances created by extra global properties or multi-solution builds.

### Caveat: `under()` Search in Multi-Solution Builds

When using `search_binlog` with `under($project SolutionName)` to determine which solution a project instance belongs to, be aware that `under()` matches through the **entire build hierarchy**. If both solutions share a common ancestor (e.g., Arcade SDK's `Build.proj`), all project instances will appear "under" both solutions.

Instead, use `get_evaluation_global_properties` and compare the `SolutionFileName` / `CurrentSolutionConfigurationContents` properties to reliably determine which solution an evaluation belongs to.

### Expected Output Structure

For each evaluation, collect:
- Project file path
- Evaluation ID
- TargetFramework (if multi-targeting)
- Configuration
- OutputPath
- IntermediateOutputPath

### Clash Detection Logic

```
For each unique OutputPath:
  - If multiple evaluations share it → CLASH
  
For each unique IntermediateOutputPath:
  - If multiple evaluations share it → CLASH
```

## Common Causes and Fixes

### Multi-targeting without TargetFramework in path

**Problem:** Project uses `TargetFrameworks` but OutputPath doesn't vary by framework.

```xml
<!-- BAD: Same path for all frameworks -->
<OutputPath>bin\$(Configuration)\</OutputPath>
```

**Fix:** Include TargetFramework in the path:

```xml
<!-- GOOD: Path varies by framework -->
<OutputPath>bin\$(Configuration)\$(TargetFramework)\</OutputPath>
```

Or rely on SDK defaults which handle this automatically:

```xml
<AppendTargetFrameworkToOutputPath>true</AppendTargetFrameworkToOutputPath>
<AppendTargetFrameworkToIntermediateOutputPath>true</AppendTargetFrameworkToIntermediateOutputPath>
```

### Shared output directory across projects (CANNOT be fixed with AppendTargetFramework)

**Problem:** Multiple projects explicitly set the same `BaseOutputPath` or `BaseIntermediateOutputPath`.

```xml
<!-- Project A - Directory.Build.props -->
<BaseOutputPath>..\SharedOutput\</BaseOutputPath>
<BaseIntermediateOutputPath>..\SharedObj\</BaseIntermediateOutputPath>

<!-- Project B - Directory.Build.props -->
<BaseOutputPath>..\SharedOutput\</BaseOutputPath>
<BaseIntermediateOutputPath>..\SharedObj\</BaseIntermediateOutputPath>
```

**IMPORTANT:** Even with `AppendTargetFrameworkToOutputPath=true`, this will still clash! .NET writes certain files directly to the `IntermediateOutputPath` without the TargetFramework suffix, including:

- `project.assets.json` (NuGet restore output)
- Other NuGet-related files

This causes errors like `Cannot create a file when that file already exists` during parallel restore.

**Fix:** Each project MUST have a unique `BaseIntermediateOutputPath`. Do not share intermediate output directories across projects:

```xml
<!-- Project A -->
<BaseIntermediateOutputPath>..\obj\ProjectA\</BaseIntermediateOutputPath>

<!-- Project B -->
<BaseIntermediateOutputPath>..\obj\ProjectB\</BaseIntermediateOutputPath>
```

Or simply use the SDK defaults which place `obj` inside each project's directory.

### RuntimeIdentifier builds clashing

**Problem:** Building for multiple RIDs without RID in path.

**Fix:** Ensure RuntimeIdentifier is in the path:

```xml
<AppendRuntimeIdentifierToOutputPath>true</AppendRuntimeIdentifierToOutputPath>
```

### Multiple solutions building the same project

**Problem:** A single build invokes multiple solutions (e.g., via MSBuild task or command line) that include the same project. Each solution build evaluates and builds the project independently, with different `Solution*` global properties that don't affect the output path.

**How to detect:** Compare `SolutionFileName` and `CurrentSolutionConfigurationContents` across evaluations for the same project. Different values indicate multi-solution builds. For example:

| Property | Eval from Solution A | Eval from Solution B |
|---|---|---|
| `SolutionFileName` | `BuildAnalyzers.sln` | `Main.slnx` |
| `CurrentSolutionConfigurationContents` | 1 project entry | ~49 project entries |
| `OutputPath` | `bin\Release\netstandard2.0\` | `bin\Release\netstandard2.0\` ← **clash** |

**Example:** A repo build script builds `BuildAnalyzers.sln` then `Main.slnx`, and both solutions include `SharedAnalyzers.csproj`. Both builds write to `bin\Release\netstandard2.0\`. The first build compiles; the second skips compilation but still runs `CopyFilesToOutputDirectory`.

[truncated]