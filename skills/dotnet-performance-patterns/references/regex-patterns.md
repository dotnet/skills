# Regex Patterns

Moderate-priority patterns for choosing the right regex engine and using allocation-free matching APIs. These reduce overhead and improve safety in regular expression processing.

### Choose the Right Regex Engine Mode
🟡 **DO** select the regex engine based on your scenario's trade-offs | .NET 7+

Each regex engine mode has different trade-offs for startup cost, throughput, and worst-case behavior. Use the source generator for static patterns, `Compiled` for dynamic patterns reused often, the interpreter for one-off patterns, and `NonBacktracking` when worst-case guarantees matter.

❌
```csharp
// Using Compiled for everything — bad for one-off patterns (high startup cost)
var r = new Regex(dynamicPattern, RegexOptions.Compiled);
```
✅
```csharp
// Source Generated — zero startup, highest throughput, AOT-compatible
[GeneratedRegex("pattern")]
private static partial Regex MyRegex();

// NonBacktracking — consistent O(n) time, prevents ReDoS
var safe = new Regex(untrustedPattern, RegexOptions.NonBacktracking);

// Interpreted — default, low startup, good for rarely-used patterns
var oneOff = new Regex("pattern");
```

**Impact: Source generator gives best throughput with near-zero startup. NonBacktracking prevents O(2^N) worst case at the cost of ~128x slower best case.**

---

### Use IsMatch When You Only Need a Boolean Result
🟡 **DO** use `IsMatch` instead of `Match(...).Success` | .NET 7+

`IsMatch` avoids allocating a `Match` object. With `NonBacktracking`, it is significantly cheaper since the engine can skip capture computation entirely.

❌
```csharp
bool found = Regex.Match(input, pattern).Success; // allocates Match object
```
✅
```csharp
bool found = Regex.IsMatch(input, pattern); // no Match allocation
```

**Impact: Avoids Match object allocation; with NonBacktracking, ~3x faster by skipping capture computation.**

---

### Keep IsMatch as a Guard Before Replace When Most Inputs Don't Match
🟡 **DO NOT** remove an `IsMatch` guard before `Replace` to "reduce passes" | All versions

`Regex.Replace` is expensive even when there is no match — it must walk the entire input and build replacement state. When iterating many regex rules where most won't match a given input (e.g., pluralization rule tables), an `IsMatch` guard that rejects early is a critical fast-path optimization. Do not fold it into a single `Replace` call.

❌
```csharp
// Looks like "one pass instead of two" but Replace is expensive on no-match
public string? Apply(string word)
{
    var result = regex.Replace(word, replacement, 1);
    return result == word ? null : result; // pays full Replace cost even when no match
}
```
✅
```csharp
// IsMatch is cheap on no-match — fast rejection before expensive Replace
public string? Apply(string word)
{
    if (!regex.IsMatch(word))
        return null;
    return regex.Replace(word, replacement);
}
```

**Impact: In benchmarks on Humanizer's Vocabulary (100+ rules, most non-matching), the "single-pass" Replace approach was 20-50% slower than the IsMatch guard. The two-pass pattern is faster because IsMatch rejects the common case (no match) at far lower cost than Replace.**

---

### Use Regex.Count/EnumerateMatches Instead of Matches
🟡 **DO** use `Count()` and `EnumerateMatches()` for allocation-free match processing | .NET 7+

`Count()` returns the number of matches without allocating `Match` objects. `EnumerateMatches()` yields `ValueMatch` structs (index + length only) — both are allocation-free.

❌
```csharp
// Allocates Match objects on each iteration
int count = 0;
Match m = regex.Match(text);
while (m.Success) { count++; m = m.NextMatch(); }
```
✅
```csharp
int count = regex.Count(text); // zero allocation

foreach (ValueMatch m in Regex.EnumerateMatches(text, @"\b\w+\b"))
{
    ReadOnlySpan<char> word = text.AsSpan(m.Index, m.Length);
}
```

**Impact: ~3x faster than Match/NextMatch with NonBacktracking. Zero allocations for both Count and EnumerateMatches.**

---

### Use Span-Based Regex APIs for Allocation-Free Matching
🟡 **DO** use `ReadOnlySpan<char>` overloads for regex matching on spans | .NET 7+

.NET 7 adds `ReadOnlySpan<char>` overloads to `IsMatch`, `Count`, and `EnumerateMatches`. These enable regex matching on slices of larger buffers or stack-allocated strings without allocating strings.

❌
```csharp
// Must allocate a string to match
string sub = largeBuffer.Substring(start, length);
bool found = Regex.IsMatch(sub, pattern);
```
✅
```csharp
ReadOnlySpan<char> text = largeBuffer.AsSpan(start, length);
foreach (ValueMatch m in Regex.EnumerateMatches(text, @"\b\w+\b"))
{
    ReadOnlySpan<char> word = text.Slice(m.Index, m.Length);
}
```

**Impact: Eliminates string allocations when working with spans — particularly valuable in high-throughput parsing pipelines.**

---

### Budget Compiled Regex Instances at Startup
🟡 **CONSIDER** the cumulative startup cost when using many `RegexOptions.Compiled` instances | .NET Core+

Each `RegexOptions.Compiled` regex generates IL via `Reflection.Emit` at construction time — significantly more expensive than the default interpreted mode. When dozens or hundreds of compiled regexes are created during static initialization (e.g., pluralization rule tables, validation rule engines), the cumulative startup cost can be substantial: hundreds of milliseconds of JIT time plus persistent memory for generated IL.

For dynamically-constructed patterns (where `[GeneratedRegex]` cannot be used), evaluate whether `Compiled` is justified. The interpreter has lower startup cost, and for short input strings (single words), the steady-state throughput difference is minimal.

❌
```csharp
// 100+ rules, each compiled at startup — hundreds of ms startup cost
class Rule(string pattern, string replacement)
{
    readonly Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Each instance: ~1-5ms construction + persistent IL memory
}
```
✅
```csharp
// Option A: Use interpreter for dynamic patterns with short inputs
class Rule(string pattern, string replacement)
{
    readonly Regex regex = new(pattern, RegexOptions.IgnoreCase);
    // Each instance: ~0.01-0.1ms construction, slightly slower matching
}

// Option B: Lazy compilation — interpret first, compile on repeated use
class Rule(string pattern, string replacement)
{
    readonly Regex regex = new(pattern, RegexOptions.IgnoreCase);
    Regex? compiledRegex;

    public string? Apply(string word)
    {
        var r = compiledRegex ?? regex;
        // Promote to compiled after N uses if desired
    }
}

// Option C: Fast-path with exact/prefix matching before regex
static readonly FrozenDictionary<string, string> s_exactRules = /* ... */;

public string? Apply(string word)
{
    if (s_exactRules.TryGetValue(word, out var result))
        return result; // skip regex entirely
    return regex.IsMatch(word) ? regex.Replace(word, replacement) : null;
}
```

**Impact: Startup cost scales linearly with compiled regex count. For 100 rules: ~100-500ms saved at startup. Steady-state throughput trade-off is minimal for short inputs (<50 chars). Benchmark to validate for your specific workload.**

---

## Detection

Scan recipes for regex anti-patterns. Run these and report exact counts.

```bash
# Compiled regex count (startup cost budget — compare ratio to GeneratedRegex)
grep -rn --include='*.cs' 'RegexOptions.Compiled' --exclude-dir=bin --exclude-dir=obj . | wc -l

# GeneratedRegex count (already optimized — verify the inverse)
grep -rn --include='*.cs' 'GeneratedRegex' --exclude-dir=bin --exclude-dir=obj . | wc -l

# Uncached new Regex() calls (construction cost per call)
grep -rn --include='*.cs' 'new Regex(' --exclude-dir=bin --exclude-dir=obj . | wc -l
```

When `RegexOptions.Compiled` appears inside a class constructor or field initializer of an instantiated class (not a static singleton), count how many instances of that class are created at startup to determine total compiled regex budget. For example, if a `Rule` class compiles a regex in its constructor and 122 rules are registered, that is 122 compiled regexes at startup.

### Patterns Requiring Manual Review

- **`new Regex(` uncached**: Field assignment may span multiple lines — grep on one line is unreliable. Verify that matched instances are stored in `static readonly` fields or `[GeneratedRegex]`.
