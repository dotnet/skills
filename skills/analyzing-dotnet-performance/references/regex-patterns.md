# Regex Patterns

### Choose the Right Regex Engine Mode
🟡 **DO** select the regex engine based on your scenario's trade-offs | .NET 7+

❌
```csharp
var r = new Regex(dynamicPattern, RegexOptions.Compiled);
```
✅
```csharp
[GeneratedRegex("pattern")]
private static partial Regex MyRegex();

var safe = new Regex(untrustedPattern, RegexOptions.NonBacktracking);

var oneOff = new Regex("pattern");
```

**Impact: Source generator gives best throughput with near-zero startup. NonBacktracking prevents O(2^N) worst case at the cost of ~128x slower best case.**

### Use IsMatch When You Only Need a Boolean Result
🟡 **DO** use `IsMatch` instead of `Match(...).Success` | .NET 7+

❌
```csharp
bool found = Regex.Match(input, pattern).Success;
```
✅
```csharp
bool found = Regex.IsMatch(input, pattern);
```

**Impact: Avoids Match object allocation; with NonBacktracking, ~3x faster by skipping capture computation.**

### Keep IsMatch as a Guard Before Replace When Most Inputs Don't Match
🟡 **DO NOT** remove an `IsMatch` guard before `Replace` to "reduce passes" | All versions

❌
```csharp
public string? Apply(string word)
{
    var result = regex.Replace(word, replacement, 1);
    return result == word ? null : result;
}
```
✅
```csharp
public string? Apply(string word)
{
    if (!regex.IsMatch(word))
        return null;
    return regex.Replace(word, replacement);
}
```



### Use Regex.Count/EnumerateMatches Instead of Matches
🟡 **DO** use `Count()` and `EnumerateMatches()` for allocation-free match processing | .NET 7+

❌
```csharp
int count = 0;
Match m = regex.Match(text);
while (m.Success) { count++; m = m.NextMatch(); }
```
✅
```csharp
int count = regex.Count(text);

foreach (ValueMatch m in Regex.EnumerateMatches(text, @"\b\w+\b"))
{
    ReadOnlySpan<char> word = text.AsSpan(m.Index, m.Length);
}
```

**Impact: ~3x faster than Match/NextMatch with NonBacktracking. Zero allocations for both Count and EnumerateMatches.**

### Use Span-Based Regex APIs for Allocation-Free Matching
🟡 **DO** use `ReadOnlySpan<char>` overloads for regex matching on spans | .NET 7+

❌
```csharp
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

### Budget Compiled Regex Instances at Startup
🟡 **CONSIDER** the cumulative startup cost when using many `RegexOptions.Compiled` instances | .NET Core+

❌
```csharp
class Rule(string pattern, string replacement)
{
    readonly Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
```
✅
```csharp
// Option A: Use interpreter for dynamic patterns with short inputs
class Rule(string pattern, string replacement)
{
    readonly Regex regex = new(pattern, RegexOptions.IgnoreCase);
}

// Option B: Fast-path with exact matching before regex
static readonly FrozenDictionary<string, string> s_exactRules = /* ... */;

public string? Apply(string word)
{
    if (s_exactRules.TryGetValue(word, out var result))
        return result;
    return regex.IsMatch(word) ? regex.Replace(word, replacement) : null;
}
```

**Impact: Startup cost scales linearly with compiled regex count. For 100 rules: ~100-500ms saved at startup.**

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
