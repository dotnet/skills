---
name: sdk-workload-resolution
description: "Diagnose and fix .NET SDK and workload resolution failures. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when builds fail with NETSDK1045, NETSDK1141, MSB4236, MSB4019, or 'SDK not found' errors. Covers global.json configuration, SDK roll-forward policies, workload installation, and multi-SDK environments. DO NOT use for non-.NET SDK issues."
---

# Diagnosing and Fixing .NET SDK and Workload Resolution Failures

## SDK Resolution Failures

### How SDK Resolution Works

The .NET SDK resolver follows a strict order when determining which SDK version to use:

1. **global.json** — If a `global.json` file exists at or above the project directory, its `sdk.version` and `sdk.rollForward` settings control SDK selection.
2. **PATH** — If no `global.json` is found (or the policy allows), the first `dotnet` executable on the system PATH is used.
3. **Default install location** — If `dotnet` is not on PATH, the resolver checks the default install directory.

**SDK install locations by OS:**

| OS | Default Install Path |
|---|---|
| Windows | `C:\Program Files\dotnet` |
| Linux | `/usr/share/dotnet` or `/usr/local/share/dotnet` |
| macOS | `/usr/local/share/dotnet` |

**Feature bands vs patch versions:**

SDKs use a versioning scheme: `major.minor.featureband+patch` (e.g., `8.0.301`).

- **Feature band**: The hundreds digit of the patch — `8.0.100`, `8.0.200`, `8.0.300` are different feature bands.
- **Patch version**: Increments within a feature band — `8.0.100`, `8.0.101`, `8.0.102` are patches within the `8.0.1xx` feature band.
- Feature band changes may introduce new features and behaviors. Patch versions are bug fixes only.
- `rollForward: "patch"` stays within the same feature band. `rollForward: "feature"` can jump across feature bands.

### global.json Configuration

A `global.json` file controls which SDK version is used for a project tree. It must be located at or above the project directory. The resolver walks up from the project directory until it finds one.

```json
{
  "sdk": {
    "version": "8.0.300",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

**`sdk.version`** — The base SDK version to resolve against. This is an exact pin when `rollForward` is `disable`, or a minimum version for other policies.

**`sdk.rollForward`** — Controls how the resolver picks an SDK when the exact `version` is not installed:

| Policy | Behavior |
|---|---|
| `patch` | Uses the specified version. If not found, rolls forward to the latest patch within the same feature band. Fails if none found. |
| `feature` | Uses the latest patch for the specified feature band. If not found, rolls forward to the next higher feature band within the same minor and uses its lowest patch. Fails if none found. |
| `minor` | Same as `feature`. If not found, rolls forward to the next higher minor version and uses its lowest feature band and patch. |
| `major` | Same as `minor`. If not found, rolls forward to the next higher major version and uses its lowest minor, feature band, and patch. |
| `latestPatch` | Uses the latest installed patch version within the same feature band. |
| `latestFeature` | Uses the highest installed feature band within the same major.minor, with the latest patch. |
| `latestMinor` | Uses the highest installed minor version within the same major, with the highest feature band and latest patch. |
| `latestMajor` | Uses the highest installed SDK of any version. |
| `disable` | Exact match only. No rolling forward. Fails if the exact version is not installed. |

**`sdk.allowPrerelease`** — When `false`, prerelease SDK versions (e.g., `9.0.100-preview.7`) are excluded from resolution. Default is `true` in dev environments, `false` in CI (when not in a Visual Studio context).

**Common mistakes:**

- Pinning `version` to an exact version (e.g., `8.0.204`) with `rollForward: "disable"` — breaks when that exact patch is not installed.
- Omitting `rollForward` entirely — defaults to `latestPatch`, which won't cross feature bands.
- Placing `global.json` in the wrong directory — it must be at or above the project directory.
- Setting `allowPrerelease: false` when only preview SDKs are installed.

**Best practice:** Use `"rollForward": "latestFeature"` for most development scenarios. This allows picking up new feature bands while staying within the same major.minor. For locked-down CI builds, use `"latestPatch"` to stay within a specific feature band.

### Common SDK Errors

#### NETSDK1045: "The current .NET SDK does not support targeting .NET X.Y"

**Cause:** The installed SDK is older than the target framework. For example, building a `net9.0` project with the .NET 8 SDK.

**Fix steps:**

1. Check installed SDKs:
   ```bash
   dotnet --list-sdks
   ```
2. Check the project's `TargetFramework`:
   ```xml
   <TargetFramework>net9.0</TargetFramework>
   ```
3. Check `global.json` (if present) — it may be pinning to an old SDK:
   ```bash
   cat global.json          # Linux/Mac
   Get-Content global.json  # Windows
   ```
4. Install the correct SDK version from https://dot.net/download or via script:
   ```bash
   # Linux/Mac
   ./dotnet-install.sh --channel 9.0
   # Windows
   dotnet-install.ps1 -Channel 9.0
   ```
5. **Quick fix** — If the right SDK is installed but `global.json` is pinning too low, adjust:
   ```json
   {
     "sdk": {
       "version": "9.0.100",
       "rollForward": "latestFeature"
     }
   }
   ```
6. Verify:
   ```bash
   dotnet --version
   dotnet build
   ```

#### NETSDK1141: "Unable to resolve the .NET SDK version as specified in the global.json"

**Cause:** The `global.json` specifies an SDK version that is not installed, and the `rollForward` policy is too restrictive to find an alternative.

**Fix steps:**

1. Check what `global.json` requires:
   ```bash
   cat global.json          # Linux/Mac
   Get-Content global.json  # Windows
   ```
2. Check installed SDKs:
   ```bash
   dotnet --list-sdks
   ```
3. Compare the required version against installed versions. Look for feature band mismatches.
4. **Quick fix** — Relax the rollForward policy:
   ```json
   {
     "sdk": {
       "version": "8.0.200",
       "rollForward": "latestFeature"
     }
   }
   ```
5. **Proper fix** — Install the required SDK:
   ```bash
   # Install specific version
   dotnet-install.ps1 -Version 8.0.204   # Windows
   ./dotnet-install.sh --version 8.0.204  # Linux/Mac
   ```
6. Verify:
   ```bash
   dotnet --version
   dotnet build
   ```

#### MSB4236: "The SDK 'Microsoft.NET.Sdk' specified could not be found"

**Cause:** MSBuild cannot locate the .NET SDK. Typically means `dotnet` is not on PATH, the install is corrupt, or MSBuild is running outside the dotnet CLI (e.g., standalone `msbuild.exe` without proper SDK resolution).

**Fix steps:**

1. Check if dotnet is on PATH:
   ```bash
   where dotnet    # Windows
   which dotnet    # Linux/Mac
   ```
2. Check if any SDKs are installed:
   ```bash
   dotnet --list-sdks
   ```
3. If dotnet is not found, install it or add it to PATH:
   ```bash
   # Add to PATH (Windows, current session)
   $env:PATH = "C:\Program Files\dotnet;$env:PATH"
   # Add to PATH (Linux/Mac, current session)
   export PATH="/usr/share/dotnet:$PATH"
   ```
4. If using standalone `msbuild.exe` (from Visual Studio), ensure the .NET SDK workload is installed in the Visual Studio Installer.
5. Check for corrupt installs — look for the `sdk` subdirectory:
   ```bash
   ls "C:\Program Files\dotnet\sdk"            # Windows
   ls /usr/share/dotnet/sdk                     # Linux
   ls /usr/local/share/dotnet/sdk               # Mac
   ```
6. If corrupt, reinstall the SDK or run:
   ```bash
   dotnet-install.ps1 -Channel 8.0   # Windows
   ./dotnet-install.sh --channel 8.0  # Linux/Mac
   ```
7. Verify:
   ```bash
   dotnet --info
   dotnet build
   ```

#### MSB4019: "The imported project was not found"

**Cause:** MSBuild targets files are missing. This typically happens with partial SDK installs, workload-specific SDKs not installed, or incorrect `MSBuildSDKsPath` environment variable.

**Fix steps:**

1. Read the full error message to identify which `.targets` or `.props` file is missing.
2. Check for environment variable overrides:
   ```bash
   echo $env:MSBuildSDKsPath    # Windows PowerShell
   echo $MSBuildSDKsPath        # Linux/Mac
   ```
   If set incorrectly, unset it:
   ```bash
   Remove-Item env:MSBuildSDKsPath    # Windows PowerShell
   unset MSBuildSDKsPath              # Linux/Mac
   ```
3. Check if the target file belongs to a workload that needs to be installed (see Workload Resolution below).
4. Reinstall the SDK if core targets are missing:
   ```bash
   dotnet --list-sdks
   dotnet-install.ps1 -Version <version>
   ```
5. Verify:
   ```bash
   dotnet build
   ```

## Workload Resolution

### What Are .NET Workloads

Workloads are optional SDK components that extend the .NET SDK for specific platforms and project types. They are installed separately from the base SDK.

Common workloads:

| Workload ID | Purpose |
|---|---|
| `maui` | .NET MAUI cross-platform UI |
| `aspire` | .NET Aspire cloud-native stack |
| `wasm-tools` | WebAssembly build tools |
| `ios` | iOS development |
| `android` | Android development |
| `macos` | macOS development |
| `maccatalyst` | Mac Catalyst development |

Workloads are tied to SDK feature bands. A workload installed for SDK `8.0.100` feature band is **not** available to SDK `8.0.200` feature band.

### Common Workload Errors

#### NETSDK1073: "The FrameworkReference 'X' was not found"

**Cause:** A required workload is not installed. The project references a framework (e.g., `Microsoft.Maui`) that comes from a workload pack.

**Fix:**

```bash
# Find which workload provides the missing framework
dotnet workload search

# Install the required workload
dotnet workload install maui

# Or restore workloads required by the project
dotnet workload restore
```

#### NETSDK1147: "To build this project, the following workloads must be installed"

**Cause:** The project explicitly declares required workloads and they are not present.

**Fix:**

```bash
# The error message lists the required workload IDs. Install them:
dotnet workload install <workload-id>

# Or install all workloads required by projects in the solution:
dotnet workload restore
```

#### NETSDK1005 with workload-specific TFMs (net8.0-ios, net8.0-android, etc.)

**Cause:** Targeting a platform-specific TFM like `net8.0-ios` requires the corresponding workload.

**Fix:**

```bash
# Map the TFM suffix to the workload:
# net8.0-ios       → dotnet workload install ios
# net8.0-android   → dotnet workload install android
# net8.0-maccatalyst → dotnet workload install maccatalyst
# net8.0-macos     → dotnet workload install macos
# net8.0-windows   → (built-in, but may need Windows Desktop SDK)
# net8.0-browser   → dotnet workload install wasm-tools

dotnet workload install <workload>
```

### Workload Management Commands

```bash
# List installed workloads
dotnet workload list

# Search available workloads
dotnet workload search

# Install a specific workload
dotnet workload install <workload-id>

# Install multiple workloads at once
dotnet workload install maui aspire wasm-tools

# Install workloads required by the current project/solution
dotnet workload restore

# Update all installed workloads to latest
dotnet workload update

# Repair corrupted workload installations
dotnet workload repair

# Uninstall a workload
dotnet workload uninstall <workload-id>
```

### Workload Manifest Issues

**Workload manifests vs workload packs:**

- **Manifests** describe which workload packs are available and their versions. They are updated with `dotnet workload update` or SDK updates.
- **Packs** are the actual NuGet packages containing the SDK extensions, frameworks, and tools.

**Version alignment:**

Workloads must be aligned with the SDK feature band. If you switch SDK feature bands (e.g., from `8.0.100` to `8.0.300`), you must reinstall workloads for the new feature band:

```bash
dotnet workload install maui  # installs for the current SDK feature band
```

**Rollback files for CI:**

To pin workload versions in CI for reproducibility, use rollback files:

```bash
# Export current workload versions to a rollback file
dotnet workload update --print-rollback

# Install workloads from a rollback file (pins exact versions)
dotnet workload install maui --from-rollback-file rollback.json

# Update using a rollback file
dotnet workload update --from-rollback-file rollback.json
```

A rollback file is a JSON file mapping workload manifest IDs to versions:

```json
{
  "microsoft.net.sdk.maui": "8.0.40/8.0.100",
  "microsoft.net.sdk.aspire": "8.0.1/8.0.100"
}
```

## Multi-SDK Environments

### Common Problems

- **Multiple .NET SDK versions installed, wrong one picked up** — Without `global.json`, the latest SDK is used, which may not be desired.
- **PATH ordering affecting SDK resolution** — The first `dotnet` found on PATH wins. User-installed SDKs may shadow system-installed ones or vice versa.
- **Container/CI images with unexpected SDK versions** — Base images may include SDK versions you don't expect. Always verify with `dotnet --list-sdks`.
- **Visual Studio bundled SDK vs standalone SDK** — Visual Studio installs its own SDK copy. When building from command line vs VS, different SDKs may be used.

### Diagnostic Steps

Run these commands in order to diagnose multi-SDK issues:

```bash
# 1. Full environment dump — shows SDK version, runtime versions, install paths, environment variables
dotnet --info

# 2. All installed SDKs with their install paths
dotnet --list-sdks

# 3. All installed runtimes
dotnet --list-runtimes

# 4. Which dotnet executable is being used
where dotnet          # Windows
which dotnet          # Linux/Mac

# 5. Check global.json in current or parent directories
Get-Content global.json    # Windows PowerShell
cat global.json            # Linux/Mac

# 6. Check for multiple global.json files up the directory tree
# Windows PowerShell:
$dir = Get-Location; while ($dir) { $gj = Join-Path $dir "global.json"; if (Test-Path $gj) { Write-Host $gj; Get-Content $gj }; $dir = Split-Path $dir -Parent }
# Linux/Mac:
d=$(pwd); while [ "$d" != "/" ]; do [ -f "$d/global.json" ] && echo "$d/global.json" && cat "$d/global.json"; d=$(dirname "$d"); done
```

### Resolution Strategy

**For local development:**

Use `global.json` at the repository root to control which SDK is used by all developers:

```json
{
  "sdk": {
    "version": "8.0.300",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

**For CI/CD:**

Install a specific SDK version using the official install scripts:

```bash
# GitHub Actions — use setup-dotnet action
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'

# Azure DevOps — use UseDotNet task
- task: UseDotNet@2
  inputs:
    version: '8.0.x'

# Manual CI — use dotnet-install script
# Windows:
Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
./dotnet-install.ps1 -Channel 8.0 -Quality GA
# Linux/Mac:
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0 --quality ga
```

**For containers:**

Use specific SDK Docker images:

```dockerfile
# Pin to specific SDK version
FROM mcr.microsoft.com/dotnet/sdk:8.0.300

# Or use the latest patch in a major.minor
FROM mcr.microsoft.com/dotnet/sdk:8.0
```

**With Visual Studio:**

Visual Studio bundles its own SDK. The VS-bundled SDK version depends on the VS version and installed workloads. When building from the command line, a different SDK may be resolved than what VS uses. To align:

- Use `global.json` in the repo root.
- Ensure the CLI SDK version matches what VS has installed.
- Run `dotnet --info` from both a VS Developer Command Prompt and a regular terminal to compare.

## Target Framework Moniker (TFM) vs SDK Relationship

### Which SDK Versions Support Which TFMs

Each major SDK version introduces support for its corresponding TFM:

| TFM | Minimum SDK | Notes |
|---|---|---|
| `net6.0` | SDK 6.0.100+ | LTS, end of support Nov 2024 |
| `net7.0` | SDK 7.0.100+ | STS, end of support May 2024 |
| `net8.0` | SDK 8.0.100+ | LTS |
| `net9.0` | SDK 9.0.100+ | STS |

### Cross-Targeting Rules

**Targeting older TFMs with newer SDKs (generally works):**

A .NET 9 SDK can build `net6.0`, `net7.0`, and `net8.0` projects. Newer SDKs maintain backward compatibility with older TFMs. Multi-targeting is common:

```xml
<TargetFrameworks>net8.0;net9.0</TargetFrameworks>
```

**Targeting newer TFMs with older SDKs (fails with NETSDK1045):**

A .NET 8 SDK **cannot** build `net9.0` projects. The SDK does not have the targeting pack or build logic for future TFMs.

**Fix:** Install the SDK version that matches or exceeds the target TFM. Update `global.json` if it pins to an older SDK.

### Quick Decision Tree

```
Build fails with SDK/TFM error
├── NETSDK1045 (SDK too old for TFM)
│   ├── Is global.json pinning an old SDK? → Update global.json version
│   └── Is the right SDK installed? → Install matching SDK
├── NETSDK1141 (global.json can't resolve)
│   ├── Is the exact version installed? → Install it, or relax rollForward
│   └── Is rollForward: "disable"? → Change to "latestFeature" or "latestPatch"
├── MSB4236 (SDK not found at all)
│   ├── Is dotnet on PATH? → Add to PATH
│   └── Is the install corrupt? → Reinstall SDK
├── MSB4019 (imported project not found)
│   ├── Is MSBuildSDKsPath set wrong? → Unset it
│   └── Is a workload missing? → dotnet workload restore
├── NETSDK1073 / NETSDK1147 (workload missing)
│   └── dotnet workload restore or dotnet workload install <id>
└── NETSDK1005 with platform TFM
    └── Install the workload matching the TFM suffix
```
