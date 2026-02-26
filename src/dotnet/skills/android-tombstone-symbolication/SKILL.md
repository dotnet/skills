---
name: android-tombstone-symbolication
description: Symbolicate the .NET runtime frames in an Android tombstone file. Extracts BuildIds and PC offsets from the native backtrace, downloads debug symbols from the Microsoft symbol server, and runs llvm-symbolizer to produce function names with source file and line numbers. Use when (1) triaging a .NET MAUI or Mono Android app crash from a tombstone, (2) resolving native backtrace frames in libmonosgen-2.0.so or libcoreclr.so to .NET runtime source code, or (3) investigating SIGABRT, SIGSEGV, or other native signals originating from the .NET runtime on Android. Do not use for pure Java/Kotlin crashes, managed .NET exceptions that are already captured in logcat, or iOS crash logs.
---

# Android Tombstone .NET Symbolication

When a .NET Android app (MAUI, Xamarin, or bare Mono) crashes with a native signal, Android's `debuggerd` produces a tombstone file containing a native backtrace. The backtrace shows library-relative PC offsets and ELF BuildIds but no source-level information. This skill resolves those frames to function names, source files, and line numbers in the .NET runtime source code.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Tombstone file | Yes | The raw Android tombstone text file (from `/data/tombstones/tombstone_XX` on device) |
| `llvm-symbolizer` | Yes | From the Android NDK or any LLVM toolchain (LLVM 14+ recommended) |
| Internet access | Yes | To download debug symbols from Microsoft's symbol server |

## When Not to Use

- The crash is a managed .NET exception (visible in `adb logcat` with a managed stack trace) — no symbolication needed.
- The crashing library is not a .NET component (e.g., `libart.so`, `libhwui.so`) — use Android-specific symbol sources instead.
- The tombstone is from an iOS crash report — different format and symbol resolution process.

---

## Workflow

### Step 1: Parse the Tombstone Backtrace

The tombstone backtrace appears near the top of the file. Each frame has this format:

```
#NN pc OFFSET  /path/to/library.so (optional_symbol+0xNN) (BuildId: HEXSTRING)
```

Extract from each frame:
- **Frame number** (`#00`, `#01`, ...)
- **PC offset** (hex digits, with or without `0x` prefix) — already library-relative (Android computes this since 5.0+)
- **Library path and name** (e.g., `.../libmonosgen-2.0.so`)
- **BuildId** (hex string, typically 32 or 40 characters)

**Agent behavior:** Look for the `backtrace:` section in the tombstone. The crashing thread's backtrace is listed first. Additional thread backtraces appear later, separated by `--- --- ---` markers. Symbolicate all threads by default — background threads (GC workers, finalizers) often contain useful .NET runtime frames. Focus on the crashing thread only if the user specifically asks.

### Step 2: Identify .NET Runtime Libraries

Filter the backtrace frames to those from .NET runtime libraries. The key libraries are:

| Library | Runtime |
|---------|---------|
| `libmonosgen-2.0.so` | Mono runtime (MAUI, Xamarin, interpreter mode) |
| `libcoreclr.so` | CoreCLR runtime (JIT mode) |
| `libSystem.Native.so` | .NET BCL native component |
| `libSystem.Globalization.Native.so` | .NET BCL native component |
| `libSystem.IO.Compression.Native.so` | .NET BCL native component |
| `libSystem.Security.Cryptography.Native.OpenSsl.so` | .NET BCL native component |
| `libSystem.Net.Security.Native.so` | .NET BCL native component |

**NativeAOT note:** NativeAOT statically links the runtime into the app's own native binary — there is no `libcoreclr.so` or `libmonosgen-2.0.so`. Crash frames from the runtime will appear under the app's own library name (e.g., `libMyApp.so`). The `libSystem.*.so` BCL components remain as separate shared libraries. If you see crash frames only in the app's own binary and `libSystem.*.so` libraries with no `libmonosgen-2.0.so` or `libcoreclr.so`, the app is likely NativeAOT. The symbol server workflow still applies for the `libSystem.*.so` libraries; for the app binary itself, you need the app's own debug symbols.

Frames from `libc.so`, `libart.so`, or other Android system libraries are not .NET — skip them unless the user specifically asks.

### Step 3: Download Debug Symbols

For each unique BuildId from a .NET library, download the debug symbols from Microsoft's public symbol server. The URL scheme encodes the BuildId directly — no searching required:

```
https://msdl.microsoft.com/download/symbols/_.debug/elf-buildid-sym-<BUILDID>/_.debug
```

**Example:**
```bash
curl -sL "https://msdl.microsoft.com/download/symbols/_.debug/elf-buildid-sym-1eb39fc72918c7c6c0c610b79eb3d3d47b2f81be/_.debug" \
  -o libmonosgen-2.0.so.debug
```

**Verify the download:**
```bash
file libmonosgen-2.0.so.debug
# Should show: ELF 64-bit LSB shared object ... with debug_info, not stripped
```

If the download returns a 404 or an HTML error page, the symbols are not published for that build. This can happen with pre-release or internal builds.

### Step 4: Symbolicate Each Frame

Run `llvm-symbolizer` for each PC offset against the debug symbols file:

```bash
llvm-symbolizer --obj=libmonosgen-2.0.so.debug -f -C 0x222098
```

This produces output like:
```
ves_icall_System_Environment_FailFast
/__w/1/s/src/runtime/src/mono/mono/metadata/icall.c:6244
```

The `/__w/1/s/` prefix is the CI build agent's workspace root — ignore it. The meaningful path starts at `src/runtime/` (or `src/` for other repos), which maps to the corresponding directory in the [dotnet/dotnet VMR](https://github.com/dotnet/dotnet).

**Manual alternative with addr2line** (manual workflow only — the automation script requires `llvm-symbolizer`):
```bash
addr2line -f -C -e libmonosgen-2.0.so.debug 0x222098
```

### Step 5: Present the Symbolicated Backtrace

Combine the original frame numbers with the resolved function names and source locations:

```
#00  libc.so              abort+164
#01  libmonosgen-2.0.so   ves_icall_System_Environment_FailFast        (mono/metadata/icall.c:6244)
#02  libmonosgen-2.0.so   ves_icall_System_Environment_FailFast_raw    (mono/metadata/icall-def.h:202)
#03  libmonosgen-2.0.so   do_icall                                     (mono/mini/interp.c:2457)
#04  libmonosgen-2.0.so   L00                                          (mono/mini/interp.c:2534)
#05  libmonosgen-2.0.so   mono_interp_exec_method                      (mono/mini/interp.c)
#06  libmonosgen-2.0.so   interp_entry                                 (mono/mini/interp.c:2372)
#07  libmonosgen-2.0.so   interp_entry_static_2                        (mono/mini/interp.c:3162)
```

For frames that `llvm-symbolizer` cannot resolve (returns `??`), keep the original tombstone line with its BuildId and PC offset for further triage.

### Automation Script

A PowerShell script is provided at [scripts/Symbolicate-Tombstone.ps1](scripts/Symbolicate-Tombstone.ps1) that automates the full workflow:

```powershell
pwsh scripts/Symbolicate-Tombstone.ps1 -TombstoneFile tombstone_01.txt -LlvmSymbolizer llvm-symbolizer
```

The script parses the tombstone, downloads symbols for all unique .NET BuildIds, symbolicates every frame across all threads, and outputs the result. Pass `-CrashingThreadOnly` to limit output to the crashing thread, `-OutputFile` to write to a file instead of stdout, or `-ParseOnly` to report detected libraries, BuildIds, and symbol URLs without downloading or symbolicating.

---

## Finding llvm-symbolizer

Check the **Android NDK** first — most .NET Android developers already have it. The `ANDROID_NDK_ROOT` or `ANDROID_HOME` environment variables point to the NDK:

```bash
# Check NDK directly
$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/*/bin/llvm-symbolizer

# Or find it via ANDROID_HOME (SDK contains NDK)
$ANDROID_HOME/ndk/*/toolchains/llvm/prebuilt/*/bin/llvm-symbolizer
```

Other sources:
- **LLVM/Clang**: Any LLVM distribution (`brew install llvm`, `apt install llvm`)
- **macOS**: `xcrun --find llvm-symbolizer`, or Homebrew LLVM at `/opt/homebrew/opt/llvm/bin/llvm-symbolizer`

If none are available, `addr2line` from GNU binutils also works but may produce less detailed output.

**Important:** Do not spend time installing LLVM just to symbolicate. If `llvm-symbolizer` is not available, complete steps 1–3 (parse, identify, download symbols) and present the download commands and `llvm-symbolizer` commands the user should run. The user can install the tool and run the commands themselves.

---

## Understanding the Output

The source paths in symbolicated output follow CI build agent conventions:

| Path prefix | Maps to |
|---|---|
| `/__w/1/s/src/runtime/` | `src/runtime/` in [dotnet/dotnet](https://github.com/dotnet/dotnet) VMR |
| `/__w/1/s/src/mono/` | `src/mono/` in the VMR (older builds) |
| `/__w/1/s/` | VMR root |

To find the exact source commit, look for `Microsoft.NETCore.App.versions.txt` inside the corresponding runtime NuGet package, or use the runtime version from logcat to identify the VMR commit.

---

## Validation

1. **Symbols downloaded successfully** — `file <debug-file>` shows `ELF ... with debug_info, not stripped`
2. **At least one frame resolved** — `llvm-symbolizer` returns a function name (not `??`) for at least one .NET frame
3. **Source paths are plausible** — resolved paths contain recognizable .NET runtime source structure (e.g., `mono/metadata/`, `mono/mini/`)

## Common Pitfalls

- **Symbols not found (404)**: Pre-release, daily, or internal .NET builds may not publish symbols to the public server. When this happens:
  1. Check if the build is from an official release (preview or GA).
  2. Look for a local unstripped `.so` or `.so.dbg` file in the app's build artifacts or CI output.
  3. Check the NuGet runtime pack on disk (e.g., `~/.dotnet/packs/Microsoft.NETCore.App.Runtime.Mono.android-arm64/<version>/`) for an unstripped binary.
  4. If building from source, retain the unstripped binary from the build output directory before stripping occurs.
- **Wrong llvm-symbolizer version**: Very old versions may not handle newer DWARF formats. Use LLVM 14+ for best compatibility.
- **Confusing absolute and relative offsets**: Android tombstones always provide library-relative offsets (since Android 5.0). Do not add or subtract the library base address.
- **Multiple libraries with different BuildIds**: Each .NET library (e.g., `libmonosgen-2.0.so`, `libSystem.Native.so`) has its own BuildId. Download symbols for each one separately.
- **CoreCLR apps use libcoreclr.so**: Apps using the CoreCLR JIT runtime have `libcoreclr.so` instead of `libmonosgen-2.0.so`. The same symbol server workflow applies.
- **NativeAOT has no runtime shared library**: NativeAOT statically links the runtime into the app's own binary. There is no `libcoreclr.so` or `libmonosgen-2.0.so` in the tombstone. The `libSystem.*.so` BCL libraries are still separate and can be symbolicated via the symbol server. For the app binary itself, you need the app's own debug symbols (not from the Microsoft symbol server).
