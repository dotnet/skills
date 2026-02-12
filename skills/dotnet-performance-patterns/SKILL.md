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
- **JIT / runtime internals** — use `dotnet-jit-optimization` for inlining thresholds, PGO, tiered compilation
- **Async architectural decisions** — use `dotnet-async-patterns` for async state machine design and ValueTask lifecycle
- **Synchronization primitive selection** — use `dotnet-sync-primitives` for lock contention analysis
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
| `Regex`, `[GeneratedRegex]`, `Regex.Match`, `Regex.Replace` | [regex-patterns.md](references/regex-patterns.md) | Engine selection, `EnumerateMatches`, span-based APIs |
| `Dictionary<`, `List<`, `IEnumerable`, `.ToList()`, `.Where(`, `.Select(`, LINQ methods | [collections-and-linq.md](references/collections-and-linq.md) | `FrozenDictionary`, `CollectionsMarshal`, `EnsureCapacity` |
| `JsonSerializer`, `HttpClient`, `Stream`, `FileStream`, `Utf8JsonWriter` | [io-and-serialization.md](references/io-and-serialization.md) | Source-generated JSON, `HttpCompletionOption`, async I/O |

**Scan depth controls loading:**
- `critical-only`: Only Step 1 (critical patterns)
- `standard` (default): Steps 1 + 2 (critical + detected topics)
- `comprehensive`: Load all reference files

### Step 3: Scan Code Against Loaded Patterns

For each loaded pattern, check whether the code under review exhibits the anti-pattern. Match on:
- API usage (e.g., `.Result`, `new Regex(`, `new HttpClient()`)
- Structural patterns (e.g., `lock` around `await`, string concatenation in loops)
- Missing best practices (e.g., unsealed classes, missing `ConfigureAwait` in libraries)

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
