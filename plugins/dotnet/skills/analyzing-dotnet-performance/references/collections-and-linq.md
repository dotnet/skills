# Collections & LINQ Patterns

### Use FrozenDictionary/FrozenSet for Read-Heavy Lookup Tables
🟡 **DO** use `FrozenDictionary`/`FrozenSet` for collections created once and read many times | .NET 8+

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

### Use Dictionary Alternate Lookup for Span-Based Keys
🟡 **DO** use `GetAlternateLookup<ReadOnlySpan<char>>()` to avoid string allocation on lookups | .NET 9+

❌
```csharp
string key = headerLine.Substring(0, colonIndex);
if (s_dict.TryGetValue(key, out int value)) { /* ... */ }
```
✅
```csharp
var lookup = s_dict.GetAlternateLookup<ReadOnlySpan<char>>();
ReadOnlySpan<char> key = headerLine.AsSpan(0, colonIndex);
if (lookup.TryGetValue(key, out int value)) { }
```

**Impact: Avoids string allocation per lookup — especially valuable in parser/protocol hot paths.**

### Use CollectionsMarshal.GetValueRefOrNullRef for Lookup-and-Update
🟡 **DO** use `CollectionsMarshal.GetValueRefOrAddDefault` for dictionary update patterns | .NET 6+

❌
```csharp
_counts.TryGetValue(key, out int count);
_counts[key] = count + 1;
```
✅
```csharp
ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(_counts, key, out _);
count++;
```

**Impact: ~48% faster for lookup-and-update patterns (95µs → 49µs).**

### Use Collection Expressions [] for Zero-Allocation Span Creation
🟡 **DO** use collection expressions for `Span<T>` targets | C# 12 / .NET 8+

❌
```csharp
int[] values = new int[] { a, b, c, d };
```
✅
```csharp
Span<int> values = [a, b, c, d];
ReadOnlySpan<int> daysInMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
```

**Impact: Zero heap allocation for span-targeted collection expressions.**

### Use EnsureCapacity on List/Stack/Queue Before Bulk Adds
🟡 **DO** call `EnsureCapacity` before bulk insertions | .NET 6+

❌
```csharp
var list = new List<int>();
for (int i = 0; i < 10000; i++)
    list.Add(i);
```
✅
```csharp
var list = new List<int>();
list.EnsureCapacity(10000);
for (int i = 0; i < 10000; i++)
    list.Add(i);
```

**Impact: Reduces reallocations and array copies during bulk operations.**

### Use TryGetNonEnumeratedCount for Pre-Sizing
🟡 **DO** use `TryGetNonEnumeratedCount` to pre-size destination collections | .NET 6+

❌
```csharp
var results = new List<int>();
foreach (var item in source)
    results.Add(Transform(item));
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

### Hoist Static Data Out of Method Bodies
🟡 **AVOID** creating collections with static/deterministic data inside method bodies | .NET Core+

❌
```csharp
public string Convert(long number)
{
    var groupsMap = new Dictionary<long, Func<long, string>>
    {
        { 1_000_000_000, n => $"{Convert(n)} billion" },
        { 1_000_000, n => $"{Convert(n)} million" },
        { 1_000, n => $"{Convert(n)} thousand" },
    };
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
    // ... use s_groupsMap
}
```

**Impact: Eliminates collection + internal storage + closure allocations per call. For a Dictionary with N entries, saves ~N+3 allocations per invocation.**

### Add Overloads to Avoid params Array Allocation
🟡 **DO** add 1- and 2-argument overloads for methods that accept `params T[]`, or use `params ReadOnlySpan<T>` on .NET 9+ | .NET Core+

❌
```csharp
public static string Transform(this string input, params IStringTransformer[] transformers) =>
    transformers.Aggregate(input, (current, t) => t.Transform(current));

"hello".Transform(To.TitleCase);
```
✅
```csharp
// Option A: Explicit overloads for common arities
public static string Transform(this string input, IStringTransformer transformer) =>
    transformer.Transform(input);

public static string Transform(this string input, IStringTransformer t1, IStringTransformer t2) =>
    t2.Transform(t1.Transform(input));

public static string Transform(this string input, params IStringTransformer[] transformers) =>
    transformers.Aggregate(input, (current, t) => t.Transform(current));

// Option B (.NET 9+ / C# 13): params ReadOnlySpan<T> — zero heap allocation
public static string Transform(this string input, params ReadOnlySpan<IStringTransformer> transformers)
{
    foreach (var t in transformers)
        input = t.Transform(input);
    return input;
}
```

**Impact: Eliminates one array allocation per call for the common 1- and 2-argument cases. `params ReadOnlySpan<T>` eliminates it for all arities.**

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
