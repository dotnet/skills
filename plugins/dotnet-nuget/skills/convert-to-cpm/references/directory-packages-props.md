# Directory.Packages.props Creation

## Placement

- **Repository scope**: First group projects by the central version policy they must share. If all in-scope projects share one policy, place one file at their first common ancestor. If independent solutions or existing nearest-file boundaries require separate policies, place one file at each group's first common ancestor. This may produce one or more files, and none must be at the repository root.
- **Solution scope**: Place at the first common ancestor of all governed projects, while respecting existing nearest-file boundaries. This is the solution directory only when it is an ancestor of every governed project.
- **Single project scope**: Default to the project directory. If the project is inside a repository with other projects that may be converted later, ask the user where to place it.

Only the nearest `Directory.Packages.props` is evaluated per project. CPM also supports `Directory.Packages.props` in sub-folders — for example, test projects may have different dependencies than source code and can use a separate `Directory.Packages.props` in their sub-folder. A `Directory.Packages.props` in a sub-folder does not implicitly override or extend a parent file; it is independent and replaces the parent for projects in that folder. To share settings, explicitly chain files using MSBuild `<Import>` elements. See [Central Package Management rules](https://github.com/NuGet/docs.microsoft.com-nuget/blob/main/docs/consume-packages/Central-Package-Management.md#central-package-management-rules) for how NuGet resolves which file applies. When in doubt about placement, ask the user.

CLI targets and CPM management scopes are different concepts. Multiple solution or project targets can use one common `Directory.Packages.props`, while one repository conversion can require separate files for independent project groups. Compute placement from the projects that share policy, not from the number or location of solution files.

## Creating the file

Create the file directly so the workflow does not depend on whether the installed SDK includes the `packagesprops` template:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- PackageVersion items will be added here -->
  </ItemGroup>
</Project>
```

## Adding PackageVersion entries

Add a `<PackageVersion>` entry for each unique package, using the resolved version from the audit. Sort entries alphabetically by package ID:

```xml
<PackageVersion Include="PackageA" Version="1.2.3" />
<PackageVersion Include="PackageB" Version="4.5.6" />
```

## Conditional versions

If the same package needs different versions for different target frameworks, use MSBuild conditions:

```xml
<PackageVersion Include="PackageA" Version="1.0.0" Condition="'$(TargetFramework)' == 'netstandard2.0'" />
<PackageVersion Include="PackageA" Version="2.0.0" Condition="'$(TargetFramework)' == 'net8.0'" />
```

Preserve an existing target-framework-specific version split when a single version is incompatible. Ask only when multiple valid policies remain and the user has not already supplied a strategy. Record the preserved condition in the report.

## VersionOverride

If a project intentionally needs a different version than the centrally defined one, use `VersionOverride` in the project file instead of removing the `Version` attribute:

```xml
<PackageReference Include="System.Text.Json" VersionOverride="9.0.0" />
```

Apply `VersionOverride` only when the user's chosen strategy requires it. If no strategy was supplied, ask before applying it; in most cases, version alignment is preferred.
