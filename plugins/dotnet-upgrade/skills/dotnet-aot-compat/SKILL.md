---
name: dotnet-aot-compat
description: >
  Make .NET projects compatible with Native AOT and trimming by systematically
  resolving IL trim/AOT analyzer warnings. USE FOR: making projects AOT-compatible,
  fixing trimming warnings, resolving IL warnings (IL2026, IL2070, IL2067, IL2072,
  IL3050), adding DynamicallyAccessedMembers annotations, enabling IsAotCompatible
  or IsTrimmable. DO NOT USE FOR: .NET Framework (net4x) projects, publishing native
  AOT binaries, optimizing binary size, replacing reflection-heavy libraries with
  alternatives.
  INVOKES: no tools — pure knowledge skill.
license: MIT
---

# dotnet-aot-compat

Make .NET projects compatible with Native AOT and trimming by systematically resolving all IL trim/AOT analyzer warnings.

## When to Use This Skill

- **"Make this project AOT-compatible"**
- **"Fix trimming warnings"** or **"fix IL warnings"**
- **"Resolve IL2070 / IL2067 / IL2072 / IL2026 / IL3050 warnings"**
- **"Add DynamicallyAccessedMembers annotations"**
- **"Enable IsAotCompatible in my .csproj"** or **"declare IsTrimmable for my library"**
- **"My project has trim analyzer warnings after upgrading to net8.0"**
- **"Annotate reflection code for the trimmer"**
- **"Isolate this dynamic/plugin-loading code path so the rest of the library stays trimmable"**

## When Not to Use This Skill

Do not use this skill when the project exclusively targets .NET Framework (net4x), which does not support the trim/AOT analyzers.

## Prerequisites

An existing .NET project targeting net8.0 or later (or multi-targeting with at least one net8.0+ TFM) and the corresponding .NET SDK installed.

## Background: What AOT Compatibility Means

Native AOT and the IL trimmer perform static analysis to determine what code is reachable. Reflection can break this analysis because the trimmer can't see what types/members are accessed at runtime. The `IsAotCompatible` (apps and libraries) and `IsTrimmable` (libraries) properties enable analyzers that flag these issues as build warnings (ILXXXX codes).

Trimming (`PublishTrimmed`) and Native AOT (`PublishAot`) run the same static analysis — `IL2xxx` for trim warnings, `IL3xxx` for AOT/single-file warnings — but AOT additionally removes the JIT: `Reflection.Emit` is unsupported and constructing unknown generic instantiations at runtime is not guaranteed. A library can be trimmable but not AOT-compatible.

Reflection is not inherently wrong — it is fine and unrestricted in ordinary JIT applications. The attributes in this skill exist to *allow* reflection to survive trimming/AOT, not to forbid it. Reflection the analyzer can see statically needs no change: `typeof(KnownType).GetField("_name", ...)` with a constant name, or a `typeof(Concrete)` passed to an annotated `Type` parameter, produces no warning and the members are preserved.

## Critical Rules

### ❌ Never suppress warnings incorrectly

- **NEVER** use `#pragma warning disable` for IL warnings. It hides the warning from the Roslyn analyzer at build time only — the IL linker and AOT compiler still see the issue, so the warning comes back at publish and the code can break at runtime.
- **NEVER** use `[UnconditionalSuppressMessage]` to make a warning go away. It tells both the analyzer AND the linker to ignore the warning, so the trimmer cannot verify safety. The only exception is a leaf where an annotated registration path, `DynamicDependency`, or another explicit reflection-preserving root already preserves the reflected members; the `Justification` must name that invariant and mechanism.

### 💡 Preferred approaches

- **Prefer** `[DynamicallyAccessedMembers]` annotations to flow type information through the call chain.
- **Prefer** refactoring to eliminate patterns that break annotation flow (e.g., boxing `Type` through `object[]`).
- **Prefer generic APIs** over `Type` parameters when the type is known at compile time — `typeof(T)` is statically visible, a runtime `Type` is not.
- **Use** `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` / `[RequiresAssemblyFiles]` to mark methods as fundamentally incompatible with trimming, propagating the requirement to callers. This surfaces the issue clearly rather than hiding it — callers must explicitly acknowledge the incompatibility.

### Annotation flow is key

The trimmer tracks `[DynamicallyAccessedMembers]` annotations through assignments, parameter passing, and return values. If this flow is broken (e.g., by boxing a `Type` into `object`, storing in an untyped collection, or casting through interfaces), the trimmer loses track and warns. The fix is to preserve the flow, not suppress the warning.

## Step-by-Step Procedure

> **Do not explore the codebase up-front.** The build warnings tell you exactly which files and lines need changes. Follow a tight loop: **build → pick a warning → open that file at that line → apply the fix recipe → rebuild**. Reading or analyzing source files beyond what a specific warning points you to is wasted effort and leads to timeouts. Let the compiler guide you.
>
> ❌ Do NOT run `find`, `ls`, or `grep` to understand the project structure before building. Do NOT read README, docs, or architecture files. Your first action should be Step 1 (enable AOT analysis), then build.

### Step 1: Enable trim/AOT analysis in the .csproj

For an **application**, add `IsAotCompatible`. If the project doesn't exclusively target net8.0+, add a TFM condition (AOT analysis requires net8.0+):

```xml
<PropertyGroup>
  <IsAotCompatible Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">true</IsAotCompatible>
</PropertyGroup>
```

**Default to `IsAotCompatible` for libraries too, not just apps.** It is a strict superset of `IsTrimmable` (it also enables the single-file and AOT analyzers), and most libraries that are trim-safe are also AOT-safe with no extra work — there is no benefit to the narrower property unless the library has a real, permanent reason it cannot be AOT-safe. Reach for `IsTrimmable` alone only when the library intentionally ships functionality that fundamentally requires a JIT (e.g. a genuinely optional `Reflection.Emit`-based fast path with no AOT-safe equivalent) but you still want the trim analyzer to catch everything else:

```xml
<PropertyGroup>
  <IsTrimmable Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">true</IsTrimmable>
</PropertyGroup>
```

Either property automatically enables the trim analyzer for compatible TFMs. For multi-targeting projects (e.g., `netstandard2.0;net8.0`), the condition ensures no `NETSDK1210` warnings on older TFMs.

### Step 2: Build and collect warnings

```bash
dotnet build <project.csproj> -f <net8.0-or-later-tfm> --no-incremental 2>&1 | grep 'IL[0-9]\{4\}'
```

Sort and deduplicate. Common warning codes:
- **IL2070**: Reflection call on a `Type` parameter missing `[DynamicallyAccessedMembers]`
- **IL2067**: Passing an unannotated `Type` to a method expecting `[DynamicallyAccessedMembers]`
- **IL2072**: Return value or extracted value missing annotation (often from unboxing)
- **IL2057**: `Type.GetType(string)` with a non-constant argument
- **IL2026**: Calling a method marked `[RequiresUnreferencedCode]`
- **IL2050**: P/invoke method with COM marshalling parameters
- **IL2075**: Return value flows into reflection without annotation
- **IL2091**: Generic argument missing `[DynamicallyAccessedMembers]` required by constraint
- **IL3000**: `Assembly.Location` returns empty string in single-file/AOT apps
- **IL3050**: Calling a method marked `[RequiresDynamicCode]`

See [references/trimming-attributes.md](references/trimming-attributes.md) for the full `IL2xxx`/`IL3xxx` warning-code table.

### Step 3: Triage warnings by code (do NOT read every file)

Group the warnings from Step 2 by warning code and count them. **Do not open individual files yet.** Identify the top 1-2 patterns by count — these drive your fix strategy:

| Pattern | Typical fix |
|---------|-------------|
| Many IL2026 + IL3050 from `JsonSerializer` | **Go to Strategy C immediately** — create a `JsonSerializerContext`, then batch-update all call sites |
| IL2070/IL2087 on `Type` parameters | Add `[DynamicallyAccessedMembers]` to the innermost method, then cascade outward |
| IL2067 passing unannotated `Type` | Annotate the parameter at the source |
| Reflection you want gone entirely (object-typed public API, plugin loader, emit path) | **Go to Strategy D** — add a generic overload or isolate a trimming-safe island |

**In most real projects, IL2026/IL3050 from JsonSerializer dominate.** Start with Strategy C unless the warning breakdown clearly shows otherwise. After the batch JSON fix, handle remaining warnings with Strategies A–B, reaching for D when you'd rather remove the reflection than annotate it. Only use Strategy E, and then F, as last resorts.

### Step 4: Fix warnings iteratively (innermost first)

Work from the **innermost** reflection call outward. Each fix may cascade new warnings to callers.

**Stay warning-driven.** For each warning, open only the file and line the compiler reported, identify the pattern, apply the matching fix recipe below, and move on. Do not scan the codebase for similar patterns or try to understand the full architecture — fix what the compiler tells you, rebuild, and let new warnings guide the next change. Fix a small batch of warnings (5-10), then rebuild immediately to check progress.

**Use sub-agents when available.** If you can launch sub-agents (e.g., via a `task` tool), dispatch **multiple sub-agents in parallel** to edit different files simultaneously. Keep the main loop focused on building, parsing warnings, and dispatching — delegate actual file edits to sub-agents. For batch JSON updates, give each sub-agent 5-10 files to update in one prompt. **After 2 build-fix cycles, dispatch all remaining file edits to sub-agents in parallel — do not continue fixing files sequentially.** Example:

> Update these files to use source-generated JSON: `src/Models/Resource.Serialization.cs`, `src/Models/Identity.Serialization.cs`, `src/Models/Plan.Serialization.cs`. In each file, replace `JsonSerializer.Serialize(writer, value)` with `JsonSerializer.Serialize(writer, value, MyProjectJsonContext.Default.TypeName)` and `JsonSerializer.Deserialize<T>(ref reader)` with `JsonSerializer.Deserialize(ref reader, MyProjectJsonContext.Default.TypeName)`. Only edit the JsonSerializer call sites.

#### Strategy A: Add `[DynamicallyAccessedMembers]` (preferred)

When a method uses reflection on a `Type` parameter, annotate the parameter to tell the trimmer what members are needed:

```csharp
using System.Diagnostics.CodeAnalysis;

// Before (warns IL2070):
void Process(Type t) {
    var method = t.GetMethod("Foo");  // trimmer can't verify
}

// After (clean):
void Process([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type t) {
    var method = t.GetMethod("Foo");  // trimmer preserves public methods
}
```

When you annotate a parameter, **all callers** must now pass properly annotated types. A caller passing `typeof(Concrete)` already satisfies the annotation; the cascade only continues through callers that forward a `Type` variable — follow each of those and annotate or refactor as needed. **The caller's annotation must include at least the same member types as the callee's.** If the callee requires `PublicConstructors | NonPublicConstructors`, the caller must specify the same or a superset — using only `NonPublicConstructors` will produce IL2091.

Choose the narrowest `DynamicallyAccessedMemberTypes` for the reflection actually performed:

| Reflection | Member type |
| --- | --- |
| `Activator.CreateInstance<T>()`, `new T()`, `Activator.CreateInstance(type)` | `PublicParameterlessConstructor` |
| DI activation (`ActivatorUtilities.CreateFactory<T>`) | `PublicConstructors` |
| Explicit non-public activation | `NonPublicConstructors` |
| `type.GetInterfaces()` | `Interfaces` |
| `type.GetMethods()` | `PublicMethods` (add `NonPublicMethods` when needed) |
| `type.GetProperties()` | `PublicProperties` |
| `type.GetFields()` | `PublicFields` |
| `type.GetNestedTypes()` | `PublicNestedTypes` |

See [references/trimming-attributes.md](references/trimming-attributes.md) for the full attribute catalog, exact signatures, and the `[method:]`/`[field:]`/`[return:]` targeting rules.

#### Strategy B: Refactor to preserve annotation flow

When annotation flow is broken by boxing (storing `Type` in `object`, `object[]`, or untyped collections), **refactor** to pass the `Type` directly:

```csharp
// BROKEN: Type boxed into object[], annotation lost
void Process(object[] args) {
    Type t = (Type)args[0];  // IL2072: annotation lost through boxing
    Evaluate(t, ...);
}

// FIXED: Pass Type as a separate, annotated parameter
void Process(
    object[] args,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type calleeType,
    ...) {
    Evaluate(calleeType, ...);  // annotation flows cleanly
}
```

Common patterns that break flow and how to fix them:
- **`object[]` parameter bags**: Extract the `Type` into a dedicated annotated parameter
- **Dictionary/List storage**: Use a typed field with annotation instead
- **Interface indirection**: Add annotation to the interface method's parameter
- **Property with boxing getter**: Annotate the property's return type

#### Strategy C: Source-generated JSON serialization (batch fix)

When most warnings are IL2026/IL3050 from `JsonSerializer.Serialize`/`Deserialize`, this is a single mechanical fix applied in bulk:

1. **Collect affected types** — grep for all `JsonSerializer.Serialize` and `JsonSerializer.Deserialize` call sites. Extract the type being serialized (the `<T>` in `Deserialize<T>`, or the runtime type of the object in `Serialize`).

2. **Create one `JsonSerializerContext`** with `[JsonSerializable]` for every type found. **Skip types from external packages** (e.g., `ResponseError` from `Azure.Core`) — they won't source-generate for types you don't own. Handle external types separately via Gotcha #1 below.

```csharp
[JsonSerializerContext]
[JsonSerializable(typeof(ManagedServiceIdentity))]
[JsonSerializable(typeof(SystemData))]
// ... one attribute per type YOU OWN
// Do NOT add types from external packages (e.g., ResponseError)
internal partial class MyProjectJsonContext : JsonSerializerContext { }
```

3. **Batch-update all call sites** — do not read each file individually. Apply the pattern mechanically:
   - `JsonSerializer.Serialize(obj)` → `JsonSerializer.Serialize(obj, MyProjectJsonContext.Default.TypeName)`
   - `JsonSerializer.Deserialize<T>(json)` → `JsonSerializer.Deserialize(json, MyProjectJsonContext.Default.TypeName)`

   Find and update all call sites in one pass:
   ```bash
   # Find all files with JsonSerializer calls
   grep -rl 'JsonSerializer\.\(Serialize\|Deserialize\)' src/ --include='*.cs'
   ```
   Then use sequential `edit` calls to apply the same transformation to every matching file. **Do not use `sed` for C# code** — generics like `Deserialize<T>()` have angle brackets and nested parentheses that sed will mangle.

4. **Build once** to verify. Remaining warnings will be non-serialization issues — handle those with Strategies A–B, D, or E.

#### Strategy D: Replace the reflection instead of annotating it

When the goal isn't just silencing the warning but removing the reflective surface — a public API you don't want to ship with `[RequiresUnreferencedCode]` — replace it instead of wrapping it:

- **Generic overload**: `Process<T>(T value)` next to `Process(object value)` so `typeof(T)` flows statically instead of `value.GetType()`.
- **Trimming-safe island**: when a path is inherently dynamic (emitting proxies, loading plugins by name), move it behind a boundary and provide a trimming-safe alternative selected by `RuntimeFeature.IsDynamicCodeSupported`.

See [references/pattern-playbook.md](references/pattern-playbook.md) for full examples, plus intentional runtime-scanning boundaries.

#### Strategy E: `[RequiresUnreferencedCode]` (last resort)

When a method fundamentally requires arbitrary reflection that cannot be statically described and replacing it (Strategy D) isn't feasible:

```csharp
[RequiresUnreferencedCode("Loads plugins by name using Assembly.Load")]
public void LoadPlugin(string assemblyName) {
    var asm = Assembly.Load(assemblyName);
    // ...
}
```

This propagates to callers — they must also be annotated with `[RequiresUnreferencedCode]`. Use sparingly; it marks the entire call chain as trim-incompatible.

#### Strategy F: `[UnconditionalSuppressMessage]` (true last resort)

Only after Strategies A–E don't apply — the reflection target is fundamentally invisible to static analysis, but you have manually proven it's safe (e.g., a generic constraint guarantees the concrete type, or the code path is provably unreachable under trimming):

```csharp
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "T is constrained to IJsonSerializable, whose Deserialize method is preserved via DynamicDependency on the base type.")]
static T Load<T>() where T : IJsonSerializable => (T)LoadCore(typeof(T));
```

The `Justification` must describe the actual invariant, not restate the warning. If you can't state a real invariant, the reflection isn't provably safe — go back to Strategy A–D, or propagate with Strategy E instead.

### Step 5: Rebuild and repeat

After each small batch of fixes (5-10 warnings), rebuild with `--no-incremental` and check for new warnings. **Do not attempt to fix all warnings before rebuilding** — frequent rebuilds catch mistakes early and reveal cascading warnings. Fixes cascade — annotating an inner method may surface warnings in its callers. Repeat until `0 Warning(s)`.

### Step 6: Validate all TFMs

Build all target frameworks to ensure:
- **0 IL warnings** on net8.0+ TFMs
- **No NETSDK1210 warnings** (the `IsAotCompatible`/`IsTrimmable` condition handles this)
- **Clean builds** on older TFMs (netstandard2.0, net472, etc.)

```bash
dotnet build <project.csproj>  # builds all TFMs
```

A warning-free **build** is necessary but not sufficient — the trim/AOT analyzer runs on best-effort static analysis and can miss what the linker actually does at publish time. For an application, finish by actually publishing trimmed or AOT, not just building:

```bash
dotnet publish <project.csproj> -c Release -r <rid> -p:PublishTrimmed=true
dotnet publish <project.csproj> -c Release -r <rid> -p:PublishAot=true
```

A library has no publish step of its own — validate it with a small test app that references it, sets `PublishAot=true` (or `PublishTrimmed=true`), and roots the library's public surface.

## Stop Signals

- **Do not analyze more than 2-3 representative files per warning pattern.** After identifying the fix for a pattern, apply it to all matching files without reading each one first.
- **Start fixing after one build.** Do not do a second analysis pass — begin implementing fixes for the most common warning pattern immediately after Step 3 triage.
- Stop after achieving **0 IL warnings** for net8.0+ TFMs. Don't optimize or refactor already-clean annotations.
- If a warning requires **architectural refactoring** beyond annotation flow fixes (e.g., replacing an entire serialization layer), document it and stop — don't rewrite large subsystems.
- Limit to **3 build-fix iterations** per warning. If annotation flow doesn't resolve it after 3 attempts, move to Strategy D (replace the reflection) or escalate to Strategy E (`[RequiresUnreferencedCode]`). Reach for Strategy F only when you can state the actual invariant that makes suppression safe.
- Don't chase warnings in **third-party dependencies** you can't modify. Note them and move on.
- If the user asked a scoped question (e.g., "fix warnings in this file"), don't expand to the entire project.

## Polyfills for Older TFMs

For multi-targeting projects that include netstandard2.0 or net472, you need polyfills for `DynamicallyAccessedMembersAttribute` and related types. See [references/polyfills.md](references/polyfills.md).

## Common Gotchas

1. **External types without AOT-safe serialization**: When a type comes from a dependency you can't modify (e.g., `ResponseError` from `Azure.Core`) and it lacks a source-generated serializer, `Options.GetConverter<T>()` is reflection-based and will produce IL warnings. First check if the type implements `IJsonModel<T>` (common in Azure SDK) — if so, bypass `JsonSerializer` entirely:

```csharp
// Before (IL2026 — JsonSerializer uses reflection):
JsonSerializer.Serialize(writer, errorValue);

// After (AOT-safe — uses IJsonModel directly):
((IJsonModel<ResponseError>)errorValue).Write(writer, ModelReaderWriterOptions.Json);

// For deserialization:
var error = ((IJsonModel<ResponseError>)new ResponseError()).Create(ref reader, ModelReaderWriterOptions.Json);
```

Do **not** add the external type to your `JsonSerializerContext` — it won't source-generate for types you don't own. If the type doesn't implement `IJsonModel<T>`, write a custom `JsonConverter<T>` with manual `Utf8JsonReader`/`Utf8JsonWriter` logic and register it via `[JsonSourceGenerationOptions]` on your context.

2. **Serialization libraries**: Most reflection-based serializers (e.g., `Newtonsoft.Json`, `XmlSerializer`) are not AOT-compatible. Migrate to a source-generation-based serializer such as `System.Text.Json` with a `JsonSerializerContext`. If migration is not feasible, mark the serialization call site with `[RequiresUnreferencedCode]`.

3. **Shared projects / projitems**: When source is shared between multiple projects via `<Import>`, annotations added to shared code affect ALL consuming projects. Verify that all consumers still build cleanly.

4. **More patterns**: DAM flow through extension methods and containers, strict-mode feature flags, and warning-approval baselines for locking down an existing warning surface are covered in [references/pattern-playbook.md](references/pattern-playbook.md).

## Reference Files

- [references/trimming-attributes.md](references/trimming-attributes.md): the complete attribute catalog — `RequiresUnreferencedCode`, `RequiresDynamicCode`, `DynamicallyAccessedMembers` (full `DynamicallyAccessedMemberTypes` list), `UnconditionalSuppressMessage`, `DynamicDependency` — each with intent, exact signature, and rules, plus the `IL2xxx`/`IL3xxx` warning-code table.
- [references/pattern-playbook.md](references/pattern-playbook.md): patterns beyond the core workflow — intentional runtime scanning, DAM flow through extension methods and containers, trimming-safe islands and capability guards, object-overload → generic-overload redirects with `[OverloadResolutionPriority]`, strict-mode feature flags, warning-approval baselines, and known gotchas.
- [references/polyfills.md](references/polyfills.md): polyfills for `DynamicallyAccessedMembersAttribute` and related types on netstandard2.0/net472 multi-targeted projects.

## References

[Limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=windows%2Cnet8#limitations-of-native-aot-deployment)
[Conceptual: Understanding trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-concepts)
[How-to: trim compat](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings)
[Trimming options](https://learn.microsoft.com/dotnet/core/deploying/trimming/trimming-options)
[Prepare .NET libraries for trimming](https://learn.microsoft.com/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
[Native AOT deployment](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[ILLink error codes](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/error-codes.md)

## Checklist

- [ ] Added `<IsAotCompatible>` (apps, and libraries by default) or, only when the library has a real permanent reason it can't be AOT-safe, `<IsTrimmable>`, with a TFM condition
- [ ] Built with trim/AOT analyzers enabled (net8.0+ TFM)
- [ ] Fixed all IL warnings via annotations, refactoring, or replacing the reflection (generic overload / trimming-safe island)
- [ ] No `#pragma warning disable` used for any IL warning
- [ ] Any `[UnconditionalSuppressMessage]` usage is a genuine last resort at a leaf, with a `Justification` describing its verified invariant and independent preservation mechanism
- [ ] Polyfills present for older TFMs if needed
- [ ] All target frameworks build with 0 warnings
- [ ] Verified with an actual `dotnet publish -p:PublishTrimmed=true`/`PublishAot=true` (or a rooting test app for a library), not just a warning-free build
- [ ] Verified shared/linked source doesn't break sibling projects
