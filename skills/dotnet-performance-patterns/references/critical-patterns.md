# Critical .NET Performance Anti-Patterns

24 patterns that cause deadlocks, crashes, order-of-magnitude regressions, or security vulnerabilities.
Every pattern here is a hard rule: violating it causes measurable harm in production.

## Async / Tasks

### 1. Avoid async void
🔴 **AVOID** | .NET Core+

`async void` cannot be awaited and crashes the process on unhandled exceptions. Only valid for event handlers.

❌
```csharp
public async void ProcessAsync()
{ await FetchAsync(); throw new Exception(); } // crashes process
```
✅
```csharp
public async Task ProcessAsync()
{ await FetchAsync(); } // exceptions stored in Task
```
**Impact: Unhandled exceptions crash the process; callers cannot observe completion.**

### 2. Never Block on Async (Sync-over-Async)
🔴 **AVOID** | .NET Core+

`.Wait()`, `.Result`, `.GetAwaiter().GetResult()` cause deadlocks on limited-concurrency contexts and thread pool starvation everywhere else.

❌
```csharp
public string GetData()
    => GetDataAsync().Result; // DEADLOCK or starvation
```
✅
```csharp
public async Task<string> GetDataAsync()
    => await GetDataInternalAsync();
```
**Impact: Deadlocks or thread pool starvation; wastes threads, destroys scalability.**

### 3. Never Await a ValueTask Multiple Times
🔴 **AVOID** | .NET Core 2.1+

A `ValueTask` must only be awaited once. The backing `IValueTaskSource` may be recycled — second await yields corrupt data.

❌
```csharp
ValueTask<int> vt = SomeMethodAsync();
int a = await vt;
int b = await vt; // UNDEFINED BEHAVIOR
```
✅
```csharp
int result = await SomeMethodAsync();
// reuse result; .AsTask() if you need Task semantics
```
**Impact: Undefined behavior — silent data corruption or exceptions.**

### 4. Don't Use lock with Async Code
🔴 **AVOID** | .NET Core+

`await` inside `lock` is a compile error. `Monitor.Enter/Exit` across async boundaries fails — exit may run on a different thread.

❌
```csharp
lock (_sync) { await DoWorkAsync(); } // CS1996
```
✅
```csharp
private readonly SemaphoreSlim _sem = new(1, 1);
await _sem.WaitAsync();
try { await DoWorkAsync(); } finally { _sem.Release(); }
```
**Impact: Compile error, or if bypassed, deadlocks and thread corruption.**

### 5. Async All the Way Down
🔴 **DO** | .NET Core+

Propagate async through the entire call chain. One sync-over-async point blocks a thread and can starve the pool under load.

❌
```csharp
public IActionResult Index()
{ var d = _svc.GetDataAsync().Result; return View(d); }
```
✅
```csharp
public async Task<IActionResult> Index()
{ var d = await _svc.GetDataAsync(); return View(d); }
```
**Impact: A single blocking call can cause cascading thread pool starvation.**

## Memory / Allocation

### 6. Use Span\<T\> Instead of Arrays for Slicing
🔴 **DO** | .NET Core 2.1+

`Span<T>` provides zero-allocation views. `Substring()` and `Array.Copy()` allocate on every call.

❌
```csharp
string sub = input.Substring(5, 10); // allocates new string
```
✅
```csharp
ReadOnlySpan<char> sub = input.AsSpan(5, 10); // zero allocation
```
**Impact: Eliminates per-slice allocations; 2-4x faster via vectorization.**

### 7. Use ArrayPool\<T\> for Temporary Buffers
🔴 **DO** | .NET Core+

Allocating/discarding byte arrays creates GC pressure. `ArrayPool<T>.Shared` provides reusable arrays.

❌
```csharp
byte[] buf = new byte[4096]; // allocation every call
```
✅
```csharp
byte[] buf = ArrayPool<byte>.Shared.Rent(4096);
try { Process(buf); } finally { ArrayPool<byte>.Shared.Return(buf); }
```
**Impact: Dramatically reduces GC pressure for buffer-heavy workloads.**

### 8. Avoid stackalloc in Loops
🔴 **AVOID** | .NET 5+

Stack memory may not be freed until method return. `stackalloc` in a loop accumulates and crashes with `StackOverflowException`.

❌
```csharp
for (int i = 0; i < 10_000; i++)
    Span<byte> buf = stackalloc byte[1024]; // accumulates
```
✅
```csharp
Span<byte> buf = stackalloc byte[1024];
for (int i = 0; i < 10_000; i++) { Process(buf); }
```
**Impact: StackOverflowException — unrecoverable, no catch possible.**

### 9. Avoid Boxing Value Types
🔴 **AVOID** | .NET Core+

Boxing wraps value types in heap objects. Sources: old `string.Format`, non-generic interface calls, non-generic enum methods.

❌
```csharp
string s = string.Format("{0}.{1}", major, minor); // boxes both
```
✅
```csharp
string s = $"{major}.{minor}"; // C# 10+, no boxing
```
**Impact: C# 10 interpolation ~40% faster, ~5x less allocation.**

## Strings

### 10. Use StringComparison.Ordinal for Non-Linguistic Comparisons
🔴 **DO** | .NET Core+

Default `string.IndexOf(string)` uses culture-aware comparison — 2-3x slower. Use `Ordinal` for protocols, paths, identifiers.

❌
```csharp
bool found = text.IndexOf("Content-Type") >= 0;
```
✅
```csharp
bool found = text.Contains("Content-Type", StringComparison.Ordinal);
```
**Impact: 2-3x faster; OrdinalIgnoreCase hash codes ~3.3x faster.**

### 11. Use AsSpan Instead of Substring
🔴 **DO** | .NET Core 2.1+

`Substring()` allocates a new string. `AsSpan()` provides a zero-allocation slice for parsing or comparison.

❌
```csharp
int val = int.Parse(str.Substring(5, 3));
```
✅
```csharp
int val = int.Parse(str.AsSpan(5, 3));
```
**Impact: Eliminates one string allocation per parse operation.**

## Regular Expressions

### 12. Use Source-Generated Regex [GeneratedRegex]
🔴 **DO** | .NET 7+

`[GeneratedRegex]` emits optimized C# at compile time — equal throughput to `Compiled` with near-zero startup. AOT-compatible.

❌
```csharp
private static readonly Regex s_re =
    new(@"\w+@\w+\.\w+", RegexOptions.Compiled);
```
✅
```csharp
[GeneratedRegex(@"\w+@\w+\.\w+")]
private static partial Regex EmailRegex();
```
**Impact: Near-zero startup; required for AOT/trimming scenarios.**

### 13. Use NonBacktracking for Untrusted Input
🔴 **DO** | .NET 7+

`NonBacktracking` guarantees O(n) worst-case. Without it, untrusted input triggers O(2^N) backtracking — a ReDoS vector.

❌
```csharp
var r = new Regex(userPattern); // O(2^N) worst case
```
✅
```csharp
var r = new Regex(userPattern, RegexOptions.NonBacktracking);
```
**Impact: Backtracking at N=25: 17+s. NonBacktracking: ~0.15ms.**

### 14. Avoid Nested Quantifiers (Catastrophic Backtracking)
🔴 **AVOID** | .NET Core+

Patterns like `(\w+)+$` cause O(2^N) backtracking on non-matching input — the primary cause of ReDoS vulnerabilities.

❌
```csharp
var r = new Regex(@"^(\w+)+$"); // hangs on crafted input
```
✅
```csharp
var r = new Regex(@"^\w+$", RegexOptions.NonBacktracking);
```
**Impact: Can hang process indefinitely on crafted input.**

### 15. Cache and Reuse Regex Instances
🔴 **DO** | .NET Core+

`new Regex()` parses, optimizes, and generates IL. Store in `static readonly` or use `[GeneratedRegex]`.

❌
```csharp
bool ok = new Regex(@"\d{3}-\d{4}").IsMatch(s); // rebuilds each call
```
✅
```csharp
private static readonly Regex s_re = new(@"\d{3}-\d{4}", RegexOptions.Compiled);
bool ok = s_re.IsMatch(s);
```
**Impact: Construction cost is orders of magnitude higher than matching.**

### 16. Set Timeouts on Regex with Untrusted Input
🔴 **DO** | .NET Core+

Always set a timeout for user-supplied patterns or inputs. Defense-in-depth even with `NonBacktracking`.

❌
```csharp
var r = new Regex(userPattern); // unbounded CPU
```
✅
```csharp
var r = new Regex(userPattern, RegexOptions.None, TimeSpan.FromSeconds(2));
```
**Impact: Prevents unbounded CPU consumption; no cost on normal matches.**

## Collections

### 17. Use TryGetValue Instead of ContainsKey + Indexer
🔴 **DO** | .NET Core+

`ContainsKey()` then indexer = two hash lookups. `TryGetValue` = one. Flagged by CA1854.

❌
```csharp
if (dict.ContainsKey(key))
    Use(dict[key]); // second lookup
```
✅
```csharp
if (dict.TryGetValue(key, out var value))
    Use(value);
```
**Impact: ~2x faster (50% reduction in lookup time).**

## LINQ

### 18. Avoid LINQ in Hot Paths
🔴 **AVOID** | .NET Core+

LINQ incurs delegate + enumerator allocations and virtual dispatch. In hot loops, use `foreach`.

❌
```csharp
bool found = items.Any(x => x.Name == target);
```
✅
```csharp
bool found = false;
foreach (var item in items)
    if (item.Name == target) { found = true; break; }
```
**Impact: Eliminates 1-3 allocations per call; measurable in tight loops.**

### 19. Don't Iterate IEnumerable Multiple Times
🔴 **AVOID** | .NET Core+

Multiple enumeration doubles cost and may produce different results from deferred sources. Flagged by CA1851.

❌
```csharp
foreach (Type t in types) { Validate(t); }
_types = types.ToArray(); // second enumeration
```
✅
```csharp
Type[] arr = types.ToArray();
foreach (Type t in arr) { Validate(t); }
_types = arr;
```
**Impact: Halves enumeration cost; prevents bugs from re-executing deferred queries.**

## JSON Serialization

### 20. Use System.Text.Json Source Generator
🔴 **DO** | .NET 6+

Source generator eliminates runtime reflection, generating serialization code at compile time. Required for AOT/trimming.

❌
```csharp
string json = JsonSerializer.Serialize(post); // reflection-based
```
✅
```csharp
[JsonSerializable(typeof(BlogPost))]
internal partial class AppJsonCtx : JsonSerializerContext { }
string json = JsonSerializer.Serialize(post, AppJsonCtx.Default.BlogPost);
```
**Impact: 37-44% faster; enables trimming and Native AOT.**

### 21. Cache JsonSerializerOptions
🔴 **DO** | .NET 5+

New `JsonSerializerOptions` per call re-generates metadata. In .NET 6 this caused 592x slowdown. Cache in `static readonly`.

❌
```csharp
JsonSerializer.Serialize(obj, new JsonSerializerOptions()); // 592x slower
```
✅
```csharp
private static readonly JsonSerializerOptions s_opts = new();
JsonSerializer.Serialize(obj, s_opts);
```
**Impact: Up to 592x slower without caching (.NET 6); always cache or use defaults.**

## Networking

### 22. Reuse HttpClient Instances
🔴 **DO** | .NET Core 2.1+

New `HttpClient` per request exhausts sockets (240s `TIME_WAIT`). Create one per endpoint; set `PooledConnectionLifetime` for DNS.

❌
```csharp
using var client = new HttpClient(); // socket exhaustion
await client.GetStringAsync(url);
```
✅
```csharp
private static readonly HttpClient s_http = new(new SocketsHttpHandler
{ PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
await s_http.GetStringAsync(url);
```
**Impact: Prevents socket exhaustion; 6-12x faster concurrent HTTPS.**

## General

### 23. Seal Classes for Devirtualization
🔴 **DO** | .NET Core 3.0+

Sealing lets the JIT devirtualize/inline virtual calls and use pointer comparison for type checks.

❌
```csharp
internal class MyHandler : Base
{ public override int Run() => 42; } // virtual dispatch
```
✅
```csharp
internal sealed class MyHandler : Base
{ public override int Run() => 42; } // devirtualized + inlined
```
**Impact: Virtual calls up to 500x faster; type checks ~25x faster.**

### 24. Use SearchValues\<T\> for Repeated Set Searches
🔴 **DO** | .NET 8+

`SearchValues<T>` pre-computes SIMD search algorithms. Create once in `static readonly`; raw `IndexOfAny` rebuilds vectors each call.

❌
```csharp
int pos = text.IndexOfAny("ABCDEF".ToCharArray());
```
✅
```csharp
private static readonly SearchValues<char> s_hex = SearchValues.Create("ABCDEF");
int pos = text.AsSpan().IndexOfAny(s_hex);
```
**Impact: 2-10x faster for chars; 10-30x faster for multi-string (.NET 9+).**
