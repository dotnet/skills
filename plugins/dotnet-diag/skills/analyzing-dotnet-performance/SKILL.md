---
name: analyzing-dotnet-performance
description: >-
  Scans .NET code for ~50 performance anti-patterns across async, memory,
  strings, collections, LINQ, regex, serialization, and I/O with tiered
  severity classification. Use when analyzing .NET code for optimization
  opportunities, reviewing hot paths, or auditing allocation-heavy patterns.
license: MIT
---

# .NET Performance Patterns

Scan C#/.NET code for performance anti-patterns and produce prioritized findings with concrete fixes. Patterns sourced from the official .NET performance blog series, distilled to customer-actionable guidance.

## When to Use

- Reviewing C#/.NET code for performance optimization opportunities
- Auditing hot paths for allocation-heavy or inefficient patterns
- Systematic scan of a codebase for known anti-patterns before release
- Second-opinion analysis after manual performance review

## When Not to Use

- **Algorithmic complexity analysis** — this skill targets API usage patterns, not algorithm design
- **Code not on a hot path** with no performance requirements — avoid premature optimization

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Source code | Yes | C# files, code blocks, or repository paths to scan |
| Hot-path context | Recommended | Which code paths are performance-critical |
| Target framework | Recommended | .NET version (some patterns require .NET 8+) |
| Scan depth | Optional | `critical-only`, `standard` (default), or `comprehensive` |

## Workflow

### Step 1: Read Source and Load Needed References

For review requests, read the supplied source before loading references and do not modify it unless the user explicitly asks for changes. If a named file is not at the supplied path, make one targeted filename/path search before concluding that source is unavailable.

Load `references/critical-patterns.md` for every scan depth. For `standard`, also load only the topic-specific references selected by the signals in Step 2; use the structural reference when class or struct declarations are present. For `comprehensive`, load all topic references. Do not load other references.

**If reference files are not found** (e.g., in a sandboxed environment or when the skill is embedded as instructions only), **proceed directly to Step 3** using the scan recipes listed inline below. Do not spend time searching the filesystem for reference files — if they aren't at the expected relative path, they aren't available.

### Step 2: Detect Code Signals and Select Topic Recipes

Scan the code for signals that indicate which pattern categories to check. If reference files were loaded, use their `## Detection` sections. Otherwise, use the inline recipes in Step 3.

| Signal in Code | Topic |
|----------------|-------|
| `async`, `await`, `Task`, `ValueTask` | Async patterns |
| `Span<`, `Memory<`, `stackalloc`, `ArrayPool`, `string.Substring`, `.Replace(`, `.ToLower()`, `+=` in loops, `params` | Memory & strings |
| `Regex`, `[GeneratedRegex]`, `Regex.Match`, `RegexOptions.Compiled` | Regex patterns |
| `Dictionary<`, `List<`, `.ToList()`, `.Where(`, `.Select(`, LINQ methods, `static readonly Dictionary<` | Collections & LINQ |
| `JsonSerializer`, `HttpClient`, `Stream`, `FileStream` | I/O & serialization |

Always check structural patterns (unsealed classes) regardless of signals.

**Scan depth controls scope:**
- `critical-only`: Only critical patterns (deadlocks, >10x regressions)
- `standard` (default): Critical + detected topic patterns
- `comprehensive`: All pattern categories

### Step 3: Scan and Report

**For files under 500 lines, read the entire file first** — you'll spot most patterns faster than running individual grep recipes.

For each relevant pattern category, run every applicable detection recipe. Batch related recipes into one search command or tool call where possible; batching changes execution, not coverage. Inspect matches in source context and use the batched searches to confirm exact executable-site counts and locations, not raw textual hits.

**Core scan recipes** (run these when reference files aren't available):
```
# Strings & memory
grep -n '\.IndexOf(\"' FILE                    # Missing StringComparison
grep -n '\.Substring(' FILE                    # Substring allocations
grep -En '\.(StartsWith|EndsWith|Contains)\s*\(' FILE  # Missing StringComparison
grep -n '\.ToLower()\|\.ToUpper()' FILE        # Culture-sensitive + allocation
grep -n '\.Replace(' FILE                      # Chained Replace allocations
grep -n 'params ' FILE                         # params array allocation

# Collections & LINQ
grep -n '\.Select\|\.Where\|\.OrderBy\|\.GroupBy' FILE  # LINQ on hot path
grep -n '\.All\|\.Any' FILE                    # LINQ on string/char
grep -n 'new Dictionary<\|new List<' FILE      # Per-call allocation
grep -n 'static readonly Dictionary<' FILE     # FrozenDictionary candidate

# Regex
grep -n 'RegexOptions.Compiled' FILE           # Compiled regex budget
grep -n 'new Regex(' FILE                      # Per-call regex
grep -n 'GeneratedRegex' FILE                  # Positive: source-gen regex

# Structural
grep -n 'public class \|internal class ' FILE  # Unsealed classes
grep -n 'sealed class' FILE                    # Already sealed
grep -n ': IEquatable' FILE                    # Positive: struct equality
```

**Rules:**
- Run every relevant recipe for the detected pattern categories
- Keep a recipe-level internal checklist, but **emit a concise category checklist** before classifying findings — report actionable hit counts or `none`, not individual zero-hit searches
- A result of **0 hits** is valid and valuable (confirms good practice)
- If reference files were loaded, also run their `## Detection` recipes and manual-review checks
- Check the relevant optimized/inverse patterns and report them as positive findings or exclusions

**Verify-the-Inverse Rule:** For absence patterns, always count both sides and report the ratio (e.g., "N of M classes are sealed"). The ratio determines severity — 0/185 is systematic, 12/15 is a consistency fix.

### Step 3b: Cross-File Consistency Check

If an optimized pattern is found in one file, check whether sibling files (same directory, same interface, same base class) use the un-optimized equivalent. Flag as 🟡 Moderate with the optimized file as evidence.

### Step 3c: Compound Allocation Check

After running scan recipes, look for these multi-allocation patterns that single-line recipes miss:

1. **Branched `.Replace()` chains:** Methods that call `.Replace()` across multiple `if/else` branches — report total allocation count across all branches, not just per-line.
2. **Cross-method chaining:** When a public method delegates to another method that itself allocates intermediates (e.g., A calls B which does 3 regex replaces, then A calls C), report the total chain cost as one finding.
3. **Compound `+=` with embedded allocating calls:** Lines like `result += $"...{Foo().ToLower()}"` are 2+ allocations (interpolation + ToLower + concatenation) — flag the compound cost, not just the `.ToLower()`.
4. **`string.Format` specificity:** Distinguish resource-loaded format strings (not fixable) from compile-time literal format strings (fixable with interpolation). Enumerate the actionable sites.

### Step 4: Classify and Prioritize Findings

Assign each finding a severity:

| Severity | Criteria | Action |
|----------|----------|--------|
| 🔴 **Critical** | Deadlocks, crashes, security vulnerabilities, >10x regression | Must fix |
| 🟡 **Moderate** | 2-10x improvement opportunity, best practice for hot paths | Should fix on hot paths |
| ℹ️ **Info** | Pattern applies but code may not be on a hot path | Consider if profiling shows impact |

**Critical evidence gate:** Any allocation or API-usage finding is at most Moderate unless the user supplied a benchmark of this code showing a >10x end-to-end regression. Hot-path frequency and reference benchmark numbers do not make it Critical. Deadlocks, crashes or corruption, security/DoS risks such as catastrophic regex backtracking, and other correctness failures remain eligible for Critical without a benchmark.

**Prioritization rules:**
1. If the user identified hot-path code, prioritize findings in that code without bypassing the Critical evidence gate
2. If hot-path context is unknown, report 🔴 Critical findings unconditionally; report 🟡 Moderate findings with a note: _"Impactful if this code is on a hot path"_
3. Never suggest micro-optimizations on code that is clearly not performance-sensitive

**Scale-based severity escalation:**
When the same pattern appears across many instances, escalate severity:
- 1-10 instances of the same anti-pattern → report at the pattern's base severity
- 11-50 instances → escalate ℹ️ Info patterns to 🟡 Moderate
- 50+ instances → escalate to 🟡 Moderate with elevated priority; flag as a codebase-wide systematic issue

Always report exact verified counts, not estimates or agent summaries. Do not state nanosecond, percentage, throughput, or allocation multipliers unless the user supplied measurements for the reviewed code; reference or blog figures are not measurements of that code.

### Step 5: Generate Findings

**Keep findings compact.** Each finding is one short block — not an essay. Group by severity (🔴 → 🟡 → ℹ️), not by file.

Format per finding:

```
#### ID. Title (N instances)
**Impact:** one-line impact statement
**Files:** file1.cs:L1, file2.cs:L2, ... (list locations, don't build tables)
**Fix:** one-line description of the change (e.g., "Add `StringComparison.Ordinal` parameter")
**Caveat:** only if non-obvious (version requirement, correctness risk)
```

**Rules for compact output:**
- **No ❌/✅ code blocks** for trivial fixes (adding a keyword, parameter, or type change). A one-line fix description suffices.
- **Only include code blocks** for non-obvious transformations (e.g., replacing a LINQ chain with a foreach loop, or hoisting a closure).
- **File locations as inline comma-separated list**, not a table. Use `File.cs:L42` format.
- **No explanatory prose** beyond the Impact line — the severity icon already conveys urgency.
- **Merge related findings** that share the same fix (e.g., all `.ToLower()` calls go in one finding, not split by file).
- **Positive findings** in a bullet list, not a table. One line per relevant optimized/inverse pattern: `✅ Pattern — evidence`.

End with a summary table and disclaimer:

```markdown
| Severity | Count | Top Issue |
|----------|-------|-----------|
| 🔴 Critical | N | ... |
| 🟡 Moderate | N | ... |
| ℹ️ Info | N | ... |

> ⚠️ **Disclaimer:** These results are generated by an AI assistant and are non-deterministic. Findings may include false positives, miss real issues, or suggest changes that are incorrect for your specific context. Always verify recommendations with benchmarks and human review before applying changes to production code.
```

## Mandatory Final Audit

Do not deliver the review until every item passes:

- [ ] Supplied source was read and not modified unless implementation was requested
- [ ] Every applicable recipe and manual-review check was reconciled in the internal checklist
- [ ] Category checklist covers all applicable categories
- [ ] Every finding has verified executable-site counts, locations, evidence, and a concrete fix
- [ ] Relevant optimized/inverse patterns and exclusions are reported
- [ ] Allocation/API findings without a user-supplied >10x benchmark are not Critical
- [ ] No numeric performance estimate appears unless supplied by the user for the reviewed code
- [ ] Summary table and disclaimer are included

## Common Pitfalls

| Pitfall | Correct Approach |
|---------|-----------------|
| Flagging every `Dictionary` as needing `FrozenDictionary` | Only flag if the dictionary is never mutated after construction |
| Treating a local function or lambda as a heap allocation by default | Confirm it captures state and that the delegate/closure is allocated or escapes before reporting it |
| Suggesting `Span<T>` in async methods | Use `Memory<T>` in async code; `Span<T>` only in sync hot paths |
| Reporting LINQ outside hot paths | Only flag LINQ in identified hot paths or tight loops; LINQ is acceptable in code that runs infrequently. Since .NET 7, LINQ Min/Max/Sum/Average are vectorized — blanket bans on LINQ are misguided |
| Suggesting `ConfigureAwait(false)` in app code | Only applicable in library code; not primarily a performance concern |
| Recommending `ValueTask` everywhere | Only for hot paths with frequent synchronous completion |
| Flagging `new HttpClient()` in DI services | Check if `IHttpClientFactory` is already in use |
| Suggesting `[GeneratedRegex]` for dynamic patterns | Only flag when the pattern string is a compile-time literal |
| Suggesting `CollectionsMarshal.AsSpan` broadly | Only for ultra-hot paths with benchmarked evidence; adds complexity and fragility |
| Suggesting `unsafe` code for micro-optimizations | Avoid `unsafe` except where absolutely necessary — do not recommend it for micro-optimizations that don't matter. Safe alternatives like `Span<T>`, `stackalloc` in safe context, and `ArrayPool` cover the vast majority of performance needs |
