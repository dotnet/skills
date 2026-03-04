---
name: dotnet-pinvoke
description: >
  Correctly call native (C/C++) libraries from .NET using P/Invoke and LibraryImport.
  Covers function signatures, string marshalling, memory lifetime, SafeHandle, and
  cross-platform patterns.
  USE FOR: writing new P/Invoke or LibraryImport declarations, reviewing or debugging
  existing native interop code, wrapping a C or C++ library for use in .NET, diagnosing
  crashes, memory leaks, or corruption at the managed/native boundary.
  DO NOT USE FOR: COM interop, C++/CLI mixed-mode assemblies, or pure managed code with
  no native dependencies.
---

# .NET P/Invoke

Calling native code from .NET is powerful but unforgiving. Incorrect signatures, garbled strings, and leaked or freed memory are the most common sources of bugs — all can manifest as intermittent crashes, silent data corruption, or access violations far from the actual defect.

This skill covers both `DllImport` (available since .NET Framework 1.0) and `LibraryImport` (source-generated, .NET 7+). When targeting .NET Framework, always use `DllImport`. When targeting .NET 7+, prefer `LibraryImport` for new code. When native AOT is a requirement, `LibraryImport` is the only option.

## When to Use This Skill

- Writing a new `[DllImport]` or `[LibraryImport]` declaration from a C/C++ header
- Reviewing P/Invoke signatures for correctness (type sizes, calling conventions, string encoding)
- Wrapping an entire C library for use from .NET
- Debugging `AccessViolationException`, `DllNotFoundException`, or silent data corruption at the native boundary
- Migrating `DllImport` declarations to `LibraryImport` for AOT/trimming compatibility
- Diagnosing memory leaks or heap corruption involving native handles or buffers

## Stop Signals

- **Single function?** Map the signature (Steps 1-3), handle strings/memory only if relevant, skip tooling and migration sections.
- **Don't migrate** existing `DllImport` to `LibraryImport` unless the user asks or AOT/trimming is an explicit requirement.
- **Don't recommend CsWin32** unless the target is specifically Win32 APIs.
- **Don't generate callbacks** (Step 8) unless the native API requires function pointers.
- **Review request?** Use the validation checklist — don't rewrite working code.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Native header or documentation | Yes | C/C++ function signatures, struct definitions, calling conventions |
| Target framework | Yes | Determines whether to use `DllImport` or `LibraryImport` |
| Target platforms | Recommended | Affects type sizes (`long`, `size_t`) and library naming |
| Memory ownership contract | Yes | Who allocates and who frees each buffer or handle |

**Agent behavior:** When documentation and native headers diverge, always trust the header. Online documentation (including official Win32 API docs) frequently omits or simplifies details about types, calling conventions, and struct layout that are critical for correct P/Invoke signatures.

---

## Workflow

### Step 1: Choose DllImport or LibraryImport

| Aspect | `DllImport` | `LibraryImport` (.NET 7+) |
|--------|-------------|---------------------------|
| **Mechanism** | Runtime marshalling | Source generator (compile-time) |
| **AOT / Trim safe** | No | Yes |
| **String marshalling** | `CharSet` enum | `StringMarshalling` enum |
| **Error handling** | `SetLastError` | `SetLastPInvokeError` |
| **Availability** | .NET Framework 1.0+ | .NET 7+ only |

### Step 2: Map Native Types to .NET Types

The most dangerous mappings — these cause the majority of bugs:

| C / Win32 Type | .NET Type | Why |
|----------------|-----------|-----|
| `long` | **`CLong`** | 32-bit on Windows, 64-bit on 64-bit Unix. With `LibraryImport`, requires `[assembly: DisableRuntimeMarshalling]` |
| `size_t` | `nuint` / `UIntPtr` | Pointer-sized. Use `nuint` on .NET 8+ and `UIntPtr` on earlier .NET. Never use `ulong` |
| `BOOL` (Win32) | `int` | Not `bool` — Win32 `BOOL` is 4 bytes |
| `bool` (C99) | `[MarshalAs(UnmanagedType.U1)] bool` | Must specify 1-byte marshal |
| `HANDLE`, `HWND` | `SafeHandle` | Prefer over raw `IntPtr` |
| `LPWSTR` / `wchar_t*` | `string` | UTF-16 on Windows (lowest cost for `in` strings). Avoid in cross-platform code — `wchar_t` width is compiler-defined (typically UTF-32 on non-Windows) |
| `LPSTR` / `char*` | `string` | Must specify encoding (ANSI or UTF-8). Always requires marshalling cost for `in` parameters |

**For the complete type mapping table, struct layout, and blittable type rules**, see [references/type-mapping.md](references/type-mapping.md).

> ❌ **NEVER** use `int` or `long` for C `long` — it's 32-bit on Windows, 64-bit on Unix. Always use `CLong`.
> ❌ **NEVER** use `ulong` for `size_t` — causes stack corruption on 32-bit. Use `nuint` or `UIntPtr`.
> ❌ **NEVER** use `bool` without `MarshalAs` — the default marshal size is wrong.

### Step 3: Write the Declaration

Given a C header:

```c
int32_t process_records(const Record* records, size_t count, uint32_t* out_processed);
```

**DllImport:**

```csharp
[DllImport("mylib")]
private static extern int ProcessRecords(
    [In] Record[] records, UIntPtr count, out uint outProcessed);
```

**LibraryImport:**

```csharp
[LibraryImport("mylib")]
internal static partial int ProcessRecords(
    [In] Record[] records, nuint count, out uint outProcessed);
```

Calling conventions only need to be specified when targeting Windows x86 (32-bit), where `Cdecl` and `StdCall` differ. On x64, ARM, and ARM64, there is a single calling convention and the attribute is unnecessary.

**Agent behavior:** If you detect that Windows x86 is a target — through project properties (e.g., `<PlatformTarget>x86</PlatformTarget>`), runtime identifiers (e.g., `win-x86`), build scripts, comments, or developer instructions — flag this to the developer and recommend explicit calling conventions on all P/Invoke declarations.

```csharp
// DllImport (x86 targets)
[DllImport("mylib", CallingConvention = CallingConvention.Cdecl)]

// LibraryImport (x86 targets)
[LibraryImport("mylib")]
[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
```

If the managed method name differs from the native export name, specify `EntryPoint` to avoid `EntryPointNotFoundException`:

```csharp
// DllImport
[DllImport("mylib", EntryPoint = "process_records")]
private static extern int ProcessRecords(
    [In] Record[] records, UIntPtr count, out uint outProcessed);

// LibraryImport
[LibraryImport("mylib", EntryPoint = "process_records")]
internal static partial int ProcessRecords(
    [In] Record[] records, nuint count, out uint outProcessed);
```

### Step 4: Handle Strings Correctly

1. **Know what encoding the native function expects.** There is no safe default.
2. **Windows APIs:** Always call the `W` (UTF-16) variant. The `A` variant needs a specific reason and explicit ANSI encoding.
3. **Cross-platform C libraries:** Usually expect UTF-8.
4. **Specify encoding explicitly.** Never rely on `CharSet.Auto`.
5. **Never introduce `StringBuilder` for output buffers.**

> ❌ **NEVER** rely on `CharSet.Auto` or omit string encoding — there is no safe default.

```csharp
// DllImport — Windows API (UTF-16)
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern int GetModuleFileNameW(
    IntPtr hModule, [Out] char[] filename, int size);

// DllImport — Cross-platform C library (UTF-8)
[DllImport("mylib")]
private static extern int SetName(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

// LibraryImport — UTF-16
[LibraryImport("kernel32", StringMarshalling = StringMarshalling.Utf16,
    SetLastPInvokeError = true)]
internal static partial int GetModuleFileNameW(
    IntPtr hModule, [Out] char[] filename, int size);

// LibraryImport — UTF-8
[LibraryImport("mylib", StringMarshalling = StringMarshalling.Utf8)]
internal static partial int SetName(string name);
```

**String lifetime warning:** Marshalled strings are freed after the call returns. If native code stores the pointer (instead of copying), the lifetime must be manually managed. On Windows or .NET Framework, `CoTaskMemAlloc`/`CoTaskMemFree` is the first choice for cross-boundary ownership; on non-Windows targets, use `NativeMemory` APIs. The library may have its own allocator that must be used instead.

### Step 5: Establish Memory Ownership

When memory crosses the boundary, exactly one side must own it — and both sides must agree.

> ❌ **NEVER** free with a mismatched allocator — `Marshal.FreeHGlobal` on `malloc`'d memory is heap corruption.

**Model 1 — Caller allocates, caller frees (safest):**

```csharp
[LibraryImport("mylib")]
private static partial int GetName(
    Span<byte> buffer, nuint bufferSize, out nuint actualSize);

public static string GetName()
{
    Span<byte> buffer = stackalloc byte[256];
    int result = GetName(buffer, (nuint)buffer.Length, out nuint actualSize);
    if (result != 0) throw new InvalidOperationException($"Failed: {result}");
    return Encoding.UTF8.GetString(buffer[..(int)actualSize]);
}
```

**Model 2 — Callee allocates, caller frees (common in Win32):**

```csharp
[LibraryImport("mylib")]
private static partial IntPtr GetVersion();
[LibraryImport("mylib")]
private static partial void FreeString(IntPtr s);

public static string GetVersion()
{
    IntPtr ptr = GetVersion();
    try { return Marshal.PtrToStringUTF8(ptr) ?? throw new InvalidOperationException(); }
    finally { FreeString(ptr); } // Must use the library's own free function
}
```

**Critical rule:** Always free with the matching allocator. Never use `Marshal.FreeHGlobal` or `Marshal.FreeCoTaskMem` on `malloc`'d memory.

**Model 3 — Handle-based (callee allocates, callee frees):** Use `SafeHandle` (see Step 6).

**Pinning managed objects** — when native code stores the pointer or runs asynchronously:

```csharp
// Synchronous: use fixed
public static unsafe void ProcessSync(byte[] data)
{
    fixed (byte* ptr = data) { ProcessData(ptr, (nuint)data.Length); }
}

// Asynchronous: use GCHandle
var gcHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
// Must keep pinned until native processing completes, then call gcHandle.Free()
```

### Step 6: Use SafeHandle for Native Handles

Raw `IntPtr` leaks on exceptions and has no double-free protection. `SafeHandle` is non-negotiable.

```csharp
internal sealed class MyLibHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Required by the marshalling infrastructure to instantiate the handle.
    // Do not remove — there are no direct callers.
    private MyLibHandle() : base(ownsHandle: true) { }

    [LibraryImport("mylib", StringMarshalling = StringMarshalling.Utf8)]
    private static partial MyLibHandle CreateHandle(string config);

    [LibraryImport("mylib")]
    private static partial int UseHandle(MyLibHandle h, ReadOnlySpan<byte> data, nuint len);

    [LibraryImport("mylib")]
    private static partial void DestroyHandle(IntPtr h);

    protected override bool ReleaseHandle() { DestroyHandle(handle); return true; }

    public static MyLibHandle Create(string config)
    {
        var h = CreateHandle(config);
        if (h.IsInvalid) throw new InvalidOperationException("Failed to create handle");
        return h;
    }

    public int Use(ReadOnlySpan<byte> data) => UseHandle(this, data, (nuint)data.Length);
}

// Usage: SafeHandle is IDisposable
using var handle = MyLibHandle.Create("config=value");
int result = handle.Use(myData);
```

### Step 7: Handle Errors

```csharp
// Win32 APIs — check SetLastError
[LibraryImport("kernel32", SetLastPInvokeError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool CloseHandle(IntPtr hObject);

if (!CloseHandle(handle))
    throw new Win32Exception(Marshal.GetLastPInvokeError());

// HRESULT APIs
int hr = NativeDoWork(context);
Marshal.ThrowExceptionForHR(hr);
```

### Step 8: Handle Callbacks (if needed)

**Preferred (.NET 8+): `UnmanagedCallersOnly`** — avoids delegates entirely, no GC lifetime risk:

```csharp
[UnmanagedCallersOnly]
private static void LogCallback(int level, IntPtr message)
{
    string msg = Marshal.PtrToStringUTF8(message) ?? string.Empty;
    Console.WriteLine($"[{level}] {msg}");
}

[LibraryImport("mylib")]
private static unsafe partial void SetLogCallback(
    delegate* unmanaged<int, IntPtr, void> cb);

unsafe { SetLogCallback(&LogCallback); }
```

The method must be `static`, must not throw exceptions back to native code, and can only use blittable parameter types.

**Fallback (older TFMs or when instance state is needed): delegate with rooting**

```csharp
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] // Only needed on Windows x86
private delegate void LogCallbackDelegate(int level, IntPtr message);

// CRITICAL: prevent delegate from being garbage collected
private static LogCallbackDelegate? s_logCallback;

public static void EnableLogging(Action<int, string> handler)
{
    s_logCallback = (level, msgPtr) =>
    {
        string msg = Marshal.PtrToStringUTF8(msgPtr) ?? string.Empty;
        handler(level, msg);
    };
    SetLogCallback(s_logCallback);
}
```

If native code stores the function pointer, the delegate **must** stay rooted for its entire lifetime. A collected delegate means a crash.

**`GC.KeepAlive` for short-lived callbacks:** When converting a delegate to a function pointer with `Marshal.GetFunctionPointerForDelegate`, the GC does not track the relationship between the pointer and the delegate. Use `GC.KeepAlive` to prevent collection before the native call completes:

```csharp
var callback = new LogCallbackDelegate((level, msgPtr) =>
{
    string msg = Marshal.PtrToStringUTF8(msgPtr) ?? string.Empty;
    Console.WriteLine($"[{level}] {msg}");
});

IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(callback);
NativeUsesCallback(fnPtr);
GC.KeepAlive(callback); // prevent collection — fnPtr does not root the delegate
```

---

## Cross-Platform Library Loading

Use `NativeLibrary.SetDllImportResolver` for complex scenarios, or conditional compilation for simple cases. Use `CLong`/`CULong` for C `long`/`unsigned long`. Note: `CLong`/`CULong` with `LibraryImport` requires `[assembly: DisableRuntimeMarshalling]`.

```csharp
// Simple: conditional compilation
// WINDOWS, LINUX, MACOS are predefined only when targeting an OS-specific TFM
// (e.g., net8.0-windows). For portable TFMs (e.g., net8.0), these symbols are
// not defined — use the runtime resolver approach below instead.
#if WINDOWS
    private const string LibName = "mylib.dll";
#elif LINUX
    private const string LibName = "libmylib.so";
#elif MACOS
    private const string LibName = "libmylib.dylib";
#endif

// Complex: runtime resolver
NativeLibrary.SetDllImportResolver(typeof(MyLib).Assembly,
    (name, assembly, searchPath) =>
    {
        if (name != "mylib") return IntPtr.Zero;
        string libName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "mylib.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "libmylib.dylib" : "libmylib.so";
        NativeLibrary.TryLoad(libName, assembly, searchPath, out var handle);
        return handle;
    });
```

---

## Migrating DllImport to LibraryImport

For codebases targeting .NET 7+, migrating provides AOT compatibility and trimming safety.

1. Add `partial` to the containing class and make the method `static partial`
2. Replace `[DllImport]` with `[LibraryImport]`
3. Replace `CharSet` with `StringMarshalling`
4. Replace `SetLastError = true` with `SetLastPInvokeError = true`
5. Remove `CallingConvention` unless targeting Windows x86
6. Build and fix `SYSLIB1054`–`SYSLIB1057` analyzer warnings

Enable the interop analyzers:

```xml
<PropertyGroup>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
</PropertyGroup>
```

---

## Tooling

### CsWin32 (Win32 APIs)

For Win32 P/Invoke, prefer [Microsoft.Windows.CsWin32](https://github.com/microsoft/CsWin32) over hand-written signatures. It source-generates correct declarations from metadata. Add a `NativeMethods.txt` listing the APIs you need:

```bash
dotnet add package Microsoft.Windows.CsWin32
```

### CsWinRT (WinRT APIs)

For WinRT interop, use [Microsoft.Windows.CsWinRT](https://github.com/microsoft/CsWinRT) to generate .NET projections from `.winmd` files.

### Objective Sharpie (Objective-C APIs)

For binding Objective-C libraries (macOS/iOS), use [Objective Sharpie](https://learn.microsoft.com/previous-versions/xamarin/cross-platform/macios/binding/objective-sharpie) to generate initial P/Invoke and binding definitions from Objective-C headers.

---

## Validation

### Review checklist

- [ ] Every signature matches the native header exactly (types, sizes)
- [ ] Calling convention specified if targeting Windows x86; omitted otherwise
- [ ] String encoding is explicit — no reliance on defaults or `CharSet.Auto`
- [ ] Memory ownership is documented and matched (who allocates, who frees, with what)
- [ ] `SafeHandle` used for all native handles (no raw `IntPtr` escaping the interop layer)
- [ ] Delegates passed as callbacks are rooted to prevent GC collection
- [ ] `SetLastError`/`SetLastPInvokeError` set for APIs that use OS error codes
- [ ] Struct layout matches native (packing, alignment, field order)
- [ ] `CLong`/`CULong` used for C `long`/`unsigned long` in cross-platform code
- [ ] If using `CLong`/`CULong` with `LibraryImport`, `[assembly: DisableRuntimeMarshalling]` is applied
- [ ] No `bool` without explicit `MarshalAs` — always specify `UnmanagedType.Bool` (4-byte) or `UnmanagedType.U1` (1-byte) to ensure normalization across the language boundary.

### Runnable validation steps

1. **Build with interop analyzers enabled** — confirm zero `SYSLIB1054`–`SYSLIB1057` warnings:
   ```xml
   <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
   <EnableAotAnalyzer>true</EnableAotAnalyzer>
   ```
2. **Verify struct sizes match** — for every struct crossing the boundary, assert `Marshal.SizeOf<T>()` equals the native `sizeof`
3. **Round-trip test** — call the native function with known inputs and verify expected outputs
4. **Test with non-ASCII strings** — pass strings containing characters outside the ASCII range to confirm encoding is correct

## Reference Files

- **[references/type-mapping.md](references/type-mapping.md)** — Complete native-to-.NET type mapping table, struct layout patterns, blittable type rules. **Load when** encountering types not covered in Step 2 above, or when working with struct layout or blittable type questions.
- **[references/diagnostics.md](references/diagnostics.md)** — Common pitfalls, failure modes and recovery, debugging approach, external resources. **Load when** debugging an existing P/Invoke failure or reviewing interop code for correctness issues.
