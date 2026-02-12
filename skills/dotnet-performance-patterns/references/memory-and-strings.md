# Memory & String Patterns

Moderate-priority patterns for reducing allocations through spans, stack allocation, string interpolation, and smarter type design. These eliminate unnecessary heap pressure in .NET code.

### Use ReadOnlySpan\<byte\> for Constant Byte Data
🟡 **DO** assign constant byte arrays to `ReadOnlySpan<byte>` | .NET 5+

The compiler stores constant byte arrays assigned to `ReadOnlySpan<byte>` directly in assembly metadata — zero allocation, zero copy. Works for all blittable types.

❌
```csharp
byte[] data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
```
✅
```csharp
ReadOnlySpan<byte> data = [0x48, 0x65, 0x6C, 0x6C, 0x6F];
ReadOnlySpan<int> primes = [2, 3, 5, 7, 11, 13];
```

**Impact: ~100x faster access than static byte[] field, zero allocation.**

---

### Use stackalloc for Small Temporary Buffers
🟡 **DO** use `stackalloc` for small, fixed-size temporary buffers | .NET Core+

Stack allocation avoids heap allocation entirely — zero GC pressure, instant alloc/dealloc. Combine with `TryFormat` or `TryWrite` for allocation-free formatting.

❌
```csharp
char[] buffer = new char[64]; // heap allocation
guid.TryFormat(buffer, out int written);
```
✅
```csharp
Span<char> buffer = stackalloc char[64];
guid.TryFormat(buffer, out int written);
```

**Impact: Zero heap allocation, no GC pressure, instant alloc/dealloc.**

---

### Use C# 10+ String Interpolation Handlers
🟡 **DO** recompile on C# 10+ to get `DefaultInterpolatedStringHandler` | .NET 6+

C# 10 compiles `$""` strings using `DefaultInterpolatedStringHandler` instead of `string.Format`. This eliminates boxing, `object[]` allocation, and runtime format string parsing — no code changes needed.

❌
```csharp
// C# 9 and earlier — compiled as string.Format
string result = $"{major}.{minor}.{build}";
// Internally: string.Format("{0}.{1}.{2}", new object[] { major, minor, build });
```
✅
```csharp
// C# 10+ — compiled as handler (just recompile!)
string result = $"{major}.{minor}.{build}";
// Internally: handler.AppendFormatted(major); handler.AppendLiteral("."); ...
```

**Impact: ~45% faster, ~75% less allocation. Automatic on .NET 6 / C# 10.**

---

### Use Span.TryWrite for Allocation-Free Interpolation
🟡 **DO** use `MemoryExtensions.TryWrite` to format into `Span<char>` buffers | .NET 6+

Format interpolated strings directly into a `Span<char>` buffer. Combined with `stackalloc`, this is completely allocation-free.

❌
```csharp
string formatted = $"Date: {dt:R}"; // allocates string
destination.Write(formatted);
```
✅
```csharp
Span<char> buffer = stackalloc char[64];
buffer.TryWrite($"Date: {dt:R}", out int charsWritten);
```

**Impact: Zero heap allocation for formatting operations.**

---

### Use Span-Based Comparison Instead of Substring Equality
🟡 **DO** use `AsSpan().SequenceEqual()` instead of `Substring` for comparisons | .NET Core+

Creating a `Substring` just to compare it allocates a new string unnecessarily. `AsSpan().SequenceEqual()` performs the comparison with zero allocation.

❌
```csharp
if (s.Substring(i) == "INF") // allocates substring
```
✅
```csharp
if (s.AsSpan(i).SequenceEqual("INF")) // zero allocation
```

**Impact: Eliminates string allocation per comparison — adds up in parsing loops.**

---

### Use Span.Split() for Zero-Allocation Splitting
🟡 **DO** use `MemoryExtensions.Split` for allocation-free string splitting | .NET 9+

`MemoryExtensions.Split` returns a `ref struct` enumerator yielding `Range` values — no strings or arrays allocated. Ideal for high-throughput parsing.

❌
```csharp
string[] parts = input.Split(','); // allocates string[] and per-segment strings
```
✅
```csharp
foreach (Range range in input.AsSpan().Split(','))
{
    ReadOnlySpan<char> segment = input.AsSpan(range);
}
```

**Impact: 208 bytes → 0 bytes per split, 2x faster.**

---

### Use UTF8 String Literals (u8 suffix)
🟡 **DO** use the `u8` suffix for compile-time UTF8 `ReadOnlySpan<byte>` | .NET 7+

The `u8` suffix creates `ReadOnlySpan<byte>` with UTF8 data at compile time. Zero runtime transcoding or allocation.

❌
```csharp
byte[] header = Encoding.UTF8.GetBytes("Content-Type"); // runtime transcoding + alloc
```
✅
```csharp
ReadOnlySpan<byte> header = "Content-Type"u8; // compile-time, zero allocation
```

**Impact: 17ns → 0.006ns — eliminates runtime transcoding entirely.**

---

### Use ReadOnlySpan\<char\> Pattern Matching with switch
🟡 **DO** use `switch` on `ReadOnlySpan<char>` for allocation-free string matching | C# 11+

C# 11 supports `switch` on `ReadOnlySpan<char>`, enabling allocation-free string matching on trimmed or sliced text.

❌
```csharp
switch (attr.Value.Trim()) { case "preserve": /* ... */ break; } // allocates trimmed string
```
✅
```csharp
switch (attr.Value.AsSpan().Trim())
{
    case "preserve": return Preserve;
    case "default": return Default;
}
```

**Impact: Eliminates string allocation from Trim() in switch-based dispatch.**

---

### Use params ReadOnlySpan\<T\> to Eliminate Array Allocations
🟡 **DO** add `params ReadOnlySpan<T>` overloads to library methods | C# 13 / .NET 9+

The compiler prefers `params ReadOnlySpan<T>` over `params T[]` and generates `[InlineArray]`-based stack allocation. Callers benefit by simply recompiling.

❌
```csharp
public static void Log(params string[] messages) { /* ... */ }
Log("Starting", "Processing", "Done"); // allocates string[3]
```
✅
```csharp
public static void Log(params ReadOnlySpan<string> messages) { /* ... */ }
Log("Starting", "Processing", "Done"); // stack-allocated, zero heap allocation
```

**Impact: Eliminates params array allocation. E.g., Path.Join with 5+ segments saves 64 bytes per call.**

---

### Implement IEquatable\<T\> on Structs to Avoid Boxing
🟡 **DO** implement `IEquatable<T>` on all structs used in collections or comparisons | .NET Core+

Without `IEquatable<T>`, struct equality uses reflection-based comparison or boxing to call `object.Equals`. Implementing it enables the JIT to devirtualize and inline the comparison.

❌
```csharp
public struct Point
{
    public int X, Y;
    // Default equality uses reflection — slow and allocates
}
```
✅
```csharp
public struct Point : IEquatable<Point>
{
    public int X, Y;
    public bool Equals(Point other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Point p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
```

**Impact: ~2.5x faster equality checks, eliminates boxing allocations in Dictionary/HashSet lookups.**
