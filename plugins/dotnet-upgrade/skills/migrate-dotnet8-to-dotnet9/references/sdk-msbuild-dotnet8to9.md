# SDK and MSBuild Breaking Changes (.NET 9)

These changes affect the .NET SDK, CLI tooling, NuGet, and MSBuild behavior.

## Source-Incompatible Changes

### Version requirements for .NET 9 SDK

The .NET 9 SDK has updated minimum Visual Studio and MSBuild version requirements:

- With VS 17.11, the .NET 9.0.100 SDK can only target `net8.0` and earlier frameworks
- VS 17.12 or later is required to target `net9.0`

Attempting to target `net9.0` in VS 17.11 produces: `NETSDK1223: Targeting .NET 9.0 or higher in Visual Studio 2022 17.11 is not supported.`

### Warning emitted for .NET Standard 1.x target

Projects targeting .NET Standard 1.x now produce a build warning encouraging migration to a newer target.

### Warning emitted for .NET 7 target

Projects targeting `net7.0` now produce a build warning because .NET 7 is out of support. Update to `net8.0` or `net9.0`.

### New default RID used when targeting .NET Framework

A new default Runtime Identifier (RID) is used when targeting .NET Framework, which may affect build output paths.

## Behavioral Changes

### Terminal Logger is default

**Impact: Medium for CI scripts.** `dotnet build` and other build-related CLI commands now use Terminal Logger by default for interactive terminal sessions. Terminal Logger formats output differently from the console logger.

**Mitigation:**
- Per-command: `--tl:off`
- Global: set `MSBUILDTERMINALLOGGER=off` environment variable

### `dotnet workload` commands output change

`dotnet workload` commands have changed their output format. Scripts that parse workload command output may need updating.

### `dotnet sln add` doesn't allow invalid file names

`dotnet sln add` now validates file names more strictly.

### `dotnet watch` incompatible with Hot Reload for old frameworks

`dotnet watch` in .NET 9 SDK is not compatible with Hot Reload when targeting older frameworks. Use the SDK matching the target framework for `dotnet watch` scenarios.

### `installer` repo version no longer documented

The `installer` repository version is no longer included in `productcommits` information.

### MSBuild custom culture resource handling

MSBuild custom culture resource handling has changed (also applies to .NET 9 SDK 9.0.200/9.0.300).

### Other behavioral changes

- `--interactive` may have different defaults in some scenarios
- Default workload management may behave differently
