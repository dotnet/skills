# I/O, Serialization & General Patterns

### Use HttpCompletionOption.ResponseHeadersRead for Streaming
🟡 **DO** use `ResponseHeadersRead` when downloading large responses | .NET Core 3.0+

❌
```csharp
var response = await client.GetAsync(uri);
```
✅
```csharp
using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
using var stream = await response.Content.ReadAsStreamAsync();
await stream.CopyToAsync(destinationStream);
```

**Impact: ~2x faster for large downloads (10MB+), dramatically reduced memory usage.**

### Use Async FileStream Operations
🟡 **DO** use `FileStream` with `useAsync: true` for scalable file I/O | .NET 6+

❌
```csharp
using var fs = new FileStream(path, FileMode.Open);
```
✅
```csharp
await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
    FileShare.Read, bufferSize: 4096, useAsync: true);

byte[] buffer = new byte[1024];
while (await fs.ReadAsync(buffer) != 0) { /* process */ }
```

**Impact: Up to 3x faster async reads; allocation reduced from megabytes to hundreds of bytes.**

### Use Memory\<byte\> Overloads for Stream.ReadAsync/WriteAsync
🟡 **DO** use `Memory<byte>`-based stream overloads instead of `byte[]` overloads | .NET 5+

❌
```csharp
await stream.ReadAsync(buffer, 0, buffer.Length);
await stream.WriteAsync(buffer, 0, buffer.Length);
```
✅
```csharp
await stream.ReadAsync(buffer.AsMemory());
await stream.WriteAsync(buffer.AsMemory());
```

**Impact: Eliminates ~72 KB allocation per 1,000 read/write pairs on NetworkStream.**

### Use Span-Based TryFormat for Number Formatting
🟡 **DO** use `TryFormat` to format numbers into `Span<char>` buffers | .NET Core 2.1+

❌
```csharp
string formatted = value.ToString();
destination.Write(formatted);
```
✅
```csharp
Span<char> buffer = stackalloc char[20];
if (value.TryFormat(buffer, out int charsWritten))
    destination.Write(buffer[..charsWritten]);
```

**Impact: Int32.ToString() ~2x faster in .NET Core 2.1, Int32 parsing ~5x faster in .NET Core 3.0.**

### Use static readonly for Runtime Devirtualization
🟡 **DO** store implementations in `static readonly` fields for JIT devirtualization | .NET Core 3.0+

❌
```csharp
private static Base s_impl = new DerivedImpl();
s_impl.Process();
```
✅
```csharp
private static readonly Base s_impl = new DerivedImpl();
s_impl.Process();

private static readonly bool s_feature =
    Environment.GetEnvironmentVariable("Feature") == "1";
```

**Impact: Virtual call eliminated entirely — can be inlined to zero overhead. Dead code elimination in tier 1.**

### Avoid Explicit Static Constructors — Use Field Initializers
🟡 **AVOID** explicit `static` constructors when field initializers suffice | .NET Core 3.0+

❌
```csharp
class Foo
{
    static readonly int s_value;
    static Foo() { s_value = ComputeValue(); }
}
```
✅
```csharp
class Foo
{
    static readonly int s_value = ComputeValue();
}
```

**Impact: Enables better JIT optimization and reduces potential lock overhead on static method access.**

## Detection

Scan recipes for I/O and serialization anti-patterns. Run these and report exact counts.

```bash
# new HttpClient() (socket exhaustion risk)
grep -rn --include='*.cs' 'new HttpClient(' --exclude-dir=bin --exclude-dir=obj . | wc -l

# new JsonSerializerOptions() not cached (592x slower in .NET 6)
grep -rn --include='*.cs' 'new JsonSerializerOptions' --exclude-dir=bin --exclude-dir=obj . | grep -v 'static\|readonly' | wc -l
```

### Patterns Requiring Manual Review

- **`JsonSerializer.Serialize/Deserialize` without source-gen context**: Can't determine from grep if a context parameter is passed
