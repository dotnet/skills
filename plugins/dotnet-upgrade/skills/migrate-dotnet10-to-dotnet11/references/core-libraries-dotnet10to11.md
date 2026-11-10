# Core .NET Libraries Breaking Changes (.NET 11)

These breaking changes affect all .NET 11 projects regardless of application type. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

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

### API obsoletions with custom diagnostic IDs (SYSLIB0064, SYSLIB0066)

**Impact: Medium (for projects using `TreatWarningsAsErrors`).** Two new obsoletion warnings (see also SYSLIB0065 in the cryptography reference):

- **SYSLIB0064:** `RSACryptoServiceProvider.Encrypt(byte[], bool)` and `Decrypt(byte[], bool)`. Use the overloads that take `RSAEncryptionPadding`.
- **SYSLIB0066:** `IStartupValidator`. Use `IAsyncStartupValidator`, and don't register one implementation under both interfaces.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/obsolete-apis

## Behavioral Changes

### Decimal and BigInteger floating-point conversions are correctly rounded (Preview 7)

**Impact: High (for financial or other decimal-sensitive code).** Conversions between `decimal` and `float`/`double`, and from `BigInteger` to binary floating-point types, now round the exact source value once. Previously, `float` and `double` to `decimal` kept only 7 and 15 significant digits, which hid the binary representation error. The change compiles without any warning and only shows at run time.

```csharp
double value = 1.23;
decimal converted = (decimal)value;
// .NET 10: 1.23
// .NET 11: 1.229999999999999982236431606
```

Affected APIs include `(decimal)` casts from `float`/`double`, `new decimal(double)`, `new decimal(float)`, `Convert.ToDecimal(double)`, `Convert.ToDecimal(float)`, `(double)`/`(float)` casts from `decimal`, `Decimal.CreateChecked/Saturating/Truncating`, `BigInteger` to `double`/`float`/`Half`/`BFloat16`, and the equivalent `IConvertible` and generic math paths. Code that checks the number of decimal places, compares against decimal literals, formats amounts, or stores expected values in tests can change behavior.

When such a conversion is a compile-time constant, a compiler from the .NET 11 SDK can embed the new result in the assembly, whatever the target framework. Projects that still target `net10.0` but build with the .NET 11 SDK can therefore see the new values too.

**Fix:**
- Search for `(decimal)` casts, `Convert.ToDecimal(`, and `new decimal(` applied to `double` or `float` values (including boxed values passed to `Convert`).
- Use `m` literals for values that are meant to be decimal (`123.4567m`, not `(decimal)123.4567`), including in test data.
- Where the previous result is required, round to 15 significant digits for a `double` source (7 for `float`) before converting. Format with `"G15"` (`"G7"`) and parse back, allowing exponent notation:

  ```csharp
  static decimal ToDecimalLikeNet10(double value) =>
      decimal.Parse(value.ToString("G15", CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture);
  ```
- Update tests and serialized expected values that encoded the old result. There is no compatibility switch.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions

### Math.Round and MathF.Round return correctly rounded results (Preview 7)

**Impact: Low–Medium.** `Math.Round(value, digits)` and `MathF.Round(value, digits)` no longer compute `Round(value * 10^digits) / 10^digits`. They return the correctly rounded result of the exact value, and accept any non-negative `digits`. For example, `Math.Round(655.925, 2, MidpointRounding.AwayFromZero)` returned `655.93` and now returns `655.92`, because the `double` value is slightly below the midpoint. `Math.Round(1.5, 16)` used to throw and now returns `1.5`.

**Fix:** Review this together with the decimal conversion change. Use `decimal` (and `decimal.Round`) for currency, and update tests that expected the old results. There is no compatibility switch.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/math-round-digits

### DateOnly and TimeOnly TryParse methods throw for invalid input (Preview 2)

**Impact: Low.** `DateOnly.TryParse*` and `TimeOnly.TryParse*` now throw `ArgumentException` for invalid `DateTimeStyles` values or format specifiers instead of returning `false`.

**Fix:** Pass valid styles and formats, or catch `ArgumentException` where the arguments come from untrusted input.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/dateonly-timeonly-tryparse-argumentexception

### Nullable.GetUnderlyingType throws for custom Type subclasses (Preview 4)

**Impact: Low.** `Nullable.GetUnderlyingType` now forwards to the new virtual `Type.GetNullableUnderlyingType()`, whose base implementation throws `NotSupportedException`. Built-in `Type` implementations are not affected.

**Fix:** Custom `Type` subclasses (for example, in reflection or metadata libraries) must override `GetNullableUnderlyingType()`.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/nullable-getunderlyingtype-throws

### Environment.GetCommandLineArgs()[0] preserves the apphost invocation name (Preview 7)

**Impact: Low.** When an app is started through its apphost, `Environment.GetCommandLineArgs()[0]` now keeps the name it was invoked with (a relative path or a symlink name) instead of the resolved path. `dotnet app.dll` is unchanged.

**Fix:** Use `Environment.ProcessPath` or `Assembly.GetEntryAssembly()?.Location` when you need the executable path.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/environment-getcommandlineargs-apphost-invocation-name

### CborReader and CborWriter enforce a default maximum nesting depth (Preview 5)

**Impact: Low.** `CborReader` throws `CborContentException` beyond a nesting depth of 64, and `CborWriter` throws `InvalidOperationException` beyond 1000. Previously there was no limit.

**Fix:** Set `CborReaderOptions.MaxDepth` or `CborWriterOptions.MaxDepth` if deeper nesting is legitimate.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/cbor-max-depth

### Named mutexes default to user scope on Unix (RC 2)

**Impact: Low–Medium (Unix only).** This change also ships in servicing updates (.NET 10.0.13, 9.0.21, and 8.0.32), so a patched .NET 10 app may already have it. Named mutexes are now scoped to the current user by default on Unix, and their backing files move to `$TMPDIR`, then `$XDG_RUNTIME_DIR`, then the user database home directory. Processes running as different users no longer share a named mutex. Containers running with a UID that has no user database entry can fail.

**Fix:** To share a mutex across users, pass `NamedWaitHandleOptions.None` to a `Mutex` constructor that takes `NamedWaitHandleOptions`, or set the runtimeconfig option `System.Threading.Mutex.UnsafeDefaultMutexScopeGlobal` to `true`. In containers without a user database entry, set `XDG_RUNTIME_DIR`.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/named-mutexes-unix-default-scope

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

## Microsoft.Extensions Changes

### Some Microsoft.Extensions packages are included in the shared framework (NU1510) (Preview 4)

**Impact: Medium (for projects using `TreatWarningsAsErrors`).** Nine packages are now part of the base shared framework: `Microsoft.Extensions.Caching.Abstractions`, `Configuration.Abstractions`, `DependencyInjection.Abstractions`, `Diagnostics.Abstractions`, `FileProviders.Abstractions`, `Hosting.Abstractions`, `Logging.Abstractions`, `Microsoft.Extensions.Options`, and `Microsoft.Extensions.Primitives`. An explicit `PackageReference` to one of them in a project that targets only `net11.0` or later raises NU1510, and the assemblies are no longer copied to the output. Multi-targeted projects that still need the package for an older TFM don't get the warning.

**Fix:** Remove the `PackageReference` for `net11.0`-only projects, or condition it on the older target frameworks. Libraries compiled against older versions of these packages may need to be recompiled against .NET 11 to avoid `MissingMethodException` or `TypeLoadException`.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/11/extensions-in-shared-framework

### IHost.RunAsync and IHost.StopAsync throw when a BackgroundService fails (Preview 3)

**Impact: Medium.** With the default `BackgroundServiceExceptionBehavior.StopHost`, a failing `BackgroundService` used to stop the host while `RunAsync`, `StopAsync`, and `WaitForShutdownAsync` still completed successfully. These methods now fail with the exception (or an `AggregateException` for several failures), which typically gives a non-zero process exit code.

**Fix:** If the process must still exit with code 0, wrap `await host.RunAsync()` in try/catch. See also the BackgroundService entry above.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/11/ihost-runasync-stopasync-throw-backgroundservice-failure

### Options validation generator reports SYSLIB1205 for explicit Validate implementations (RC 1)

**Impact: Low.** The `[OptionsValidator]` source generator now reports error SYSLIB1205 when the type also implements `IValidateOptions<T>.Validate` explicitly. Suppressing the diagnostic does not help.

**Fix:** Pick one owner: remove the explicit implementation and keep `[OptionsValidator]`, or remove `[OptionsValidator]` and implement validation by hand.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/11/options-validator-explicit-validate

### Empty strings in chained configuration override earlier providers (RC 1)

**Impact: Low–Medium.** `ChainedConfigurationProvider` (used by `AddConfiguration`) now treats an empty string as a value that overrides earlier providers. `null` is still treated as absent. Binding an empty string to a non-string type such as `int` throws `InvalidOperationException`.

**Fix:** Remove the key from the chained configuration, or make it `null`, if the earlier value should win. Normalize empty values before binding to non-string types.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/11/chained-configuration-empty-string

### Dependency injection scope disposal aggregates exceptions (Preview 2)

**Impact: Low.** Disposing a scope or the root `ServiceProvider` now attempts to dispose every captured service. A single failure is rethrown unchanged; several failures are thrown as an `AggregateException`. Previously disposal stopped at the first exception.

**Fix:** Handle both a single exception and `AggregateException` around scope disposal. Prefer `Dispose`/`DisposeAsync` implementations that don't throw.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/11/dependency-injection-disposal-exceptions

### Configuration reload behavior changes (Preview 7)

**Impact: Low.** Three related changes:

- **`ChangeToken.OnChange` async overloads:** New `Func<Task>` overloads mean recompiled `async` lambdas are no longer `async void`. Re-registration waits for the returned task, and changes during the callback are coalesced. Cast to `Action` to keep the old binding. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/11/changetoken-onchange-async-overloads-rebind-callbacks
- **`FileConfigurationProvider` reload token:** After a load failure ignored through `OnLoadException` (`Ignore = true`), the reload token no longer fires. Run recovery code from the `OnLoadException` callback. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/11/fileconfigurationprovider-reload-token-load-failure
- **`FileConfigurationSource.OnLoadException` and IO errors:** IO errors now reach the callback, so `FileLoadExceptionContext.Exception` can be any exception type. Avoid unconditional casts to `InvalidDataException` or `FileNotFoundException`. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/11/fileconfigurationsource-onloadexception-io-errors
