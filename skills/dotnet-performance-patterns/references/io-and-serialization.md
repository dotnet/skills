# I/O, Serialization & General Patterns

Moderate-priority patterns for JSON serialization, file/network I/O, number formatting, and general JIT-friendly coding. These reduce overhead across common .NET application scenarios.

### Use JsonSerializerContext for Trimming/AOT
🟡 **DO** use source-generated `JsonSerializerContext` for trim and AOT support | .NET 6+

The JSON source generator enables full trimming compatibility and Native AOT compilation. Without it, `JsonSerializer` relies on reflection which is not trim-safe. The generated code contains all property accessors and metadata.

❌
```csharp
// Reflection-based — not trim-safe, breaks in AOT
var json = JsonSerializer.Serialize(blogPost);
```
✅
```csharp
[JsonSerializable(typeof(BlogPost))]
internal partial class AppJsonContext : JsonSerializerContext { }

var json = JsonSerializer.Serialize(blogPost, AppJsonContext.Default.BlogPost);
```

**Impact: Enables trimming (reduces app size), eliminates startup reflection cost, required for Native AOT.**

---

### Use Utf8JsonWriter for Maximum Serialization Performance
🟡 **DO** use `Utf8JsonWriter` directly when maximum throughput is needed | .NET Core 3.0+

For the absolute fastest JSON serialization, bypass `JsonSerializer` entirely and write directly with `Utf8JsonWriter`. This eliminates all indirection, metadata lookups, and polymorphic dispatch.

❌
```csharp
var json = JsonSerializer.Serialize(blogPost); // reflection + metadata overhead
```
✅
```csharp
using var writer = new Utf8JsonWriter(stream);
writer.WriteStartObject();
writer.WriteString("Title", blogPost.Title);
writer.WriteNumber("PublicationYear", blogPost.PublicationYear);
writer.WriteEndObject();
writer.Flush();
```

**Impact: ~44% faster than reflection-based JsonSerializer. Use when you know the schema and need maximum throughput.**

---

### Use HttpCompletionOption.ResponseHeadersRead for Streaming
🟡 **DO** use `ResponseHeadersRead` when downloading large responses | .NET Core 3.0+

Starts processing as soon as headers arrive rather than buffering the entire response body into memory. Reduces memory usage and doubles throughput for large downloads.

❌
```csharp
var response = await client.GetAsync(uri); // buffers entire response
```
✅
```csharp
using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
using var stream = await response.Content.ReadAsStreamAsync();
await stream.CopyToAsync(destinationStream);
```

**Impact: ~2x faster for large downloads (10MB+), dramatically reduced memory usage.**

---

### Use Async FileStream Operations
🟡 **DO** use `FileStream` with `useAsync: true` for scalable file I/O | .NET 6+

`FileStream` was entirely rewritten in .NET 6, fixing long-standing async I/O issues on Windows. Async operations are now truly async and nearly allocation-free. Always set `useAsync: true`.

❌
```csharp
// Synchronous reads or default FileStream — blocks threads
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

---

### Use Memory\<byte\> Overloads for Stream.ReadAsync/WriteAsync
🟡 **DO** use `Memory<byte>`-based stream overloads instead of `byte[]` overloads | .NET 5+

The `Memory<byte>` overloads return `ValueTask<int>` / `ValueTask` which can complete synchronously without allocation. The older `byte[]` overloads return `Task<int>` / `Task` which always allocate.

❌
```csharp
await stream.ReadAsync(buffer, 0, buffer.Length);  // always allocates Task
await stream.WriteAsync(buffer, 0, buffer.Length);
```
✅
```csharp
await stream.ReadAsync(buffer.AsMemory());  // ValueTask, avoids allocation
await stream.WriteAsync(buffer.AsMemory());
```

**Impact: Eliminates ~72 KB allocation per 1,000 read/write pairs on NetworkStream.**

---

### Use Span-Based TryFormat for Number Formatting
🟡 **DO** use `TryFormat` to format numbers into `Span<char>` buffers | .NET Core 2.1+

`TryFormat` formats numbers directly into a `Span<char>` buffer, avoiding the string allocation that `ToString()` incurs. All numeric types implement `ISpanFormattable`.

❌
```csharp
string formatted = value.ToString(); // allocates string
destination.Write(formatted);
```
✅
```csharp
Span<char> buffer = stackalloc char[20];
if (value.TryFormat(buffer, out int charsWritten))
    destination.Write(buffer[..charsWritten]);
```

**Impact: Int32.ToString() ~2x faster in .NET Core 2.1, Int32 parsing ~5x faster in .NET Core 3.0.**

---

### Enum.HasFlag Is Now a JIT Intrinsic — Use It Freely
🟡 **DO** use `Enum.HasFlag` instead of manual bit testing | .NET Core 2.1+

`Enum.HasFlag` was historically expensive (boxing + allocation). Since .NET Core 2.1, it is a JIT intrinsic that generates the same code as manual bit testing — zero allocation, ~50x faster than .NET Framework.

❌
```csharp
// Manual bit testing — was necessary on .NET Framework
if ((flags & MyEnum.Option1) != 0) { /* ... */ }
```
✅
```csharp
// Use HasFlag freely on .NET Core 2.1+
if (flags.HasFlag(MyEnum.Option1)) { /* ... */ }
```

**Impact: ~50x faster than .NET Framework, zero allocation. No reason to avoid it anymore.**

---

### Use Random.Shared Instead of new Random()
🟡 **DO** use `Random.Shared` for thread-safe random number generation | .NET 6+

`Random.Shared` is a thread-safe static instance using the fast xoshiro256** algorithm. Avoids the constructor cost and heap allocation of creating a new `Random` instance.

❌
```csharp
int value = new Random().Next(); // 115ns, 72B allocation
```
✅
```csharp
int value = Random.Shared.Next(); // 5ns, 0B allocation
```

**Impact: ~21x faster, zero allocation.**

---

### Use static readonly for Runtime Devirtualization
🟡 **DO** store implementations in `static readonly` fields for JIT devirtualization | .NET Core 3.0+

The JIT can see the actual runtime type in `static readonly` fields, devirtualizing and inlining virtual method calls. With tiered compilation, `static readonly` fields become constants in tier 1, enabling dead code elimination.

❌
```csharp
private static Base s_impl = new DerivedImpl(); // JIT can't devirtualize
s_impl.Process(); // virtual call
```
✅
```csharp
private static readonly Base s_impl = new DerivedImpl();
s_impl.Process(); // devirtualized + inlined in .NET Core 3.0+

private static readonly bool s_feature =
    Environment.GetEnvironmentVariable("Feature") == "1";
// JIT eliminates dead branch in tier 1
```

**Impact: Virtual call eliminated entirely — can be inlined to zero overhead. Dead code elimination in tier 1.**

---

### Avoid Explicit Static Constructors — Use Field Initializers
🟡 **AVOID** explicit `static` constructors when field initializers suffice | .NET Core 3.0+

Types with explicit `static` constructors are not marked `beforefieldinit`, giving the runtime less flexibility in initialization timing. This prevents certain JIT optimizations and may require additional locking.

❌
```csharp
class Foo
{
    static readonly int s_value;
    static Foo() { s_value = ComputeValue(); } // no beforefieldinit
}
```
✅
```csharp
class Foo
{
    static readonly int s_value = ComputeValue(); // gets beforefieldinit
}
```

**Impact: Enables better JIT optimization and reduces potential lock overhead on static method access.**
