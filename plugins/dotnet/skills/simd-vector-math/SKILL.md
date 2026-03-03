---
name: simd-vector-math
description: Optimizes scalar float/double vector math in .NET 8+ with cross-platform Vector128/Vector256 SIMD intrinsics. Transforms hot-path scalar code into vectorized implementations — never generates greenfield.
---

# SIMD Vector Math Optimization

> **STOP — Not everything is a SIMD opportunity.** String processing (`ToLower`, `Trim`, `Sort`), small collections (< 20 items), and operations covered by framework APIs (`System.Text.Ascii`, `TensorPrimitives`, `SearchValues<T>`) are NOT candidates for manual SIMD. Using `Vector256` for ASCII lowercasing when `System.Text.Ascii.ToLower()` exists is harmful — it adds complexity for zero benefit. If the code does not contain a scalar loop over `float[]`/`double[]`/`byte[]` arrays ≥ 16 elements, report "no optimization opportunity" and stop immediately.

> **Early exit:** If the code is computing a standard operation covered by `TensorPrimitives` (dot product, cosine similarity, softmax, element-wise add/multiply, etc.), replace with the `TensorPrimitives` call and stop. Those are already SIMD-optimized internally. This skill is for cases that need manual vectorization.

Scan an existing .NET 8+ codebase for scalar floating-point vector math on hot paths that would benefit from manual SIMD vectorization using cross-platform `Vector128`/`Vector256` intrinsics. Focus on operations where no existing framework API covers the computation. If no SIMD-eligible candidates exist, report that and stop.

## Decision Gate (mandatory — do this FIRST, before writing any code)

1. Does the code contain a scalar loop over numeric arrays (`float[]`, `double[]`, `byte[]`, `int[]`)? **If NO → stop, report "no optimization opportunity"**
2. Are the arrays ≥ 16 elements on the hot path? **If NO → stop**
3. Is the operation already covered by `TensorPrimitives` or another framework API? **If YES → use that API instead and stop**
4. Is this string/object processing, not numeric array math? **If YES → stop, this skill does not apply**

State your assessment before implementing: `[SIMD CANDIDATE: <method name>, Category <A/B/C/D>]` or `[NO SIMD OPPORTUNITY: <reason>]`. Do NOT proceed to implementation without stating one of these.

## When to Use

- Multi-array fused computations with 3+ input arrays where no single `TensorPrimitives` call applies (e.g., weighted distance, fused multiply-accumulate with per-element parameters)
- Cross-type conversions combined with arithmetic (e.g., quantized int8→float dequantization with scale/offset)
- Custom distance metrics or similarity functions not covered by `TensorPrimitives`
- Domain-specific float processing with no suitable framework API (e.g., specialized quantization, custom activation functions)

## When NOT to Use — Report "no opportunity" instead

- No existing scalar code to optimize (greenfield)
- Operation is covered by `TensorPrimitives` (dot product, cosine similarity, softmax, element-wise arithmetic, etc.) — use it instead
- Code already uses `TensorPrimitives`, `Vector256<float>`, `Vector128<float>`, or `Vector<T>`
- Vectors consistently < 16 floats (64 bytes)
- Bottleneck is memory bandwidth, not compute
- String/object processing with small collections — no float array math
- Code requires `decimal` or arbitrary-precision arithmetic

## Pattern Categories

**A — Multi-input fused computations:** Operations on 3+ parallel arrays that require a single pass for efficiency (e.g., weighted Euclidean distance: `sum += w[i] * (a[i] - b[i])²`). No `TensorPrimitives` method handles these directly.

**B — Cross-type conversions:** Converting between integer and float types combined with arithmetic (e.g., quantized dequantization: `output[i] = (quantized[i] - zeroPoint) * scale`). Requires SIMD widening and type conversion intrinsics.

**C — Custom distance/similarity metrics:** Domain-specific distance functions not in `TensorPrimitives` (e.g., Mahalanobis distance, weighted Minkowski distance).

**D — Batch lookup/gather:** Embedding table lookups, scatter/gather, quantized dequantization. No framework API typically exists.

## Transformation Rules

### Assessment (do this quickly)
- **First:** Check if `TensorPrimitives` or another existing API covers the operation — if so, use it and stop (early exit, no manual SIMD needed)
- Skip if vectors typically < 16 floats
- Skip if already uses `TensorPrimitives`/`Vector256`/`Vector<T>`

### Required imports
```csharp
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
```

Do NOT use platform-specific imports (`System.Runtime.Intrinsics.X86`, `System.Runtime.Intrinsics.Arm`). Use the cross-platform `Vector128`/`Vector256` APIs instead.

### Dispatch pattern

Use cross-platform hardware acceleration checks. Do NOT use platform-specific checks like `Avx2.IsSupported`, `Fma.IsSupported`, or `AdvSimd.IsSupported`.

```csharp
if (!Vector128.IsHardwareAccelerated || data.Length < Vector128<float>.Count)
{
    // scalar fallback (never delete this path)
}
else if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<float>.Count)
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
- **Create/broadcast:** `Vector128.Create(value)` to broadcast scalar to all lanes
- **Load/store:** `Vector128.LoadUnsafe(ref src, offset)`, `Vector128.StoreUnsafe(vec, ref dst, offset)`
- **Arithmetic:** operators `+`, `-`, `*`, `/` on vector types
- **FMA:** `Vector128.MultiplyAddEstimate(a, b, c)` for fused multiply-add
- **Min/Max:** `Vector128.Min(a, b)`, `Vector128.Max(a, b)`
- **Horizontal sum:** `Vector128.Sum(vec)` for final reduction
- **Type conversion:** `Vector128.WidenLower(v)` / `Vector128.WidenUpper(v)` for widening, `Vector128.ConvertToSingle(intVec)` for int→float

### Multi-input fused computation pattern
Load from multiple arrays in the same loop, perform fused arithmetic, accumulate into vector accumulator(s). Use `Vector128.Sum` for final horizontal reduction. Always handle the loop remainder with scalar code for accumulations (do NOT use overlapping-vector to avoid double-counting).

### Cross-type conversion pattern
For byte→float: Load `Vector128<byte>` (16 bytes) → `WidenLower`/`WidenUpper` to `Vector128<ushort>` → widen again to `Vector128<uint>` → `ConvertToSingle` to `Vector128<float>`. Process 4 float vectors per byte vector load.

### Memory access pattern
Process data in vector-width chunks. For accumulations (reductions): use scalar remainder for remaining elements after the last full vector. For element-wise transforms: overlapping last-vector is safe if the operation is idempotent. Always use `ref MemoryMarshal.GetReference(span)` and `LoadUnsafe(ref T, nuint elementOffset)`.

Use `Assert.Equal(expected, actual, precision: 5)` for float comparisons in tests, since SIMD may reorder floating-point additions.

## Validation (required)

```bash
dotnet build -c Release -warnaserror
dotnet test -c Release
```

If no tests exist, add tests for: empty array, array < vector width, exactly one vector width, large array, special values (0, NaN). **All existing tests must pass.**

## Key Rules
- Preserve original method signature — drop-in replacement
- Never delete scalar code — it's the fallback
- Use cross-platform `Vector128`/`Vector256` APIs — never platform-specific intrinsics (`Avx2`, `Fma`, `Sse`, `AdvSimd`)
- If no SIMD candidates found, report "no optimization opportunity" and explain why
- Skip categories with no matching code — don't generate from scratch
