# SDK and MSBuild Breaking Changes (.NET 10)

These changes affect the .NET SDK, CLI tooling, NuGet, and MSBuild behavior.

## Source-Incompatible Changes

### dnx.ps1 file removed from .NET SDK

The legacy `dnx.ps1` file is no longer included. Remove any references to it.

### Double quotes in file-level directives are disallowed

File-level directives (e.g., `#r`, `#load`) no longer accept double-quoted paths. Use single quotes or unquoted paths.

### project.json not supported in `dotnet restore`

`project.json` is no longer recognized. Migrate to `PackageReference` format.

### NU1510 raised for direct references pruned by NuGet

If NuGet prunes a direct `PackageReference` because the package is already part of the shared framework, a `NU1510` warning is raised. Remove the explicit reference if it's provided by the framework.

### NuGet packages with no runtime assets aren't included in deps.json

Packages that contribute no runtime assets are now excluded from the generated `deps.json`. This is source-incompatible if your code manually reads `deps.json` or uses `DependencyContext` to find these packages.

### `ToolCommandName` not set for non-tool packages

The `ToolCommandName` MSBuild property is no longer set for packages that are not .NET tools. If your build scripts rely on this property, update them.

### HTTP warnings promoted to errors in `dotnet package list` and `dotnet package search`

HTTP (non-HTTPS) package sources now cause errors instead of warnings. Update NuGet sources to HTTPS.

## Behavioral Changes

### `dotnet new sln` defaults to SLNX file format

`dotnet new sln` now generates an XML-based `.slnx` file instead of the classic `.sln` format:
```xml
<Solution>
</Solution>
```
**Mitigation:** Use `dotnet new sln --format sln` for the classic format. Ensure your tooling (Visual Studio, Rider, etc.) supports SLNX.

### .NET CLI `--interactive` defaults to `true` in user scenarios

The `--interactive` flag now defaults to `true` when running in user-interactive scenarios. This may cause unexpected prompts in scripts. Use `--interactive false` to opt out.

### `dotnet` CLI commands log non-command-relevant data to stderr

Diagnostic and telemetry output now goes to stderr instead of stdout. Scripts that parse stdout output should be unaffected, but scripts that capture stderr may see new output.

### .NET tool packaging creates RuntimeIdentifier-specific tool packages

Tool packages now include RID-specific content. This may affect tool package size and restore behavior.

### Default workload configuration switched to 'workload sets' mode

The default workload management mode is now 'workload sets' instead of 'loose manifests'. This affects how `dotnet workload` commands resolve and install workloads.

### Code coverage EnableDynamicNativeInstrumentation defaults to false

Dynamic native code instrumentation is disabled by default for code coverage. Set `EnableDynamicNativeInstrumentation` to `true` if needed.

### `dotnet package list` performs restore

`dotnet package list` now performs a restore before listing packages. This ensures accurate results but may slow down the command.

### `dotnet restore` audits transitive packages

NuGet now audits transitive packages for known vulnerabilities during restore. This may surface new warnings for indirect dependencies.

### `dotnet tool install --local` creates manifest by default

Installing a local tool now creates a tool manifest (`dotnet-tools.json`) if one doesn't exist.

### `dotnet watch` logs to stderr instead of stdout

Watch output now goes to stderr. Scripts that parse `dotnet watch` stdout should be unaffected.

### PackageReference without a version raises an error

Every `<PackageReference>` must now have a `Version` attribute or use Central Package Management. Missing versions raise an error instead of resolving to the latest.

### PrunePackageReference privatizes direct prunable references

Prunable direct package references are now treated as `PrivateAssets="All"` automatically.

### SHA-1 fingerprint support deprecated in `dotnet nuget sign`

SHA-1 fingerprints are deprecated for NuGet signing. Use SHA-256 or stronger.

### MSBUILDCUSTOMBUILDEVENTWARNING escape hatch removed

The `MSBUILDCUSTOMBUILDEVENTWARNING` environment variable is no longer recognized.

### MSBuild custom culture resource handling

MSBuild now handles custom culture resources differently. Review if you use custom cultures in resource satellite assemblies.

### NUGET_ENABLE_ENHANCED_HTTP_RETRY environment variable removed

The `NUGET_ENABLE_ENHANCED_HTTP_RETRY` env var is no longer recognized. Enhanced HTTP retry is now always enabled.

### NuGet logs an error for invalid package IDs

Package IDs that don't conform to NuGet naming rules now produce errors.
