# Deployment and Runtime Breaking Changes (.NET 9)

These changes affect runtime configuration, deployment, and JIT compiler behavior.

## Deployment

### Environment variables take precedence in app runtime configuration settings

**Impact: Medium.** When both an environment variable and a corresponding `runtimeconfig.json` setting are provided, the environment variable now takes precedence.

Example `runtimeconfig.json`:
```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.Server": true
    }
  }
}
```

Previously, this `runtimeconfig.json` setting would override `DOTNET_gcServer=0`. Now `DOTNET_gcServer=0` overrides the config file, disabling server GC.

**Mitigation:** If your app runs in an environment with runtime configuration environment variables, either unset them or set them to the desired values. Ensure environment variables and config files are consistent.

### Deprecated desktop Windows/macOS/Linux MonoVM runtime packages

**Impact: Low.** The desktop MonoVM runtime packages (`Microsoft.NETCore.App.Runtime.Mono.*`) are deprecated. Apps that explicitly used Mono on desktop should migrate to CoreCLR or use other supported runtimes.

## JIT Compiler

### Floating point to integer conversions are saturating

**Impact: Medium.** On x86/x64, conversions from `float`/`double` to integer types now use **saturating** behavior instead of the previous platform-specific wrapping:

| Scenario | .NET 8 (x86/x64) | .NET 9 |
|----------|-------------------|--------|
| `(int)float.MaxValue` | `int.MinValue` | `int.MaxValue` |
| `(int)float.NaN` | `int.MinValue` | `0` |
| `(uint)(-1.0f)` | Wrapping result | `0` |
| `(ulong)(double.MaxValue)` | Wrapping result | `ulong.MaxValue` |

```csharp
// .NET 8: (int)float.PositiveInfinity == int.MinValue  (wrapping)
// .NET 9: (int)float.PositiveInfinity == int.MaxValue   (saturating)

// .NET 8: (int)float.NaN == int.MinValue
// .NET 9: (int)float.NaN == 0

// .NET 8: (uint)(-1.0f) == some wrapping result
// .NET 9: (uint)(-1.0f) == 0
```

**Mitigation:** If you relied on the previous wrapping behavior (which was already non-deterministic across platforms), use the new `ConvertToIntegerNative<TInteger>` methods on `Single`, `Double`, and `Half` for the fast, platform-native behavior. Or use platform-specific hardware intrinsics for exact control.

### Some SVE APIs removed

A small number of ARM SVE (Scalable Vector Extension) APIs were removed in RC2 that were previously available in previews.
