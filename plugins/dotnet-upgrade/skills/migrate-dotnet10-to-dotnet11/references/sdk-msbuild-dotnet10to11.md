# SDK and MSBuild Breaking Changes (.NET 11)

These changes affect the .NET SDK, CLI tooling, NuGet, and MSBuild behavior. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

> **Note:** .NET 11 is in preview. Additional SDK/MSBuild breaking changes are expected in later previews.

## Behavioral Changes

### Mono launch target not set for .NET Framework apps

**Impact: Low.** The mono launch target is no longer set automatically for .NET Framework apps. If you require Mono for execution on Linux, you need to specify it explicitly in the configuration.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/11/mono-launch-target-removed

### NETSDK1235 warning for PackAsTool with custom .nuspec (Preview 2)

**Impact: Low.** A new build warning `NETSDK1235` is emitted when a project has both `PackAsTool=true` and a custom `NuspecFile` property, which violates .NET Tool packaging requirements. Projects with `TreatWarningsAsErrors=true` will fail.

**Fix:** Remove the custom `NuspecFile` property when packaging as a .NET Tool, or suppress the warning if the .nuspec is compatible.

Source: https://github.com/dotnet/sdk/pull/52810

### `dotnet publish --self-contained` now parses the passed value (Preview 3)

**Impact: Low.** `dotnet publish --self-contained` previously always interpreted the flag as `true` regardless of the passed value. It now correctly parses the value (e.g., `--self-contained false` actually produces a framework-dependent publish).

**Fix:** Review build scripts that pass `--self-contained` to ensure the intended value is correct.

Source: https://github.com/dotnet/sdk/pull/52333
