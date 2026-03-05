---
name: ios-crash-symbolication
description: Symbolicate .NET runtime frames in an iOS .ips crash log. Extracts UUIDs and addresses from the native backtrace, locates dSYM debug symbols, and runs atos to produce function names with source file and line numbers. USE FOR triaging a .NET MAUI or Mono iOS app crash from an .ips file, resolving native backtrace frames in libcoreclr or libmonosgen-2.0 to .NET runtime source code, retrieving .ips crash logs from a connected iOS device or iPhone, or investigating EXC_CRASH, EXC_BAD_ACCESS, SIGABRT, or SIGSEGV originating from the .NET runtime on iOS. DO NOT USE FOR pure Swift/Objective-C crashes with no .NET components, or Android tombstone files. INVOKES Symbolicate-Crash.ps1 script, atos, dwarfdump, idevicecrashreport.
---

# iOS Crash Log .NET Symbolication

Resolves native backtrace frames from .NET MAUI and Mono iOS app crashes to function names, source files, and line numbers using Mach-O UUIDs and dSYM debug symbol bundles.

**Inputs:** iOS crash log file (`.ips` JSON format, iOS 15+), `atos` (from Xcode), optionally a connected iOS device to pull crash logs from.

**Do not use when:** The crashing library is not a .NET component (e.g., pure Swift/UIKit), or the crash log is an Android tombstone.

---

## Workflow

### Step 1: Retrieve the Crash Log

Pull crash logs from a connected iOS device using `idevicecrashreport` (from [libimobiledevice](https://libimobiledevice.org/)):

```bash
idevicecrashreport -e /tmp/crashlogs/
find /tmp/crashlogs/ -iname '*MyApp*' -name '*.ips'
```

If `idevicecrashreport` is unavailable, crash logs can also be found in **Xcode → Window → Devices and Simulators → View Device Logs**, or at `~/Library/Logs/CrashReporter/` for Mac Catalyst apps.

### Step 2: Parse the Crash Log

**Two-part file**: line 1 is a JSON metadata header; remaining lines are a separate JSON crash body. Parse them separately:

```python
lines = open('crash.ips').readlines()
metadata = json.loads(lines[0])        # app_name, bundleID, os_version, slice_uuid
crash    = json.loads(''.join(lines[1:])) # Full crash report
```

Key fields in the crash body:
- `usedImages[N]` → `name`, `base` (load address), `uuid`, `arch`
- `threads[N].frames[M]` → `imageOffset`, `imageIndex`; address = `usedImages[imageIndex].base + imageOffset`
- `exception.type`, `exception.signal` → e.g., `EXC_CRASH` / `SIGABRT`
- `asi` → Application Specific Information — often contains the managed exception message (e.g., `XamlParseException`, `NullReferenceException`)
- `lastExceptionBacktrace` → frames from the exception that triggered the crash
- `faultingThread` → index into `threads` array

Always check `asi` first — for .NET mobile framework crashes, the managed exception type and message are typically here, bridged through `xamarin_process_managed_exception`.

### Step 3: Identify .NET Runtime Libraries

| Library | Runtime |
|---------|---------|
| `libcoreclr` | CoreCLR runtime |
| `libmonosgen-2.0` | Mono runtime |
| `libSystem.Native` | .NET BCL native component |
| `libSystem.Globalization.Native` | .NET BCL globalization |
| `libSystem.Security.Cryptography.Native.Apple` | .NET BCL crypto |
| `libSystem.IO.Compression.Native` | .NET BCL compression |
| `libSystem.Net.Security.Native` | .NET BCL net security |

On iOS, these ship as `.framework` bundles (e.g., `libcoreclr.framework/libcoreclr`), so image names may omit the `.dylib` extension. The app binary may appear **twice** in `usedImages` — once as the main executable and once as a framework bundle — with different UUIDs.

**Key bridge functions** in the app binary: `xamarin_process_managed_exception` (managed exception re-thrown as ObjC NSException), `xamarin_main`, `mono_jit_exec`, `coreclr_execute_assembly`.

**NativeAOT:** Runtime is statically linked into the app binary. `libSystem.*` BCL libraries remain separate. The app binary needs its own dSYM from the build output.

Skip `libsystem_kernel.dylib`, `UIKitCore`, and other Apple system frameworks unless specifically asked.

### Step 4: Locate dSYM Debug Symbols

Search order:
1. **Build output:** `bin/Debug/net*-ios/ios-arm64/<App>.app.dSYM/`
2. **Local .NET SDK packs:** `$DOTNET_ROOT/packs/Microsoft.NETCore.App.Runtime.ios-arm64/<version>/runtimes/ios-arm64/native/`
3. **NuGet cache:** `~/.nuget/packages/microsoft.netcore.app.runtime.ios-arm64/<version>/runtimes/ios-arm64/native/`
4. **NuGet.org:** Download the runtime package and extract

Verify UUID match before symbolicating:
```bash
dwarfdump --uuid libcoreclr.dSYM
```

If the UUID doesn't match, the local dSYM does not correspond to the binary that crashed — locate the correct build artifacts from the original build that produced the deployed app.

### Step 5: Symbolicate

```bash
atos -arch arm64 -o <path.dSYM/Contents/Resources/DWARF/binary_name> -l <load_address> <frame_addresses...>
```

Where `-o` points to the DWARF binary inside the dSYM bundle (not the bundle itself), `-l` is the load address from `usedImages[N].base`, and addresses are `base + imageOffset`. Use the `arch` from `usedImages[N].arch` (usually `arm64`, but may be `arm64e`). Pass multiple addresses per invocation for batch symbolication.

```bash
# Example: symbolicate libcoreclr frames
atos -arch arm64 -o libcoreclr.dSYM/Contents/Resources/DWARF/libcoreclr -l 0x104000000 0x104522098 0x1043c0014
```

The `/__w/1/s/` prefix in output paths is the CI workspace root — meaningful paths start at `src/runtime/`, mapping to [dotnet/dotnet](https://github.com/dotnet/dotnet) VMR.

### Finding atos

`atos` ships with Xcode Command Line Tools: `xcrun atos` or `/usr/bin/atos`. Install via `xcode-select --install`. Alternative: `llvm-symbolizer` from LLVM 14+ also supports Mach-O.

If unavailable, complete steps 1–4 and present the `atos` commands for the user to run. Do not install Xcode.

### Automation Script

[scripts/Symbolicate-Crash.ps1](scripts/Symbolicate-Crash.ps1) automates the full workflow:

```powershell
pwsh scripts/Symbolicate-Crash.ps1 -CrashFile MyApp-2026-02-25.ips
```

Flags: `-CrashingThreadOnly` (limit to faulting thread), `-OutputFile path` (write to file), `-ParseOnly` (report libraries/UUIDs/addresses without symbolicating), `-SkipVersionLookup` (skip runtime version identification), `-DsymSearchPaths path1,path2` (additional dSYM search directories).

**macOS only** — `atos` requires Xcode Command Line Tools. The script searches for dSYMs in SDK packs (`$DOTNET_ROOT/packs/`), NuGet cache (`~/.nuget/packages/`), and user-provided paths.

---

## Runtime Version Identification

The script identifies the exact .NET runtime version by matching Mach-O UUIDs against locally-installed runtime packs. It searches: SDK packs (`$DOTNET_ROOT/packs/`), NuGet cache (`~/.nuget/packages/`). When found, it extracts the version and source commit from the `.nuspec` `<repository commit="..." />` element. Pass `-SkipVersionLookup` to disable.

---

## Validation

1. `dwarfdump --uuid <dsym>` matches UUID from the crash log
2. At least one .NET frame resolves to a function name (not a raw address)
3. Resolved paths contain recognizable .NET runtime structure (e.g., `mono/metadata/`, `mono/mini/`)

## Stop Signals

- **No .NET frames found**: Report parsed frames and stop.
- **All frames resolved**: Present symbolicated backtrace. Do not trace into source or debug the runtime.
- **dSYM not available / UUID mismatch**: Report unsymbolicated frames with UUIDs and addresses. Suggest locating the original build artifacts.
- **atos not available**: Parse the crash log only, present manual commands for the user to run. Do not install Xcode.

## Common Pitfalls

- **Two-part .ips format**: Line 1 is a JSON metadata header, line 2+ is the crash body. Do not parse the entire file as one JSON document.
- **UUID mismatch**: dSYM UUID must match crash log UUID exactly. Mismatch means the dSYM is from a different build than the one that crashed — locate the original build artifacts.
- **Framework bundles**: .NET libraries on iOS are `.framework` bundles — image names may omit `.dylib`. Match using substring (e.g., `libcoreclr` not `libcoreclr.dylib`).
- **Managed exception in ASI**: The `asi` field often contains the full managed exception text. Check it before symbolicating — the root cause may already be visible.
- **NativeAOT**: No runtime dylib — runtime is in the app binary. `libSystem.*` BCL libraries still work; app binary needs its own dSYM.
- **Load address required**: `atos` needs the exact load address from `usedImages[N].base` or the Binary Images section. Wrong load address = wrong results.
