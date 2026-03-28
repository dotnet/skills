# SDK and MSBuild Breaking Changes (.NET 11)

These changes affect the .NET SDK, CLI tooling, NuGet, and MSBuild behavior. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

> **Note:** .NET 11 is in preview. Additional SDK/MSBuild breaking changes are expected in later previews.

## Behavioral Changes

### Mono launch target not set for .NET Framework apps

**Impact: Low.** The mono launch target is no longer set automatically for .NET Framework apps. If you require Mono for execution on Linux, you need to specify it explicitly in the configuration.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/11/mono-launch-target-removed
