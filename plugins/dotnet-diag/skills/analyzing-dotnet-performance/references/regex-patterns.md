# Regex Patterns

### Choose the Right Regex Engine Mode
🟡 **DO** use `[GeneratedRegex]` for all static regex patterns, but never remove `NonBacktracking` if present | .NET 7+

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

**Impact: Source generator is always beneficial for static patterns. NonBacktracking prevents O(2^N) worst case — never remove it if present.**

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
