# Directory.Packages.props Creation

## Placement

- **Repository scope**: Place at the first common ancestor directory of all in-scope .NET projects. This may not be the repository root — many repos nest source code under `src/` or similar directories.
- **Solution scope**: Place in the solution directory.
- **Single project scope**: Default to the project directory. If the project is inside a repository with other projects that may be converted later, ask the user where to place it.

Only the nearest `Directory.Packages.props` is evaluated per project. CPM also supports `Directory.Packages.props` in sub-folders — for example, test projects may have different dependencies than source code and can use a separate `Directory.Packages.props` in their sub-folder. A `Directory.Packages.props` in a sub-folder does not implicitly override or extend a parent file; it is independent and replaces the parent for projects in that folder. To share settings, explicitly chain files using MSBuild `<Import>` elements. See [Central Package Management rules](https://github.com/NuGet/docs.microsoft.com-nuget/blob/main/docs/consume-packages/Central-Package-Management.md#central-package-management-rules) for how NuGet resolves which file applies. When in doubt about placement, ask the user.

## Creating the file

Use the .NET CLI (available in .NET 8+):

```bash
dotnet new packagesprops
```

This generates a `Directory.Packages.props` with `ManagePackageVersionsCentrally` set to `true`. If the CLI template is not available, create the file manually:

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
<PackageVersion Include="Microsoft.Extensions.Logging" Version="9.0.0" />
<PackageVersion Include="System.Text.Json" Version="10.0.1" />
```

## Conditional versions

If the same package needs different versions for different target frameworks, use MSBuild conditions:

```xml
<PackageVersion Include="PackageA" Version="1.0.0" Condition="'$(TargetFramework)' == 'netstandard2.0'" />
<PackageVersion Include="PackageA" Version="2.0.0" Condition="'$(TargetFramework)' == 'net8.0'" />
```

Ask the user before using conditional versions — it may be preferable to standardize on a single version.

## VersionOverride

If a project intentionally needs a different version than the centrally defined one, use `VersionOverride` in the project file instead of removing the `Version` attribute:

```xml
<PackageReference Include="System.Text.Json" VersionOverride="9.0.0" />
```

Ask the user before applying `VersionOverride` — in most cases, version alignment is preferred.
