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

### Globalization: Japanese Calendar minimum supported date corrected

**Impact: Low.** The minimum supported date for the Japanese Calendar has been corrected. Code using very early dates in the Japanese Calendar may be affected.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/11/japanese-calendar-min-date

### ZipArchive now validates CRC32 when reading entries (Preview 3)

**Impact: Low–Medium.** ZIP archive reads now validate the CRC32 checksum of each entry. Previously, corrupt or truncated archives were silently accepted; they now throw `InvalidDataException`.

**Fix:** Ensure ZIP files are not corrupted. If processing partially-written or legacy archives, add error handling for `InvalidDataException`.

Source: https://github.com/dotnet/runtime/pull/124766

### Unhandled BackgroundService exceptions now stop the host (Preview 3)

**Impact: Medium.** Unhandled exceptions thrown from `BackgroundService.ExecuteAsync()` now propagate and stop the host application. Previously they were silently swallowed.

```csharp
// .NET 10: exception silently swallowed, host continues
// .NET 11: exception propagates, host stops
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    throw new InvalidOperationException("oops"); // now kills the host
}

// FIX: Add proper exception handling
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    try
    {
        // ... work ...
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogError(ex, "Background service failed");
    }
}
```

**Fix:** Add try/catch in `ExecuteAsync()` for any `BackgroundService` that should not crash the host on failure.

Source: https://github.com/dotnet/runtime/pull/124863

### TarWriter emits HardLink entries for hard-linked files (Preview 3)

**Impact: Low.** When `TarWriter` archives a directory containing hard links, the same inode encountered more than once is now written as a `HardLink` entry pointing back to the first occurrence, rather than duplicating the file data.

**Fix:** If consuming tar archives produced by .NET code, ensure the reader handles `HardLink` entry types.

Source: https://github.com/dotnet/runtime/pull/123874

### Zstandard APIs moved from preview package to System.IO.Compression (Preview 3)

**Impact: Low.** `ZstandardStream` and related APIs that were previously in the `System.IO.Compression.Zstandard` preview NuGet package are now in-box in `System.IO.Compression`.

**Fix:** Remove the `<PackageReference Include="System.IO.Compression.Zstandard" />` preview package if present. The APIs are now available without any additional package reference.

Source: https://github.com/dotnet/runtime/pull/114545
