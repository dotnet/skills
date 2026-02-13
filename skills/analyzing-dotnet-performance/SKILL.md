---
name: analyzing-dotnet-performance
description: >-
  Scans .NET code for performance anti-patterns and optimization opportunities
  based on the official .NET performance analysis articles. Use when reviewing
  hot-path code, async patterns, memory allocation, regex usage, collections,
  serialization, or I/O for customer-actionable improvements.
---

# .NET Performance Patterns

Scan C#/.NET code for performance anti-patterns and produce prioritized findings with concrete fixes. Patterns sourced from the official .NET performance blog series, distilled to customer-actionable guidance.

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

Always load `references/critical-patterns.md`. These 24 patterns represent deadlocks, crashes, order-of-magnitude regressions, and security vulnerabilities (ReDoS). Flag every match unconditionally.

### Step 2: Detect Code Signals and Load Topic References

Scan the code for signals that indicate which topic-specific reference files to load. Only load files relevant to the code under review.

| Signal in Code | Load Reference |
|----------------|----------------|
| `async`, `await`, `Task`, `ValueTask` | [async-patterns.md](references/async-patterns.md) |
| `Span<`, `Memory<`, `stackalloc`, `ArrayPool`, `string.Substring`, `.Replace(`, `.ToLower()`, `+=` in loops, `params ` | [memory-and-strings.md](references/memory-and-strings.md) |
| `Regex`, `[GeneratedRegex]`, `Regex.Match`, `RegexOptions.Compiled` | [regex-patterns.md](references/regex-patterns.md) |
| `Dictionary<`, `List<`, `.ToList()`, `.Where(`, `.Select(`, LINQ methods, `static readonly Dictionary<` | [collections-and-linq.md](references/collections-and-linq.md) |
| `JsonSerializer`, `HttpClient`, `Stream`, `FileStream`, `Utf8JsonWriter` | [io-and-serialization.md](references/io-and-serialization.md) |

Always load [structural-patterns.md](references/structural-patterns.md) for absence-based detection (unsealed classes, structs missing `IEquatable<T>`).

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

For each finding: **What** (one sentence) → **Why** (impact) → **Fix** (❌ current / ✅ suggested code) → **Caveat** (version requirements or trade-offs).

Group by file, then by severity (🔴 → 🟡 → ℹ️). End with a summary table and disclaimer:

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
| Reporting LINQ outside hot paths | Only flag LINQ in identified hot paths or tight loops |
| Suggesting `ConfigureAwait(false)` in app code | Only needed in library code |
| Recommending `ValueTask` everywhere | Only for hot paths with frequent synchronous completion |
| Flagging `new HttpClient()` in DI services | Check if `IHttpClientFactory` is already in use |
| Suggesting `[GeneratedRegex]` for dynamic patterns | Only flag when the pattern string is a compile-time literal |
| Merging `IsMatch` + `Replace` into one call | Keep `IsMatch` as a fast-path guard when most inputs don't match |

## Further Reading

- [Full .NET Performance Patterns Reference (162 patterns)](https://gist.github.com/artl93/73f93751bd31d3725f8ff9fc8bd4cd3f)
- The annual "Performance Improvements in .NET" series on the .NET Blog
