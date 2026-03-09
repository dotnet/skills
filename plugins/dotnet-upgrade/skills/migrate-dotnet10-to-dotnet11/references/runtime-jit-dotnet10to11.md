# Runtime and JIT Compiler Breaking Changes (.NET 11)

These breaking changes affect the .NET runtime and JIT compiler. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11

> **Note:** .NET 11 is in preview. Additional runtime breaking changes are expected in later previews.

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


