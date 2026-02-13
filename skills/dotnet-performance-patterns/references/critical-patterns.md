# Critical .NET Performance Anti-Patterns

24 patterns that cause deadlocks, crashes, order-of-magnitude regressions, or security vulnerabilities.

## Async / Tasks

### 1. Avoid async void
🔴 **AVOID** | .NET Core+

❌
```csharp
public async void ProcessAsync()
{ await FetchAsync(); throw new Exception(); }
```
✅
```csharp
public async Task ProcessAsync()
{ await FetchAsync(); }
```
**Impact: Unhandled exceptions crash the process; callers cannot observe completion.**

### 2. Never Block on Async (Sync-over-Async)
🔴 **AVOID** | .NET Core+

❌
```csharp
public string GetData()
    => GetDataAsync().Result;
```
✅
```csharp
public async Task<string> GetDataAsync()
    => await GetDataInternalAsync();
```
**Impact: Deadlocks or thread pool starvation; wastes threads, destroys scalability.**

### 3. Never Await a ValueTask Multiple Times
🔴 **AVOID** | .NET Core 2.1+

❌
```csharp
ValueTask<int> vt = SomeMethodAsync();
int a = await vt;
int b = await vt;
```
✅
```csharp
int result = await SomeMethodAsync();
```
**Impact: Undefined behavior — silent data corruption or exceptions.**

### 4. Don't Use lock with Async Code
🔴 **AVOID** | .NET Core+

❌
```csharp
lock (_sync) { await DoWorkAsync(); }
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

❌
```csharp
string sub = input.Substring(5, 10);
```
✅
```csharp
ReadOnlySpan<char> sub = input.AsSpan(5, 10);
```
**Impact: Eliminates per-slice allocations; 2-4x faster via vectorization.**

### 7. Use ArrayPool\<T\> for Temporary Buffers
🔴 **DO** | .NET Core+

❌
```csharp
byte[] buf = new byte[4096];
```
✅
```csharp
byte[] buf = ArrayPool<byte>.Shared.Rent(4096);
try { Process(buf); } finally { ArrayPool<byte>.Shared.Return(buf); }
```
**Impact: Dramatically reduces GC pressure for buffer-heavy workloads.**

### 8. Avoid stackalloc in Loops
🔴 **AVOID** | .NET 5+

❌
```csharp
for (int i = 0; i < 10_000; i++)
    Span<byte> buf = stackalloc byte[1024];
```
✅
```csharp
Span<byte> buf = stackalloc byte[1024];
for (int i = 0; i < 10_000; i++) { Process(buf); }
```
**Impact: StackOverflowException — unrecoverable, no catch possible.**

### 9. Avoid Boxing Value Types
🔴 **AVOID** | .NET Core+

❌
```csharp
string s = string.Format("{0}.{1}", major, minor);
```
✅
```csharp
string s = $"{major}.{minor}";
```
**Impact: C# 10 interpolation ~40% faster, ~5x less allocation.**

## Strings

### 10. Use StringComparison.Ordinal for Non-Linguistic Comparisons
🔴 **DO** | .NET Core+

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

❌
```csharp
var r = new Regex(userPattern);
```
✅
```csharp
var r = new Regex(userPattern, RegexOptions.NonBacktracking);
```
**Impact: Backtracking at N=25: 17+s. NonBacktracking: ~0.15ms.**

### 14. Avoid Nested Quantifiers (Catastrophic Backtracking)
🔴 **AVOID** | .NET Core+

❌
```csharp
var r = new Regex(@"^(\w+)+$");
```
✅
```csharp
var r = new Regex(@"^\w+$", RegexOptions.NonBacktracking);
```
**Impact: Can hang process indefinitely on crafted input.**

### 15. Cache and Reuse Regex Instances
🔴 **DO** | .NET Core+

❌
```csharp
bool ok = new Regex(@"\d{3}-\d{4}").IsMatch(s);
```
✅
```csharp
private static readonly Regex s_re = new(@"\d{3}-\d{4}", RegexOptions.Compiled);
bool ok = s_re.IsMatch(s);
```
**Impact: Construction cost is orders of magnitude higher than matching.**

### 16. Set Timeouts on Regex with Untrusted Input
🔴 **DO** | .NET Core+

❌
```csharp
var r = new Regex(userPattern);
```
✅
```csharp
var r = new Regex(userPattern, RegexOptions.None, TimeSpan.FromSeconds(2));
```
**Impact: Prevents unbounded CPU consumption; no cost on normal matches.**

### 17. Use TryGetValue Instead of ContainsKey + Indexer
🔴 **DO** | .NET Core+

❌
```csharp
if (dict.ContainsKey(key))
    Use(dict[key]);
```
✅
```csharp
if (dict.TryGetValue(key, out var value))
    Use(value);
```
**Impact: ~2x faster (50% reduction in lookup time).**

### 18. Avoid LINQ in Hot Paths
🔴 **AVOID** | .NET Core+

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

❌
```csharp
foreach (Type t in types) { Validate(t); }
_types = types.ToArray();
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

❌
```csharp
string json = JsonSerializer.Serialize(post);
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

❌
```csharp
JsonSerializer.Serialize(obj, new JsonSerializerOptions());
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

❌
```csharp
using var client = new HttpClient();
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

→ **Moved to [structural-patterns.md](structural-patterns.md)**

**Impact: Virtual calls up to 500x faster; type checks ~25x faster.**

### 24. Use SearchValues\<T\> for Repeated Set Searches
🔴 **DO** | .NET 8+

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

## Detection

Scan recipes for critical anti-patterns. Run these and report exact counts.

```bash
# .IndexOf(string) without StringComparison (culture-aware, 2-3x slower)
grep -rn --include='*.cs' -E '\.IndexOf\("[^"]+"\)' --exclude-dir=bin --exclude-dir=obj . | wc -l

# .Substring( calls (allocates new string — consider AsSpan)
grep -rn --include='*.cs' '\.Substring(' --exclude-dir=bin --exclude-dir=obj . | wc -l

# .StartsWith/.EndsWith/.Contains without StringComparison (culture-aware, 2-3x slower)
grep -rn --include='*.cs' -E '\.(StartsWith|EndsWith|Contains)\("[^"]+"\)' --exclude-dir=bin --exclude-dir=obj . | wc -l
```
