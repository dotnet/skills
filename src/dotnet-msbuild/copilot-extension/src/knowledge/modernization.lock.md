<!-- AUTO-GENERATED — DO NOT EDIT. Regenerate with: node src/dotnet-msbuild/build.js -->

---
name: msbuild-modernization
description: "Guide for modernizing and migrating MSBuild project files to SDK-style format. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when encountering legacy .csproj/.vbproj files with verbose XML, packages.config, or AssemblyInfo.cs patterns. Covers legacy-to-SDK migration, removing boilerplate, PackageReference migration, and Directory.Build consolidation. Invoke when asked to modernize, migrate, or clean up project files."
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

Centralizes NuGet version management across a multi-project solution. See [`shared/central-package-management.md`](../shared/central-package-management.md) for the full CPM setup pattern.

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
    <ProjectReference Include="..\MyLibrary\MyLibrary.csproj" />
  </ItemGroup>
</Project>
```

## Tools and Automation

| Tool | Usage |
|------|-------|
| `dotnet try-convert` | Automated legacy-to-SDK conversion. Install: `dotnet tool install -g try-convert` |
| .NET Upgrade Assistant | Full migration including API changes. Install: `dotnet tool install -g upgrade-assistant` |
| Visual Studio | Right-click `packages.config` → *Migrate packages.config to PackageReference* |
| Manual migration | Often cleanest for simple projects — follow the checklist above |

**Recommended approach:**

1. Run `try-convert` for a first pass
2. Review and clean up the output manually
3. Build and fix any issues
4. Enable modern features (nullable, implicit usings)
5. Consolidate shared settings into `Directory.Build.props`

---

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