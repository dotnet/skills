# Never Set `<TargetFramework>` or `<TargetFrameworks>` in Directory.Build.props

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

If you want to centralize the *version string* to avoid drift, define a custom property in `Directory.Build.props` and reference it from each project:

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <DefaultTargetFramework>net8.0</DefaultTargetFramework>
  </PropertyGroup>
</Project>

<!-- MyLib.csproj — each project still declares TargetFramework itself -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>
  </PropertyGroup>
</Project>

<!-- MyApp.csproj — a project that needs to multi-target works fine -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>$(DefaultTargetFramework);netstandard2.0</TargetFrameworks>
  </PropertyGroup>
</Project>
```

This keeps the version in one place while ensuring each project controls whether it uses the singular or plural form.
