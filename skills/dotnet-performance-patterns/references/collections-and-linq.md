# Collections & LINQ Patterns

Moderate-priority patterns for choosing the right collection types, reducing lookup overhead, and using LINQ effectively. These improve throughput for data-heavy .NET code.

### Use FrozenDictionary/FrozenSet for Read-Heavy Lookup Tables
🟡 **DO** use `FrozenDictionary`/`FrozenSet` for collections created once and read many times | .NET 8+

Frozen collections spend more time during construction to analyze data and choose optimal internal strategies (minimal substring hashing for strings, direct array indexing for dense integers), resulting in significantly faster reads.

❌
```csharp
private static readonly Dictionary<string, int> s_statusCodes = new()
{
    ["OK"] = 200, ["NotFound"] = 404, ["InternalServerError"] = 500
};
```
✅
```csharp
private static readonly FrozenDictionary<string, int> s_statusCodes =
    new Dictionary<string, int>
    {
        ["OK"] = 200, ["NotFound"] = 404, ["InternalServerError"] = 500
    }.ToFrozenDictionary();
```

**Impact: ~50% faster lookups than Dictionary, ~14x faster than ImmutableDictionary.**

---

### Use Dictionary Alternate Lookup for Span-Based Keys
🟡 **DO** use `GetAlternateLookup<ReadOnlySpan<char>>()` to avoid string allocation on lookups | .NET 9+

Enables looking up string-keyed dictionaries using `ReadOnlySpan<char>` without allocating a string. Valuable when parsing data where keys are already available as spans.

❌
```csharp
string key = headerLine.Substring(0, colonIndex); // allocates string
if (s_dict.TryGetValue(key, out int value)) { /* ... */ }
```
✅
```csharp
var lookup = s_dict.GetAlternateLookup<ReadOnlySpan<char>>();
ReadOnlySpan<char> key = headerLine.AsSpan(0, colonIndex);
if (lookup.TryGetValue(key, out int value)) { /* no string allocated */ }
```

**Impact: Avoids string allocation per lookup — especially valuable in parser/protocol hot paths.**

---

### Use CollectionsMarshal.GetValueRefOrNullRef for Lookup-and-Update
🟡 **DO** use `CollectionsMarshal.GetValueRefOrAddDefault` for dictionary update patterns | .NET 6+

Returns a `ref TValue` directly into the dictionary's internal storage, avoiding duplicate lookups and value copies for the common lookup-then-update pattern.

❌
```csharp
_counts.TryGetValue(key, out int count);
_counts[key] = count + 1; // two lookups
```
✅
```csharp
ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(_counts, key, out _);
count++; // single lookup
```

**Impact: ~48% faster for lookup-and-update patterns (95µs → 49µs).**

---

### Use CollectionsMarshal.AsSpan for Direct List\<T\> Access
🟡 **DO** use `CollectionsMarshal.AsSpan` for direct span access over list internals | .NET 5+

Returns a `Span<T>` over the list's internal array, enabling vectorized operations without copying. `CollectionsMarshal.SetCount` lets you pre-size and write directly into backing storage.

❌
```csharp
List<int> list = new(count);
for (int i = 0; i < count; i++)
    list.Add(source[i]); // per-element overhead
```
✅
```csharp
List<int> list = new();
CollectionsMarshal.SetCount(list, count);
Span<int> span = CollectionsMarshal.AsSpan(list);
source.AsSpan(0, count).CopyTo(span); // vectorized copy
```

**Impact: Avoids per-element overhead and enumerator allocation. Enables vectorized operations on list contents.**

---

### Use Collection Expressions [] for Zero-Allocation Span Creation
🟡 **DO** use collection expressions for `Span<T>` targets | C# 12 / .NET 8+

C# 12 collection expressions (`[a, b, c]`) for `Span<T>` or `ReadOnlySpan<T>` use `[InlineArray]` under the covers, resulting in stack-allocated storage with zero heap allocation.

❌
```csharp
int[] values = new int[] { a, b, c, d }; // heap allocation
```
✅
```csharp
Span<int> values = [a, b, c, d]; // stack-allocated, zero heap allocation
ReadOnlySpan<int> daysInMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
```

**Impact: Zero heap allocation for span-targeted collection expressions.**

---

### Use EnsureCapacity on List/Stack/Queue Before Bulk Adds
🟡 **DO** call `EnsureCapacity` before bulk insertions | .NET 6+

Pre-allocates internal storage to avoid repeated resizing and copying during bulk insertions. Available on `List<T>`, `Stack<T>`, and `Queue<T>`.

❌
```csharp
var list = new List<int>();
for (int i = 0; i < 10000; i++)
    list.Add(i); // multiple internal resizes
```
✅
```csharp
var list = new List<int>();
list.EnsureCapacity(10000);
for (int i = 0; i < 10000; i++)
    list.Add(i); // single allocation upfront
```

**Impact: Reduces reallocations and array copies during bulk operations.**

---

### Use Array.Empty\<T\>() Instead of new T[0]
🟡 **DO** use `Array.Empty<T>()` for empty array returns | .NET Core+

`Array.Empty<T>()` returns a cached singleton empty array, avoiding allocation. The compiler also uses this for `params` arrays when no arguments are passed.

❌
```csharp
return new string[0]; // allocates a new empty array each time
```
✅
```csharp
return Array.Empty<string>(); // returns cached singleton
```

**Impact: Eliminates one allocation per call site — adds up in high-throughput scenarios.**

---

### LINQ Is Fine Outside Hot Paths — Don't Over-Optimize
🟡 **AVOID** blanket bans on LINQ in codebases | .NET 7+

LINQ is significantly optimized since .NET 7 — `Min`/`Max`/`Sum`/`Average` are vectorized, `Order()` is allocation-lighter, and source arrays/lists get special fast paths. Avoid LINQ on proven hot paths, but use it freely elsewhere.

❌
```csharp
// Over-engineering — replacing all LINQ with manual loops
bool found = false;
foreach (var item in items)
    if (item.Name == target) { found = true; break; }
```
✅
```csharp
// LINQ is fine outside hot paths — and vectorized in .NET 7+
int min = data.Min();         // 40x faster than .NET 6
int max = data.Max();         // 38x faster than .NET 6
bool found = items.Any(x => x.Name == target); // fine if not a hot path
```

**Impact: LINQ Min/Max/Sum/Average are now vectorized — up to 40x faster for arrays of numeric types.**

---

### Use TryGetNonEnumeratedCount for Pre-Sizing
🟡 **DO** use `TryGetNonEnumeratedCount` to pre-size destination collections | .NET 6+

Returns the count of an enumerable without enumerating it, if the count can be determined cheaply (array, list, or `ICollection`). Use to pre-size destination collections and avoid resizing.

❌
```csharp
var results = new List<int>();
foreach (var item in source)
    results.Add(Transform(item)); // may resize multiple times
```
✅
```csharp
var results = source.TryGetNonEnumeratedCount(out int count)
    ? new List<int>(count)
    : new List<int>();
foreach (var item in source)
    results.Add(Transform(item));
```

**Impact: Avoids O(n) enumeration for counting; eliminates resizing allocations.**

---

### Prefer Any() Over Count() != 0
🟡 **DO** use `Any()` for emptiness checks | .NET 5+

In .NET 5, `Enumerable.Any()` was fixed to special-case `ICollection<T>.Count`, making it allocation-free and consistent with `Count()`. It is now the correct and efficient choice for all emptiness checks.

❌
```csharp
if (myList.Count() != 0) { /* ... */ } // may enumerate to count
```
✅
```csharp
if (myList.Any()) { /* ... */ } // allocation-free, single interface dispatch
```

**Impact: Any() is now allocation-free and uses one interface dispatch for ICollection\<T\>.**

---

### Hoist Static Data Out of Method Bodies
🟡 **AVOID** creating collections with static/deterministic data inside method bodies | .NET Core+

When a `Dictionary`, `List`, `HashSet`, or array is created inside a method body and populated with the same data on every call, it should be hoisted to a `static readonly` field. This eliminates per-call allocation of the collection, its internal storage, and any delegate/closure objects stored in it.

❌
```csharp
public string Convert(long number)
{
    // Rebuilt on every call — Dictionary + internal arrays + 7 Func<> closures
    var groupsMap = new Dictionary<long, Func<long, string>>
    {
        { 1_000_000_000, n => $"{Convert(n)} billion" },
        { 1_000_000, n => $"{Convert(n)} million" },
        { 1_000, n => $"{Convert(n)} thousand" },
    };
    // ... use groupsMap
}
```
✅
```csharp
private static readonly FrozenDictionary<long, Func<long, string>> s_groupsMap =
    new Dictionary<long, Func<long, string>>
    {
        { 1_000_000_000, n => $"{Convert(n)} billion" },
        { 1_000_000, n => $"{Convert(n)} million" },
        { 1_000, n => $"{Convert(n)} thousand" },
    }.ToFrozenDictionary();

public string Convert(long number)
{
    // ... use s_groupsMap — zero allocation per call
}
```

**Impact: Eliminates collection + internal storage + closure allocations per call. For a Dictionary with N entries, saves ~N+3 allocations per invocation.**

---

### Add Overloads to Avoid params Array Allocation
🟡 **DO** add 1- and 2-argument overloads for methods that accept `params T[]` | .NET Core+

Every call to a `params T[]` method allocates an array on the heap, even for 1 or 2 arguments. Adding explicit overloads for the common 1- and 2-argument cases eliminates the array allocation entirely. On .NET 9+ / C# 13, consider `params ReadOnlySpan<T>` overloads instead.

❌
```csharp
// Every call allocates a new IStringTransformer[] array
public static string Transform(this string input, params IStringTransformer[] transformers) =>
    transformers.Aggregate(input, (current, t) => t.Transform(current));

// Caller: allocates IStringTransformer[1]
"hello".Transform(To.TitleCase);
```
✅
```csharp
// Zero-allocation overload for the common single-transformer case
public static string Transform(this string input, IStringTransformer transformer) =>
    transformer.Transform(input);

// Zero-allocation overload for two transformers
public static string Transform(this string input, IStringTransformer t1, IStringTransformer t2) =>
    t2.Transform(t1.Transform(input));

// Keep params overload for 3+ transformers
public static string Transform(this string input, params IStringTransformer[] transformers) =>
    transformers.Aggregate(input, (current, t) => t.Transform(current));
```

**Impact: Eliminates one array allocation per call for the common 1- and 2-argument cases.**

---

## Detection

Scan recipes for collection and LINQ anti-patterns. Run these and report exact counts.

```bash
# Static Dictionary not using FrozenDictionary (read-only after init)
grep -rn --include='*.cs' 'static readonly Dictionary<' --exclude-dir=bin --exclude-dir=obj . | wc -l

# Static FrozenDictionary (already optimized — verify the inverse)
grep -rn --include='*.cs' 'static readonly FrozenDictionary<' --exclude-dir=bin --exclude-dir=obj . | wc -l

# Per-call List allocation (inside method bodies, not static/readonly fields)
grep -rn --include='*.cs' 'new List<' --exclude-dir=bin --exclude-dir=obj . | grep -v 'static\|readonly' | wc -l

# Per-call Dictionary allocation (inside method bodies, not static/readonly fields)
grep -rn --include='*.cs' 'new Dictionary<' --exclude-dir=bin --exclude-dir=obj . | grep -v 'static\|readonly' | wc -l

# StringComparer.CurrentCulture usage (almost always wrong in library code — use Ordinal)
grep -rn --include='*.cs' 'StringComparer.CurrentCulture' --exclude-dir=bin --exclude-dir=obj . | wc -l

# LINQ chains in extension/hot-path files (.Select, .Where, .Cast, .Take, .Aggregate)
grep -rn --include='*.cs' -E '\.(Select|Where|Cast|Take|Aggregate)\(' --exclude-dir=bin --exclude-dir=obj . | wc -l
```

For the LINQ chain recipe: any hit in a file whose name ends in `Extensions.cs`, `Formatter.cs`, or implements a method called from a public extension method is a hot-path candidate. Inspect each hit in these files and flag LINQ chains that allocate delegates, enumerators, or intermediate collections on every call. Hits in localization converters or one-time initialization are lower priority.

### Patterns Requiring Manual Review

- **ContainsKey + indexer double-lookup**: Requires verifying the same key is used in a subsequent indexer access — multi-line/multi-statement context
- **LINQ on hot paths**: The LINQ chain recipe above catches call sites, but distinguishing hot-path from cold-path requires context. Prioritize hits in `*Extensions.cs` and `*Formatter.cs` files, which are typically called on every user invocation
- **`new Dictionary/List<` in method bodies vs fields**: The grep heuristic (`grep -v 'static\|readonly'`) catches most cases but may include false positives from field initializers without `static`/`readonly` — spot-check flagged lines
