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
