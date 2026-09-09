---
name: vectorization
description: >
  Design, implement, optimize, and review SIMD code in .NET.
  USE FOR: vectorizing scalar loops with TensorPrimitives,
  Vector64/128/256/512, or platform hardware intrinsics; reviewing existing SIMD
  code, including Vector<T>, for contract equivalence, tail handling, memory
  safety, portability, fallbacks, and measured performance. DO NOT USE FOR:
  performance work unrelated to SIMD or vectorization.
license: MIT
---

# .NET SIMD vectorization

Produce a portable optimization that preserves the scalar contract, remains memory-safe at every
length, and earns its complexity with measured results. **Read the official
[SIMD and hardware-intrinsics guidance](https://learn.microsoft.com/dotnet/standard/simd) first**
and follow its comprehensive implementation templates. In particular, use its self-contained
per-width dispatch, dedicated small-input handling, loop, and remainder shapes rather than reducing
them to a chain of width checks. This skill supplies the decision rules and validation checks to
apply while changing real code.

## Inputs and prerequisites

Discover these from the repository before asking the user:

| Input | Required | What to establish |
| --- | --- | --- |
| Scalar implementation and tests | Yes | Existing contract, representative call sites, and supported overlap |
| Target frameworks and platforms | Yes | Available SIMD APIs and architectures that must behave consistently |
| Build and test workflow | Yes | The repository's normal commands and how to launch separate test processes |
| Representative workload or benchmark | For optimization | Typical input sizes and the baseline to beat |

Do not add a package merely because an API exists there. First check the target framework and the
project's existing dependency/versioning policy.

## Core rules

1. **Use the highest-level API that matches the contract, then stop.** `Span<T>` and `string`
   operations, `TensorPrimitives`, and tensor types already accelerate many operations. LINQ
   reductions such as `Sum`, `Min`, `Max`, and `Average` can also accelerate when the source exposes
   its underlying span. Verify empty-input and floating-point behavior rather than assuming similarly
   named operations are interchangeable. Once an existing API preserves the contract, use it instead
   of continuing into handwritten SIMD. Before writing an explicit loop, name the framework APIs
   considered and why none applies. Fixed-shape `System.Numerics` types remain appropriate for
   graphics and similar domains.
2. **Start new explicit SIMD loops with `Vector128<T>`.** It is accelerated across the broadest
   hardware set. Add wider fixed-width paths only when measurements justify them.
3. **Keep platforms consistent.** Prefer cross-platform operations on the fixed-width vector types;
   they lower to the appropriate target instructions. For example,
   `(vector & mask) == Vector128<byte>.Zero` becomes `ptest` on x86/x64. Use
   architecture-specific intrinsics only for a measured gap, guard them with `IsSupported`, and
   retain equivalent portable or scalar behavior.
4. **Read `IsHardwareAccelerated`, `IsSupported`, and `Count` directly.** The JIT treats them as
   constants, so caching them adds no value and obscures which branches disappear.
5. **Prefer operators where they are clear.** Parenthesize expressions that mix bitwise and
   comparison operators so precedence is explicit.

If the task is review-only, do not rewrite the code. Report correctness and memory-safety defects
before performance opportunities.

## Authoring checklist

- **Contract:** identify behavior for empty and short inputs, overlap, overflow, NaN, signed zero,
  ordering, and exceptions before changing the implementation.
- **Framework gate:** inspect the target framework and existing package references, then compile or
  probe the highest-level candidate API with the required edge cases. A small contract adapter, such
  as preserving special empty-input behavior, does not justify reimplementing the operation. If the
  API preserves the contract, use it and stop; do not claim it is unavailable without checking.
- **Structure:** for new explicit SIMD, implement `Vector128<T>` and scalar first. Only after
  measurements justify wider paths, check `Vector512<T>`, then `Vector256<T>`, optional `Vector<T>`,
  `Vector128<T>`, and finally scalar. Omit paths the implementation does not need. Each outer
  fixed-width guard checks only its `IsHardwareAccelerated` property and, for generic element types,
  `IsSupported`. Inside that block, run the width-specific helper when the input has at least
  `Count` elements; otherwise run a dedicated small-input helper, then return. Do not put the length
  check in the outer guard and fall through to repeat dispatch at narrower widths. Keeping each
  supported-width block self-contained lets the JIT remove unsupported blocks and avoids redundant
  work on common small inputs.
- **Loads and stores:** prefer span-based `Vector128.Create(span)` and `CopyTo`; the JIT keeps them
  efficient and they require no pinning or reference arithmetic. Unsafe loads and stores are largely
  unnecessary. When a path genuinely must walk a buffer by managed reference, use the element-offset
  `LoadUnsafe(ref T, nuint)` and `StoreUnsafe` overloads rather than pointers or manually advanced
  references.
- **Empty inputs:** in a reference-based path, obtain the starting reference with
  `MemoryMarshal.GetReference(span)` or `MemoryMarshal.GetArrayDataReference(array)`, not by indexing
  element `0`.
- **Unsupported element types:** the fixed-width vectors support primitive numeric element types,
  not `char` or `bool`. Reinterpret with `MemoryMarshal.Cast` or `As<TFrom, TTo>`; reinterpretation
  changes only the type, not the bits. Keep Boolean data as `0` or `1` and characters as valid
  UTF-16, normalizing results before storing when necessary.
- **Offsets:** prove the input contains a full vector before subtracting `Count` or converting an
  index to `nuint`; otherwise a negative value becomes a huge unsigned offset.
- **Managed references:** do not form references before the start or past the end of a span,
  including a one-past-end reference. The runtime permits a non-dereferenced managed pointer exactly
  one past an object or array, but this guidance intentionally prohibits the pattern because it is
  fragile and easy to misuse. Keep the base reference in range and express traversal with an element
  offset.
- **Remainders:** cover every length, including `0`, `Count - 1`, `Count`, `Count + 1`, and
  nonmultiples of each width. Once the input contains a full vector, keep the tail vectorized by
  reprocessing the last full vector. An idempotent operation can fold that overlap in directly. A
  non-idempotent operation must use `ConditionalSelect` to replace repeated lanes with the
  operation's identity before folding them in. This is the JIT-recognized general pattern; it can
  reduce a zero-identity selection to a bitwise mask while retaining broader optimization
  opportunities. For in-place transforms, preserve the original tail values before overlapping
  stores and write only valid results.
- **Buffer overlap:** choose a traversal direction or staging strategy that prevents stores from
  corrupting values not yet loaded.
- **Numeric behavior:** account for floating-point reassociation, NaN and signed-zero semantics,
  checked or unchecked integer overflow, and endianness where the algorithm depends on byte order.
  `Native` and `Estimate` operations can intentionally relax precision or IEEE edge-case behavior;
  use them only when the contract permits it and measurements justify them.

The official guidance contains the complete dispatch, small-input, unrolling, and remainder
templates; use those for the full implementation. The following excerpt illustrates only the inner
safe `Vector128<T>` loop for an in-place elementwise transform, after its self-contained dispatch
block has established at least one full vector. `Transform` represents the operation being
implemented:

```csharp
Span<int> tail = data.Slice(data.Length - Vector128<int>.Count);
Vector128<int> end = Vector128.Create<int>(tail);
Span<int> remaining = data;

while (remaining.Length >= Vector128<int>.Count)
{
    Vector128<int> values = Vector128.Create<int>(remaining);
    Transform(values).CopyTo(remaining);
    remaining = remaining.Slice(Vector128<int>.Count);
}

if (!remaining.IsEmpty)
{
    Transform(end).CopyTo(tail);
}
```

The early `end` load preserves original values before overlapping stores. For a read-only reduction,
load the same final span after the main loop and use `ConditionalSelect` to replace already-processed
lanes with the operation's identity. Do not substitute `LoadUnsafe`/`StoreUnsafe` or a scalar
epilogue merely to avoid span bounds checks.

## Testing checklist

- Compare the optimized implementation with the scalar contract across boundary lengths,
  randomized values, empty inputs, supported overlap, and numeric edge cases. Cover every
  implemented width and the scalar path with inputs both large enough and too small to benefit.
- Exercise every implemented width and the scalar fallback in separate processes. On x86/x64
  CoreCLR, `DOTNET_EnableAVX2=0` disables AVX2 and `DOTNET_EnableHWIntrinsic=0` disables hardware
  intrinsics. Use the repository's normal test command and do not change these process-wide
  settings inside a unit test. These settings do not change code already compiled as ReadyToRun or
  ahead of time, so confirm the target code is JIT-compiled when using them to force a path.
- For unsafe loads and stores, use guard-page or equivalent boundary tests when available. Put the
  inaccessible page after the buffer for forward iteration and before it for backwards iteration,
  and include nonmultiple lengths. An ordinary array allocation does not reliably expose an
  out-of-bounds read.

## Benchmarking

Use BenchmarkDotNet to measure representative small and large inputs before keeping the added
complexity. Compare scalar, `Vector128<T>`, and each wider implemented path in the same run. Small
inputs can be slower because setup dominates, and speedups are rarely the theoretical vector-width
multiple because memory throughput, alignment, and latency still apply. Report throughput or time
with noise context and, when relevant, generated code size or instruction counts. Control allocation
alignment for stable measurements or randomize it to observe the distribution. A wider vector is
not automatically faster.

If the project cannot target the required framework, run the relevant architecture, or execute the
fallback configuration, state exactly which path remains unverified. Do not claim success from a
default-hardware test alone.

## Completion contract

- **Authoring:** leave the scalar contract covered by tests; identify the framework or SIMD layer
  selected; report measurements for the representative workload; name any architecture or fallback
  path that could not be exercised.
- **Review:** report only concrete findings, ordered by correctness, memory safety, portability,
  tests, then performance evidence. If none remain, say so directly.
- Do not call an optimization complete when it only builds, only passes on the current machine, or
  has no comparison against the scalar baseline.

## Review checklist

Review in this order:

1. Scalar-contract equivalence, including signed zero, NaN, overflow, and relevant endianness
2. Reuse of an existing accelerated framework API
3. Tail correctness for idempotent versus non-idempotent work
4. Memory safety, unsigned offset arithmetic, empty inputs, and overlapping buffers
5. Portable dispatch and behaviorally equivalent fallbacks
6. Tests that force each width and the scalar path
7. Benchmarks that justify explicit SIMD and additional widths
