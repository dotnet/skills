# P/Invoke Diagnostics

## Common Pitfalls

| Pitfall | Impact | Solution |
|---------|--------|----------|
| `int` for `size_t` | Stack corruption on 64-bit | Use `nuint` (.NET 8+) or `UIntPtr` on older frameworks |
| `long` for C `long` | Wrong on Windows (32-bit) | Use `CLong` / `CULong` (with `LibraryImport`, requires `[assembly: DisableRuntimeMarshalling]`) |
| `bool` without `MarshalAs` | Wrong marshal size | Specify `UnmanagedType.Bool` (4B) or `U1` (1B) |
| Implicit string encoding | Corrupts non-ASCII | Always specify `CharSet` or `StringMarshalling` |
| Wrong allocator for free | Heap corruption | Use the library's own free function |
| Raw `IntPtr` for handles | Leaks on exception | Use `SafeHandle` subclass |
| Delegate callback GC'd | Crash in native code | Keep a rooted reference for the delegate's lifetime |
| Missing `SetLastError` | Stale error codes | Set `SetLastError = true` on Win32 APIs |
| Struct packing mismatch | Fields at wrong offsets | Match `Pack` to native `#pragma pack` |
| Managed object as `void*` | Object moves during GC | Pin with `GCHandle` or `fixed` |

## Failure Modes and Recovery

| Symptom | Likely Cause | Diagnosis |
|---------|-------------|-----------|
| `DllNotFoundException` | Library not found at runtime | Check library name, path, and platform. Use `NativeLibrary.TryLoad` to test loading manually. On Linux, verify `LD_LIBRARY_PATH` or `rpath`. |
| `EntryPointNotFoundException` | Export name mismatch | Inspect the native binary's export table (`dumpbin /exports` on Windows, `nm -D` on Linux). Check for name mangling (C++ without `extern "C"`). |
| `AccessViolationException` | Signature mismatch, use-after-free, or missing pinning | Compare managed and native signatures byte-for-byte. Check struct sizes with `Marshal.SizeOf<T>()` vs native `sizeof`. Verify memory lifetime. |
| Silent data corruption | Wrong type size or encoding | Add temporary logging at the boundary. Compare `Marshal.SizeOf<T>()` to native struct size. Test with known input/output pairs. |
| Intermittent crashes | GC moved an unpinned object or collected a delegate | Ensure callbacks are rooted. Use `GCHandle` or `fixed` for any pointer held across calls. Run under a debugger with managed debugging assistants (MDAs) enabled. |
| Heap corruption on free | Wrong allocator | Confirm which allocator the native side used and free with the matching function. Never mix `malloc`/`free` with `CoTaskMemAlloc`/`CoTaskMemFree` or `Marshal.FreeHGlobal`. |

## General Debugging Approach

1. Reproduce under a debugger with native and managed debugging enabled
2. On .NET 8+, set `DOTNET_EnableDiagnostics=1` and use dotnet-dump or dotnet-trace for post-mortem analysis
3. Verify struct layout: `Marshal.SizeOf<T>()` must equal the native `sizeof` for every struct crossing the boundary
4. (.NET Framework only) Enable [Managed Debugging Assistants](https://learn.microsoft.com/en-us/dotnet/framework/debug-trace-profile/diagnosing-errors-with-managed-debugging-assistants) (MDAs) for `pInvokeStackImbalance` and `invalidOverlappedToPinvoke`

## Optional Generators

### CsWin32 (Win32 APIs)

For Win32 P/Invoke, prefer [Microsoft.Windows.CsWin32](https://github.com/microsoft/CsWin32) over hand-written signatures. It source-generates declarations from metadata. Add a `NativeMethods.txt` file listing the required APIs.

### CsWinRT (WinRT APIs)

For WinRT interop, use [Microsoft.Windows.CsWinRT](https://github.com/microsoft/CsWinRT) to generate .NET projections from `.winmd` files.

### Objective Sharpie (Objective-C APIs)

For binding Objective-C libraries on macOS or iOS, use [Objective Sharpie](https://learn.microsoft.com/previous-versions/xamarin/cross-platform/macios/binding/objective-sharpie) to generate the initial declarations and binding definitions.

## Resources

- [P/Invoke](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke)
- [LibraryImport source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation)
- [Type marshalling](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/type-marshalling)
- [SafeHandle](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle)
- [NativeLibrary](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.nativelibrary)
- [Best practices](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices)
