# SDK and MSBuild Breaking Changes (.NET 11)

These changes affect the .NET SDK, CLI tooling, NuGet, and MSBuild behavior. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

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

### Restore doesn't search the global packages folder for a higher version (RC 1)

**Impact: Low–Medium.** When an exact package version isn't available, restore no longer picks the lowest higher version found in the global packages folder. It only searches the configured package sources, so restore may choose a different higher version (NU1603) or fail with NU1102. An exact version already in the global packages folder is still used.

**Fix:** Reference package versions that exist on your configured package sources.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/11/restore-global-packages-folder-version-search

### VSTest removes its dependency on Newtonsoft.Json (Preview 4)

**Impact: Medium (for test projects).** `Microsoft.NET.Test.Sdk` no longer brings in `Newtonsoft.Json` transitively. Test code that uses it without a direct reference fails to compile, and tests or test extensions that relied on VSTest's copy can fail at run time with `FileNotFoundException`.

**Fix:** Add an explicit `PackageReference` to `Newtonsoft.Json` in test projects that use it, and remove any `ExcludeAssets="runtime"` restriction on it.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/11/vstest-removes-newtonsoft-json

### dnx scripts bypass global.json SDK selection (Preview 6)

**Impact: Low.** The `dnx` script now selects the newest installed SDK directly instead of going through the `dotnet` muxer, so a `global.json` SDK pin no longer applies to it. This also ships in .NET 10 SDK 10.0.302 and 10.0.400.

**Fix:** Run `dotnet dnx` explicitly where the `global.json` SDK selection must apply.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/11/dnx-scripts-bypass-global-json
