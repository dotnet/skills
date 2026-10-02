# AOT and Trimming Playbook Reference

Patterns beyond the core workflow in `SKILL.md`, for reflection-heavy .NET code that has to become trimming-safe and AOT-compatible. Each technique builds on the attribute model in [trimming-attributes.md](trimming-attributes.md).

## Contents

1. [Intentional runtime scanning](#intentional-runtime-scanning)
2. [DAM flow through extension methods and containers](#dam-flow-through-extension-methods-and-containers)
3. [Trimming-safe islands](#trimming-safe-islands)
4. [Separating trimming-unsafe from trimming-safe code](#separating-trimming-unsafe-from-trimming-safe-code)
5. [Object overloads to generic overloads](#object-overloads-to-generic-overloads)
6. [Strict-mode feature flag (AppContext switch)](#strict-mode-feature-flag-appcontext-switch)
7. [Warning-approval baseline](#warning-approval-baseline)
8. [MSBuild property reference](#msbuild-property-reference)
9. [Known gotchas](#known-gotchas)
10. [Full generation checklist](#full-generation-checklist)

---

## Intentional runtime scanning

Some APIs intentionally scan a caller-provided assembly for conventions. Do not pretend that this is trimming-safe: keep `[RequiresUnreferencedCode]` on the public constructor or registration API so callers make the trade-off explicitly. If scanning also constructs runtime-created components or otherwise needs runtime code generation, add `[RequiresDynamicCode]` as well.

Centralize the operation in a small private helper and put the narrowly scoped `[UnconditionalSuppressMessage]` there when the only reachable path is already behind the annotated public API:

```csharp
[RequiresUnreferencedCode("Scanning the configured assembly is not supported in trimming scenarios.")]
public ConventionSource(Assembly assembly) => this.assembly = assembly;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "This helper is reachable only through the annotated assembly-scanning API.")]
static Type[] ScanAssemblyTypes(Assembly assembly) => assembly.GetTypes();
```

The suppression documents the intentional boundary; it does not make `Assembly.GetTypes()` safe under trimming. Prefer an explicit generic `Register<T>()` overload when the API can offer it.

The same boundary applies to a dispatcher with both typed and runtime-type paths. Keep the typed path clean; isolate the fallback behind a helper and suppress it only when the branch invariant is that it can be reached through an API already marked `[RequiresUnreferencedCode]`. Do not suppress the whole dispatcher, because that would hide warnings from the statically knowable path.

---

## DAM flow through extension methods and containers

The annotation cascade in `SKILL.md` Strategy A covers parameters, fields, and returns. Two places where the flow is easy to lose:

**Extension methods.** `type.GetInterfaces()`, `type.GetMethods()`, and similar are instance methods on `Type` that already demand DAM on their `this`. Your own extension methods forwarding to them must propagate that requirement onto *their* `this` parameter:

```csharp
public static class TypeExtensions
{
    public static IEnumerable<Type> GetInterfacesIncludingInherited(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] this Type type)
        => type.GetInterfaces(); // satisfied: the this annotation flows to the built-in call.

    // No annotation needed: IsClass/IsAbstract/IsInterface do not reflect over members.
    public static bool IsConcrete(this Type type)
        => type.IsClass && !type.IsAbstract && !type.IsInterface;
}
```

**Carry DAM through a container.** A dictionary, cache, or tuple strips the annotation from a stored `Type`. Wrap it in a record whose **explicit property** carries `[DynamicallyAccessedMembers]`, and the flow survives the container:

```csharp
public readonly record struct TypeKey
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public required Type Type { get; init; }
}

// The wrapped Type keeps its DAM annotation through the dictionary key.
private static readonly Dictionary<TypeKey, int> _methodCounts = new();

public static int GetMethodCount(TypeKey key)
{
    if (!_methodCounts.TryGetValue(key, out var count))
    {
        count = key.Type.GetMethods().Length; // no warning: key.Type carries PublicMethods
        _methodCounts[key] = count;
    }
    return count;
}
```

The annotation on the property flows to both the backing field and the getter's return value, so reading `key.Type` yields an annotated `Type`. Use a `record` (struct or class) for value/structural equality so it works as a dictionary key, and put the annotation on an **explicit property** — a positional record parameter's attribute does not flow to the getter return.

**Collections and enumerables drop the flow.** `[DynamicallyAccessedMembers]` is only valid on a bare `Type` or `string` — annotating a `Type[]`, `List<Type>`, `IEnumerable<Type>`, or any collection-typed member is invalid (`IL2097`/`IL2098`). Arrays are the one narrow exception: when an array is created in the same method and read back at a constant index, the analyzer tracks the stored annotated `Type`. A `Type[]` parameter, an array returned from another method, or a variable index loses the flow (`IL2065`). Generic collections and LINQ never preserve it — `list[0]`, a dictionary key, and `enumerable.First()` all return an unannotated `Type` (`IL2075`), and a LINQ lambda's element parameter is unannotated too (`IL2070`):

```csharp
public static int ViaList([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type t)
{
    var list = new List<Type> { t };
    return list[0].GetMethods().Length; // IL2075: List<Type> drops the annotation
}

public static int ViaArray([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type t)
{
    var arr = new[] { t };
    return arr[0].GetMethods().Length; // OK: local array, constant index
}
```

When a `Type` must round-trip through a generic collection, wrap it in a `TypeKey`-style record (above), or keep it in an annotated field/parameter and avoid the collection round-trip.

---

## Trimming-safe islands

**General rule:** isolate code that cannot be made trimming- or AOT-safe behind an interface, and provide a separate safe implementation selected at construction time. Note the guard's scope: `RuntimeFeature.IsDynamicCodeSupported` gates *dynamic code* (AOT) only — a trimmed JIT app still returns `true`. Trimming (reachability) has no equivalent runtime flag; handle it with annotations or a feature switch.

**Shape:**

```csharp
public interface ITypeMapper
{
    Type Map(Type source);
}

// Dynamic-code island: emits a proxy. Unsafe only for AOT (needs the JIT).
[RequiresDynamicCode("Generates a dynamic proxy type at runtime.")]
public sealed class EmitTypeMapper : ITypeMapper
{
    public Type Map(Type source) => /* Reflection.Emit ... */;
}

// Safe island: identity mapping, no reflection, no emit — safe under both trimming and Native AOT.
public sealed class IdentityTypeMapper : ITypeMapper
{
    public Type Map(Type source) => source;
}

public static class TypeMapperFactory
{
    public static ITypeMapper Create()
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            return new EmitTypeMapper();
        }

        return new IdentityTypeMapper();
    }
}
```

**Rules:**
- The safe island must be genuinely reflection-free. If the safe path still calls `Activator.CreateInstance(Type)` or enumerates members, it is not an island — fix it or annotate it.
- Put the right attribute on the unsafe island's public surface — `[RequiresDynamicCode]` for emit, `[RequiresUnreferencedCode]` for reflection-by-name — so any direct use is warned.
- From `net9.0`, the analyzer treats `if (RuntimeFeature.IsDynamicCodeSupported)` as a guard, so the factory builds without `IL3050`. On `net8.0` it still warns; there, `[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only constructed when dynamic code is supported.")]` on `Create` is the one justified suppression — keep it at the factory.
- The guard does **not** cover trimming. If the unsafe island also reflects by name/type, annotate that path (`[RequiresUnreferencedCode]`/DAM) separately.
- The safe island can also *throw* for unsupported shapes (for example, mapping an interface/abstract type) rather than silently doing the wrong thing — fail fast with actionable guidance.

---

## Separating trimming-unsafe from trimming-safe code

**General rule:** do not sprinkle suppressions across a class. Pull the unsafe reflection into its own type or its own guarded region, so the rest of the code stays clean and the boundary is obvious.

- **Type-level separation** (the island above): unsafe type + safe type + factory.
- **Method-level separation**: one annotated method, one clean method, a dispatcher. The mechanism depends on *which* unsafety you are separating: `RuntimeFeature.IsDynamicCodeSupported` for dynamic code (AOT), `#if` or annotations for trimming — there is no runtime "is trimming active" flag.

```csharp
private static Plugin[] LoadViaManifest(string path) { /* no reflection */ }

[RequiresUnreferencedCode("Loads plugins by assembly name.")]
private static Plugin[] LoadViaReflection(string path) { /* ... */ }

public static Plugin[] Load(string path)
{
#if ENABLE_REFLECTION_LOADING
    return LoadViaReflection(path); // compiled in only when the feature is enabled
#else
    return LoadViaManifest(path);
#endif
}
```

- Move the annotation **up to the public entry point** rather than hiding it. A caller of a public API that ultimately reflects should get `IL2026`/`IL3050` so the decision (trim, or migrate) is theirs — not a blanket `IL2104`/`IL3053` assembly-level collapse.

---

## Object overloads to generic overloads

**General rule:** frameworks and libraries often expose an `object`-based overload — `Process(object value)`, `Register(object handler)` — that discovers the real type through `value.GetType()`. The trimmer cannot see through that. Add a **generic overload** that carries `typeof(T)` statically, redirect callers to it, and keep the object overload only for type-erased scenarios.

1. **Add the generic overload and annotate the object one.** The object overload's `GetType()` discovery is exactly the reflection the linker cannot see, so mark it `[RequiresUnreferencedCode]`. Put `[DynamicallyAccessedMembers]` on `T` when the library later reflects over its members (the member set depends on what is accessed):

```csharp
public void Process<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(T value)
{
    // typeof(T) is statically visible to the linker.
}

[RequiresUnreferencedCode("Routes by the runtime type of the value, which trimming cannot analyze.")]
public void Process(object value) { /* uses value.GetType() */ }
```

2. **Choose overload resolution deliberately** with `[OverloadResolutionPriority]` (.NET 9+, `System.Runtime.CompilerServices`). Higher priority wins; the default is 0.
   - **Prefer the generic overload** (`+1`) when the two are behaviorally identical and silent redirection is safe.
   - **Deprioritize the generic overload** (`-1`) when overload selection is *behavioral* — for example, the object overload routes by runtime type and the generic by static type, so silently switching would change runtime behavior. Keep the object overload the default for recompiled callers and drive migration explicitly instead.

```csharp
[OverloadResolutionPriority(-1)]
public void Process<T>(T value) { /* ... */ }

public void Process(object value) { /* existing runtime-type routing */ }
```

3. **Ship an opt-in analyzer** that flags the object-overload calls, but only activates when the *consuming* project opts into trimming. Read the MSBuild properties through `build_property.*` in `AnalyzerConfigOptionsProvider.GlobalOptions` and gate the diagnostic on `build_property.IsTrimmable`, `build_property.IsAotCompatible`, `build_property.PublishTrimmed`, `build_property.PublishAot`, or `build_property.EnableTrimAnalyzer`. Non-trimming consumers stay quiet; trimming adopters get the diagnostic and a code fix to the generic overload. A per-rule severity in `.editorconfig` still overrides the automatic activation.

4. **Add an explicit-`Type` overload** for middleware or platform code that lost the generic type but still knows the logical type, instead of forcing `MethodInfo.MakeGenericMethod`:

```csharp
public void Process(object value, Type valueType) { /* known logical type, no emit */ }
```

5. **Plan the removal.** Keep the object overload in the current major with the diagnostics quiet by default; remove it in the next major, with an error-by-default diagnostic for the cases where generic inference could change behavior.

---

## Strict-mode feature flag (AppContext switch)

**General rule:** when a library can *either* fall back to reflection *or* require pre-registration, expose a runtime **strict mode** so trimming adopters can prove their setup is complete. The compiler and analyzer see only what is statically reachable; types registered at runtime, or loaded by name, are invisible to them. Strict mode turns "silently accept and fall back" into "fail fast", giving a runtime backstop to verify a trimmed/AOT build against.

**Shape:**

1. Declare the switch as a static `bool` getter-only property and mark it with `[FeatureSwitchDefinition]` (.NET 9+, `System.Diagnostics.CodeAnalysis`). The attribute lets the linker substitute the property's value with the feature-switch setting at trim time:

```csharp
internal static class FeatureSwitches
{
    [FeatureSwitchDefinition("MyLibrary.StrictMode")]
    internal static bool IsStrictModeEnabled =>
        AppContext.TryGetSwitch("MyLibrary.StrictMode", out var enabled) && enabled;
}
```

2. Gate the strict behavior on it — when on, throw with actionable guidance instead of falling back to reflection:

```csharp
public void Register(object component)
{
    if (FeatureSwitches.IsStrictModeEnabled)
    {
        throw new InvalidOperationException(
            "MyLibrary.StrictMode is enabled, but the component was not registered up front. " +
            "Call Register<TComponent>() during startup.");
    }

    // Lenient path: reflection-based fallback for non-trimming scenarios.
    RegisterViaReflection(component);
}
```

3. Users opt in at runtime, or at trim time:

```csharp
// Tests: opt in before the switch is first read.
AppContext.SetSwitch("MyLibrary.StrictMode", true);
```

```xml
<!-- App/trim time: Trim="true" lets the linker substitute the value and remove the
     lenient reflection path when the switch is on (strict mode). -->
<ItemGroup>
  <RuntimeHostConfigurationOption Include="MyLibrary.StrictMode" Value="true" Trim="true" />
</ItemGroup>
```

**Guidance:**
- Strict mode must be the stronger policy: derive effective dynamic loading as `configuredDynamicLoading && !strictMode`, and apply it before registration state or other startup components initialize. Otherwise a permissive setting can re-enable dynamic type loading after strict mode has been requested. Test the precedence explicitly, including registrations made before initialization.
- Keep it **opt-in** — existing JIT users must see no new failures.
- Treat it as a **detector, not the safety mechanism**: the actual trimming-safety still comes from generic registration and annotations; strict mode only surfaces the gaps.
- Make failures **actionable** — point at the generic overload or the explicit `Register<T>()` API that closes the gap.
- It complements the compile-time analyzer: the analyzer catches what it can see, strict mode catches what it cannot (runtime-discovered types).
- It only fits when the library already has an explicit registration concept. With nothing to be "strict" about, the pattern does not apply.
- `[FeatureSwitchDefinition]` is available from .NET 9. On older TFMs, declare the same static `bool` property and read `AppContext.TryGetSwitch` directly — you lose only the trim-time substitution.
- Feature switches complicate unit testing and code sharing (different app configurations can disagree on the value). Prefer structuring APIs so trimming happens naturally; reserve switches for when API changes are not feasible.

---

## Warning-approval baseline

**General rule:** use an approval baseline only as a migration aid when a large codebase cannot reach zero warnings overnight. Once the warning set is empty, delete the approval test and checked-in warning file; enforce trimming directly through the project analyzers and publish validation. A permanent baseline can hide regressions behind an ever-growing list.

**Shape:**

1. Enable the analyzers in the library build:

```xml
<PropertyGroup>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
</PropertyGroup>
```

2. Build, capture the `IL2xxx`/`IL3xxx` diagnostics, scrub volatile parts (absolute paths, line numbers), normalize separators, and group by file.
3. Store the result as an approved baseline (a checked-in text file).
4. On every build, compare current warnings against the baseline; any new warning fails the build and must either be fixed or explicitly approved in the same PR.

This is the same idea as snapshot testing: the baseline documents known debt and makes every change to the warning surface a reviewed decision.

---

## MSBuild property reference

### Libraries (declare compatibility)

| Property | Effect |
| --- | --- |
| `IsTrimmable` | Declares the assembly trim-compatible; enables trim warnings during its build. |
| `IsAotCompatible` | Declares AOT compatibility; implies `IsTrimmable`, `EnableTrimAnalyzer`, `EnableSingleFileAnalyzer`, `EnableAotAnalyzer`. |
| `EnableTrimAnalyzer` | Roslyn analyzer for a subset of trim warnings, independent of publish. |
| `EnableAotAnalyzer` | AOT analyzer (`IL305x` dynamic-code warnings), implied by `IsAotCompatible`. |
| `ILLinkTreatWarningsAsErrors` | Set `false` to keep ILLink warnings from becoming errors under a global `TreatWarningsAsErrors`. |
| `VerifyReferenceTrimCompatibility` | `true` → `IL2125` for referenced assemblies lacking `IsTrimmable` metadata. |

### Applications (publish)

| Property | Effect |
| --- | --- |
| `PublishTrimmed` | Trim on publish; disables trim-incompatible framework features; enables trim analysis. |
| `PublishAot` | Native AOT publish; implies trimming. |
| `TrimMode` | `full` (trim all assemblies; default for console apps on .NET 7+) vs `partial` (only `IsTrimmable` assemblies). |
| `TrimmerSingleWarn` | `false` shows every warning; default collapses to one warning per dependency assembly. |
| `TrimmerRemoveSymbols` | `true` removes PDBs from the output. |
| `TrimmerRootAssembly` (item) | Roots an assembly so all its paths are analyzed (use in the library test app). |

### Feature switches (size + trim framework code)

`InvariantGlobalization`, `EventSourceSupport`, `UseSystemResourceKeys`, `DebuggerSupport`, `StackTraceSupport` (.NET 8+), `UseNativeHttpHandler`, `MetadataUpdaterSupport`, `MetricsSupport`, `EnableUnsafeBinaryFormatterSerialization`, `UseSizeOptimizedLinq` (.NET 10+).

---

## Known gotchas

Attribute-level rules (`#pragma` vs `[UnconditionalSuppressMessage]`, DAM targets, `All`, invalid suppressions) are in [trimming-attributes.md](trimming-attributes.md).

- **Overrides/interface implementations must match.** `[RequiresUnreferencedCode]` (`IL2046`), `[RequiresDynamicCode]` (`IL3051`), and DAM annotations must be identical across base/override/interface — a primary reason to avoid annotating `virtual`/interface members.
- **Reflection in a static constructor** propagates warnings to every member of the class; annotate the type instead.

---

## Full generation checklist

When introducing trimming/AOT support or resolving warnings:

1. **Project configuration**
   - Libraries: set `IsAotCompatible` (TFM-conditioned to `net8.0+`) by default; set `IsTrimmable` alone only when the library has a permanent JIT-only path.
   - Apps: set `PublishTrimmed`/`PublishAot`; publish and confirm **zero** warnings.
   - Consider `TrimmerSingleWarn=false` while fixing, then restore per-assembly collapse.

2. **Make reflection visible (or selectively replace it)**
   - Leave reflection the analyzer already sees (`typeof(Known)` with constant member names) alone.
   - Add generic `Register<T>()`/`Process<T>()` overloads next to `Type`/`object` ones so `typeof(T)` flows statically.
   - Keep assembly scanning behind `[RequiresUnreferencedCode]` on the public API.

3. **Annotate what remains**
   - Narrowest `DynamicallyAccessedMembers` on every `Type`/`string` source; re-declare through the chain.
   - `[RequiresUnreferencedCode]` on reflection-by-name paths; `[RequiresDynamicCode]` on emit/dynamic paths; propagate to public entry points.

4. **Isolate the unsafe**
   - Move reflection/emit behind an interface with a safe alternative.
   - Gate *dynamic-code* paths with `RuntimeFeature.IsDynamicCodeSupported`; separate *trimming* paths with `#if` or annotations (there is no runtime trimming flag).
   - Keep the safe path genuinely reflection-free; fail fast on unsupported shapes.

5. **Suppress minimally**
   - Only `[UnconditionalSuppressMessage]` with a mandatory, invariant-stating justification, at a leaf.
   - Never `#pragma`/`[SuppressMessage]`.

6. **Verify and lock**
   - Publish trimmed and AOT test apps that root the library (`TrimmerRootAssembly`).
   - Use an approval baseline only while migrating; once warnings are resolved, enforce a zero-warning project build and remove the baseline.
   - Add an opt-in analyzer + code fix when retiring reflective public APIs (see [Object overloads to generic overloads](#object-overloads-to-generic-overloads)).

By following these rules, refactored .NET code cooperates with the linker's reachability analysis and runs correctly under Native AOT.
