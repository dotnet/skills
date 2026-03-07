---
name: dotnet-maui-doctor
description: >-
  Diagnoses and fixes .NET MAUI development environment issues. Validates .NET SDK,
  workloads, Java JDK, Android SDK, Xcode, and Windows SDK. All version requirements
  discovered dynamically from NuGet WorkloadDependencies.json — never hardcoded.
  Use when: setting up MAUI development, build errors mentioning SDK/workload/JDK/Android,
  "Android SDK not found", "Java version" errors, "Xcode not found", environment verification
  after updates, or any MAUI toolchain issues. Do not use for: non-MAUI .NET projects,
  Xamarin.Forms apps, runtime app crashes unrelated to environment setup, or app store
  publishing issues. Works on macOS, Windows, and Linux.
---

# .NET MAUI Doctor

Validate and fix .NET MAUI development environments. All version requirements are discovered dynamically from NuGet APIs — never hardcode versions.

## When to Use

- Setting up a new .NET MAUI development environment
- Build errors mentioning missing SDKs, workloads, JDK, or Android components
- Errors like "Android SDK not found", "Java version", or "Xcode not found"
- Verifying environment health after SDK or OS updates

## When Not to Use

- Non-MAUI .NET projects (use standard .NET SDK troubleshooting instead)
- Xamarin.Forms apps (different toolchain and workload requirements)
- Runtime app crashes unrelated to environment setup
- App store publishing or signing issues
- IDE-specific issues (Visual Studio or VS Code configuration)

## Important: .NET Version Currency

Your training data may be outdated regarding .NET versions. .NET ships new major releases annually (November). Always check the releases-index.json (Task 2) to discover the **latest active major release** — do not assume your training data reflects the current version. For example, if you know about .NET 9.0 but the releases index shows .NET 10.0 as active, use .NET 10.0.

## Inputs

- A development machine running macOS, Windows, or Linux
- Shell access (Bash on macOS/Linux, PowerShell on Windows)
- Internet access for NuGet API queries and SDK downloads
- Admin/sudo access may be required for installing SDKs and workloads
- **Bash prerequisites**: `curl`, `jq`, and `unzip` (macOS/Linux)
- **PowerShell prerequisites**: `Invoke-RestMethod` and `System.IO.Compression` (built-in on Windows)

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

**Only Microsoft OpenJDK supported.** Verify `java -version` output contains "Microsoft". See `references/microsoft-openjdk.md` for detection paths.

> Use the JDK version recommended by WorkloadDependencies.json (`jdk.recommendedVersion`), ensuring it satisfies the `jdk.version` range. Do not hardcode JDK versions.

**JAVA_HOME is NOT required.** .NET MAUI tools auto-detect Microsoft OpenJDK installations from known paths. Do not tell users to set JAVA_HOME — it is unnecessary and risks pointing to a non-Microsoft JDK.

| JAVA_HOME state | OK? | Action |
|-----------------|-----|--------|
| Not set | ✅ | None needed — auto-detection works |
| Set to Microsoft JDK | ✅ | None needed |
| Set to non-Microsoft JDK | ⚠️ | **Report as anomaly** — let user decide to unset or redirect |

### Task 6: Validate Android SDK

Check packages from `androidsdk.packages`, `buildToolsVersion`, `apiLevel` (Task 4). See `references/installation-commands.md` for sdkmanager commands.

### Task 7: Validate Xcode (macOS Only)

```bash
xcodebuild -version
```

Compare against `xcode.version` range from Task 4. See `references/installation-commands-macos.md`.

### Task 8: Validate Windows SDK (Windows Only)

The Windows SDK is typically installed as part of the .NET MAUI workload or Visual Studio. See `references/installation-commands-windows.md`.

### Task 9: Remediation

See `references/installation-commands.md` for all commands.

Key rules:
- **Workloads**: Always use `--version` flag. Never use `workload update` or `workload repair`.
- **JDK**: Only install Microsoft OpenJDK. Do not set JAVA_HOME (auto-detected).
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

After a successful build, **ask the user** if they want to launch the app on a target platform to verify end-to-end:

```bash
# Replace net10.0 with the current major .NET version
dotnet build -t:Run -f net10.0-android
dotnet build -t:Run -f net10.0-ios        # macOS only
dotnet build -t:Run -f net10.0-maccatalyst # macOS only
dotnet build -t:Run -f net10.0-windows    # Windows only
```

Only run the target frameworks relevant to the user's platform and intent. This step deploys to an emulator/simulator/device, so confirm with the user before proceeding.

## Common Pitfalls

- **`maui` vs `maui-android` workload**: On Linux, the `maui` meta-workload is not available — use `maui-android` instead. On macOS/Windows, `maui` installs all platform workloads.
- **`workload update` / `workload repair`**: Never use these commands. Always install workloads with an explicit `--version` flag to ensure version consistency.
- **Non-Microsoft JDK**: Only Microsoft OpenJDK is supported. Other distributions (Oracle, Adoptium, Azul) will cause build failures even if the version is correct.
- **Unnecessary JAVA_HOME**: Do not set JAVA_HOME. MAUI auto-detects JDK from known install paths. If JAVA_HOME is set to a non-Microsoft JDK (e.g., Temurin), report this as an anomaly — it may override auto-detection and cause failures. Let the user decide whether to unset it.
- **Hardcoded versions**: Never hardcode SDK, workload, or dependency versions. Always discover them dynamically from the NuGet APIs (see Task 4).
- **Android SDK `sdkmanager` on Windows**: Use `sdkmanager.bat`, not `sdkmanager`, on Windows.
- **Stale training data**: LLM training data may reference outdated .NET versions. Always check the releases-index.json to discover the current active release.

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
