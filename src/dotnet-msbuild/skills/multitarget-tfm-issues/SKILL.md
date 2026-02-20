---
name: multitarget-tfm-issues
description: "Diagnose and fix target framework (TFM) and multi-targeting build failures in .NET projects. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when builds fail due to TFM incompatibility, conditional compilation issues, platform-specific API usage, or RID-specific errors. Covers TFM compatibility matrix, multi-targeting patterns, #if directives, and platform abstractions. DO NOT use for non-.NET framework issues."
---

# Diagnosing and Fixing TFM and Multi-targeting Build Failures

## Target Framework Moniker (TFM) Reference

### Current .NET (net5.0+)

| TFM | Status |
|---|---|
| `net9.0` | Current (LTS) |
| `net8.0` | Supported (LTS) |
| `net7.0` | End-of-life |
| `net6.0` | End-of-life |
| `net5.0` | End-of-life |

### .NET Standard

| TFM | Status |
|---|---|
| `netstandard2.1` | Supported (no new versions planned) |
| `netstandard2.0` | Supported (maximum compatibility) |

### .NET Framework (Windows-only)

| TFM | Status |
|---|---|
| `net48` / `net481` | Supported (latest .NET Framework) |
| `net472` | Supported |
| `net471` | Supported |
| `net462` | Supported |
| `net461` | End-of-life but still commonly targeted |

### Platform-specific TFMs

Platform-specific TFMs append a platform suffix to a base TFM:

```
net8.0-windows
net8.0-windows10.0.19041.0
net8.0-ios
net8.0-android
net8.0-macos
net8.0-maccatalyst
net8.0-tizen
net8.0-browser
```

These require corresponding workloads installed (`dotnet workload install ios`, etc.).

## TFM Compatibility Matrix

Libraries built for a given TFM can be consumed by projects targeting compatible TFMs:

| Library targets | Can be consumed by |
|---|---|
| `netstandard2.0` | `net6.0+`, `net461+`, `net48`, Xamarin, Unity, UWP |
| `netstandard2.1` | `net6.0+`, Xamarin (NOT `net48` or any netfx!) |
| `net6.0` | `net6.0`, `net7.0`, `net8.0`, `net9.0` |
| `net8.0` | `net8.0`, `net9.0` |
| `net48` | `net48`, `net481` only |

**Key rules:**
- `netstandard2.0` provides the widest reach — use it when your library must work everywhere.
- `netstandard2.1` excludes all .NET Framework consumers — this catches many developers off guard.
- `net6.0+` libraries **cannot** be consumed by `net48` or `netstandard` projects.
- A project-to-project reference from `net48` → `net8.0` will fail with NU1201.

### Compatibility shims and NU1701

When NuGet resolves a package that doesn't have an exact TFM match, it may fall back with warning **NU1701**:

```
warning NU1701: Package 'SomePackage 1.0.0' was restored using '.NETFramework,Version=v4.6.1'
instead of the project target framework '.NETStandard,Version=v2.0'. This package may not be
fully compatible with your project.
```

Suppress selectively only when you have verified compatibility:

```xml
<PackageReference Include="SomePackage" Version="1.0.0" NoWarn="NU1701" />
```

### When to target netstandard2.0

Target `netstandard2.0` when:
- Your library has no dependency on APIs added after .NET Standard 2.0
- You need to support .NET Framework 4.6.1+ consumers
- You are publishing a NuGet package with maximum reach

Multi-target with `netstandard2.0` + a modern TFM to light up newer APIs:

```xml
<TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>
```

## Multi-targeting Setup

### Basic Configuration

Use the **plural** `<TargetFrameworks>` element (with an `s`) to multi-target:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net472;netstandard2.0</TargetFrameworks>
  </PropertyGroup>
</Project>
```

**Common mistake:** Using singular `<TargetFramework>` when you intend to multi-target. The singular form accepts only one TFM. The plural form accepts a semicolon-delimited list. If both are present, `<TargetFrameworks>` wins but it is confusing — remove the singular one.

### Conditional PropertyGroups

Use `Condition` attributes to set properties per TFM:

```xml
<PropertyGroup Condition="'$(TargetFramework)' == 'net472'">
  <DefineConstants>$(DefineConstants);LEGACY_NETFX</DefineConstants>
  <PlatformTarget>x86</PlatformTarget>
</PropertyGroup>

<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

> ⚠️ **Evaluation order caveat:** PropertyGroup conditions on `$(TargetFramework)` are **only safe in project files and `.targets` files**. In `.props` files, `$(TargetFramework)` is only available for multi-targeting projects. For single-targeting projects, the property is empty during `.props` evaluation. See [`shared/targetframework-props-evaluation.md`](../shared/targetframework-props-evaluation.md) for the full explanation. ItemGroup and Target conditions are not affected.

### Conditional ItemGroups

Add TFM-specific package references and files:

```xml
<!-- Only needed on netstandard2.0 / netfx -->
<ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0' Or '$(TargetFramework)' == 'net472'">
  <PackageReference Include="System.Text.Json" Version="8.0.5" />
  <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="8.0.0" />
  <PackageReference Include="System.Threading.Tasks.Extensions" Version="4.5.4" />
</ItemGroup>

<!-- Platform-specific references -->
<ItemGroup Condition="$(TargetFramework.StartsWith('net8.0'))">
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
</ItemGroup>
```

You can also use TFM-specific source files via folder convention. Files in a folder named after the TFM are automatically included only for that TFM when using `<EnableDefaultCompileItems>true</EnableDefaultCompileItems>` (the default).

### Conditional PackageReferences with MSBuild functions

Use MSBuild property functions for version-range conditions:

```xml
<ItemGroup Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net6.0'))">
  <PackageReference Include="SomeModernPackage" Version="2.0.0" />
</ItemGroup>

<ItemGroup Condition="!$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net6.0'))">
  <PackageReference Include="SomeLegacyPackage" Version="1.0.0" />
</ItemGroup>
```

## Conditional Compilation

### Built-in Preprocessor Symbols

The SDK automatically defines preprocessor symbols based on the target framework. These follow a naming convention:

| TFM | Exact symbol | "Or greater" symbol |
|---|---|---|
| `net8.0` | `NET8_0` | `NET8_0_OR_GREATER` |
| `net7.0` | `NET7_0` | `NET7_0_OR_GREATER` |
| `net6.0` | `NET6_0` | `NET6_0_OR_GREATER` |
| `netstandard2.0` | `NETSTANDARD2_0` | `NETSTANDARD2_0_OR_GREATER` |
| `netstandard2.1` | `NETSTANDARD2_1` | `NETSTANDARD2_1_OR_GREATER` |
| `net472` | `NET472` | `NET472_OR_GREATER` |
| `net48` | `NET48` | `NET48_OR_GREATER` |

Umbrella symbols are also defined:

| Symbol | Meaning |
|---|---|
| `NETFRAMEWORK` | Any .NET Framework TFM |
| `NETSTANDARD` | Any .NET Standard TFM |
| `NET` | Any net5.0+ TFM (NOT netfx, NOT netstandard) |

### Platform-specific Symbols

When using platform-specific TFMs like `net8.0-windows`:

| Symbol | Defined when |
|---|---|
| `WINDOWS` | `net8.0-windows` or similar |
| `IOS` | `net8.0-ios` |
| `ANDROID` | `net8.0-android` |
| `BROWSER` | `net8.0-browser` |
| `MACCATALYST` | `net8.0-maccatalyst` |

### Usage Patterns

```csharp
// Polyfill a type that only exists in modern .NET
#if NETSTANDARD2_0 || NETFRAMEWORK
using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif

// Use a newer API when available, fall back otherwise
public string GetData()
{
#if NET8_0_OR_GREATER
    return JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
#elif NET6_0_OR_GREATER
    return JsonSerializer.Serialize(value);
#else
    return Newtonsoft.Json.JsonConvert.SerializeObject(value);
#endif
}

// Platform-specific code
#if WINDOWS
    Registry.SetValue(@"HKEY_CURRENT_USER\Software\MyApp", "Setting", value);
#elif IOS
    NSUserDefaults.StandardUserDefaults.SetString(value, "Setting");
#else
    File.WriteAllText(settingsPath, value);
#endif
```

### Custom DefineConstants per TFM

Add your own symbols in the project file or `Directory.Build.targets`:

```xml
<!-- In a project file or Directory.Build.targets (NOT Directory.Build.props — see note below) -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net472'">
  <DefineConstants>$(DefineConstants);USE_NEWTONSOFT;LEGACY_SUPPORT</DefineConstants>
</PropertyGroup>
```

> ⚠️ **Do not place TFM-conditional DefineConstants in `.props` files.** `$(TargetFramework)` is not available during `.props` evaluation for single-targeting projects. Use `.targets` or the project file itself.

**Common pitfall:** Using the wrong symbol name. Preprocessor symbols use underscores, not dots or hyphens:
- ✅ `NET8_0_OR_GREATER`
- ❌ `NET8.0_OR_GREATER`
- ❌ `NET80_OR_GREATER`
- ❌ `NET8_0-OR-GREATER`

**Common pitfall:** Forgetting to append to existing constants. Always use `$(DefineConstants);YOUR_SYMBOL`, not just `YOUR_SYMBOL`, or you will lose SDK-defined symbols.

## Platform-specific API Usage

### The Platform Compatibility Analyzer (CA1416)

When you call a platform-specific API from cross-platform code, the analyzer produces **CA1416**:

```
warning CA1416: 'Registry.SetValue(string, string, object)' is supported on 'windows'
```

### Suppression Strategies

**1. Runtime guard (preferred):**

```csharp
if (OperatingSystem.IsWindows())
{
    Registry.SetValue(@"HKEY_CURRENT_USER\Software\MyApp", "Key", value);
}
```

Available guards: `OperatingSystem.IsWindows()`, `IsLinux()`, `IsMacOS()`, `IsIOS()`, `IsAndroid()`, `IsBrowser()`, etc.

**2. Mark the calling method as platform-specific:**

```csharp
[SupportedOSPlatform("windows")]
public void SaveToRegistry(string key, string value)
{
    Registry.SetValue(@"HKEY_CURRENT_USER\Software\MyApp", key, value);
}
```

**3. Conditional compilation:**

```csharp
#if WINDOWS
    Registry.SetValue(@"HKEY_CURRENT_USER\Software\MyApp", key, value);
#else
    throw new PlatformNotSupportedException("Registry is only available on Windows.");
#endif
```

### Polyfill Packages for Older TFMs

When multi-targeting to `netstandard2.0` or `net472`, many modern APIs are missing. Use polyfill packages:

| Missing API / Feature | Polyfill Package |
|---|---|
| `System.Text.Json` | `System.Text.Json` |
| `IAsyncEnumerable<T>` | `Microsoft.Bcl.AsyncInterfaces` |
| `ValueTask`, `ValueTask<T>` | `System.Threading.Tasks.Extensions` |
| `Span<T>`, `Memory<T>` | `System.Memory` |
| `ImmutableArray<T>` etc. | `System.Collections.Immutable` |
| `HttpClient` improvements | `System.Net.Http` |
| `[NotNull]`, `[MaybeNull]` | `System.Diagnostics.DiagnosticSource` or manual polyfill |
| `Index` / `Range` | `Microsoft.Bcl.HashCode` + manual polyfill |
| `IsExternalInit` (for `init`) | Manual polyfill (internal class) |

Add polyfills conditionally:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
  <PackageReference Include="System.Text.Json" Version="8.0.5" />
  <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="8.0.0" />
  <PackageReference Include="System.Memory" Version="4.5.5" />
</ItemGroup>
```

## Common Multi-targeting Errors

### "Type X is not available" / CS0246 on a specific TFM

**Symptom:** Build succeeds for `net8.0` but fails for `netstandard2.0` with CS0234 or CS0246.

**Diagnosis:**
1. Identify which TFM is failing from the build output — look for the `TargetFramework` in the project context.
2. Check if the type exists in that TFM's BCL or if a polyfill package is needed.
3. Check if a `PackageReference` is missing a TFM condition.

**Fix — add a polyfill package conditionally:**

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
  <PackageReference Include="System.Text.Json" Version="8.0.5" />
</ItemGroup>
```

**Fix — use conditional compilation to exclude code:**

```csharp
#if NET6_0_OR_GREATER
public async IAsyncEnumerable<int> StreamDataAsync() { /* ... */ }
#endif
```

### CS0234 / CS0246 only on specific TFM — missing conditional PackageReference

**Symptom:** `error CS0234: The type or namespace name 'X' does not exist in the namespace 'Y'` but only when building for one TFM.

**Root cause:** A `PackageReference` needed for that TFM is missing or has the wrong condition.

**Diagnosis:** Compare the `<ItemGroup>` conditions in the project file. Ensure the package is included for all TFMs that need it.

### MSB3644: Reference assemblies not found

```
error MSB3644: The reference assemblies for .NETFramework,Version=v4.7.2 were not found.
```

**Cause:** The targeting pack for that .NET Framework version is not installed on the build machine.

**Fix:**
- Install the .NET Framework Developer Pack for the required version.
- Or install the `Microsoft.NETFramework.ReferenceAssemblies` NuGet package (enables building netfx targets on non-Windows / without installed targeting packs):

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
</ItemGroup>
```

### NU1201: Project is not compatible

```
error NU1201: Project MyLibrary is not compatible with netstandard2.0 (.NETStandard,Version=v2.0).
Project MyLibrary supports: net8.0 (.NETCoreApp,Version=v8.0)
```

**Cause:** A project-to-project reference targets a TFM that is not compatible with the consuming project's TFM.

**Fix:**
- Add `netstandard2.0` to the referenced library's `<TargetFrameworks>`.
- Or change the consuming project to target a compatible TFM.

### Build succeeds for one TFM, fails for another

**Diagnosis steps:**
1. Build with `/p:TargetFramework=<failing-tfm>` to isolate:
   ```
   dotnet build /p:TargetFramework=netstandard2.0
   ```
2. Check the errors — are they missing types, missing packages, or API incompatibility?
3. Apply conditional compilation or conditional package references as needed.
4. Use binary log analysis to compare evaluations across TFMs.

## RID-specific Issues

### Runtime Identifiers

RIDs identify the target platform for published applications:

| RID | Platform |
|---|---|
| `win-x64` | Windows 64-bit |
| `win-x86` | Windows 32-bit |
| `win-arm64` | Windows ARM64 |
| `linux-x64` | Linux 64-bit |
| `linux-arm64` | Linux ARM64 |
| `linux-musl-x64` | Alpine Linux |
| `osx-x64` | macOS Intel |
| `osx-arm64` | macOS Apple Silicon |

### Singular vs Plural

```xml
<!-- Single RID for publish -->
<RuntimeIdentifier>win-x64</RuntimeIdentifier>

<!-- Multiple RIDs — builds for each (slow, large output) -->
<RuntimeIdentifiers>win-x64;linux-x64;osx-arm64</RuntimeIdentifiers>
```

**Common mistake:** Using `<RuntimeIdentifiers>` (plural) when you only want one — this produces multiple outputs and slows the build.

**Common mistake:** Setting `<RuntimeIdentifier>` in the project file permanently. Prefer passing it at publish time:

```
dotnet publish -r win-x64
```

### RID Graph and Fallback

RIDs form a hierarchy. If a NuGet package doesn't have assets for `linux-arm64`, NuGet falls back through: `linux-arm64` → `linux` → `unix` → `any`.

Starting with .NET 8, the RID graph is simplified. Use `UseRidGraph=true` to opt into the legacy full graph if needed:

```xml
<PropertyGroup>
  <UseRidGraph>true</UseRidGraph>
</PropertyGroup>
```

### Self-contained vs Framework-dependent

| Mode | RID required? | Output |
|---|---|---|
| Framework-dependent (default) | No | Small app, requires .NET runtime on host |
| Self-contained | Yes | Large app, bundles the runtime |

```
dotnet publish -c Release --self-contained -r linux-x64
dotnet publish -c Release --no-self-contained
```

Setting `<SelfContained>true</SelfContained>` in the project file **requires** a `<RuntimeIdentifier>` or the build will fail.

### Native Dependency Issues per RID

Native libraries (`.dll`, `.so`, `.dylib`) must match the RID. Use `runtimes/` folder convention in NuGet packages:

```
runtimes/
  win-x64/native/mylib.dll
  linux-x64/native/libmylib.so
  osx-arm64/native/libmylib.dylib
```

In a project, include native assets conditionally:

```xml
<ItemGroup Condition="'$(RuntimeIdentifier)' == 'win-x64'">
  <None Include="native\win-x64\mylib.dll" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

### NETSDK1083: Unrecognized RuntimeIdentifier

```
error NETSDK1083: The specified RuntimeIdentifier 'win10-x64' is not recognized.
```

**Cause:** The RID is not in the known RID catalog. In .NET 8+ the RID graph was simplified and many old RIDs like `win10-x64` were removed.

**Fix:**
- Use a portable RID instead: `win-x64` instead of `win10-x64`.
- Or set `<UseRidGraph>true</UseRidGraph>` to restore legacy behavior.

## Migration Patterns

### .NET Framework → .NET 6+

Key breaking changes:
- `AppDomain` — no multi-domain support; use `AssemblyLoadContext` instead.
- `Remoting` — removed; use gRPC or REST.
- `System.Drawing` — Windows-only; use `System.Drawing.Common` with `<EnableUnsafeBinaryFormatterSerialization>` or alternatives like `SkiaSharp` / `ImageSharp`.
- `Windows.Forms` / `WPF` — available on .NET 6+ but Windows-only (requires `net8.0-windows` TFM).
- `web.config` / `app.config` — replaced by `appsettings.json` and `IConfiguration`.
- `BinaryFormatter` — disabled by default in .NET 8+; use `System.Text.Json` or `MessagePack`.

Migration steps:
1. Run the **.NET Upgrade Assistant**: `dotnet tool install -g upgrade-assistant && upgrade-assistant upgrade MyProject.csproj`
2. Start by retargeting libraries to `netstandard2.0` where possible.
3. Move the application project to `net8.0` (or `net8.0-windows` for WinForms/WPF).
4. Replace removed APIs with modern equivalents.
5. Update NuGet packages to versions that support the new TFM.

### .NET Standard → .NET 6+

When to drop `netstandard`:
- All your consumers are on `net6.0` or later.
- You need APIs only available in modern .NET (e.g., `Span<T>` first-class support, `static abstract` interface members).
- You want to use C# language features that require runtime support (e.g., `required` members, `generic math`).

When to keep `netstandard2.0`:
- You publish a NuGet package consumed by .NET Framework projects.
- You need Unity or Xamarin compatibility.

**Gradual approach:** Multi-target `netstandard2.0;net8.0` and use `#if NET8_0_OR_GREATER` to light up newer APIs while keeping older consumers working.

### Platform-specific → Cross-platform

Abstracting OS dependencies:

```csharp
// Define an abstraction
public interface ISettingsStore
{
    void Save(string key, string value);
    string? Load(string key);
}

// Platform-specific implementations
#if WINDOWS
public class RegistrySettingsStore : ISettingsStore { /* ... */ }
#endif

public class FileSettingsStore : ISettingsStore { /* ... */ }

// Register via DI based on platform
services.AddSingleton<ISettingsStore>(sp =>
{
    if (OperatingSystem.IsWindows())
        return new RegistrySettingsStore();
    return new FileSettingsStore();
});
```

### Useful Migration Tools

- **.NET Upgrade Assistant**: `dotnet tool install -g upgrade-assistant`
- **try-convert**: `dotnet tool install -g try-convert` — converts old-style csproj to SDK-style
- **API Compatibility Analyzer**: checks for API breaking changes across TFMs
- **Portability Analyzer**: identifies APIs that are not portable across target platforms
