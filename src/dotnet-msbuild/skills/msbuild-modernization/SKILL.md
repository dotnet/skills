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
