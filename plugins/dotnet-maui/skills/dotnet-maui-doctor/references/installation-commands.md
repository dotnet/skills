# Installation Commands Reference

Commands for installing and validating .NET MAUI development dependencies.

**See also platform-specific references:**
- macOS: `installation-commands-macos.md`
- Windows: `installation-commands-windows.md`

---

**Important**: All specific versions shown below are placeholders. Always discover the actual versions to use:
- **SDK/Workload versions**: Query releases-index.json and NuGet APIs (see `workload-dependencies-discovery.md`)
- **Android SDK packages**: From `androidsdk` in WorkloadDependencies.json
- **JDK version**: From `jdk.version` in WorkloadDependencies.json

## .NET SDK

For installation instructions, see the official docs: https://dotnet.microsoft.com/download

For scripted/CI installs, use the [dotnet-install scripts](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script).

---

## .NET Workloads

**Always use explicit workload set version** to ensure consistent, reproducible installs.

First, find the latest workload set version using the process in `workload-dependencies-discovery.md`:
```bash
# Discover NuGet search endpoint from service index
NUGET_SEARCH_URL=$(curl -s "https://api.nuget.org/v3/index.json" | \
  jq -r '.resources[] | select(.["@type"]=="SearchQueryService") | .["@id"]' | head -1)

# Query for latest workload set
# SDK band = first 2 segments of SDK version (e.g., 10.0 from 10.0.102)
curl -s "$NUGET_SEARCH_URL?q=Microsoft.NET.Workloads.$SDK_BAND&prerelease=false" | \
  jq '.data[] | select(.id | test("^Microsoft.NET.Workloads.$SDK_BAND.[0-9]+$")) | {id, version}'

# Convert NuGet version to CLI version:
# NuGet A.B.C → CLI A.0.B (e.g., NuGet 10.102.0 → CLI 10.0.102)
```

Then install with explicit version:
```bash
# Full MAUI installation (recommended)
dotnet workload install maui --version $WORKLOAD_VERSION

# Individual workloads
dotnet workload install android --version $WORKLOAD_VERSION
dotnet workload install ios --version $WORKLOAD_VERSION           # macOS only meaningful
dotnet workload install maccatalyst --version $WORKLOAD_VERSION   # macOS only meaningful

# Multiple at once
dotnet workload install maui android ios maccatalyst --version $WORKLOAD_VERSION
```

### List Installed Workloads

```bash
dotnet workload list
```

### ⚠️ Commands to Avoid

**Never use these commands** - they can cause version inconsistencies:
- ❌ `dotnet workload update` - Can introduce mixed versions
- ❌ `dotnet workload repair` - May not fix version issues
- ❌ `dotnet workload install` without `--version` - Gets unpredictable versions

**Instead**: Always reinstall with explicit `--version` to fix workload issues.

---

## Java JDK (Microsoft OpenJDK ONLY)

**CRITICAL: Only Microsoft Build of OpenJDK is supported.** Other JDK vendors (Oracle, Azul, Amazon Corretto, Temurin, etc.) are NOT supported for .NET MAUI development.

> Use the JDK version recommended by WorkloadDependencies.json (`jdk.recommendedVersion`), ensuring it satisfies the `jdk.version` range. Do not hardcode JDK versions.

See `microsoft-openjdk.md` for detection paths, identification, and JAVA_HOME guidance.

For installation instructions, see the official docs: https://learn.microsoft.com/en-us/java/openjdk/install

After installing, verify it is Microsoft OpenJDK:
```bash
# MUST show "Microsoft" in output
java -version
```

---

## Android SDK

### Detecting Existing Android SDK

```bash
# Check common environment variables
echo $ANDROID_HOME
echo $ANDROID_SDK_ROOT

# Common SDK locations:
# macOS: ~/Library/Android/sdk
# Linux: ~/Android/Sdk
# Windows: %LOCALAPPDATA%\Android\Sdk

# Check if sdkmanager is available
# macOS/Linux
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager --version

# Windows
%ANDROID_SDK_ROOT%\cmdline-tools\latest\bin\sdkmanager.bat --version
```

### Installing Android SDK Command-Line Tools

If no Android SDK exists, download the command-line tools:

1. Download from: https://developer.android.com/studio#command-line-tools-only
2. Extract to your SDK root:

```bash
# macOS/Linux
export ANDROID_SDK_ROOT="$HOME/Library/Android/sdk"  # macOS
# export ANDROID_SDK_ROOT="$HOME/Android/Sdk"        # Linux
mkdir -p "$ANDROID_SDK_ROOT/cmdline-tools"
# Extract downloaded zip, move contents to:
# $ANDROID_SDK_ROOT/cmdline-tools/latest/
```

```powershell
# Windows
$env:ANDROID_SDK_ROOT = "$env:LOCALAPPDATA\Android\Sdk"
New-Item -ItemType Directory -Force -Path "$env:ANDROID_SDK_ROOT\cmdline-tools"
# Extract downloaded zip, move contents to:
# $env:ANDROID_SDK_ROOT\cmdline-tools\latest\
```

### Install Required Packages with sdkmanager

Get exact versions from WorkloadDependencies.json (`androidsdk.packages`, `androidsdk.buildToolsVersion`, `androidsdk.apiLevel`).

```bash
# macOS/Linux
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager "platform-tools"
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager "build-tools;$BUILD_TOOLS_VERSION"
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager "platforms;android-$API_LEVEL"
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager "cmdline-tools;$CMDLINE_TOOLS_VERSION"

# Accept all licenses
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager --licenses
```

```powershell
# Windows
& "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" "platform-tools"
& "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" "build-tools;$BUILD_TOOLS_VERSION"
& "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" "platforms;android-$API_LEVEL"
& "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" "cmdline-tools;$CMDLINE_TOOLS_VERSION"

# Accept all licenses
& "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" --licenses
```

### Verify Android SDK

```bash
# Check ADB
adb --version

# List installed packages (macOS/Linux)
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager --list_installed

# List installed packages (Windows)
# & "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" --list_installed
```
