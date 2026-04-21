---
name: exp-simd-vectorization
description: "Optimizes hot-path scalar loops in .NET 8+ with cross-platform Vector128/Vector256/Vector512 SIMD intrinsics, or replaces manual math loops with single TensorPrimitives API calls. Covers byte-range validation, character counting, bulk bitwise ops, cross-type conversion, fused multi-array computations, and float/double math operations."
---

# SIMD Vectorization

## Decision Gate
1. **Check `Span<T>` and `MemoryExtensions` first.** If the operation can be expressed using built-in `Span<T>` methods (e.g., `Contains`, `IndexOf`, `CopyTo`, `SequenceEqual`) or `MemoryExtensions`, use them — no additional dependency is needed and the runtime already vectorizes many of these internally.
2. **Check for TensorPrimitives next.** If one or more TensorPrimitives methods cover the operation → use them. If the `.csproj` does NOT already reference `System.Numerics.Tensors`, **add the package**, for example: `<PackageReference Include="System.Numerics.Tensors" />` (or use the versioning approach already used by your solution). Then replace the scalar loop with TP calls and stop. See the full API table below. Compose multiple TP calls when needed (e.g., finding both min and max → `TensorPrimitives.Min(span)` + `TensorPrimitives.Max(span)` as two calls). Do NOT write manual Vector128 code for operations TP already handles.
3. **Scalar loop over contiguous array/span** of `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `nint`, `nuint`, `float`, `double` (and `char` via reinterpretation as `ushort`)? → Implement with explicit `Vector128<T>` / `Vector256<T>` / `Vector512<T>` intrinsics using the patterns below.
4. **No contiguous numeric arrays to process** (dictionary lookups, tree traversals, linked lists, state machines, string formatting, small collections, enum comparisons, recursive algorithms, decimal arithmetic)? → Report `[NO SIMD OPPORTUNITY]` and write a **full paragraph** explaining WHY, referencing the specific code characteristics that prevent vectorization (e.g., "State machines require sequential branching on enum values — there are no contiguous numeric arrays to process in parallel, and each transition depends on the previous state"). This explanation is graded.

## TensorPrimitives API Reference
TensorPrimitives APIs are generic and work for any primitive type that satisfies the method's generic constraints — not just `float`/`double`. For example, `Sum` requires `IAdditionOperators<T,T,T>` + `IAdditiveIdentity<T,T>` and works for all primitive numeric types, while `CosineSimilarity` requires `IRootFunctions<T>` and only works for `float`/`double`. If the project doesn't already reference `System.Numerics.Tensors`, add it to the `.csproj`. Replace the entire manual loop with **one or more** `TensorPrimitives` calls as needed (prefer a single call when possible):

### Reductions (span → scalar)
| Operation | API |
|-----------|-----|
| Sum | `TensorPrimitives.Sum(span)` |
| Sum of squares | `TensorPrimitives.SumOfSquares(span)` |
| Sum of magnitudes (L1 norm) | `TensorPrimitives.SumOfMagnitudes(span)` |
| L2 norm | `TensorPrimitives.Norm(span)` |
| Product of all elements | `TensorPrimitives.Product(span)` |
| Min value | `TensorPrimitives.Min(span)` |
| Max value | `TensorPrimitives.Max(span)` |
| Index of max | `TensorPrimitives.IndexOfMax(span)` |
| Index of min | `TensorPrimitives.IndexOfMin(span)` |
| Dot product | `TensorPrimitives.Dot(a, b)` |
| Cosine similarity | `TensorPrimitives.CosineSimilarity(a, b)` |
| Euclidean distance | `TensorPrimitives.Distance(a, b)` |

### Element-wise transforms (span → span)
| Operation | API |
|-----------|-----|
| Negate | `TensorPrimitives.Negate(src, dst)` |
| Abs | `TensorPrimitives.Abs(src, dst)` |
| Sqrt | `TensorPrimitives.Sqrt(src, dst)` |
| Exp | `TensorPrimitives.Exp(src, dst)` |
| Log | `TensorPrimitives.Log(src, dst)` |
| Log2 | `TensorPrimitives.Log2(src, dst)` |
| Tanh | `TensorPrimitives.Tanh(src, dst)` |
| Sigmoid | `TensorPrimitives.Sigmoid(src, dst)` |
| SoftMax | `TensorPrimitives.SoftMax(src, dst)` |
| Sinh | `TensorPrimitives.Sinh(src, dst)` |
| Cosh | `TensorPrimitives.Cosh(src, dst)` |
| Round | `TensorPrimitives.Round(src, dst)` |
| Floor | `TensorPrimitives.Floor(src, dst)` |
| Ceiling | `TensorPrimitives.Ceiling(src, dst)` |
| CopySign | `TensorPrimitives.CopySign(src, sign, dst)` |
| Pow | `TensorPrimitives.Pow(bases, exponents, dst)` |

### Two-span operations (a, b → dst)
| Operation | API |
|-----------|-----|
| Add | `TensorPrimitives.Add(a, b, dst)` |
| Subtract | `TensorPrimitives.Subtract(a, b, dst)` |
| Multiply | `TensorPrimitives.Multiply(a, b, dst)` |
| Divide | `TensorPrimitives.Divide(a, b, dst)` |
| Element-wise Min | `TensorPrimitives.Min(a, b, dst)` |
| Element-wise Max | `TensorPrimitives.Max(a, b, dst)` |

### Three-span fused operations
| Operation | API |
|-----------|-----|
| (x+y)*z | `TensorPrimitives.AddMultiply(x, y, z, dst)` |
| x*y+z | `TensorPrimitives.MultiplyAdd(x, y, z, dst)` |
| fma(x,y,z) | `TensorPrimitives.FusedMultiplyAdd(x, y, z, dst)` |

> `AddMultiply` and `MultiplyAdd` are distinct — they optimize differently depending on whether the dependency chain flows from the addend or the multiplier. `FusedMultiplyAdd` is the IEEE 754 fused form of (x*y)+z with a single rounding step.

## Manual SIMD with Vector128/Vector256/Vector512

Use this when TensorPrimitives doesn't have a single API for the operation. This is required for byte-level operations, character class counting, range validation, bitwise bulk ops, cross-type conversions, and custom patterns.

### Required imports
```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
```
Prefer cross-platform APIs (`System.Runtime.Intrinsics`). Only use platform-specific intrinsics (`System.Runtime.Intrinsics.X86`, `.Arm`) when there is a significant performance advantage that justifies the increased code complexity of maintaining separate code paths.

### Three-tier dispatch pattern
Always include all three tiers. Use `if`/`else if` so that small inputs hit only one branch before reaching the scalar fallback — a fallthrough pattern (sequential `if`s) pessimizes the scalar case by requiring up to three not-taken branches that may mispredict. The `IsHardwareAccelerated` checks are JIT-time constants, so dead paths are eliminated at compile time:
```csharp
ref var src = ref MemoryMarshal.GetReference(span);
uint i = 0;
uint length = (uint)span.Length;

if (Vector512.IsHardwareAccelerated && Vector512<T>.IsSupported)
{
    uint vec512Count = (uint)Vector512<T>.Count;
    while (i + vec512Count <= length)
    {
        var vec = Vector512.LoadUnsafe(ref src, i);
        // ... process vec ...
        i += vec512Count;
    }
}
else if (Vector256.IsHardwareAccelerated && Vector256<T>.IsSupported)
{
    uint vec256Count = (uint)Vector256<T>.Count;
    while (i + vec256Count <= length)
    {
        var vec = Vector256.LoadUnsafe(ref src, i);
        // ... process vec ...
        i += vec256Count;
    }
}
else if (Vector128.IsHardwareAccelerated && Vector128<T>.IsSupported)
{
    uint vec128Count = (uint)Vector128<T>.Count;
    while (i + vec128Count <= length)
    {
        var vec = Vector128.LoadUnsafe(ref src, i);
        // ... process vec ...
        i += vec128Count;
    }
}
// Scalar fallback for remaining elements (and the only loop hit for small inputs)
for (; i < length; i++)
{
    // ... scalar processing ...
}
```

### Core SIMD operations
- **Load/Store:** `Vector128.LoadUnsafe(ref src, offset)` / `.StoreUnsafe(ref dst, offset)`
- **Arithmetic:** `+`, `-`, `*`, `/` operators on vector types
- **Multiply-add (approximate):** `Vector128.MultiplyAddEstimate(a, b, c)` — performs a multiply-add with implementation-defined approximation; not guaranteed to be a strict IEEE fused multiply-add. For precise fused semantics, use `Vector128.FusedMultiplyAdd(a, b, c)`.
- **Comparison:** `Vector128.Equals`, `.LessThan`, `.GreaterThan` — returns mask vector
- **Mask ops:** `Vector128.All(mask)`, `.Any(mask)`, `.None(mask)`, `.Count(mask)`, `.CountWhereAllBitsSet(mask)`
- **Horizontal:** `Vector128.Sum(vec)` for reduction; `.Min(a,b)`, `.Max(a,b)` element-wise
- **Broadcast:** `Vector128.Create(scalarValue)` — fill all lanes with one value
- **Bitwise:** `&`, `|`, `^`, `~` operators; `Vector128.ShiftLeft`, `.ShiftRightLogical`
- **Widening:** `Vector128.WidenLower(v)` / `.WidenUpper(v)` for byte→short, short→int
- **Narrowing:** `Vector128.Narrow(lower, upper)` for int→short, short→byte
- **Type convert:** `Vector128.ConvertToSingle(intVec)`, `.ConvertToInt32(floatVec)`
- **Shuffle:** `Vector128.Shuffle(vec, indices)` — lookup table / permutation
- **Conditional:** `Vector128.ConditionalSelect(mask, trueVec, falseVec)`

### Pattern: Unsigned range check (byte-range validation)
For checking if all bytes are in range [lo, hi]:
```csharp
var vLo = Vector128.Create((byte)lo);
var vRange = Vector128.Create((byte)(hi - lo));
// (b - lo) > range means out-of-range (unsigned wraparound catches b < lo)
var shifted = Vector128.Subtract(vec, vLo);
var inRange = Vector128.LessThanOrEqual(shifted, vRange);
if (!Vector128.All(inRange.AsByte())) return false; // for validation
// or: count += Vector128.CountWhereAllBitsSet(inRange); // for counting
```

### Pattern: Nibble-lookup counting (character classes, popcount, etc.)
For counting bytes matching a sparse set of values (vowels, digits, punctuation, bit counts) — build two 16-byte lookup tables indexed by low/high nibble:
```csharp
var lo_lut = Vector128.Create(/* 16 bytes: bit pattern for low nibble match */);
var hi_lut = Vector128.Create(/* 16 bytes: bit pattern for high nibble match */);
var nibbleMask = Vector128.Create((byte)0x0F);

var lo_nibble = vec & nibbleMask;
var hi_nibble = Vector128.ShiftRightLogical(vec.AsUInt16(), 4).AsByte() & nibbleMask;
var lo_match = Vector128.Shuffle(lo_lut, lo_nibble);
var hi_match = Vector128.Shuffle(hi_lut, hi_nibble);
var match = lo_match & hi_match;
count += Vector128.CountWhereAllBitsSet(~Vector128.Equals(match, Vector128<byte>.Zero));
```
This same technique works for popcount (LUT = {0,1,1,2,1,2,2,3,1,2,2,3,2,3,3,4}).
For simpler cases (single byte value, adjacent range), use `Equals` + `Count` or range check instead.

### Pattern: Cross-type conversion (widening chains)
When the source and destination types differ (e.g., byte→float for dequantization, short→byte for narrowing):
```csharp
// Widen: byte → short → int → float
var bytes = Vector128.LoadUnsafe(ref src, offset);
var (lo16, hi16) = Vector128.Widen(bytes);
var (lo32a, lo32b) = Vector128.Widen(lo16);
var f0 = Vector128.ConvertToSingle(lo32a.AsInt32());

// Narrow: int → short → byte (with saturation via Min/Max clamping)
var clamped = Vector128.Min(Vector128.Max(vec, Vector128<short>.Zero), Vector128.Create((short)255));
var narrowed = Vector128.Narrow(clamped.AsUInt16(), nextVec.AsUInt16());
```

### Trailing elements
- **Idempotent ops** (validation, search): overlap last vector — re-processing is safe
- **Aggregations** (sum, count, min/max): scalar loop for remainder to avoid double-counting
- **Store ops** (transform in-place): use `ConditionalSelect` to merge with last stored vector

## Key Rules
- Preserve original method signature — drop-in replacement
- Keep scalar code as fallback — never delete it
- Use `Vector128<T>` / `Vector256<T>` / `Vector512<T>` explicitly — never `Vector<T>`
- Prefer portable `Vector128<T>`/`Vector256<T>`/`Vector512<T>` APIs over platform-specific intrinsics (`Avx2`, `Sse42`, `AdvSimd`, `Fma`) unless there is a significant performance advantage
- Testing: use `dotnet run` (NOT `dotnet test`) — xunit.v3 is an in-process runner
