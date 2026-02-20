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

```xml
<!-- BAD -->
<Exec Command="C:\tools\mytool.exe" />

<!-- GOOD -->
<Exec Command="$(ToolsDir)mytool.exe" />
```

### 2. Copy-paste properties across .csproj files → use Directory.Build.props

```xml
<!-- BAD: Same properties duplicated in every .csproj -->
<!-- MyLib.csproj, MyApp.csproj, MyTests.csproj all have: -->
<PropertyGroup>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>

<!-- GOOD: Define once in Directory.Build.props -->
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

### 3. Manual file listing → use SDK default globs

```xml
<!-- BAD -->
<ItemGroup>
  <Compile Include="Foo.cs" />
  <Compile Include="Bar.cs" />
</ItemGroup>

<!-- GOOD: Remove the ItemGroup entirely — SDK includes *.cs by default -->
```

### 4. `<Exec>` for simple operations → use built-in tasks or property functions

```xml
<!-- BAD -->
<Exec Command="mkdir $(OutputPath)logs" />

<!-- GOOD -->
<MakeDir Directories="$(OutputPath)logs" />
```

### 5. Monolithic .csproj → split into .props/.targets for complex logic

```xml
<!-- BAD: 200-line .csproj with custom tasks, targets, and shared properties -->

<!-- GOOD: Split concerns -->
<!-- Directory.Build.props — shared properties -->
<!-- MyProject.targets — custom build logic -->
<!-- MyProject.csproj — project-specific settings only -->
```

### 6. Setting properties in .targets that should be in .props

```xml
<!-- BAD: Property set in .targets — may be too late, other .targets already evaluated -->
<!-- custom.targets -->
<PropertyGroup>
  <MyDefaultValue>foo</MyDefaultValue>
</PropertyGroup>

<!-- GOOD: Defaults belong in .props (evaluated early) -->
<!-- custom.props -->
<PropertyGroup>
  <MyDefaultValue Condition="'$(MyDefaultValue)' == ''">foo</MyDefaultValue>
</PropertyGroup>
```

### 7. Using `<Reference>` for NuGet packages → use `<PackageReference>`

`<Reference>` with HintPath to a NuGet package folder is a legacy pattern. Use `<PackageReference>` for NuGet packages and `<ProjectReference>` for local project dependencies. Note: `<Reference>` is still appropriate for .NET Framework projects referencing GAC assemblies (e.g., `WindowsBase`, `PresentationCore`).

```xml
<!-- BAD -->
<Reference Include="..\packages\Newtonsoft.Json.13.0.3\lib\netstandard2.0\Newtonsoft.Json.dll" />

<!-- GOOD: NuGet package -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />

<!-- ALSO OK: .NET Framework GAC assembly -->
<Reference Include="WindowsBase" />
```

### 8. Defining the same property unconditionally in multiple places

```xml
<!-- BAD: Last write wins — silent override, confusing -->
<!-- Directory.Build.props -->
<PropertyGroup>
  <OutputPath>bin\custom\</OutputPath>
</PropertyGroup>
<!-- MyProject.csproj -->
<PropertyGroup>
  <OutputPath>bin\other\</OutputPath>  <!-- silently overrides the above -->
</PropertyGroup>

<!-- GOOD: Use a condition so the project can opt out intentionally -->
<!-- Directory.Build.props -->
<PropertyGroup>
  <OutputPath Condition="'$(OutputPath)' == ''">bin\custom\</OutputPath>
</PropertyGroup>
```

### 9. DefineConstants overwrites instead of appending

`<DefineConstants>` is a semicolon-delimited property. Setting it without preserving the existing value **silently kills** SDK-defined constants like `TRACE` and `DEBUG`.

```xml
<!-- BAD: This REPLACES all existing DefineConstants (TRACE, DEBUG vanish) -->
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <DefineConstants>ENABLE_LOGGING</DefineConstants>
</PropertyGroup>

<!-- GOOD: Append to existing constants -->
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <DefineConstants>$(DefineConstants);ENABLE_LOGGING</DefineConstants>
</PropertyGroup>
```

### 10. Analyzer PackageReference without PrivateAssets

Analyzer packages are build-time-only tools. Without `<PrivateAssets>all</PrivateAssets>`, the analyzer dependency **leaks to downstream consumers** of your library via transitive dependency resolution.

```xml
<!-- BAD: Analyzer leaks to consumers -->
<PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" />

<!-- GOOD: Analyzer stays private to this project -->
<PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>

<!-- BEST: Use GlobalPackageReference in Directory.Packages.props (handles PrivateAssets automatically) -->
<GlobalPackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" />
```

### 11. Property defaults in .targets that projects can't override

Properties set in `Directory.Build.targets` override any values set in project files because `.targets` is imported **after** the project. If you intend a value as an overridable default, put it in `.props`.

**Evaluation order:** `Directory.Build.props → SDK .props → YourProject.csproj → SDK .targets → Directory.Build.targets`

```xml
<!-- BAD: In Directory.Build.targets — projects cannot override this -->
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>

<!-- GOOD: In Directory.Build.props — projects can override -->
<PropertyGroup>
  <TreatWarningsAsErrors Condition="'$(TreatWarningsAsErrors)' == ''">true</TreatWarningsAsErrors>
</PropertyGroup>
```
