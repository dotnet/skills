# MSBuild extension points

Based on the original `extension-points` skill: import hooks, wildcard extensions, control
properties, NuGet build assets, import guards, and Directory.Build discovery.
Use it for a specific import/hook failure or requested authoring modernization.

## CustomBefore / CustomAfter hooks

Common MSBuild targets expose hooks such as:

```xml
<PropertyGroup>
  <CustomBeforeMicrosoftCommonTargets Condition="'$(CustomBeforeMicrosoftCommonTargets)' == ''">
    $(MSBuildExtensionsPath)\v$(MSBuildToolsVersion)\Custom.Before.Microsoft.Common.targets
  </CustomBeforeMicrosoftCommonTargets>
</PropertyGroup>

<Import Project="$(CustomBeforeMicrosoftCommonTargets)"
        Condition="'$(CustomBeforeMicrosoftCommonTargets)' != '' and Exists('$(CustomBeforeMicrosoftCommonTargets)')" />
<Import Project="$(CustomAfterMicrosoftCommonTargets)"
        Condition="'$(CustomAfterMicrosoftCommonTargets)' != '' and Exists('$(CustomAfterMicrosoftCommonTargets)')" />
```

Inspect the actual importing file and version; do not assume every targets file exposes the same
hooks. Preserve prior values when extending a supported import list:

```xml
<PropertyGroup>
  <CustomBeforeMicrosoftCommonTargets>$(CustomBeforeMicrosoftCommonTargets);$(MSBuildThisFileDirectory)MyExtension.targets</CustomBeforeMicrosoftCommonTargets>
</PropertyGroup>
```

MSBuild imports support semicolon-separated paths; an `Exists()` guard does not by itself make
such a list invalid. For a custom consumer that really accepts one file, use an aggregation file.
In either case, verify that both existing and new hooks run in the intended order.

Guard **optional** imports; required imports should fail explicitly when missing. Defaults that
refer to installed MSBuild extensions can include a toolset/version segment for side-by-side
installations. Do not overwrite a configured hook or disable all imports to hide one bad file.

For target execution rather than file imports, prefer uniquely named targets with
`BeforeTargets`/`AfterTargets`, or extend a documented `...DependsOn` property while preserving its
existing value. Define the list before a hook extends it, and check for dependency cycles.

## Wildcard import directories

Wildcard imports are sorted by filename:

```xml
<Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Imports\Microsoft.Common.props\ImportBefore\*"
        Condition="'$(ImportByWildcardBeforeMicrosoftCommonProps)' == 'true'
                   and Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Imports\Microsoft.Common.props\ImportBefore')" />
```

| Property | Typical location | Scope |
| --- | --- | --- |
| `MSBuildUserExtensionsPath` | Per-user MSBuild extension directory | User-specific |
| `MSBuildExtensionsPath` | MSBuild installation | Machine/toolset |
| `MSBuildProjectExtensionsPath` | Intermediate directory, normally `obj` | Project/NuGet |

Use names such as `01-defaults.props` and `02-overrides.props` when order is part of the contract.
Check the expanded imports. A machine- or user-wide extension affects more than the current repo.

## Import gating and control properties

Standard extension/discovery points use control properties with defaults such as:

```xml
<PropertyGroup>
  <ImportByWildcardBeforeMicrosoftCommonProps
      Condition="'$(ImportByWildcardBeforeMicrosoftCommonProps)' == ''">true</ImportByWildcardBeforeMicrosoftCommonProps>
  <ImportDirectoryBuildProps
      Condition="'$(ImportDirectoryBuildProps)' == ''">true</ImportDirectoryBuildProps>
</PropertyGroup>
```

| Property | Controlled behavior |
| --- | --- |
| `ImportDirectoryBuildProps` | Directory.Build.props discovery/import |
| `ImportDirectoryBuildTargets` | Directory.Build.targets discovery/import |
| `ImportProjectExtensionProps` | Generated project-extension props, including NuGet |
| `ImportProjectExtensionTargets` | Generated project-extension targets, including NuGet |
| `ImportByWildcardBefore*` | Matching ImportBefore extension point |
| `ImportByWildcardAfter*` | Matching ImportAfter extension point |

Set a control property before its consumer evaluates. These switches change build behavior:
disabling NuGet-generated imports or shared settings is not a general correctness/performance fix.

## NuGet package build extension layout

Automatically imported entry files use the package ID as their basename:

```text
MyPackage\
  build\
    MyPackage.props
    MyPackage.targets
  buildTransitive\
    MyPackage.props
    MyPackage.targets
```

- `build` serves direct consumers.
- `buildTransitive` can flow to transitive consumers with PackageReference.
- `buildMultiTargeting` serves the outer cross-targeting build, not ordinary per-TFM execution.
- Props are imported early and targets late. Framework-specific files can be placed in TFM
  subdirectories; NuGet selects compatible assets rather than necessarily an exact TFM match.
- A differently named entry file can cause `NU5129` and not be imported. Supporting files can
  have other names when an entry file explicitly imports them. Inspect the generated
  `*.nuget.g.props`/`*.nuget.g.targets` and restored assets to confirm discovery.

Package build files must not redefine restore inputs such as `TargetFramework`, `PackageReference`,
or `PackageVersion`. See the
[NuGet build-file conventions](https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets).

### Forwarding chain

When sharing implementation, keep a clear `buildTransitive -> build -> shared` chain instead of
duplicating direct/transitive logic or forwarding blindly to `buildMultiTargeting`.
For per-TFM layouts, include the matched asset folder segment. Derive it from the importing file,
not the consumer's `TargetFramework`: a `net10.0` project may consume the package's `net8.0` folder.

```xml
<!-- Inside packed buildTransitive/<tfm>/MyPackage.props. -->
<Import Project="$(MSBuildThisFileDirectory)..\..\build\$([System.IO.Path]::GetFileName($([System.IO.Path]::GetDirectoryName('$(MSBuildThisFileDirectory)'))))\MyPackage.props" />
```

Only use that path for that actual packed layout. Non-TFM-specific assets need a different
relative path, not an invented framework directory.

## Source tree vs packed layout

A source folder is not necessarily the layout of its `.nupkg`. Before reporting a missing
`build` or `buildTransitive` import:

1. Inspect every `.nuspec` in the project directory and its **immediate parent**; do not search
   unrelated ancestors. Resolve the relevant `<file src="..." target="...">` mappings.
2. Inspect the project's `Pack`/`PackagePath` item metadata and SDK packaging properties such as
   `BuildOutputTargetFolder` and `IncludeBuildOutput`.
3. Project the destination paths, including renamed files and per-TFM copies. Prefer inspecting
   the actual package when one is available.
4. Flag the import only if its target is absent from both the applicable source layout and the
   packed contract. A required file guaranteed by packaging must not gain an `Exists()` guard
   that would silently hide a broken package.

One shared source can be packed to multiple framework-specific destinations:

```xml
<files>
  <file src="buildTransitive\common\MyAdapter.props" target="buildTransitive\net462\MyAdapter.props" />
  <file src="buildTransitive\common\MyAdapter.props" target="buildTransitive\net8.0\MyAdapter.props" />
</files>
```

Equivalent SDK pack metadata can place the same source at each destination:

```xml
<ItemGroup>
  <None Include="buildTransitive\common\MyAdapter.props"
        Pack="true" PackagePath="buildTransitive\net462\MyAdapter.props" />
  <None Include="buildTransitive\common\MyAdapter.props"
        Pack="true" PackagePath="buildTransitive\net8.0\MyAdapter.props" />
</ItemGroup>
```

The absence of those TFM directories in source is not a missing-import defect. Preserve required
package contracts; do not add an existence guard to silently conceal a broken published layout.
See [msbuild-antipatterns](msbuild-antipatterns.md) AP-13.

## Import guard pattern

An imported file can record successful initialization:

```xml
<!-- At the end of the relevant props initialization. -->
<PropertyGroup>
  <MicrosoftCommonPropsHasBeenImported>true</MicrosoftCommonPropsHasBeenImported>
</PropertyGroup>

<!-- In the corresponding targets file. -->
<Import Project="Microsoft.Common.props"
        Condition="'$(MicrosoftCommonPropsHasBeenImported)' != 'true'" />
```

This pattern supports projects that enter through the targets file. Do not set another SDK's
guard to pretend its initialization happened; custom SDKs should use their own guard property.

## Directory.Build discovery

Only the nearest `Directory.Build.props` and nearest `Directory.Build.targets` are discovered
automatically. Nested files must explicitly import parents when inheritance is intended:

```xml
<Project>
  <PropertyGroup>
    <_ParentPropsPath>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)..\'))</_ParentPropsPath>
  </PropertyGroup>
  <Import Project="$(_ParentPropsPath)" Condition="'$(_ParentPropsPath)' != ''" />
</Project>
```

Start the search above the current file to avoid recursion. Whether an absent parent is optional
is a repository contract; fail if it is required. Defaults belong before their consumers, while
values depending on project-defined properties must be evaluated late enough to see them.
In particular, early property conditions and later item conditions differ; see AP-21 in
[additional anti-patterns](antipatterns/additional-antipatterns.md).

## Creating your own extension point

For a custom SDK whose required `MySDK.props` sets `MySDKPropsImported`, a targets file can expose
before/after imports and target hooks:

```xml
<Project>
  <Import Project="$(MSBuildThisFileDirectory)MySDK.props"
          Condition="'$(MySDKPropsImported)' != 'true'" />
  <PropertyGroup>
    <CustomBeforeMySDK Condition="'$(CustomBeforeMySDK)' == ''">$(MSBuildProjectDirectory)\MySDK.Before.targets</CustomBeforeMySDK>
    <CustomAfterMySDK Condition="'$(CustomAfterMySDK)' == ''">$(MSBuildProjectDirectory)\MySDK.After.targets</CustomAfterMySDK>
    <MySDKBuildDependsOn Condition="'$(MySDKBuildDependsOn)' == ''">BeforeMySDKBuild;CoreMySDKBuild;AfterMySDKBuild</MySDKBuildDependsOn>
  </PropertyGroup>
  <Import Project="$(CustomBeforeMySDK)" Condition="Exists('$(CustomBeforeMySDK)')" />
  <Target Name="MySDKBuild" DependsOnTargets="$(MySDKBuildDependsOn)" />
  <Target Name="BeforeMySDKBuild" />
  <Target Name="CoreMySDKBuild" />
  <Target Name="AfterMySDKBuild" />
  <Import Project="$(CustomAfterMySDK)" Condition="Exists('$(CustomAfterMySDK)')" />
</Project>
```

Initialize the dependency list **before** importing extensions so a before-hook's appended
targets are not overwritten afterward. An extension can preserve the list and add its own
target, or use a uniquely named `BeforeTargets`/`AfterTargets` hook. Test that prior extensions,
the core work, and the new work all remain scheduled.

## Verify the extension

Check the import graph (a preprocessed project can show import ordering), evaluated values, and
target execution with the original scenario. Preprocessing is not a substitute for evaluated
property/item values. Exercise both single-targeted and multi-targeted consumers when affected,
and direct/transitive consumers of package assets. Confirm pre-existing hooks still run, new hooks
run exactly where intended, and optional versus required missing files behave differently.
