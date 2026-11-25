# SDK and MSBuild Breaking Changes (.NET 10)

These changes affect the .NET SDK, CLI tooling, NuGet, and MSBuild behavior.

## Source-Incompatible Changes

### NU1510 raised for direct references pruned by NuGet

If NuGet prunes a direct `PackageReference` because the package is already part of the shared framework, a `NU1510` warning is raised. Remove the explicit reference if it's provided by the framework.

### PackageReference without a version raises an error

Every `<PackageReference>` must now have a `Version` attribute or use Central Package Management. Missing versions raise an error instead of resolving to the latest.

### Other source-incompatible changes

- `dnx.ps1` file removed from .NET SDK — remove any references
- File-level directives (`#r`, `#load`) no longer accept double-quoted paths — remove the quotes (e.g., `#r MyLib.dll` instead of `#r "MyLib.dll"`)
- `project.json` no longer recognized by `dotnet restore` — use `PackageReference` format
- NuGet packages with no runtime assets excluded from `deps.json`
- `ToolCommandName` not set for non-tool packages
- HTTP sources in `dotnet package list`/`dotnet package search` now error (use HTTPS)

## Behavioral Changes

### `dotnet new sln` defaults to SLNX file format

`dotnet new sln` now generates an XML-based `.slnx` file instead of the classic `.sln` format:
```xml
<Solution>
</Solution>
```
**Mitigation:** Use `dotnet new sln --format sln` for the classic format. Ensure your tooling (Visual Studio, Rider, etc.) supports SLNX.

## IDE / Visual Studio compatibility

To target `net10.0` with official Visual Studio support, use Visual Studio 2026 (18.0+) or a later release. Microsoft's [SDK, MSBuild, and Visual Studio compatibility table](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs) lists .NET SDK 10.0.100 as loadable starting with Visual Studio 17.14, but that version of Visual Studio supports targeting only through `net9.0`; targeting `net10.0` is officially supported in Visual Studio 18.0+ only. Using a newer SDK to target a newer framework in an older Visual Studio version is unsupported and produces a build warning. See the [.NET 10 release notes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview) and [Visual Studio 2026 release notes](https://learn.microsoft.com/en-us/visualstudio/releases/2026/release-notes).

Upgrade Visual Studio to 2026 (18.0+) for supported IDE builds. To build from the command line instead, install the .NET 10 SDK, pin the installed version in `global.json`, and use `dotnet build`; this does not add .NET 10 support to an older Visual Studio IDE.

The [SLNX compatibility guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default) describes SLNX as well-supported by major .NET tooling but does not specify minimum IDE versions. If an IDE's SLNX support is uncertain, use `dotnet new sln --format sln` to create the classic `.sln` format.

### Other behavioral changes

- `--interactive` defaults to `true` in user scenarios — use `--interactive false` in scripts
- CLI diagnostic output now goes to stderr
- Tool packages now include RID-specific content
- Default workload management mode is 'workload sets' (not 'loose manifests')
- `EnableDynamicNativeInstrumentation` defaults to false for code coverage
- `dotnet package list` performs restore before listing
- `dotnet tool install --local` creates manifest by default
- `dotnet watch` logs to stderr instead of stdout
- `PrunePackageReference` privatizes direct prunable references (`PrivateAssets="All"`)
- SHA-1 fingerprints deprecated in `dotnet nuget sign` — use SHA-256
- `MSBUILDCUSTOMBUILDEVENTWARNING` escape hatch removed
- MSBuild custom culture resource handling changed
- `NUGET_ENABLE_ENHANCED_HTTP_RETRY` env var removed (enhanced retry always on)
- NuGet logs errors for invalid package IDs
