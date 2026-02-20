# TargetFramework Availability in .props vs .targets

**`$(TargetFramework)` is NOT reliably available in `Directory.Build.props` or any `.props` file imported before the project body.** It is only set that early for multi-targeting projects, which receive `TargetFramework` as a global property from the outer build. Single-targeting projects (using singular `<TargetFramework>`) set it in the project body, which is evaluated *after* `.props`.

For a detailed explanation of MSBuild's evaluation and execution phases, see [Build process overview](https://learn.microsoft.com/en-us/visualstudio/msbuild/build-process-overview).

This means **property conditions on `$(TargetFramework)` in `.props` files silently fail** for single-targeting projects — the condition never matches because the property is empty. This applies to both `<PropertyGroup Condition="...">` and individual `<Property Condition="...">` elements.

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

## ⚠️ Important: Item and Target conditions are NOT affected

**This restriction applies ONLY to property conditions** (`<PropertyGroup Condition="...">` and `<Property Condition="...">`).

**Item conditions (`<ItemGroup Condition="...">`) and Target conditions in `.props` files are SAFE** because items and targets evaluate after all properties (including those set in the project body) have been evaluated. This includes `PackageVersion` items in `Directory.Packages.props`, `PackageReference` items in `Directory.Build.props`, and any other item types.

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
