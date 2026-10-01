---
name: analyzing-dotnet-performance
description: >-
  Scans .NET code for ~50 performance anti-patterns across async, memory,
  strings, collections, LINQ, regex, serialization, and I/O with tiered
  severity classification. Use only when reviewing a user-identified or
  measured .NET hot path, sustained high-throughput code, or an allocation-heavy
  path with a stated performance requirement.
  DO NOT USE FOR choosing algorithms or asymptotic-complexity analysis, or for
  one-time startup/configuration code with no measured performance requirement,
  including simple LINQ that reads a small settings or configuration file once
  before a long-running workload.
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

### Step 1: Load Critical Reference

Resolve bundled paths from the directory that contains this `SKILL.md`, not from the user's workspace. Load this reference file first:

- `references/critical-patterns.md`

If a direct read fails, list this skill's `references/` directory once and retry only when the listing shows the expected file. Do not use workspace file or text search to locate the skill installation.

### Step 2: Detect Code Signals and Select Topic Recipes

Scan the code for signals that indicate which pattern categories to check. Use the `## Detection` section from the critical reference when available and the inline recipes in Step 3 for initial signal detection.

After detecting signals, load only the topic-specific references selected by scan depth:

- `critical-only`: No additional references (use only `critical-patterns.md`)
- `standard` (default): Load references matching detected signals from this list:
  - `references/async-patterns.md` — async/Task/ValueTask signals
  - `references/memory-and-strings.md` — Span/Memory/string allocation signals
  - `references/regex-patterns.md` — Regex signals
  - `references/collections-and-linq.md` — Dictionary/List/LINQ signals
  - `references/io-and-serialization.md` — JsonSerializer/HttpClient/Stream signals
  - `references/structural-patterns.md` — always loaded (unsealed classes checked regardless)
- `comprehensive`: Load all six topic-specific references above

For coverage reporting, the selected references are `references/critical-patterns.md` plus only the topic-specific references selected above. If any selected reference remains unavailable after retry, use the inline recipes in Step 3 for the missing coverage. Include `Reference coverage: reduced; unavailable: <paths>; used inline recipes for missing references.` in the final report, with `<paths>` replaced by the missing relative paths of the selected references only.

Use the `## Detection` sections from loaded reference files and the inline recipes in Step 3 for categories whose reference files are unavailable.

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

**For files under 500 lines, read the entire file first** — you'll spot most patterns faster than running individual grep recipes. Use grep to confirm counts and catch patterns you might miss visually.

For each relevant pattern category, run the detection recipes below. Report exact counts, not estimates.
Before proposing optimizations, read enough surrounding control flow to detect correctness defects.
A crash, non-terminating recursion, data race, wrong result, or behavior-changing fix outranks every
allocation or throughput improvement. Report that defect first and do not bury it in an informational
note.

**Core scan recipes** (run these when reference files aren't available):
```
# Strings & memory
grep -n '\.IndexOf(\"' FILE                    # Missing StringComparison
grep -n '\.Substring(' FILE                    # Substring allocations
grep -En '\.(StartsWith|EndsWith|Contains)\s*\(' FILE  # Missing StringComparison
grep -n '\.ToLower()\|\.ToUpper()' FILE        # Culture-sensitive + allocation
grep -n '\.Replace(' FILE                      # Inspect chained Replace scans and conditional allocations
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
- Emit a scan execution checklist before classifying findings. For one compact file, use one concise
  line containing only relevant recipes and counts. Use a table only for a multi-file or comprehensive
  audit.
- A result of **0 hits** is valid and valuable (confirms good practice)
- If reference files were loaded, also run their `## Detection` recipes

**Verify-the-Inverse Rule:** For absence patterns, always count both sides and report the ratio (e.g., "N of M classes are sealed"). The ratio determines severity — 0/185 is systematic, 12/15 is a consistency fix.

### Step 3b: Cross-File Consistency Check

If an optimized pattern is found in one file, check whether sibling files (same directory, same interface, same base class) use the un-optimized equivalent. Flag as 🟡 Moderate with the optimized file as evidence.

### Step 3c: Compound Allocation Check

After running scan recipes, look for these multi-allocation patterns that single-line recipes miss:

1. **Branched `.Replace()` chains:** Follow the reachable branch for one call. A `string.Replace`
   call does not allocate a replacement string when no match occurs. Report scans separately from
   allocations, and express allocation counts as conditional on the relevant token being present.
   Do not sum mutually exclusive branches.
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
1. Correctness and safety defects outrank performance findings, even when the request is framed as
   optimization. Examples include non-progressing recursion, data races, and output-changing fixes.
2. If the user identified hot-path code, elevate applicable performance findings to their maximum severity.
3. If hot-path context is unknown, report 🔴 Critical findings unconditionally; report 🟡 Moderate findings with a note: _"Impactful if this code is on a hot path"_
4. Never suggest micro-optimizations on code that is clearly not performance-sensitive.

**Scale-based severity escalation:**
When the same pattern appears across many instances, escalate severity:
- 1-10 instances of the same anti-pattern → report at the pattern's base severity
- 11-50 instances → escalate ℹ️ Info patterns to 🟡 Moderate
- 50+ instances → escalate to 🟡 Moderate with elevated priority; flag as a codebase-wide systematic issue

Always report exact counts (from scan recipes), not estimates or agent summaries.

### Step 5: Generate Findings

**Keep findings compact.** Each finding is one short block — not an essay. Group by severity (🔴 → 🟡 → ℹ️), not by file.

**Scale the report to the input.** For one compact file with at most three findings, use a short
checklist line, the findings, and a one-line severity summary. Do not add a scan table, a summary
table, or repeated positive-finding sections merely to satisfy the template. Use the full tables for
multi-file or comprehensive audits.

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

For multi-file or comprehensive audits, end with a summary table and disclaimer:

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

- [ ] All critical patterns were checked (from reference files or inline recipes)
- [ ] Correctness defects were checked and ranked ahead of micro-optimizations
- [ ] Topic-specific recipes run only when matching signals detected
- [ ] Each finding includes a concrete code fix
- [ ] Scan execution checklist is complete and scaled to the input
- [ ] Summary table is included for multi-file or comprehensive audits

## Common Pitfalls

| Pitfall | Correct Approach |
|---------|-----------------|
| Flagging every `Dictionary` as needing `FrozenDictionary` | Only flag if the dictionary is never mutated after construction |
| Calling every lambda a closure | A closure captures state and normally needs a display object. Noncapturing lambdas may use cached delegates; distinguish collection allocation, delegate allocation, and closure allocation |
| Treating `string.Replace` as an unconditional allocation | It can return the original string when no replacement occurs. Count an allocation only for inputs that match and change |
| Adding an `IndexOf`/`Contains` check before every `Replace` | A precheck adds another scan and can slow the common-match case. Recommend it only when misses dominate and a benchmark supports it |
| Inferring equality/hash correctness defects from a struct declaration | `ValueType` supplies value semantics. Only recommend custom equality/hash members when usage proves boxing, hashing cost, or an actual contract defect |
| Suggesting `Span<T>` in async methods | Use `Memory<T>` in async code; `Span<T>` only in sync hot paths |
| Reporting LINQ outside hot paths | Only flag LINQ in identified hot paths or tight loops; LINQ is acceptable in code that runs infrequently. Since .NET 7, LINQ Min/Max/Sum/Average are vectorized — blanket bans on LINQ are misguided |
| Suggesting `ConfigureAwait(false)` in app code | Only applicable in library code; not primarily a performance concern |
| Recommending `ValueTask` everywhere | Only for hot paths with frequent synchronous completion |
| Flagging `new HttpClient()` in DI services | Check if `IHttpClientFactory` is already in use |
| Suggesting `[GeneratedRegex]` for dynamic patterns | Only flag when the pattern string is a compile-time literal |
| Suggesting source generation for a runtime rule API | If a method accepts the regex pattern as data, treat it as dynamic unless inspected call sites prove a closed set of literals. Do not offer `[GeneratedRegex]` as a generic fallback |
| Suggesting `CollectionsMarshal.AsSpan` broadly | Only for ultra-hot paths with benchmarked evidence; adds complexity and fragility |
| Suggesting `unsafe` code for micro-optimizations | Avoid `unsafe` except where absolutely necessary — do not recommend it for micro-optimizations that don't matter. Safe alternatives like `Span<T>`, `stackalloc` in safe context, and `ArrayPool` cover the vast majority of performance needs |
