# Core .NET Libraries Breaking Changes (.NET 11)

These breaking changes affect all .NET 11 projects regardless of application type. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

> **Note:** .NET 11 is in preview. Additional breaking changes are expected in later previews.

## Obsoleted APIs

### NamedPipeClientStream constructor with `isConnected` parameter obsoleted (SYSLIB0063)

**Impact: High (for projects using `TreatWarningsAsErrors`).** The `NamedPipeClientStream` constructor overload that accepts a `bool isConnected` parameter has been obsoleted. The `isConnected` argument never had any effect — pipes created from an existing `SafePipeHandle` are always connected. A new constructor without the parameter has been added.

```csharp
// .NET 10: compiles without warning
var pipe = new NamedPipeClientStream(PipeDirection.InOut, isAsync: true, isConnected: true, safePipeHandle);

// .NET 11: SYSLIB0063 warning (error with TreatWarningsAsErrors)
// Fix: remove the isConnected parameter
var pipe = new NamedPipeClientStream(PipeDirection.InOut, isAsync: true, safePipeHandle);
```

**Fix:** Remove the `isConnected` argument and use the new 3-parameter constructor `NamedPipeClientStream(PipeDirection, bool isAsync, SafePipeHandle)`.

Source: https://github.com/dotnet/runtime/pull/120328

## Behavioral Changes

### DeflateStream and GZipStream write headers and footers for empty payloads

**Impact: Medium.** `DeflateStream` and `GZipStream` now always write format headers and footers to the output stream, even when no data is written. Previously, these streams produced no output for empty payloads.

This ensures the output is a valid compressed stream per the Deflate and GZip specifications, but code that checks for zero-length output will need updating.

```csharp
// .NET 10: output stream is empty (0 bytes)
// .NET 11: output stream contains valid headers/footers
using var ms = new MemoryStream();
using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
{
    // write nothing
}
// ms.Length was 0 in .NET 10, now > 0 in .NET 11
```

**Fix:** If your code checks for empty output to detect "no data was compressed," check the uncompressed byte count instead, or adjust the length check to account for headers/footers.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/deflatestream-gzipstream-empty-payload

### MemoryStream maximum capacity updated and exception behavior changed

**Impact: Medium.** The maximum capacity of `MemoryStream` has been updated and the exception behavior for exceeding capacity has changed.

**Fix:** Review code that creates very large `MemoryStream` instances or catches specific exception types related to capacity limits.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/memorystream-max-capacity

### TAR-reading APIs verify header checksums when reading

**Impact: Medium.** TAR-reading APIs now verify header checksums during reading. Previously, invalid checksums were silently ignored.

```csharp
// .NET 11: throws if TAR header checksum is invalid
using var reader = new TarReader(stream);
var entry = reader.GetNextEntry(); // may throw for corrupted files
```

**Fix:** Ensure TAR files have valid checksums. If processing hand-crafted or legacy TAR files, add error handling for checksum validation failures.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/tar-checksum-validation

### ZipArchive.CreateAsync eagerly loads ZIP archive entries

**Impact: Low.** `ZipArchive.CreateAsync` now eagerly loads ZIP archive entries instead of lazy loading. This may affect memory usage for very large archives.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/ziparchive-createasync-eager-load

### Environment.TickCount made consistent with Windows timeout behavior

**Impact: Low.** `Environment.TickCount` behavior has been made consistent with Windows timeout behavior. Code that relies on specific tick count wrapping or comparison patterns may need adjustment.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/environment-tickcount-windows-behavior
