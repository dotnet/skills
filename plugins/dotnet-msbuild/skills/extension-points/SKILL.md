---
name: extension-points
description: "Guide for MSBuild extensibility: CustomBefore/CustomAfter hooks, wildcard imports with alphabetic ordering, import gating with control properties, NuGet package build extension layout (build/buildTransitive), and the MicrosoftCommonPropsHasBeenImported guard. Only activate in MSBuild/.NET build context. USE FOR: diagnosing and fixing MSBuild import and hook patterns, reviewing and fixing extension point anti-patterns in Directory.Build files, fixing missing Exists() guards on imports that break fresh clones, fixing NuGet package hooks being silently dropped instead of appended, making build targets extensible for other projects, injecting custom logic into the build pipeline, creating NuGet packages that extend the build, conditionally disabling imports. DO NOT USE FOR: target authoring patterns (use target-authoring), props vs targets placement (use directory-build-organization), general anti-patterns (use msbuild-antipatterns), non-MSBuild build systems."
license: MIT
---

# MSBuild Extension Points

How the MSBuild pipeline provides hooks for SDKs, NuGet packages, repos, and users to inject custom logic.

## CustomBefore / CustomAfter Hooks

Every major `.targets` file defines import hooks:

```xml
<PropertyGroup>
  <CustomBeforeMicrosoftCommonTargets Condition="'$(CustomBeforeMicrosoftCommonTargets)' == ''">
    $(MSBuildExtensionsPath)\v$(MSBuildToolsVersion)\Custom.Before.Microsoft.Common.targets
  </CustomBeforeMicrosoftCommonTargets>
</PropertyGroup>

<Import Project="$(CustomBeforeMicrosoftCommonTargets)"
    Condition="'$(CustomBeforeMicrosoftCommonTargets)' != '' and Exists('$(CustomBeforeMicrosoftCommonTargets)')"/>
<!-- ... core targets ... -->
<Import Project="$(CustomAfterMicrosoftCommonTargets)"
    Condition="'$(CustomAfterMicrosoftCommonTargets)' != '' and Exists('$(CustomAfterMicrosoftCommonTargets)')"/>
```

### Rules

- Default path includes version (`v$(MSBuildToolsVersion)`) for side-by-side installations.
- Always check `Exists()`. The file may not be present on every machine.
- **Append** to the property (don't overwrite) to chain multiple hooks:

```xml
<PropertyGroup>
  <CustomBeforeMicrosoftCommonTargets>
    $(CustomBeforeMicrosoftCommonTargets);$(MSBuildThisFileDirectory)MyExtension.targets
  </CustomBeforeMicrosoftCommonTargets>
</PropertyGroup>
```

## Wildcard Import Directories

MSBuild imports all files in extension directories, sorted alphabetically:

```xml
<Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Imports\Microsoft.Common.props\ImportBefore\*"
    Condition="'$(ImportByWildcardBeforeMicrosoftCommonProps)' == 'true'
               and Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Imports\Microsoft.Common.props\ImportBefore')" />
```

### Key paths

| Property | Resolves to | Scope |
|---|---|---|
| `$(MSBuildUserExtensionsPath)` | `%APPDATA%\Microsoft\MSBuild` | Per-user |
| `$(MSBuildExtensionsPath)` | MSBuild install directory | Machine-wide |
| `$(MSBuildProjectExtensionsPath)` | `obj/` directory | Per-project (NuGet) |

Name files with numeric prefixes for ordering: `01-first.props`, `02-second.props`.

## Import Gating — Control Properties

Every wildcard import is gated by a boolean property:

```xml
<PropertyGroup>
  <ImportByWildcardBeforeMicrosoftCommonProps
      Condition="'$(ImportByWildcardBeforeMicrosoftCommonProps)' == ''">true</ImportByWildcardBeforeMicrosoftCommonProps>
  <ImportDirectoryBuildProps
      Condition="'$(ImportDirectoryBuildProps)' == ''">true</ImportDirectoryBuildProps>
</PropertyGroup>
```

### Available control properties

| Property | What it disables |
|---|---|
| `ImportDirectoryBuildProps` | Directory.Build.props auto-discovery |
| `ImportDirectoryBuildTargets` | Directory.Build.targets auto-discovery |
| `ImportProjectExtensionProps` | NuGet-generated `*.props` in obj/ |
| `ImportProjectExtensionTargets` | NuGet-generated `*.targets` in obj/ |
| `ImportByWildcardBefore*` | Machine-level ImportBefore extensions |
| `ImportByWildcardAfter*` | Machine-level ImportAfter extensions |

## NuGet Package Build Extension Layout

NuGet packages inject build logic via `build/` or `buildTransitive/` folders:

```text
MyPackage/
  build/
    MyPackage.props      ← imported via *.props wildcard
    MyPackage.targets    ← imported via *.targets wildcard
  buildTransitive/
    MyPackage.props      ← imported by transitive consumers
    MyPackage.targets
```

### Rules

- File names **must match the package ID** exactly.
- `build/` affects direct consumers only. `buildTransitive/` affects the entire dependency chain.
- Props are imported early (before the project), targets are imported late (after the project).

## Source Tree vs Packed Layout

When reviewing a NuGet build-extension package, the **source layout** in the repository can legitimately differ from the **packed layout** inside the produced `.nupkg`. This is a common source of false-positive "import points at a missing file" findings.

Three packaging mechanisms reshape the layout at pack time:

1. **`.nuspec` `<file src=… target=…>` mappings** — copy a single source file into multiple per-TFM targets:

   ```xml
   <!-- Source tree has ONE shared file:
          buildTransitive\common\MyAdapter.props
        Pack rewrites it to per-TFM targets inside the .nupkg:
          buildTransitive\net462\MyAdapter.props
          buildTransitive\net8.0\MyAdapter.props
          buildTransitive\net9.0\MyAdapter.props -->
   <files>
     <file src="buildTransitive\common\MyAdapter.props" target="buildTransitive\net462\MyAdapter.props" />
     <file src="buildTransitive\common\MyAdapter.props" target="buildTransitive\net8.0\MyAdapter.props" />
     <file src="buildTransitive\common\MyAdapter.props" target="buildTransitive\net9.0\MyAdapter.props" />
   </files>
   ```

   In the `<file>` element, a `target` ending in `\` is treated as a folder (filename preserved from `src`); a `target` ending in a filename renames the file.

2. **`.csproj` `<PackagePath>` metadata** on `<None Update=…>` or `<Content Include=…>` items — same effect via SDK pack. Use one item per destination to keep the mapping unambiguous:

   ```xml
   <ItemGroup>
     <None Include="buildTransitive\common\MyAdapter.props" Pack="true" PackagePath="buildTransitive\net8.0\MyAdapter.props" />
     <None Include="buildTransitive\common\MyAdapter.props" Pack="true" PackagePath="buildTransitive\net9.0\MyAdapter.props" />
   </ItemGroup>
   ```

   NuGet/SDK pack also accepts a semicolon-separated list (`PackagePath="buildTransitive\net8.0\;buildTransitive\net9.0\"`) to fan one source out to multiple destinations, but the multi-item form above is harder to misread.

3. **SDK conventions** — `IncludeBuildOutput`, `BuildOutputTargetFolder`, `IncludeContentInPack` automatically place built outputs under `lib/<tfm>/` or `build/<tfm>/`.

### Implication for reviewers

A forwarder like the following inside a packed `build/net462/` folder is **not** a "missing-file" bug, even if the source tree has no `buildTransitive/net462/` directory:

```xml
<!-- In packed build/net462/MyAdapter.props -->
<Project>
  <Import Project="$(MSBuildThisFileDirectory)..\..\buildTransitive\net462\MyAdapter.props" />
</Project>
```

Before flagging an unguarded `<Import>` inside a `build/<tfm>/` or `buildTransitive/<tfm>/` folder:

1. Look for `*.nuspec` in the project directory and its immediate parent directory (do not walk further up). Read every `<file target=…>` whose `target` matches the imported path.
2. Read the `.csproj` for `<PackagePath>` metadata on `<None>`/`<Content>` items.
3. Only flag the import if the target path is missing from **both** the source tree *and* the projected package layout.

See also `msbuild-antipatterns` AP-13 ("NuGet package forwarders" exception).

## Import Guard Pattern

The `.targets` file ensures `.props` was imported using a guard property:

```xml
<!-- End of Microsoft.Common.props -->
<PropertyGroup>
  <MicrosoftCommonPropsHasBeenImported>true</MicrosoftCommonPropsHasBeenImported>
</PropertyGroup>

<!-- Top of Microsoft.Common.CurrentVersion.targets -->
<Import Project="Microsoft.Common.props"
    Condition="'$(MicrosoftCommonPropsHasBeenImported)' != 'true'" />
```

This handles projects that only import `.targets`.

## Directory.Build Discovery

MSBuild walks up the directory tree to find the nearest `Directory.Build.props`:

```xml
<_DirectoryBuildPropsBasePath>
  $([MSBuild]::GetDirectoryNameOfFileAbove('$(MSBuildProjectDirectory)', 'Directory.Build.props'))
</_DirectoryBuildPropsBasePath>
```

Only the **nearest** file is discovered. Nested hierarchies must explicitly import parents:

```xml
<!-- src/Directory.Build.props -->
<PropertyGroup>
  <_ParentPropsPath>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</_ParentPropsPath>
</PropertyGroup>
<Import Project="$(_ParentPropsPath)" Condition="'$(_ParentPropsPath)' != ''" />
```

## Creating Your Own Extension Point

```xml
<!-- MySDK.targets -->
<Project>
  <Import Project="MySDK.props" Condition="'$(MySDKPropsImported)' != 'true'" />

  <PropertyGroup>
    <CustomBeforeMySDK Condition="'$(CustomBeforeMySDK)' == ''">$(MSBuildProjectDirectory)\MySDK.Before.targets</CustomBeforeMySDK>
    <CustomAfterMySDK Condition="'$(CustomAfterMySDK)' == ''">$(MSBuildProjectDirectory)\MySDK.After.targets</CustomAfterMySDK>
  </PropertyGroup>

  <Import Project="$(CustomBeforeMySDK)" Condition="Exists('$(CustomBeforeMySDK)')" />

  <PropertyGroup>
    <MySDKBuildDependsOn>BeforeMySDKBuild;CoreMySDKBuild;AfterMySDKBuild</MySDKBuildDependsOn>
  </PropertyGroup>
  <Target Name="MySDKBuild" DependsOnTargets="$(MySDKBuildDependsOn)" />
  <Target Name="BeforeMySDKBuild" />
  <Target Name="AfterMySDKBuild" />
  <Target Name="CoreMySDKBuild">
    <!-- implementation -->
  </Target>

  <Import Project="$(CustomAfterMySDK)" Condition="Exists('$(CustomAfterMySDK)')" />
</Project>
```

## Common Pitfalls

- **Missing `Exists()` on optional imports** causes build failures when files are absent. **Exception**: imports inside published `build/<tfm>/` and `buildTransitive/<tfm>/` folders of a NuGet package are a package contract — the target is guaranteed by the packed layout (see "Source Tree vs Packed Layout" above). Don't guard them and don't flag them.
- **Overwriting Custom* properties** drops prior hooks. Append with `;` separator.
- **NuGet package file names not matching package ID** silently skips the import.
- **Nested Directory.Build.props** without parent import loses repo-root settings.
