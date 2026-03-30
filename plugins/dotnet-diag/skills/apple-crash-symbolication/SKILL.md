---
name: apple-crash-symbolication
description: Symbolicate .NET runtime frames in Apple platform .ips crash logs (iOS, tvOS, Mac Catalyst, macOS). Extracts UUIDs and addresses from the native backtrace, locates dSYM debug symbols, and runs atos to produce function names with source file and line numbers. Automatically downloads .dwarf symbols from the Microsoft symbol server using Mach-O UUIDs. USE FOR triaging a .NET MAUI or Mono app crash from an .ips file on any Apple platform, resolving native backtrace frames in libcoreclr or libmonosgen-2.0 to .NET runtime source code, retrieving .ips crash logs from a connected iOS device or iPhone, or investigating EXC_CRASH, EXC_BAD_ACCESS, SIGABRT, or SIGSEGV originating from the .NET runtime. DO NOT USE FOR pure Swift/Objective-C crashes with no .NET components, or Android tombstone files. INVOKES Symbolicate-Crash.ps1 script, atos, dwarfdump, idevicecrashreport.
---

# Apple Platform Crash Log .NET Symbolication

Resolves native backtrace frames from .NET MAUI and Mono app crashes on Apple platforms (iOS, tvOS, Mac Catalyst, macOS) to function names, source files, and line numbers using Mach-O UUIDs and dSYM debug symbol bundles.

**Inputs:** Crash log file (`.ips` JSON format, iOS 15+ / macOS 12+), `atos` (from Xcode), optionally a connected iOS device to pull crash logs from.

**Do not use when:** The crashing library is not a .NET component (e.g., pure Swift/UIKit), or the crash log is an Android tombstone.

---

## Workflow

### Step 1: Parse the .ips Crash Log

**Format check:** Before proceeding, verify the file is `.ips` JSON format. The first line must be valid JSON. If the file is plain text (e.g., Android tombstone with `#NN pc` frame lines, or legacy Apple `.crash` text format), **stop immediately** — this workflow does not apply. Report the format mismatch to the user and do not attempt any symbolication.

The `.ips` file is **two-part JSON**: line 1 is a metadata header; the remaining lines are a separate JSON crash body. Parse them separately:

```python
lines = open('crash.ips').readlines()
metadata = json.loads(lines[0])           # app_name, bundleID, os_version, slice_uuid
crash    = json.loads(''.join(lines[1:])) # Full crash report
```

Key fields in the crash body:
- `usedImages[N]` has `name`, `base` (load address), `uuid`, `arch` for each loaded binary
- `threads[N].frames[M]` has `imageOffset`, `imageIndex`; frame address = `usedImages[imageIndex].base + imageOffset`
- `exception.type`, `exception.signal` (e.g., `EXC_CRASH` / `SIGABRT`)
- `asi` (Application Specific Information) often contains the managed exception message
- `lastExceptionBacktrace` has frames from the exception that triggered the crash
- `faultingThread` is the index into the `threads` array

**Parsing gotcha:** Some .ips files have case-conflicting duplicate keys (`vmRegionInfo` / `vmregioninfo`). Pre-process the raw JSON to rename the lowercase duplicate before parsing. The `asi` field may be absent.

### Step 2: Identify .NET Runtime Libraries

Filter `usedImages` to .NET runtime libraries:

| Library | Runtime |
|---------|---------|
| `libcoreclr` | CoreCLR runtime |
| `libmonosgen-2.0` | Mono runtime |
| `libSystem.Native` | .NET BCL native component |
| `libSystem.Globalization.Native` | .NET BCL globalization |
| `libSystem.Security.Cryptography.Native.Apple` | .NET BCL crypto |
| `libSystem.IO.Compression.Native` | .NET BCL compression |
| `libSystem.Net.Security.Native` | .NET BCL net security |

On Apple platforms these ship as `.framework` bundles, so image names may omit `.dylib`. Match using substring (e.g., `libcoreclr` not `libcoreclr.dylib`). The app binary may appear **twice** in `usedImages` with different UUIDs.

**Key bridge functions** in the app binary: `xamarin_process_managed_exception` (managed exception bridged to ObjC NSException), `xamarin_main`, `mono_jit_exec`, `coreclr_execute_assembly`.

**NativeAOT:** Runtime is statically linked into the app binary. `libSystem.*` BCL libraries remain separate. The app binary needs its own dSYM from the build output.

Skip `libsystem_kernel.dylib`, `UIKitCore`, and other Apple system frameworks unless specifically asked.

### Step 3: Interpret the Crash

**Start with `asi`** (Application Specific Information) — for .NET crashes, it often contains the managed exception type and message (e.g., `XamlParseException`, `NullReferenceException`). The root cause may already be visible here.

Then examine the **faulting thread** (`threads[faultingThread]`). Explain what frames #0 and #1 mean before examining other threads. Cross-thread context (GC state, thread pool) is useful for validation but not evidence of causation.

Also check `lastExceptionBacktrace` for the managed exception path through bridge functions like `xamarin_process_managed_exception`.

Sometimes the .NET runtime version is visible in image paths in `usedImages`, particularly on macOS when using shared-framework installs or NuGet-pack-style layouts (e.g., `.../Microsoft.NETCore.App/10.0.4/libcoreclr.dylib`). On iOS, however, image paths are typically inside the app bundle (for example, `.../Frameworks/libcoreclr.framework/libcoreclr`) and do not embed the runtime version, so you usually need to infer it via the Mach-O UUID by matching against SDK packs or symbol-server downloads rather than relying on the path alone.

### Step 4: Locate dSYMs

For each .NET library needing symbolication, locate a UUID-matched dSYM:

1. **Microsoft symbol server** (automatic): Download `.dwarf` via `https://msdl.microsoft.com/download/symbols/_.dwarf/mach-uuid-sym-{UUID}/_.dwarf` (UUID lowercase, no dashes). Convert to `.dSYM` bundle (use the image name from `usedImages[].name`, e.g., `libcoreclr`):
   ```bash
   mkdir -p libcoreclr.dSYM/Contents/Resources/DWARF
   cp _.dwarf libcoreclr.dSYM/Contents/Resources/DWARF/libcoreclr
   ```
2. **Build output**: `bin/Debug/net*-ios/ios-arm64/<App>.app.dSYM/`
3. **SDK packs**: `$DOTNET_ROOT/packs/Microsoft.NETCore.App.Runtime.<rid>/<version>/runtimes/<rid>/native/`
4. **NuGet cache**: `~/.nuget/packages/microsoft.netcore.app.runtime.<rid>/<version>/runtimes/<rid>/native/`
5. **`dotnet-symbol`**: `dotnet-symbol --symbols -o symbols-out <path-to-binary.dylib>`

Always verify: `dwarfdump --uuid <dsym>` must match the UUID from the crash log exactly.

### Step 5: Symbolicate with atos

```bash
atos -arch arm64 -o <path.dSYM/Contents/Resources/DWARF/binary_name> -l <load_address> <frame_addresses...>
```

- `-o` points to the DWARF binary **inside** the `.dSYM` bundle (`Contents/Resources/DWARF/`), not the bundle itself
- `-l` is the load address from `usedImages[N].base`
- Use the `arch` from `usedImages[N].arch` (usually `arm64`, may be `arm64e`)
- Pass multiple addresses per invocation for batch symbolication

```bash
# Example: symbolicate libcoreclr frames
atos -arch arm64 -o libcoreclr.dSYM/Contents/Resources/DWARF/libcoreclr -l 0x104000000 0x104522098 0x1043c0014
```

Strip the `/__w/1/s/` CI workspace prefix from output — meaningful paths start at `src/runtime/`, mapping to the [dotnet/dotnet](https://github.com/dotnet/dotnet) VMR.

### Automation Script

[scripts/Symbolicate-Crash.ps1](scripts/Symbolicate-Crash.ps1) automates the full workflow (parsing, dSYM lookup, symbol download, and symbolication). Resolve the path relative to this SKILL.md file.

```powershell
# $SKILL_DIR is the directory containing this SKILL.md
pwsh "$SKILL_DIR/scripts/Symbolicate-Crash.ps1" -CrashFile MyApp-2026-02-25.ips
```

Start with `-ParseOnly` for a fast overview without requiring `atos`. The script automatically downloads symbols from the Microsoft symbol server when local dSYMs are missing.

Flags: `-CrashingThreadOnly`, `-OutputFile path`, `-ParseOnly`, `-SkipVersionLookup`, `-SkipSymbolDownload`, `-SymbolCacheDir path`, `-DsymSearchPaths path1,path2`.

---

## Retrieving Crash Logs

Pull crash logs from a connected iOS device using `idevicecrashreport` (from [libimobiledevice](https://libimobiledevice.org/)):

```bash
idevicecrashreport -e /tmp/crashlogs/
find /tmp/crashlogs/ -iname '*MyApp*' -name '*.ips'
```

Also available in **Xcode > Window > Devices and Simulators > View Device Logs**, or at `~/Library/Logs/CrashReporter/` (Mac Catalyst), `~/Library/Logs/DiagnosticReports/` (macOS).

---

## Validation

1. `dwarfdump --uuid <dsym>` matches UUID from the crash log
2. At least one .NET frame resolves to a function name (not a raw address)
3. Resolved paths contain recognizable .NET runtime structure (e.g., `src/coreclr/`, `mono/metadata/`, `mono/mini/`)

## Stop Signals

- **Wrong file format**: If the file is not `.ips` JSON (e.g., Android tombstone with `#NN pc` stack frames, legacy `.crash` text format), **stop immediately** — report the format mismatch to the user and do not proceed with any symbolication. Do not attempt to symbolicate using other tools or workflows.
- **No .NET frames found**: Report parsed frames and stop.
- **All frames resolved**: Present symbolicated backtrace with brief crash analysis (faulting thread, exception type, likely area). If the user asks for deeper investigation, proceed.
- **dSYM not available / UUID mismatch**: Report unsymbolicated frames with UUIDs and addresses. Suggest locating the original build artifacts.
- **atos not available**: Present the manual `atos` commands for the user to run. Do not install Xcode. `atos` ships with Xcode Command Line Tools (`xcode-select --install`).

## References

- **IPS format details**: See [references/ips-crash-format.md](references/ips-crash-format.md) for additional .ips parsing details and macOS symbol package differences.
