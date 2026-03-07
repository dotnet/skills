---
name: apple-crash-symbolication
description: Symbolicate .NET runtime frames in Apple platform .ips crash logs (iOS, tvOS, Mac Catalyst, macOS). Extracts UUIDs and addresses from the native backtrace, locates dSYM debug symbols, and runs atos to produce function names with source file and line numbers. USE FOR triaging a .NET MAUI or Mono app crash from an .ips file on any Apple platform, resolving native backtrace frames in libcoreclr or libmonosgen-2.0 to .NET runtime source code, retrieving .ips crash logs from a connected iOS device or iPhone, or investigating EXC_CRASH, EXC_BAD_ACCESS, SIGABRT, or SIGSEGV originating from the .NET runtime. DO NOT USE FOR pure Swift/Objective-C crashes with no .NET components, or Android tombstone files. INVOKES Symbolicate-Crash.ps1 script, atos, dwarfdump, idevicecrashreport.
---

# Apple Platform Crash Log .NET Symbolication

Resolves native backtrace frames from .NET MAUI and Mono app crashes on Apple platforms (iOS, tvOS, Mac Catalyst, macOS) to function names, source files, and line numbers using Mach-O UUIDs and dSYM debug symbol bundles.

**Inputs:** Crash log file (`.ips` JSON format, iOS 15+ / macOS 12+), `atos` (from Xcode), optionally a connected iOS device to pull crash logs from.

**Do not use when:** The crashing library is not a .NET component (e.g., pure Swift/UIKit), or the crash log is an Android tombstone.

---

## Workflow

### Step 1: Retrieve the Crash Log

Pull crash logs from a connected iOS device using `idevicecrashreport` (from [libimobiledevice](https://libimobiledevice.org/)):

```bash
idevicecrashreport -e /tmp/crashlogs/
find /tmp/crashlogs/ -iname '*MyApp*' -name '*.ips'
```

If `idevicecrashreport` is unavailable, crash logs can also be found in **Xcode → Window → Devices and Simulators → View Device Logs**, or at `~/Library/Logs/CrashReporter/` for Mac Catalyst and macOS apps: `~/Library/Logs/DiagnosticReports/`.

### Step 2: Run the Automation Script

[scripts/Symbolicate-Crash.ps1](scripts/Symbolicate-Crash.ps1) automates parsing, dSYM lookup, and symbolication. The script is located in this skill's `scripts/` directory — resolve the path relative to this SKILL.md file (do **not** search the filesystem with `find` or `locate`).

```powershell
# $SKILL_DIR is the directory containing this SKILL.md
pwsh "$SKILL_DIR/scripts/Symbolicate-Crash.ps1" -CrashFile MyApp-2026-02-25.ips
```

**Start with `-ParseOnly`** to get a fast overview of libraries, UUIDs, and addresses without requiring `atos` or dSYMs. Present those results to the user first. Only proceed to full symbolication if `atos` is available and dSYMs are found.

Flags: `-CrashingThreadOnly` (limit to faulting thread), `-OutputFile path` (write to file), `-ParseOnly` (report libraries/UUIDs/addresses without symbolicating), `-SkipVersionLookup` (skip runtime version identification), `-DsymSearchPaths path1,path2` (additional dSYM search directories).

The script searches for dSYMs in SDK packs (`$DOTNET_ROOT/packs/`), NuGet cache (`~/.nuget/packages/`), and user-provided paths across all Apple platform RIDs (`ios-arm64`, `tvos-arm64`, `maccatalyst-arm64/x64`, `osx-arm64/x64`). Do **not** run broad filesystem searches (`find /`, `find ~`) for dSYMs — if the script's built-in search paths don't find them, report the missing UUIDs and let the user provide the paths.

The script requires `.ips` JSON format (iOS 15+ / macOS 12+). Legacy `.crash` text files are not supported.

### Step 3: Interpret Results

The script outputs a symbolicated backtrace with function names and source locations. Check the output for:

- **`asi` (Application Specific Information)**: Often contains the managed exception message (e.g., `XamlParseException`). The root cause may already be visible here — check before diving into frames.
- **Runtime version**: The script identifies the .NET version and source commit from `.nuspec` metadata. Use the commit link to browse runtime source at the exact revision.
- **Unsymbolicated frames**: If dSYMs were not found, the script outputs raw addresses with UUIDs. Help the user locate dSYMs — see Step 4.

Strip the `/__w/1/s/` CI workspace prefix from resolved paths — meaningful paths start at `src/runtime/`.

### Step 4: Locate Missing dSYMs

When the script reports missing dSYMs, help the user find them. The script already searches SDK packs and NuGet cache automatically. Additional options:

1. **Build output**: Check the app's build directory (e.g., `bin/Debug/net*-ios/ios-arm64/<App>.app.dSYM/`)
2. **NuGet.org**: Download the matching `Microsoft.NETCore.App.Runtime.<rid>` package and extract
3. **User-provided paths**: Re-run with `-DsymSearchPaths` pointing to the dSYM location

Always verify UUID match with `dwarfdump --uuid <dsym>` before symbolicating. For **NativeAOT** apps, the runtime is in the app binary itself — its dSYM comes from the build output.

---

## Validation

1. `dwarfdump --uuid <dsym>` matches UUID from the crash log
2. At least one .NET frame resolves to a function name (not a raw address)
3. Resolved paths contain recognizable .NET runtime structure (e.g., `mono/metadata/`, `mono/mini/`)

## Stop Signals

- **No .NET frames found**: Report parsed frames and stop.
- **All frames resolved**: Present symbolicated backtrace. Do not trace into source or debug the runtime.
- **dSYM not available / UUID mismatch**: Report unsymbolicated frames with UUIDs and addresses. Suggest locating the original build artifacts.
- **atos not available**: Run with `-ParseOnly` and present the manual `atos` commands for the user to run. Do not install Xcode. `atos` ships with Xcode Command Line Tools (`xcode-select --install`).

## References

- **IPS format & manual symbolication**: See [references/ips-crash-format.md](references/ips-crash-format.md) for .ips file structure, .NET runtime library table, dSYM search paths, and manual `atos` usage.
