# TargetFramework Pitfalls in Directory.Build.props

## Never Set `<TargetFramework>` or `<TargetFrameworks>` in Directory.Build.props

**Do NOT centralize `<TargetFramework>` or `<TargetFrameworks>` in `Directory.Build.props`** — not even as a "default" when every project happens to share the same TFM. This applies to `Directory.Build.props` at any level (repo root, `src/`, `test/`, etc.).

**Why it's dangerous**: If `Directory.Build.props` sets the singular `<TargetFramework>` and a project later sets the plural `<TargetFrameworks>` (or vice versa), **both** properties exist simultaneously. MSBuild interprets `<TargetFrameworks>` (plural) as a request for an outer build that dispatches inner builds per TFM, while also seeing `<TargetFramework>` (singular) which signals an inner build. The build is now simultaneously an inner and outer build — targets run in the wrong order, items appear in the wrong phase, and the build produces corrupt or nonsensical results.

This is not a theoretical risk. It happens whenever someone:
- Adds multi-targeting to a project that was single-targeting, or
- Switches a multi-targeting project to single-targeting

…and forgets (or doesn't know) that the other variant is set in `Directory.Build.props`.

```xml
<!-- BAD: Directory.Build.props sets a "default" TFM -->
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>  <!-- NEVER DO THIS -->
  </PropertyGroup>
</Project>

<!-- A project then tries to multi-target: -->
<!-- MyLib.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>
  </PropertyGroup>
</Project>
<!-- 💥 BOTH TargetFramework=net8.0 AND TargetFrameworks=net8.0;netstandard2.0 are set.
     MSBuild does inner+outer build simultaneously. Everything breaks. -->

<!-- GOOD: Each project declares its own TFM — no ambiguity -->
<!-- MyLib.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

**Even when every project uses the same TFM**, keep it in each `.csproj`. It is the one property that must never be centralized. The cost of the repetition is near-zero; the cost of the collision is a completely broken build with baffling error messages.

---

# AP-21: Property Conditioned on TargetFramework in .props Files

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
