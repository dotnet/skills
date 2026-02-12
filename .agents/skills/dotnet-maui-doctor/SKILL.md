---
name: dotnet-maui-doctor
description: Diagnoses and fixes .NET MAUI development environment issues. Validates .NET SDK, workloads, Java JDK, Android SDK, Xcode, and Windows SDK. All version requirements discovered dynamically from NuGet WorkloadDependencies.json - never hardcoded. Use when: setting up MAUI development, build errors mentioning SDK/workload/JDK/Android, "Android SDK not found", "Java version" errors, "Xcode not found", environment verification after updates, or any MAUI toolchain issues. Works on macOS, Windows, and Linux.
---

# .NET MAUI Doctor

Validate and fix .NET MAUI development environments. All version requirements are discovered dynamically from NuGet APIs — never hardcode versions.

## Important: .NET Version Currency

Your training data may be outdated regarding .NET versions. .NET ships new major releases annually (November). Always check the releases-index.json (Task 2) to discover the **latest active major release** — do not assume your training data reflects the current version. For example, if you know about .NET 9.0 but the releases index shows .NET 10.0 as active, use .NET 10.0.

## Behavior

- Run through ALL tasks autonomously
- Re-validate after each fix
- Iterate until complete or no further actions possible
- After detecting platform (Task 1), load only the matching platform-specific references

## Workflow

### Task 1: Detect Environment

```bash
# macOS
sw_vers && uname -m

# Windows
systeminfo | findstr /B /C:"OS Name" /C:"OS Version"

# Linux
cat /etc/os-release && uname -m
```

After detection, load the matching platform references:
- **macOS**: `references/platform-requirements-macos.md`, `references/installation-commands-macos.md`, `references/troubleshooting-macos.md`
- **Windows**: `references/platform-requirements-windows.md`, `references/installation-commands-windows.md`, `references/troubleshooting-windows.md`
- **Linux**: `references/platform-requirements-linux.md`

### Task 2: Check .NET SDK

```bash
dotnet --info
```

Compare installed vs `latest-sdk` from https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json where `support-phase` is `"active"`.

### Task 3: Check MAUI Workloads

| Workload | macOS | Windows | Linux |
|----------|-------|---------|-------|
| `maui` | Required | Required | ❌ Use `maui-android` |
| `maui-android` | Alias | Alias | Required |
| `android` | Required | Required | Required |
| `ios` | Required | Optional | N/A |

### Task 4: Discover Requirements from NuGet

See `references/workload-dependencies-discovery.md` for complete process.

Query NuGet for workload manifest → extract `WorkloadDependencies.json` → get:
- `jdk.version` range and `jdk.recommendedVersion`
- `androidsdk.packages`, `buildToolsVersion`, `apiLevel`
- `xcode.version` range

### Task 5: Validate Java JDK

**Only Microsoft OpenJDK supported.** Verify `java -version` output contains "Microsoft". See `references/microsoft-openjdk.md` for detection paths and JAVA_HOME guidance.

> **⚠️ TEMPORARY WORKAROUND**: Recommend JDK 21 regardless of `jdk.recommendedVersion` until WorkloadDependencies.json is updated. Rule: If WorkloadDependencies suggests ≤21, recommend 21. If >21, use that value.

### Task 6: Validate Android SDK

Check packages from `androidsdk.packages`, `buildToolsVersion`, `apiLevel` (Task 4). See `references/installation-commands.md` for sdkmanager commands.

### Task 7: Validate Xcode (macOS Only)

```bash
xcodebuild -version
```

Compare against `xcode.version` range from Task 4. See `references/installation-commands-macos.md`.

### Task 8: Validate Windows SDK (Windows Only)

```powershell
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots"
```

See `references/installation-commands-windows.md`.

### Task 9: Remediation

See `references/installation-commands.md` for all commands.

Key rules:
- **Workloads**: Always use `--version` flag. Never use `workload update` or `workload repair`.
- **JDK**: Only install Microsoft OpenJDK.
- **Android SDK**: Use `sdkmanager` (from Android SDK command-line tools). On Windows use `sdkmanager.bat`.

### Task 10: Re-validate

After each fix, re-run the relevant validation task. Iterate until all checks pass.

## Validation

A successful run produces:
- .NET SDK installed and matches an active release
- All required workloads installed with consistent versions
- Microsoft OpenJDK detected (`java -version` contains "Microsoft")
- All required Android SDK packages installed (per WorkloadDependencies.json)
- Xcode version in supported range (macOS only)
- Windows SDK detected (Windows only)

### Build Verification (Recommended)

After all checks pass, create and build a test project to confirm the environment actually works:

```bash
TEMP_DIR=$(mktemp -d)
dotnet new maui -o "$TEMP_DIR/MauiTest"
dotnet build "$TEMP_DIR/MauiTest"
rm -rf "$TEMP_DIR"
```

On Windows, use `$env:TEMP` or `New-TemporaryFile` for the temp directory.

If the build succeeds, the environment is verified. If it fails, use the error output to diagnose remaining issues.

### Run Verification (Optional — Ask User First)

As of the latest .NET 10 SDK (10.0.103), `dotnet run` works for MAUI projects. After a successful build, **ask the user** if they want to launch the app on a target platform to verify end-to-end:

```bash
# Replace net10.0 with the current major .NET version
dotnet run -f net10.0-android
dotnet run -f net10.0-ios        # macOS only
dotnet run -f net10.0-maccatalyst # macOS only
dotnet run -f net10.0-windows    # Windows only
```

Only run the target frameworks relevant to the user's platform and intent. This step deploys to an emulator/simulator/device, so confirm with the user before proceeding.

## References

- `references/workload-dependencies-discovery.md` — NuGet API discovery process
- `references/microsoft-openjdk.md` — JDK detection paths, identification, JAVA_HOME
- `references/installation-commands.md` — .NET workloads, Android SDK (sdkmanager)
- `references/troubleshooting.md` — Common errors and solutions
- `references/platform-requirements-{platform}.md` — Platform-specific requirements
- `references/installation-commands-{platform}.md` — Platform-specific install commands
- `references/troubleshooting-{platform}.md` — Platform-specific troubleshooting

Official docs:
- [.NET MAUI Installation](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation)
- [.NET SDK Downloads](https://dotnet.microsoft.com/download)
- [Microsoft OpenJDK](https://learn.microsoft.com/en-us/java/openjdk/install)
- [Android SDK Command-Line Tools](https://developer.android.com/studio#command-line-tools-only)
- [Xcode Downloads](https://developer.apple.com/xcode/)
