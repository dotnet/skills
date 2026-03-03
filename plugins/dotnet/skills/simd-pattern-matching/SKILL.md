---
name: simd-pattern-matching
description: Optimizes scalar byte/string pattern matching in .NET 8+ with cross-platform Vector128/Vector256 SIMD intrinsics. Transforms hot-path scalar code into vectorized implementations — never generates greenfield.
---

# SIMD Pattern Matching Optimization

> **STOP — Not everything is a SIMD opportunity.** String processing on small collections (`ToLower`, `Trim`, `Sort`, dedup on < 20 items), operations covered by framework APIs (`Span<T>.IndexOf`, `SearchValues<T>`, `System.Text.Ascii`), and code without large byte/char buffer loops are NOT candidates for manual SIMD. Using `Vector256` for ASCII lowercasing when `System.Text.Ascii.ToLower()` exists is harmful — it adds complexity for zero benefit. If the code does not contain a scalar loop over a byte/char buffer ≥ 64 bytes, report "no optimization opportunity" and stop immediately.

> **Early exit:** If the code is simply reimplementing a framework API (`Span<T>.IndexOf`, `MemoryExtensions.IndexOfAny`, `SearchValues<T>`, etc.), replace with the API call and stop. Those are already SIMD-optimized internally. This skill is for cases that need manual vectorization.

Scan an existing .NET 8+ codebase for scalar pattern matching code on hot paths that would benefit from manual SIMD vectorization using cross-platform `Vector128`/`Vector256` intrinsics. Focus on byte-level operations over large buffers where no existing framework API covers the operation. If no SIMD-eligible candidates exist, report that and stop.

## Decision Gate (mandatory — do this FIRST, before writing any code)

1. Does the code contain a scalar loop over a byte or char buffer? **If NO → stop, report "no optimization opportunity"**
2. Are the buffers ≥ 64 bytes on the hot path? **If NO → stop**
3. Is the operation already covered by a framework API (`IndexOf`, `Contains`, `SearchValues<T>`, `System.Text.Ascii`)? **If YES → use that API instead and stop**
4. Is this string/object processing on small collections (< 20 items), not bulk buffer scanning? **If YES → stop, this skill does not apply**

State your assessment before implementing: `[SIMD CANDIDATE: <method name>, Category <A/B/C/D>]` or `[NO SIMD OPPORTUNITY: <reason>]`. Do NOT proceed to implementation without stating one of these.

## When to Use

- Character-class membership counting/classification in tight loops over large buffers (e.g., counting alphanumeric bytes, multi-range byte classification)
- Custom byte-range validation on large buffers (e.g., verifying all bytes are printable ASCII, valid hex digits)
- Approximate matching (Levenshtein distance in loops, edit-distance thresholding, pattern ≤ 63 bytes)
- Bulk byte scanning with custom logic that no framework API covers (e.g., multi-range classification, nibble-based lookup)

## When NOT to Use — Report "no opportunity" instead

- No existing scalar code to optimize (greenfield)
- Operation is already covered by a framework API (`IndexOf`, `Contains`, `SequenceEqual`, `SearchValues<T>`) — use the API instead
- Code already uses `Vector256`/`Vector128`/`TensorPrimitives`/`Vector<T>`/`SearchValues<T>`
- Buffers consistently < 64 bytes
- Regex with back-tracking/capture groups/lookahead
- String processing on small collections (< 20 items) where HashSet/sort dominates
- `ReadOnlySpan<char>` UTF-16 that can't convert to byte processing
- Code that doesn't process large byte/char buffers in loops

## Pattern Categories

**A — Character class membership:** `if` chains testing byte/char ranges in tight loops (`c >= 'a' && c <= 'z'`), lookup table arrays indexed by byte value. SIMD range comparison or nibble-lookup approach.

**B — Byte-range validation:** Loops checking if every byte satisfies a condition (e.g., all printable ASCII, all valid Base64). SIMD subtract-and-compare for unsigned range checks across full vectors.

**C — Approximate matching:** Levenshtein distance in loops, edit-distance thresholding (only if pattern ≤ 63 bytes).

**D — Bulk byte counting/scanning:** Counting byte occurrences, scanning with custom multi-condition logic that no single framework API covers.

## Transformation Rules

### Assessment (do this quickly)
- **First:** Check if an existing framework API covers the operation — if so, use it and stop (early exit, no manual SIMD needed)
- Skip if buffer typically < 64 bytes
- Skip if already uses `Vector256`/`Vector128`/`TensorPrimitives`/`SearchValues<T>`

### Required imports (for manual SIMD only)
```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
```

Do NOT use platform-specific imports (`System.Runtime.Intrinsics.X86`, `System.Runtime.Intrinsics.Arm`). Use the cross-platform `Vector128`/`Vector256` APIs instead.

### Dispatch pattern

Use cross-platform hardware acceleration checks. Do NOT use platform-specific checks like `Avx2.IsSupported`, `Sse42.IsSupported`, or `AdvSimd.IsSupported`.

```csharp
if (!Vector128.IsHardwareAccelerated || buffer.Length < Vector128<byte>.Count)
{
    // scalar fallback (never delete this path)
}
else if (Vector256.IsHardwareAccelerated && buffer.Length >= Vector256<byte>.Count)
{
    // Vector256 code path
}
else
{
    // Vector128 code path
}
```

### SIMD operations reference

Use cross-platform `Vector128`/`Vector256` operations:
- **Create/broadcast:** `Vector128.Create(value)`, `Vector256.Create(value)`
- **Load/store:** `Vector128.LoadUnsafe(ref src, offset)`, `Vector256.StoreUnsafe(vec, ref dst, offset)`
- **Comparison:** `Vector128.Equals(a, b)`, `Vector128.GreaterThan(a, b)`, `Vector128.LessThan(a, b)`
- **Bitwise:** operators `&`, `|`, `^`, `~`
- **Mask extraction:** `vec.ExtractMostSignificantBits()` → `uint` bitmask
- **Population count:** `BitOperations.PopCount(mask)` for counting matches
- **Shuffle:** `Vector128.Shuffle(vec, indices)` for nibble-lookup tables

### Vectorized range comparison
Broadcast range bounds → subtract lower bound (wraps for out-of-range bytes in unsigned arithmetic) → compare against range width. For validation: check all lanes pass. For counting: `ExtractMostSignificantBits` → `PopCount`.

### Nibble-lookup for character classes
Build two 16-entry lookup tables (high/low nibble). `Vector128.Shuffle` with nibble index → AND results → `ExtractMostSignificantBits` + `PopCount` for counting.

### Memory access pattern
Head (scalar for pre-vector bytes) → Body (`Vector256.LoadUnsafe` / `Vector128.LoadUnsafe`) → Tail (overlapping last-vector for idempotent operations, or scalar remainder). Always use `ref MemoryMarshal.GetReference(span)` and `LoadUnsafe(ref T, nuint elementOffset)`.

## Validation (required)

```bash
dotnet build -c Release -warnaserror
dotnet test -c Release
```

If no tests exist for the method, add tests for: empty input, input < vector width, match at start/end, no match, input of exactly one vector width. **All existing tests must pass.**

## Key Rules
- Preserve original method signature — drop-in replacement
- Never delete scalar code — it's the fallback
- Use cross-platform `Vector128`/`Vector256` APIs — never platform-specific intrinsics (`Avx2`, `Sse42`, `AdvSimd`)
- If no SIMD candidates found, report "no optimization opportunity" and explain why
- Skip categories with no matching code — don't generate from scratch
