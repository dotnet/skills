# Runtime and JIT Compiler Breaking Changes (.NET 11)

These breaking changes affect the .NET runtime and JIT compiler. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

## Behavioral Changes

### Minimum hardware requirements updated

**Impact: High for deployments on older hardware.** .NET 11 updates the minimum hardware requirements for both x86/x64 and Arm64 architectures.

#### x86/x64 changes

The baseline is updated from `x86-64-v1` to `x86-64-v2` on all operating systems. This means the minimum CPU must support:
- `CMOV`, `CX8`, `SSE`, `SSE2` (previously required)
- `CX16`, `POPCNT`, `SSE3`, `SSSE3`, `SSE4.1`, `SSE4.2` (newly required)

This aligns with Windows 11 requirements and covers all Intel/AMD CPUs still in official support (older chips went out of support around 2013).

The ReadyToRun (R2R) target is updated to `x86-64-v3` for Windows and Linux, adding `AVX`, `AVX2`, `BMI1`, `BMI2`, `F16C`, `FMA`, `LZCNT`, and `MOVBE`. Hardware that meets `x86-64-v2` but not `x86-64-v3` will experience additional JIT overhead at startup.

| OS | Previous JIT/AOT min | New JIT/AOT min | Previous R2R target | New R2R target |
|----|---------------------|-----------------|--------------------|--------------------|
| Apple | x86-64-v1 | x86-64-v2 | x86-64-v2 | (No change) |
| Linux | x86-64-v1 | x86-64-v2 | x86-64-v2 | x86-64-v3 |
| Windows | x86-64-v1 | x86-64-v2 | x86-64-v2 | x86-64-v3 |

#### Arm64 changes

- **Apple**: No change to minimum hardware or R2R target.
- **Linux**: No change to minimum hardware (still supports Raspberry Pi). R2R target updated to include `LSE`.
- **Windows**: Baseline updated to require `LSE` (Load-Store Exclusive), required by Windows 11 and all Arm64 CPUs officially supported by Windows 10. R2R target updated to `armv8.2-a + RCPC`.

| OS | Previous JIT/AOT min | New JIT/AOT min | Previous R2R target | New R2R target |
|----|---------------------|-----------------|--------------------|--------------------|
| Apple | Apple M1 | (No change) | Apple M1 | (No change) |
| Linux | armv8.0-a | (No change) | armv8.0-a | armv8.0-a + LSE |
| Windows | armv8.0-a | armv8.0-a + LSE | armv8.0-a | armv8.2-a + RCPC |

#### Impact

Starting with .NET 11, .NET fails to run on older hardware and prints:

> The current CPU is missing one or more of the baseline instruction sets.

For ReadyToRun-capable assemblies, there may be additional startup overhead on supported hardware that doesn't meet the R2R target.

**Fix:** Verify all deployment targets meet the new minimum requirements. For x86/x64, any CPU from ~2013 or later should be fine. For Windows Arm64, ensure `LSE` support (all Windows 11 compatible Arm64 devices).

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/11/minimum-hardware-requirements

### NativeAOT native-library outputs use `lib` prefix on Unix (Preview 3)

**Impact: Low.** NativeAOT shared/native library outputs on Linux and macOS now follow Unix conventions and include the `lib` prefix (e.g., `libMyLib.so` instead of `MyLib.so`).

**Fix:** Update build scripts, deployment pipelines, or P/Invoke declarations that reference output filenames by the old name without the `lib` prefix.

Source: https://github.com/dotnet/runtime/pull/124611

### Floating-point conversions to small integral types are saturating (Preview 7)

**Impact: Low–Medium.** On CoreCLR and Native AOT, unchecked conversions from `float`, `double`, or `Half` to `sbyte`, `byte`, `short`, `ushort`, and `char` now saturate at the bounds of the target type, and `NaN` converts to `0`. Previously the value was typically converted to `int` first and the low bits kept. Checked conversions are unchanged.

```csharp
double big = 65578.0, negative = -42.0;
short s = (short)big;       // .NET 10: 42      .NET 11: 32767
ushort u = (ushort)negative; // .NET 10: 65494   .NET 11: 0
```

**Fix:** Clamp or validate values explicitly where out-of-range input is possible, such as indexes, offsets, and pixel or audio sample math. `unchecked((ushort)(int)x)` matches the previous two-step conversion, and `double.ConvertToIntegerNative<T>` gives platform-native behavior; neither is guaranteed to reproduce every old result. There is no compatibility switch.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/11/fp-to-small-integer
