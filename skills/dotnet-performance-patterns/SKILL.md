---
name: dotnet-performance-patterns
description: >-
  Scans .NET code for performance anti-patterns and optimization opportunities
  based on the official .NET performance analysis articles. Use when reviewing
  hot-path code, async patterns, memory allocation, regex usage, collections,
  serialization, or I/O for customer-actionable improvements.
---

# .NET Performance Patterns

Detect and fix .NET performance anti-patterns in customer code. Patterns are sourced from the official annual .NET performance improvement articles and deep-dive posts on the .NET Blog, distilled to customer-actionable guidance.

## Purpose

Scan a body of C# / .NET code and produce a prioritized list of performance findings with concrete fix suggestions. Focus on patterns customers can act on — not runtime internals.

## When to Use

- Reviewing code that runs on hot paths (request pipelines, tight loops, high-throughput services)
- Auditing async/await usage for deadlocks, thread starvation, or unnecessary overhead
- Checking memory allocation patterns (boxing, substring, temporary arrays)
- Reviewing regex usage for catastrophic backtracking or missed source generation
- Evaluating collection and LINQ usage in performance-sensitive code
- Pre-flight check before performance benchmarking

## When Not to Use

- **Algorithmic complexity analysis** — this skill targets API usage patterns, not algorithm design
- **Code that is not on a hot path** and has no performance requirements — avoid premature optimization

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Source code | Yes | C# files, code blocks, or repository paths to scan |
| Hot-path context | Recommended | Which code paths are performance-critical (e.g., request pipeline, inner loop) |
| Target framework | Recommended | .NET version (affects which patterns apply — some require .NET 8+) |
| Scan depth | Optional | `critical-only` (default), `standard`, or `comprehensive` |

## Workflow

### Step 1: Load Critical Patterns

Always load `references/critical-patterns.md`. These 24 patterns represent deadlocks, crashes, order-of-magnitude regressions, and security vulnerabilities (ReDoS). Flag every match unconditionally.

### Step 2: Detect Code Signals and Load Topic References

Scan the code for signals that indicate which topic-specific reference files to load. Only load files relevant to the code under review.

| Signal in Code | Load Reference | Examples |
|----------------|----------------|----------|
| `async`, `await`, `Task`, `ValueTask` | [async-patterns.md](references/async-patterns.md) | `ConfigureAwait`, `ValueTask` reuse, `Parallel.ForEachAsync` |
| `Span<`, `Memory<`, `stackalloc`, `ArrayPool`, `string.Substring`, string concatenation in loops | [memory-and-strings.md](references/memory-and-strings.md) | `AsSpan()`, interpolation handlers, `u8` literals |
| `.Replace(`, `.ToLower()`, `.ToUpper()`, `+=` inside loops, `char.ToString()` | [memory-and-strings.md](references/memory-and-strings.md) | Chained string intermediates, per-char allocation |
| `params ` in method signatures | [memory-and-strings.md](references/memory-and-strings.md) | `params T[]` array allocation on every call |
| `Regex`, `[GeneratedRegex]`, `Regex.Match`, `Regex.Replace` | [regex-patterns.md](references/regex-patterns.md) | Engine selection, `EnumerateMatches`, span-based APIs |
| `RegexOptions.Compiled` (count > 10 instances in codebase) | [regex-patterns.md](references/regex-patterns.md) | Startup cost budget for many compiled regexes |
| `Dictionary<`, `List<`, `IEnumerable`, `.ToList()`, `.Where(`, `.Select(`, LINQ methods | [collections-and-linq.md](references/collections-and-linq.md) | `FrozenDictionary`, `CollectionsMarshal`, `EnsureCapacity` |
| `.Select(`, `.Where(`, `.Cast(`, `.Take(`, `.Aggregate(` in `*Extensions.cs` or `*Formatter.cs` | [collections-and-linq.md](references/collections-and-linq.md) | LINQ chains on hot paths — delegate + enumerator allocations per call |
| `new Dictionary<`, `new List<`, `new HashSet<` inside method bodies (not fields) | [collections-and-linq.md](references/collections-and-linq.md) | Per-call collection creation that should be hoisted to static fields |
| `static readonly Dictionary<` (not `FrozenDictionary`) | [collections-and-linq.md](references/collections-and-linq.md) | Missed `FrozenDictionary` opportunity on read-heavy tables |
| `JsonSerializer`, `HttpClient`, `Stream`, `FileStream`, `Utf8JsonWriter` | [io-and-serialization.md](references/io-and-serialization.md) | Source-generated JSON, `HttpCompletionOption`, async I/O |

#### Structural-Absence Signals (always load)

These patterns are detected by the **absence** of a keyword, not its presence. Always load `structural-patterns.md` and run the counting scans described in it.

| Absence Signal | Load Reference | What to Count |
|----------------|----------------|---------------|
| Classes missing `sealed` keyword | [structural-patterns.md](references/structural-patterns.md) | Non-abstract, non-static classes that are not sealed |
| Structs missing `IEquatable<T>` | [structural-patterns.md](references/structural-patterns.md) | Structs without `IEquatable<T>` that appear in collections |

**Scan depth controls loading:**
- `critical-only`: Only Step 1 (critical patterns)
- `standard` (default): Steps 1 + 2 (critical + detected topics)
- `comprehensive`: Load all reference files

### Step 3: Scan Code Against Loaded Patterns

For each loaded pattern, check whether the code under review exhibits the anti-pattern. Match on:
- API usage (e.g., `.Result`, `new Regex(`, `new HttpClient()`)
- Structural patterns (e.g., `lock` around `await`, string concatenation in loops)
- Missing best practices (e.g., unsealed classes, missing `ConfigureAwait` in libraries)

#### Scan Recipes

Each reference file contains a **## Detection** section with grep-based scan recipes and a **### Patterns Requiring Manual Review** section for items that need type context or multi-line analysis. After loading a reference file in Step 2, run all of its detection recipes and report exact counts. Do not rely on summarized agent output for counting.

**Rules:**
- Run every recipe in every loaded reference file
- Report exact counts, not estimates
- When a compiled regex appears inside a class constructor, count how many instances of that class are created at startup to determine total budget
- For absence patterns, always count both sides (the Verify-the-Inverse Rule below)

#### Mandatory Execution Checklist (Gate)

**Before proceeding to Step 4 (Classify and Prioritize), you MUST emit a scan execution checklist.** This checklist ensures no detection recipe was skipped. For each loaded reference file, list every grep/scan recipe from its `## Detection` section, the exact command you ran, and the result count.

Format:

```markdown
### Scan Execution Checklist

#### critical-patterns.md (3 recipes)
- [x] `.IndexOf(string)` without StringComparison → `grep -rn ...` → **N hits**
- [x] `.Substring(` calls → `grep -rn ...` → **N hits**
- [x] `.StartsWith/.EndsWith/.Contains` without StringComparison → `grep -rn ...` → **N hits**

#### memory-and-strings.md (4 recipes)
- [x] `.ToLower()/.ToUpper()` without culture → `grep -rn ...` → **N hits**
- [x] Chained `.Replace(` calls → `grep -rn ...` → **N hits**
- [x] `params ` in method signatures → `grep -rn ...` → **N hits**
- [x] LINQ `.All/.Any` on char → `grep -rn ...` → **N hits**

#### regex-patterns.md (3 recipes)
- [x] `RegexOptions.Compiled` count → **N hits**
- [x] `GeneratedRegex` count → **N hits**
- [x] `new Regex(` uncached → **N hits**

#### collections-and-linq.md (6 recipes)
- [x] `static readonly Dictionary<` → **N hits**
- [x] `static readonly FrozenDictionary<` → **N hits**
- [x] `new Dictionary<` in method bodies → **N hits**
- [x] `new List<` in method bodies → **N hits**
- [x] `StringComparer.CurrentCulture` → **N hits**
- [x] LINQ chains in extension/hot-path files → **N hits**

#### structural-patterns.md (4 recipes)
- [x] Unsealed classes → **N hits**
- [x] Sealed classes → **N hits**
- [x] Structs without IEquatable → **N hits**
- [x] Structs with IEquatable → **N hits**

#### async-patterns.md (1 recipe)
- [x] `async void` → **N hits**

#### io-and-serialization.md (2 recipes)
- [x] `new HttpClient(` → **N hits**
- [x] `new JsonSerializerOptions` uncached → **N hits**

**Total: N/N recipes executed.**
```

**Gate rule:** If any recipe shows `[ ]` (not executed), you must go back and run it before proceeding. Do not skip recipes because you believe the codebase doesn't contain the pattern — the scan confirms it. A result of **0 hits** is a valid and valuable finding (positive confirmation of good practice).

#### Verify-the-Inverse Rule

For every absence pattern, **always count both sides** and report the ratio:
- "N of M classes are sealed" (not just "N classes are unsealed")
- "N of M structs implement IEquatable" (not just "N structs are missing it")
- "N of M static dictionaries use FrozenDictionary" (not just "N are plain Dictionary")

A 0/185 ratio is a codebase-wide systematic issue. A 12/15 ratio is a consistency fix. The ratio determines severity.

### Step 3b: Cross-File Consistency Check

For each optimization pattern detected in one file, check whether sibling files use the un-optimized equivalent. Siblings are files in the same directory, implementing the same interface, or inheriting from the same base class.

**Examples:**
- If one truncator uses `StringHumanizeExtensions.Concat()` with `.AsSpan()`, flag sibling truncators that use `value + truncationString` (raw `+` concatenation)
- If one number-to-words converter uses `StringBuilder`, flag sibling converters that use `+=` in loops
- If one static dictionary uses `FrozenDictionary`, flag sibling static dictionaries that use plain `Dictionary`

Flag inconsistencies as 🟡 Moderate — the optimized pattern is already proven in the codebase but not applied uniformly. Include the file that uses the optimized pattern as evidence.

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

### Step 5: Generate Fix Suggestions

For each finding, provide:
1. **What**: The anti-pattern detected (one sentence)
2. **Why**: Performance impact (from the reference file's impact statement)
3. **Fix**: Concrete code change — show the ❌ current code and ✅ suggested replacement
4. **Caveat**: Any .NET version requirement or trade-off

Group findings by file, then by severity (🔴 first, then 🟡, then ℹ️).

### Step 6: Summarize

End with a summary table:

```markdown
| Severity | Count | Top Issue |
|----------|-------|-----------|
| 🔴 Critical | N | e.g., Sync-over-async in request pipeline |
| 🟡 Moderate | N | e.g., LINQ in hot loop |
| ℹ️ Info | N | e.g., Unsealed public class |
```

## Output Format

```markdown
## Performance Scan Results

**Target framework:** .NET 8
**Scan depth:** standard
**Files scanned:** 12
**Reference files loaded:** critical-patterns, async-patterns, collections-and-linq

### 🔴 Critical Findings

#### [File.cs:42] Sync-over-Async Deadlock Risk
**Pattern:** Never Block on Async (critical-patterns.md #2)

The call to `GetDataAsync().Result` blocks the calling thread...

❌ Current:
\```csharp
var data = GetDataAsync().Result;
\```

✅ Suggested:
\```csharp
var data = await GetDataAsync();
\```

**Impact:** Deadlock on SynchronizationContext; thread pool starvation under load.

---

### 🟡 Moderate Findings
...

### Summary
| Severity | Count | Top Issue |
|----------|-------|-----------|
| 🔴 Critical | 2 | Sync-over-async |
| 🟡 Moderate | 5 | LINQ in hot loop |

> ⚠️ **Disclaimer:** These results are generated by an AI assistant and are non-deterministic. Findings may include false positives, miss real issues, or suggest changes that are incorrect for your specific context. Always verify recommendations with benchmarks and human review before applying changes to production code.
```

## Validation

Before delivering results, verify:

- [ ] All 🔴 Critical patterns from `critical-patterns.md` were checked against the code
- [ ] Topic reference files were loaded only when matching signals were detected
- [ ] Each finding includes a concrete code fix, not just a warning
- [ ] Findings are grouped by file, ordered by severity
- [ ] .NET version requirements are noted when a fix requires a newer framework
- [ ] No findings were raised for code that is clearly not on a hot path (unless scan depth is `comprehensive`)
- [ ] Hot-path context from the user was applied to prioritization
- [ ] Summary table is included at the end

### Recipe Completeness Verification (Final Gate)

**After generating all findings but before delivering the final report**, perform this completeness check:

1. **Count recipes defined**: For each loaded reference file, count the number of grep/scan commands in its `## Detection` section.
2. **Count recipes executed**: Count the `[x]` entries in your Scan Execution Checklist from Step 3.
3. **Compare**: If `executed < defined`, go back and run the missing recipes. Report the comparison:

```
Recipe completeness: 23/23 executed across 7 reference files ✅
```

or

```
Recipe completeness: 20/23 executed across 7 reference files ❌
Missing:
  - memory-and-strings.md: LINQ .All/.Any on char (not run)
  - collections-and-linq.md: StringComparer.CurrentCulture (not run)
  - collections-and-linq.md: LINQ chains in extension/hot-path files (not run)
→ Running missing recipes now...
```

4. **Cross-reference findings with zero-count recipes**: For every recipe that returned 0 hits, include it in the report as a **positive confirmation** (e.g., "✅ No `.Substring()` calls found — already clean"). Zero-count results prevent future analysts from re-scanning the same patterns.

5. **Verify manual review items**: For each `### Patterns Requiring Manual Review` section in loaded reference files, confirm you at least acknowledged the item (even if you couldn't fully assess it via grep). List any manual review items you were unable to assess and explain why.

## Common Pitfalls

| Pitfall | Why It's Wrong | Correct Approach |
|---------|---------------|-----------------|
| Flagging every `Dictionary` as needing `FrozenDictionary` | Only read-heavy, static collections benefit | Check if the dictionary is mutated after construction |
| Suggesting `Span<T>` in async methods | `Span<T>` is a ref struct — cannot cross await boundaries | Use `Memory<T>` in async code, `Span<T>` in sync hot paths |
| Reporting LINQ usage outside hot paths | LINQ is fine for readability when not performance-critical | Only flag LINQ in identified hot paths or tight loops |
| Suggesting `ConfigureAwait(false)` in application code | Only needed in library code; app code uses `SynchronizationContext` intentionally | Check if the code is in a library or application project |
| Recommending `ValueTask` everywhere | `ValueTask` has stricter consumption rules — misuse causes bugs | Only recommend for hot paths with frequent synchronous completion |
| Flagging `new HttpClient()` in DI-registered services | `IHttpClientFactory` handles lifetime; direct `new` in a singleton is the problem | Check whether the code uses dependency injection |
| Suggesting source-generated regex for dynamic patterns | `[GeneratedRegex]` requires compile-time constant patterns | Only flag when the pattern string is a literal |
| Merging `IsMatch` + `Replace` into a single `Replace` call | `Replace` is expensive even on no-match (walks full input, builds replacement state). When most inputs don't match (e.g., rule tables), `IsMatch` rejects cheaply. | Keep `IsMatch` as a fast-path guard before `Replace` when iterating many rules where the common case is no-match |

## Further Reading

- [Full .NET Performance Patterns Reference (162 patterns)](https://gist.github.com/artl93/73f93751bd31d3725f8ff9fc8bd4cd3f) — complete corpus with detailed rationale
- The annual "Performance Improvements in .NET" series on the .NET Blog
