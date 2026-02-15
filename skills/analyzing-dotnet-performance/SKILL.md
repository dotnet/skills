---
name: analyzing-dotnet-performance
description: >-
  Scans .NET code for ~50 performance anti-patterns across async, memory,
  strings, collections, LINQ, regex, serialization, and I/O with tiered
  severity classification. Use when analyzing .NET code for optimization
  opportunities, reviewing hot paths, or auditing allocation-heavy patterns.
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

### Step 1: Load Critical Patterns

Always load `references/critical-patterns.md`. These 17 patterns represent deadlocks, order-of-magnitude regressions, and excessive allocations. Flag every match unconditionally.

### Step 2: Detect Code Signals and Load Topic References

Scan the code for signals that indicate which topic-specific reference files to load. Only load files relevant to the code under review.

| Signal in Code | Load Reference |
|----------------|----------------|
| `async`, `await`, `Task`, `ValueTask` | [async-patterns.md](references/async-patterns.md) |
| `Span<`, `Memory<`, `stackalloc`, `ArrayPool`, `string.Substring`, `.Replace(`, `.ToLower()`, `+=` in loops, `params ` | [memory-and-strings.md](references/memory-and-strings.md) |
| `Regex`, `[GeneratedRegex]`, `Regex.Match`, `RegexOptions.Compiled` | [regex-patterns.md](references/regex-patterns.md) |
| `Dictionary<`, `List<`, `.ToList()`, `.Where(`, `.Select(`, LINQ methods, `static readonly Dictionary<` | [collections-and-linq.md](references/collections-and-linq.md) |
| `JsonSerializer`, `HttpClient`, `Stream`, `FileStream` | [io-and-serialization.md](references/io-and-serialization.md) |

Always load [structural-patterns.md](references/structural-patterns.md) for absence-based detection (unsealed classes).

**Scan depth controls loading:**
- `critical-only`: Only Step 1 (critical patterns)
- `standard` (default): Steps 1 + 2 (critical + detected topics)
- `comprehensive`: Load all reference files

### Step 3: Scan and Report

For each loaded reference file, run every grep/scan recipe from its `## Detection` section. Report exact counts, not estimates.

**Rules:**
- Run every recipe in every loaded reference file
- **Emit a scan execution checklist** before classifying findings — list each recipe, the command run, and the hit count
- A result of **0 hits** is valid and valuable (confirms good practice)
- If any recipe was not executed, go back and run it before proceeding

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

**Prioritization rules:**
1. If the user identified hot-path code, elevate all findings in that code to their maximum severity
2. If hot-path context is unknown, report 🔴 Critical findings unconditionally; report 🟡 Moderate findings with a note: _"Impactful if this code is on a hot path"_
3. Never suggest micro-optimizations on code that is clearly not performance-sensitive (startup, configuration, one-time initialization)

**Scale-based severity escalation:**
When the same pattern appears across many instances, escalate severity:
- 1-10 instances of the same anti-pattern → report at the pattern's base severity
- 11-50 instances → escalate ℹ️ Info patterns to 🟡 Moderate
- 50+ instances → escalate to 🟡 Moderate with elevated priority; flag as a codebase-wide systematic issue

Always report exact counts (from scan recipes), not estimates or agent summaries.

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
- **Positive findings** in a bullet list, not a table. One line per pattern: `✅ Pattern — evidence`.

End with a summary table and disclaimer:

```markdown
| Severity | Count | Top Issue |
|----------|-------|-----------|
| 🔴 Critical | N | ... |
| 🟡 Moderate | N | ... |
| ℹ️ Info | N | ... |

> ⚠️ **Disclaimer:** These results are generated by an AI assistant and are non-deterministic. Findings may include false positives, miss real issues, or suggest changes that are incorrect for your specific context. Always verify recommendations with benchmarks and human review before applying changes to production code.
```

## Validation

Before delivering results, verify:

- [ ] All critical patterns from `critical-patterns.md` were checked
- [ ] Topic reference files loaded only when matching signals detected
- [ ] Each finding includes a concrete code fix
- [ ] Scan execution checklist is complete (all recipes run)
- [ ] Summary table included at end

## Common Pitfalls

| Pitfall | Correct Approach |
|---------|-----------------|
| Flagging every `Dictionary` as needing `FrozenDictionary` | Only flag if the dictionary is never mutated after construction |
| Suggesting `Span<T>` in async methods | Use `Memory<T>` in async code; `Span<T>` only in sync hot paths |
| Reporting LINQ outside hot paths | Only flag LINQ in identified hot paths or tight loops; LINQ is fine in startup, config, and one-time code. Since .NET 7, LINQ Min/Max/Sum/Average are vectorized — blanket bans on LINQ are misguided |
| Suggesting `ConfigureAwait(false)` in app code | Only applicable in library code; not primarily a performance concern |
| Recommending `ValueTask` everywhere | Only for hot paths with frequent synchronous completion |
| Flagging `new HttpClient()` in DI services | Check if `IHttpClientFactory` is already in use |
| Suggesting `[GeneratedRegex]` for dynamic patterns | Only flag when the pattern string is a compile-time literal |
| Suggesting `CollectionsMarshal.AsSpan` broadly | Only for ultra-hot paths with benchmarked evidence; adds complexity and fragility |
| Suggesting `unsafe` code for micro-optimizations | Avoid `unsafe` except where absolutely necessary — do not recommend it for micro-optimizations that don't matter. Safe alternatives like `Span<T>`, `stackalloc` in safe context, and `ArrayPool` cover the vast majority of performance needs |
