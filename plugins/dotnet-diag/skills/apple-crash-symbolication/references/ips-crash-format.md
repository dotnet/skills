# .ips Crash Log Format & .NET Symbolication Reference

## .ips File Structure

**Two-part JSON**: line 1 is a metadata header; remaining lines are a separate JSON crash body. Parse them separately — do not parse the entire file as one JSON document.

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

## .NET Runtime Libraries

| Library | Runtime |
|---------|---------|
| `libcoreclr` | CoreCLR runtime |
| `libmonosgen-2.0` | Mono runtime |
| `libSystem.Native` | .NET BCL native component |
| `libSystem.Globalization.Native` | .NET BCL globalization |
| `libSystem.Security.Cryptography.Native.Apple` | .NET BCL crypto |
| `libSystem.IO.Compression.Native` | .NET BCL compression |
| `libSystem.Net.Security.Native` | .NET BCL net security |

On Apple platforms, these ship as `.framework` bundles (e.g., `libcoreclr.framework/libcoreclr`), so image names may omit the `.dylib` extension. Match using substring (e.g., `libcoreclr` not `libcoreclr.dylib`). The app binary may appear **twice** in `usedImages` — once as the main executable and once as a framework bundle — with different UUIDs.

**Key bridge functions** in the app binary: `xamarin_process_managed_exception` (managed exception re-thrown as ObjC NSException), `xamarin_main`, `mono_jit_exec`, `coreclr_execute_assembly`.

**NativeAOT:** Runtime is statically linked into the app binary. `libSystem.*` BCL libraries remain separate. The app binary needs its own dSYM from the build output.

Skip `libsystem_kernel.dylib`, `UIKitCore`, and other Apple system frameworks unless specifically asked.

## dSYM Search Paths

Search order for locating dSYM debug symbols:

1. **Build output:** `bin/Debug/net*-ios/ios-arm64/<App>.app.dSYM/` (or equivalent for the target platform)
2. **Local .NET SDK packs:** `$DOTNET_ROOT/packs/Microsoft.NETCore.App.Runtime.<rid>/<version>/runtimes/<rid>/native/`
3. **NuGet cache:** `~/.nuget/packages/microsoft.netcore.app.runtime.<rid>/<version>/runtimes/<rid>/native/`
4. **NuGet.org:** Download the runtime package and extract

Supported RIDs: `ios-arm64`, `iossimulator-arm64`, `iossimulator-x64`, `tvos-arm64`, `tvossimulator-arm64`, `tvossimulator-x64`, `maccatalyst-arm64`, `maccatalyst-x64`, `osx-arm64`, `osx-x64`.

Verify UUID match: `dwarfdump --uuid <dsym>` must match the UUID from the crash log exactly. Mismatch means the dSYM is from a different build — locate the original build artifacts.

## Manual Symbolication with atos

```bash
atos -arch arm64 -o <path.dSYM/Contents/Resources/DWARF/binary_name> -l <load_address> <frame_addresses...>
```

- `-o` points to the DWARF binary inside the dSYM bundle (not the bundle itself)
- `-l` is the load address from `usedImages[N].base` — wrong load address = wrong results
- Use the `arch` from `usedImages[N].arch` (usually `arm64`, may be `arm64e`)
- Pass multiple addresses per invocation for batch symbolication

```bash
# Example: symbolicate libcoreclr frames
atos -arch arm64 -o libcoreclr.dSYM/Contents/Resources/DWARF/libcoreclr -l 0x104000000 0x104522098 0x1043c0014
```

The `/__w/1/s/` prefix in output paths is the CI workspace root — meaningful paths start at `src/runtime/`, mapping to [dotnet/dotnet](https://github.com/dotnet/dotnet) VMR.

## macOS Symbol Packages

On macOS (`osx-arm64`, `osx-x64`), .NET runtime symbols are distributed differently than iOS:

- **iOS / Mac Catalyst / tvOS**: dSYM bundles ship inside the `Microsoft.NETCore.App.Runtime.<rid>` NuGet package.
- **macOS**: Symbols are in a **separate** package: `Microsoft.NETCore.App.Runtime.<rid>.symbols` (note `.symbols` suffix — not `.snupkg`). The main runtime package does not contain debug symbols.

The macOS symbols package contains **flat `.dwarf` files** (produced by `dsymutil --flat`), not `.dSYM` bundles. `atos` requires a `.dSYM` bundle directory structure. Convert:

```bash
# For each .dwarf file (e.g., libcoreclr.dylib.dwarf):
mkdir -p libcoreclr.dylib.dSYM/Contents/Resources/DWARF
cp libcoreclr.dylib.dwarf libcoreclr.dylib.dSYM/Contents/Resources/DWARF/libcoreclr.dylib
# Verify: dwarfdump --uuid libcoreclr.dylib.dSYM
```

The flat `.dwarf` is the same Mach-O DWARF format that lives inside the bundle — the conversion is purely structural (mkdir + cp).

> **`dotnet-symbol` alternative:** If you have the runtime binary locally (e.g., from the main NuGet runtime package), `dotnet-symbol --symbols <binary>` downloads matching `.dwarf` debug symbols from the Microsoft symbol server (`msdl.microsoft.com`). This avoids needing the separate `.symbols` NuGet package. The downloaded `.dwarf` files still require the `.dSYM` bundle conversion above.

## .ips JSON Parsing Gotchas

Some Apple .ips files contain **case-conflicting duplicate keys** at the top level (e.g., `vmRegionInfo` and `vmregioninfo`). Most JSON parsers reject this. Workaround: pre-process the raw JSON string to rename the lowercase duplicate before parsing.

The `asi` (Application Specific Information) field is not present in all crash logs — access it safely (check existence before reading).
