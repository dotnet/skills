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

For review requests, read but do not modify the supplied source unless the user explicitly asks for changes. If a named file is not at the supplied path, make one targeted filename/path search before concluding that source is unavailable.

Then load `references/critical-patterns.md`, `references/structural-patterns.md`, and the reference for each topic selected by the scan depth. Skip unselected topic references.

**If a needed reference file is not found** (e.g., in a sandboxed environment or when the skill is embedded as instructions only), **proceed directly to Step 3** using the scan recipes listed inline below. Do not spend time searching the filesystem for reference files — if they aren't at the expected relative path, they aren't available.

### Step 2: Detect Code Signals and Select Topic Recipes

Scan the code for signals that indicate which pattern categories to check. If reference files were loaded, use their `## Detection` sections. Otherwise, use the inline recipes in Step 3.

| Signal in Code | Topic | Reference |
|----------------|-------|-----------|
| `async`, `await`, `Task`, `ValueTask` | Async patterns | `references/async-patterns.md` |
| `Span<`, `Memory<`, `stackalloc`, `ArrayPool`, `string.Substring`, `.Replace(`, `.ToLower()`, `+=` in loops, `params` | Memory & strings | `references/memory-and-strings.md` |
| `Regex`, `[GeneratedRegex]`, `Regex.Match`, `RegexOptions.Compiled` | Regex patterns | `references/regex-patterns.md` |
| `Dictionary<`, `List<`, `.ToList()`, `.Where(`, `.Select(`, LINQ methods, `static readonly Dictionary<` | Collections & LINQ | `references/collections-and-linq.md` |
| `JsonSerializer`, `HttpClient`, `Stream`, `FileStream` | I/O & serialization | `references/io-and-serialization.md` |

Always check structural patterns (unsealed classes) regardless of signals.

**Scan depth controls scope:**
- `critical-only`: Only critical patterns (deadlocks, >10x regressions)
- `standard` (default): Critical + detected topic patterns
- `comprehensive`: All pattern categories

### Step 3: Scan and Report

**For files under 500 lines, read the entire file first.** Use one batched search per applicable category to confirm counts, locations, and positive/inverse patterns; do not run a separate search for each recipe.

Maintain an internal coverage ledger for every applicable recipe and manual-review pattern from the loaded references or inline guidance. Reconcile each as checked with hits, checked with no hits, or not applicable. This is a completeness contract, not an output format or a requirement for one tool call per recipe.

Search hits are candidates, not findings. Inspect their context and report exact executable-site counts and locations; for per-instance patterns, scale the count by constructed instances.

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
- Reconcile every applicable recipe and manual-review pattern, combining related confirmation searches by category
- **Emit a concise category checklist** before classifying findings — report actionable counts or `none`, not individual zero-hit searches
- For each detected category, check the relevant optimized/inverse patterns and preserve them as positive findings or exclusions
- If a reference file was loaded, use its `## Detection` and manual-review guidance for that topic
- Do not elevate an ambiguous allocation or performance claim without confirming it from the code; state uncertainty when context is insufficient

**Verify-the-Inverse Rule:** For absence patterns, always count both sides and report the ratio (e.g., "N of M classes are sealed"). The ratio determines severity — 0/185 is systematic, 12/15 is a consistency fix.

### Step 3b: Cross-File Consistency Check

If an optimized pattern is found in one file, check whether sibling files (same directory, same interface, same base class) use the un-optimized equivalent. Flag as 🟡 Moderate with the optimized file as evidence.

### Step 3c: Compound Allocation Check

After running scan recipes, look for these multi-allocation patterns that single-line recipes miss:

1. **Branched `.Replace()` chains:** Methods that call `.Replace()` across multiple `if/else` branches — report total allocation count across all branches, not just per-line.
2. **Cross-method chaining:** When a public method delegates to another method that itself allocates intermediates (e.g., A calls B which does 3 regex replaces, then A calls C), report the total chain cost as one finding.
3. **Compound `+=` with embedded allocating calls:** Lines like `result += $"...{Foo().ToLower()}"` are 2+ allocations (interpolation + ToLower + concatenation) — flag the compound cost, not just the `.ToLower()`.
4. **`string.Format` specificity:** Distinguish resource-loaded format strings (not fixable) from compile-time literal format strings (fixable with interpolation). Enumerate the actionable sites.

### Step 3d: Category Completeness Guardrails

- **Strings & LINQ:** Trace the complete hot call chain. Include every branched `Replace` location and preserve existing span-based helpers.
- **Regex:** Compare compiled and generated regexes, scale constructor sites by created instances, and distinguish static literals from dynamic patterns.
- **Structural:** Search inheritance before recommending `sealed`; keep bases unsealed and identify leaf derivatives.
- **Collections:** Verify mutation and lifetime before recommending frozen collections; preserve intentionally mutable caches.

### Step 4: Classify and Prioritize Findings

Assign each finding a severity:

| Severity | Criteria | Action |
|----------|----------|--------|
| 🔴 **Critical** | Deadlocks, crashes, security vulnerabilities, >10x regression | Must fix |
| 🟡 **Moderate** | 2-10x improvement opportunity, best practice for hot paths | Should fix on hot paths |
| ℹ️ **Info** | Pattern applies but code may not be on a hot path | Consider if profiling shows impact |

**Prioritization rules:**
1. If the user identified hot-path code, prioritize findings and use the highest severity supported by evidence; hot-path context alone does not make an allocation finding Critical
2. If hot-path context is unknown, report 🔴 Critical findings unconditionally; report 🟡 Moderate findings with a note: _"Impactful if this code is on a hot path"_
3. Never suggest micro-optimizations on code that is clearly not performance-sensitive

**Scale-based severity escalation:**
When the same pattern appears across many instances, escalate severity:
- 1-10 instances of the same anti-pattern → report at the pattern's base severity
- 11-50 instances → escalate ℹ️ Info patterns to 🟡 Moderate
- 50+ instances → escalate to 🟡 Moderate with elevated priority; flag as a codebase-wide systematic issue

Do not invent throughput or allocation multipliers. Use reference measurements only when code shape and runtime assumptions match, and label them as reference data.

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
- **Positive findings** in a bullet list, not a table. Include the relevant inverse/optimized evidence for detected categories: `✅ Pattern — evidence`.

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

- [ ] Source was read without modification unless implementation was requested
- [ ] Available references were loaded for critical, structural, and selected topics
- [ ] Coverage ledger reconciles every applicable recipe and manual-review pattern
- [ ] Each finding has verified counts, locations, evidence, and a concrete fix
- [ ] Relevant positive/inverse patterns and exclusions were preserved
- [ ] Category checklist covers all applicable performance categories
- [ ] Summary table included at end

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
