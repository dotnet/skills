# Core .NET Libraries Breaking Changes (.NET 9)

These breaking changes affect all .NET 9 projects regardless of application type.

## Source-Incompatible Changes

### C# overload resolution prefers `params` span-type overloads

**Impact: High.** .NET 9 added `params ReadOnlySpan<T>` overloads to many core methods. C# 13 overload resolution prefers `params Span<T>`/`params ReadOnlySpan<T>` over `params T[]`. This causes errors inside `Expression` lambdas, which cannot contain `ref struct` types.

Affected methods include: `String.Join`, `String.Concat`, `String.Format`, `String.Split`, `Path.Combine`, `Path.Join`, `Task.WhenAll`, `Task.WhenAny`, `Task.WaitAll`, `Console.Write`, `Console.WriteLine`, `StringBuilder.AppendFormat`, `StringBuilder.AppendJoin`, `CancellationTokenSource.CreateLinkedTokenSource`, `ImmutableArray.Create`, `Delegate.Combine`, `JsonArray` constructors, `JsonTypeInfoResolver.Combine`, and more.

```csharp
// BREAKS — CS8640/CS9226 inside Expression lambda
Expression<Func<string, string, string>> join =
    (x, y) => string.Join("", x, y);  // binds to ReadOnlySpan overload
```

**Fix:** Pass an explicit array to force binding to the `params T[]` overload:
```csharp
Expression<Func<string, string, string>> join =
    (x, y) => string.Join("", new string[] { x, y });
```

### Ambiguous overload resolution affecting StringValues

**Impact: Medium.** `StringValues` (from `Microsoft.Extensions.Primitives`) has implicit operators for `string` and `string[]` that conflict with the new `params Span<T>` overloads. The compiler throws CS0121 for ambiguous calls.

**Fix:** Explicitly cast arguments to the appropriate type or use named parameters.

### New TimeSpan.From*() overloads that take integers

**Impact: Low (primarily F#).** New integer overloads of `TimeSpan.FromDays`, `FromHours`, `FromMinutes`, etc. cause ambiguity in F# code. Specify the argument type to select the correct overload.

### String.Trim(params ReadOnlySpan<char>) overload removed

**Impact: Medium.** The `Trim`, `TrimStart`, and `TrimEnd` overloads accepting `ReadOnlySpan<char>` were added in .NET 9 previews but removed in GA because they caused behavioral changes with common extension methods (e.g., `"prefixinfixsuffix".TrimEnd("suffix")` would change behavior).

Code compiled against .NET 9 previews that explicitly passes `ReadOnlySpan<char>` to these overloads may fail with `MissingMethodException` at runtime when run on the GA runtime, or fail to compile when retargeted to .NET 9 GA. Code using `params char[]` continues to work.

```csharp
// BREAKS — compiled against .NET 9 Preview with ReadOnlySpan<char> overloads
static string TrimLogEntry(string str)
{
    ReadOnlySpan<char> trimChars = [';', ',', '.'];
    return str.Trim(trimChars); // calls Trim(ReadOnlySpan<char>) in previews only
}

// Fix — target GA and use char[] so Trim binds to existing overloads
static string TrimLogEntry(string str)
{
    char[] trimChars = [';', ',', '.'];
    return str.Trim(trimChars); // calls Trim(char[]) / Trim(params char[])
}
```

> **Note:** Assemblies compiled against .NET 9 Preview 6 through RC2 must be recompiled to avoid `MissingMethodException` at runtime.

### API obsoletions (SYSLIB0054–SYSLIB0057)

| Diagnostic | What's obsolete | Replacement |
|------------|----------------|-------------|
| SYSLIB0054 | `Thread.VolatileRead`/`Thread.VolatileWrite` | `Volatile.Read`/`Volatile.Write` |
| SYSLIB0055 | `AdvSimd.ShiftRightLogicalRoundedNarrowingSaturate*` signed overloads | Unsigned overloads |
| SYSLIB0056 | `Assembly.LoadFrom` with `AssemblyHashAlgorithm` | Overloads without `AssemblyHashAlgorithm` |
| SYSLIB0057 | `X509Certificate2`/`X509Certificate` binary/file constructors and `X509Certificate2Collection.Import` | `X509CertificateLoader` methods |

These use custom diagnostic IDs — suppressing `CS0618` does not suppress them.

### New version of some OOB packages

**Impact: Low.** Some out-of-band packages have updated major versions. Review and update your package references.

## Behavioral Changes

### BinaryFormatter always throws

**Impact: High.** `BinaryFormatter.Serialize` and `BinaryFormatter.Deserialize` now always throw `NotSupportedException` regardless of any configuration. The `EnableUnsafeBinaryFormatterSerialization` AppContext switch has been removed. See `serialization-networking-dotnet8to9.md` for full details.

### BigInteger maximum length restriction

**Impact: Low.** `BigInteger` is now limited to `(2^31) - 1` bits (approximately 2.14 billion bits / ~256 MB). Values exceeding this throw `OverflowException`.

### Default InlineArray Equals() and GetHashCode() throw

`Equals()` and `GetHashCode()` on types marked with `[InlineArrayAttribute]` now throw instead of returning incorrect results.

### Inline array struct size limit is enforced

**Impact: Low.** `[InlineArray]` structs with a byte size exceeding 1 MiB (1,048,576 bytes) now fail to load at runtime.

### Creating type of array of System.Void not allowed

Attempting to create `typeof(void[])` or similar constructs now throws `TypeLoadException`.

### EnumConverter validates registered types

`EnumConverter` now validates that the type passed is actually an enum, throwing for non-enum types.

### FromKeyedServicesAttribute no longer injects non-keyed parameter

**Impact: Medium.** When `[FromKeyedServices("key")]` is used and the keyed service isn't registered, it no longer falls back to injecting a non-keyed service. Instead, `InvalidOperationException` is thrown.

### IncrementingPollingCounter initial callback is asynchronous

The initial measurement callback for `IncrementingPollingCounter` is now invoked asynchronously.

### InMemoryDirectoryInfo prepends rootDir to files

`InMemoryDirectoryInfo` now prepends the `rootDir` to file paths, which may change how matching works.

### RuntimeHelpers.GetSubArray returns different type

`RuntimeHelpers.GetSubArray` may return a different concrete array type than before.

### Support for empty environment variables

Empty environment variables are now supported and no longer treated as unset.

### ZipArchiveEntry names and comments respect UTF8 flag

`ZipArchiveEntry` now correctly uses UTF-8 encoding for names and comments when the UTF-8 flag is set in the entry header, which may change how non-ASCII entry names are read.

### Adding ZipArchiveEntry with CompressionLevel sets header flags

Adding a `ZipArchiveEntry` with an explicit `CompressionLevel` now sets the general-purpose bit flags in the ZIP central directory header.

### Altered UnsafeAccessor support for non-open generics

`UnsafeAccessor` behavior changed for non-open generic types.

### BinaryReader.ReadString() returns "\uFFFD" on malformed sequences

`BinaryReader.ReadString()` now returns the Unicode replacement character instead of throwing for malformed byte sequences.

### Other behavioral changes (lower impact)

- `ServicePointManager` (SYSLIB0014) is now fully obsolete — settings do not affect `SslStream` or `HttpClient`
- `AuthenticationManager` (SYSLIB0009) methods now no-op or throw `PlatformNotSupportedException`
