# Judge Report: Detects struct missing IEquatable<T>

## Baseline Judge
Overall Score: 1/5
Reasoning: The agent completely failed to deliver on the task. While it took reasonable steps to explore the working directory (view, glob, ls), upon finding it empty it simply gave up and asked the user for code. It made no attempt to search more broadly, generate analysis based on common Money struct patterns, or provide any useful technical content. The output contains zero analysis against any of the four rubric criteria. The agent should have either found the code (if it existed elsewhere) or, at minimum, provided general guidance about common performance pitfalls with Money structs in C#/.NET.

- **Identifies the struct implements Equals(object) but not IEquatable<Money>, causing boxing**: 1/5 — The agent did not analyze any code at all. It found an empty directory and asked for the code, never identifying the Equals(object) vs IEquatable<Money> issue.
- **Recommends implementing IEquatable<Money> with a strongly-typed Equals(Money other) method**: 1/5 — No recommendation was made. The agent produced no analysis whatsoever regarding IEquatable<Money> or a strongly-typed Equals method.
- **Notes the struct is mutable and should be a readonly struct for performance and correctness**: 1/5 — The agent did not examine or comment on mutability of the struct or recommend making it readonly. No analysis was performed.
- **Explains that boxing occurs when Money is used as a Dictionary key or in LINQ comparisons without IEquatable<T>**: 1/5 — No explanation of boxing in Dictionary or LINQ scenarios was provided. The agent's output contains no technical analysis at all.

## With-Skill Judge
Overall Score: 5/5
Reasoning: The agent produced an excellent, comprehensive performance analysis. It correctly identified all key issues (missing IEquatable<T>, mutable struct, missing operators, unsealed classes), provided accurate severity rankings, gave concrete code fixes, and included positive findings. The analysis was efficient (6 tool calls, 49.8s, no meaningful errors). The output is well-structured with a checklist, detailed findings, code samples, and a summary table. The technical explanations are accurate and demonstrate deep .NET performance knowledge. The disclaimer about AI-generated results is a nice touch for production use.

- **Identifies the struct implements Equals(object) but not IEquatable<Money>, causing boxing**: 5/5 — The agent clearly and accurately identifies this as Finding #1 ('Critical'), correctly noting that Money has Equals(object) at line 14 but does not implement IEquatable<Money>. It explains the boxing consequence in detail, including per-probe allocation costs in collection lookups.
- **Recommends implementing IEquatable<Money> with a strongly-typed Equals(Money other) method**: 5/5 — The agent provides a complete, correct code fix showing the IEquatable<Money> implementation with a strongly-typed Equals(Money other) method and properly delegates Equals(object) to it. The code sample is production-ready.
- **Notes the struct is mutable and should be a readonly struct for performance and correctness**: 5/5 — Finding #2 explicitly addresses mutable struct concerns, correctly identifying public setters on lines 5-6 and recommending readonly struct with init setters. The explanation about defensive copies when used with readonly fields or in parameters is technically accurate and demonstrates deep understanding.
- **Explains that boxing occurs when Money is used as a Dictionary key or in LINQ comparisons without IEquatable<T>**: 5/5 — Finding #1 explicitly states: 'Every call to Equals() or use in Dictionary/HashSet/LINQ boxes the Money value, allocating on the heap. In collection lookups this is per-probe — O(n) allocations for a hash collision chain.' This directly and thoroughly addresses the criterion with specific collection types mentioned.

## Baseline Agent Output
```
The working directory is empty — there's no codebase present. Could you provide or point me to the source files containing the `Money` struct? You can:

1. **Paste the code** directly in the chat
2. **Provide a path** to the repository or files
3. **Clone a repo** — give me the URL and I'll pull it down
```

## With-Skill Agent Output
```
Now I have the full picture. Here is the analysis:

---

## .NET Performance Analysis: `Models.cs`

### Scan Execution Checklist

| Recipe | Result |
|--------|--------|
| Struct missing `IEquatable<T>` | ✅ 1 hit (`Money`) |
| Struct not marked `readonly` | ✅ 1 hit (`Money`) |
| `Equals(object)` on struct (boxing) | ✅ 1 hit (`Money.Equals`) |
| Mutable struct (public setters) | ✅ 1 hit (`Money`) |
| Unsealed classes | ✅ 2 hits (`OrderItem`, `InvoiceLine`) |
| Missing `==`/`!=` operators on struct with `Equals` | ✅ 1 hit (`Money`) |
| Async/Task patterns | ⬜ No signals |
| Regex patterns | ⬜ No signals |
| Collections/LINQ patterns | ⬜ No signals |
| I/O/Serialization patterns | ⬜ No signals |
| String concat/replace patterns | ⬜ No signals |

---

### Findings

#### 1. Struct missing `IEquatable<T>` — boxing on every equality check (1 instance)
🔴 **Critical**
**Impact:** Every call to `Equals()` or use in `Dictionary`/`HashSet`/LINQ boxes the `Money` value, allocating on the heap. In collection lookups this is per-probe — O(n) allocations for a hash collision chain.
**Files:** `Models.cs:L14` — `Equals(object obj)`
**Fix:** Implement `IEquatable<Money>` with a strongly-typed `Equals(Money other)` method; delegate `Equals(object)` to it.

```csharp
public readonly struct Money : IEquatable<Money>
{
    // ...
    public bool Equals(Money other)
        => Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object obj)
        => obj is Money other && Equals(other);
}
```

#### 2. Mutable struct — defensive-copy risk (1 instance)
🟡 **Moderate**
**Impact:** `Money` has public setters, making it a mutable struct. When stored in a `readonly` field or passed via `in`, the compiler generates silent defensive copies to preserve value semantics, doubling copy cost. Mutable value-type semantics are also a common source of logic bugs (mutations lost on copies).
**Files:** `Models.cs:L5-6`
**Fix:** Mark the struct `readonly` and change setters to `init` (or remove them, using the constructor only).

```csharp
public readonly struct Money : IEquatable<Money>
{
    public decimal Amount { get; init; }
    public string Currency { get; init; }
    // ...
}
```

#### 3. Missing `==` / `!=` operators (1 instance)
🟡 **Moderate**
**Impact:** Without operators, callers must use `.Equals()` which goes through the `object` overload and boxes unless `IEquatable<Money>` is implemented. Even with `IEquatable`, missing operators are a usability gap that pushes callers toward the boxing path.
**Files:** `Models.cs:L3-25`
**Fix:** Add `operator ==` and `operator !=` delegating to `Equals(Money)`.

```csharp
public static bool operator ==(Money left, Money right) => left.Equals(right);
public static bool operator !=(Money left, Money right) => !left.Equals(right);
```

#### 4. Unsealed classes (2 instances)
ℹ️ **Info**
**Impact:** Unsealed classes prevent the JIT from devirtualizing method calls. Sealing enables direct dispatch for virtual/interface calls, which matters on hot paths.
**Files:** `Models.cs:L27` (`OrderItem`), `Models.cs:L34` (`InvoiceLine`)
**Fix:** Add `sealed` keyword: `public sealed class OrderItem`, `public sealed class InvoiceLine`.

### Positive Findings

- ✅ `GetHashCode()` uses `HashCode.Combine` — correct and allocation-free
- ✅ No async, LINQ, regex, or I/O anti-patterns present
- ✅ Struct is appropriately small (decimal + string reference = ~24 bytes)

---

### Summary

| Severity | Count | Top Issue |
|----------|-------|-----------|
| 🔴 Critical | 1 | Missing `IEquatable<Money>` causes boxing on every comparison |
| 🟡 Moderate | 2 | Mutable struct + missing operators |
| ℹ️ Info | 1 | Unsealed classes |

> ⚠️ **Disclaimer:** These results are generated by an AI assistant and are non-deterministic. Findings may include false positives, miss real issues, or suggest changes that are incorrect for your specific context. Always verify recommendations with benchmarks and human review before applying changes to production code.
```