# Native-to-.NET Type Mapping

Complete reference for mapping C/Win32 types to .NET types. Every parameter must match exactly — this is where most P/Invoke bugs originate.

## Primitive Types

| C / Win32 Type | .NET Type | Notes |
|----------------|-----------|-------|
| `int` | `int` | Always 32-bit in Win32 ABI |
| `int32_t` | `int` | |
| `uint32_t` | `uint` | |
| `int64_t` | `long` | |
| `uint64_t` | `ulong` | |
| `DWORD` | `uint` | |
| `HRESULT` | `int` | Some tools project this as an enumeration |
| `float` | `float` | |
| `double` | `double` | |

## Dangerous Types (Most Common Bug Sources)

These types have non-obvious mappings that frequently cause bugs:

| C / Win32 Type | .NET Type | Why It's Dangerous |
|----------------|-----------|-------------------|
| `long` | **`CLong`** | C `long` is 32-bit on Windows, 64-bit on 64-bit Unix — never use `int` or `long`. With `LibraryImport`, requires `[assembly: DisableRuntimeMarshalling]` or you get SYSLIB1051. With `DllImport`, works without it |
| `size_t` | `nuint` / `UIntPtr` | Pointer-sized. Use `nuint` on .NET 8+ and `UIntPtr` on earlier .NET. Never use `ulong` — causes stack corruption on 32-bit |
| `intptr_t` | `nint` | Pointer-sized |
| `BOOL` (Win32) | `int` | Not `bool` — Win32 `BOOL` is 4 bytes |
| `bool` (C99) | `[MarshalAs(UnmanagedType.U1)] bool` | Must specify 1-byte marshal |
| `void*` | `void*` | Requires `unsafe` context |

## Handle and String Types

| C / Win32 Type | .NET Type | Notes |
|----------------|-----------|-------|
| `HANDLE`, `HWND` | `SafeHandle` | Prefer over raw `IntPtr` |
| `LPWSTR` / `wchar_t*` | `string` | UTF-16 on Windows (lowest cost for `in` strings). Avoid in cross-platform code — `wchar_t` width is compiler-defined (typically UTF-32 on non-Windows) |
| `LPSTR` / `char*` | `string` | Must specify encoding (ANSI or UTF-8). Always requires marshalling cost for `in` parameters |

## Blittable Types

Blittable types have identical managed and native layouts — zero marshalling overhead.

**Blittable:** `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `nint`, `nuint`, and structs of only blittable fields. With `[assembly: DisableRuntimeMarshalling]`, `bool` (1 byte) and `char` (2 bytes, `char16_t`) are also treated as blittable.

**Not blittable (without `DisableRuntimeMarshalling`):** `bool`, `char`, `string`, `decimal`, anything with `MarshalAs`.

## Struct Layout

```csharp
// Sequential layout (most common)
[StructLayout(LayoutKind.Sequential)]
internal struct Vec3 { public float X, Y, Z; }

// Explicit layout for unions
// C: typedef union { int32_t i; float f; } Value;
[StructLayout(LayoutKind.Explicit, Size = 4)]
internal struct Value
{
    [FieldOffset(0)] public int I;
    [FieldOffset(0)] public float F;
}

// Non-default packing
// C: #pragma pack(push, 1)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct PackedHeader
{
    public byte Magic;
    public uint Size;    // At offset 1, not 4
    public ushort Flags; // At offset 5, not 8
}
```
