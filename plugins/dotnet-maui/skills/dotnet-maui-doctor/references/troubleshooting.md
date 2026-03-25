# Troubleshooting .NET MAUI Environment Issues

Common problems and solutions when setting up or using .NET MAUI.

**See also platform-specific troubleshooting:**
- macOS: `troubleshooting-macos.md`
- Windows: `troubleshooting-windows.md`

## .NET SDK Issues

### "dotnet: command not found"

**Cause**: .NET SDK not installed or not in PATH.

**Solution**:
```bash
# Check if dotnet exists
which dotnet

# macOS/Linux - Add to PATH if installed via dotnet-install script
export PATH="$PATH:$HOME/.dotnet"
```

If not installed, see: https://dotnet.microsoft.com/download

### "The required workload is not installed"

**Cause**: MAUI workload not installed.

**Solution**:
```bash
dotnet workload install maui --version $WORKLOAD_VERSION
```

### "Workload version mismatch"

**Cause**: Workloads from different SDK versions or incomplete installation.

**Solution**: Reinstall workloads with explicit version:
```bash
# First, find the correct workload set version for your SDK
# Query NuGet APIs (see workload-dependencies-discovery.md)

# Then reinstall with explicit version (macOS example — omit ios/maccatalyst on Linux)
dotnet workload install maui android ios maccatalyst --version $WORKLOAD_VERSION
```

**Note**: Avoid `dotnet workload update` or `dotnet workload repair` as they can cause version inconsistencies.

### SDK version conflict with global.json

**Cause**: Project requires specific SDK version not installed.

**Solution**:
```bash
# Check required version
cat global.json

# Install specific version
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version X.Y.Z
```

---

## Java JDK Issues

**IMPORTANT: Only Microsoft Build of OpenJDK is supported.** Other JDK vendors (Oracle, Azul, Amazon Corretto, Temurin) are NOT supported for .NET MAUI development.

See `microsoft-openjdk.md` for complete installation paths by platform.

### "JAVA_HOME is not set"

**This is usually NOT a problem.** The .NET MAUI toolchain auto-detects JDK installations without needing `JAVA_HOME`.

**When JAVA_HOME matters:**
- ⚠️ `JAVA_HOME` is set but points to a **non-Microsoft JDK** → Report as anomaly; user should unset or redirect to Microsoft JDK
- ✅ `JAVA_HOME` is not set → Fine, tools will auto-detect
- ✅ `JAVA_HOME` points to Microsoft JDK → Fine

**Solution (only if JAVA_HOME is set to wrong JDK):**

Report this as an anomaly to the user: "JAVA_HOME is set to a non-Microsoft JDK. .NET MAUI auto-detects Microsoft OpenJDK, so JAVA_HOME is not needed and may cause build issues."

The user can then decide to:
- Unset JAVA_HOME (lets auto-detection work)
- Point it to Microsoft JDK if they have a specific reason to keep it set

### "Unsupported Java version" or "Wrong JDK vendor"

**Cause**: JDK version outside required range OR non-Microsoft JDK installed.

> Use the JDK version recommended by WorkloadDependencies.json (`jdk.recommendedVersion`), ensuring it satisfies the `jdk.version` range. Do not hardcode JDK versions.

**Solution**: Install the recommended Microsoft OpenJDK version using the [official installation guide](https://learn.microsoft.com/en-us/java/openjdk/install).

### Non-Microsoft JDK detected

**Cause**: Oracle, Azul, Corretto, or other non-Microsoft JDK is installed and selected.

**How to identify**: Run `java -version` - if output does NOT contain "Microsoft", wrong JDK is selected.

**Solution**:
1. Install the recommended Microsoft OpenJDK version (see commands above)
2. If `JAVA_HOME` is set and points to a non-Microsoft JDK, report this as an anomaly — the user should unset it or point it to the Microsoft JDK path
3. Optionally uninstall the non-Microsoft JDK

### Multiple JDKs installed, wrong one selected

**Solution**:
```bash
# macOS - find Microsoft JDK
/usr/libexec/java_home -V 2>&1 | grep -i microsoft

# Linux - set Microsoft as default
sudo update-java-alternatives --set msopenjdk-{VERSION}-amd64
```

---

## Android SDK Issues

### "Android SDK not found"

**Cause**: SDK not installed or path not configured.

**Solution**:
```bash
# Check environment variables
echo $ANDROID_HOME
echo $ANDROID_SDK_ROOT

# Common SDK locations:
# macOS: ~/Library/Android/sdk
# Linux: ~/Android/Sdk
# Windows: %LOCALAPPDATA%\Android\Sdk

# If no SDK found, download command-line tools from:
# https://developer.android.com/studio#command-line-tools-only
```

### "Failed to find Build Tools"

**Cause**: Required build-tools package not installed.

**Solution**:
```bash
# Get build tools version from WorkloadDependencies.json (androidsdk.buildToolsVersion)

# Use sdkmanager (macOS/Linux)
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager "build-tools;$BUILD_TOOLS_VERSION"

# Windows
# & "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" "build-tools;$BUILD_TOOLS_VERSION"
```

### "License not accepted"

**Cause**: Android SDK licenses not accepted.

**Solution**:
```bash
# macOS/Linux
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager --licenses

# Windows
# & "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" --licenses
```

### "Platform not found: android-XX"

**Cause**: Target platform not installed.

**Solution**:
```bash
# Get required API level from WorkloadDependencies.json (androidsdk.apiLevel)

# macOS/Linux
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager "platforms;android-$API_LEVEL"

# Windows
# & "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" "platforms;android-$API_LEVEL"
```

### Emulator won't start

**Causes & Solutions**:

1. **HAXM/KVM not enabled**:
   ```bash
   # Linux - check KVM
   kvm-ok

   # Enable KVM
   sudo modprobe kvm
   ```

2. **Insufficient disk space**:
   - Clear AVD cache: `~/.android/avd/`

See also: `troubleshooting-windows.md` for Hyper-V conflicts.

---

## Build Errors

### "The target framework 'net10.0-android' is not available"

**Cause**: Android workload not installed for this SDK.

**Solution**:
```bash
dotnet workload install android --version $WORKLOAD_VERSION
```

### "Could not find android.jar"

**Cause**: Android platform not installed.

**Solution**:
```bash
# Discover required API level from WorkloadDependencies.json, then:
$ANDROID_SDK_ROOT/cmdline-tools/latest/bin/sdkmanager "platforms;android-$API_LEVEL"
```

### "MSB4019: The imported project was not found"

**Cause**: Workload not properly installed.

**Solution**: Reinstall with explicit version:
```bash
# Get correct workload version for your SDK band from NuGet APIs, then:
dotnet workload install maui --version $WORKLOAD_VERSION
```

### "NETSDK1147: To build this project, the following workloads must be installed"

**Solution**: Install the listed workloads with explicit version:
```bash
dotnet workload install [workload-name] --version $WORKLOAD_VERSION
```

---

## Performance Issues

### Slow builds

**Solutions**:
1. Enable incremental builds (default)
2. Use Hot Reload during development
3. Build only necessary platforms:
   ```bash
   dotnet build -f net10.0-android
   ```

### Slow Android emulator

**Solutions**:
1. Enable hardware acceleration (HAXM/KVM/Hyper-V)
2. Use x86_64 system image (not ARM on Intel)
3. Increase emulator RAM in AVD settings
4. Use physical device for testing

---

## Environment Variable Reference

| Variable | Purpose | Required | Notes |
|----------|---------|----------|-------|
| `JAVA_HOME` | JDK location | No | Report as anomaly if set to non-Microsoft JDK |
| `ANDROID_HOME` | Android SDK location | No | Auto-detected |
| `ANDROID_SDK_ROOT` | Android SDK location | No | Alternative to ANDROID_HOME |
| `DOTNET_ROOT` | .NET SDK location | No | Usually auto-detected |
| `PATH` | Must include dotnet | Yes | Required for CLI access |

**Key point about JAVA_HOME:**
- ✅ Not set → Fine, tools auto-detect Microsoft JDK
- ✅ Set to Microsoft JDK path → Fine
- ⚠️ Set to non-Microsoft JDK → Anomaly — report to user

**Note**: The .NET MAUI toolchain auto-detects most paths. Only set these manually if auto-detection fails or wrong JDK is being selected.

---

## Getting Help

### Diagnostic Commands

```bash
# Full .NET info
dotnet --info

# Workload status
dotnet workload list

# JDK info
java -version
```

See platform-specific troubleshooting files for additional diagnostic commands.

### Resources

- [.NET MAUI GitHub Issues](https://github.com/dotnet/maui/issues)
- [Stack Overflow - maui tag](https://stackoverflow.com/questions/tagged/maui)
- [.NET MAUI Documentation](https://learn.microsoft.com/en-us/dotnet/maui/)
