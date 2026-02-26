---
name: dotnet-aot-compat
description: Make a .NET project AOT/trimming compatible. Iteratively finds and fixes IL warnings.
---

# dotnet-aot-compat

Make .NET projects compatible with Native AOT and trimming by systematically resolving all IL trim/AOT analyzer warnings.

## When to Use This Skill

- **"Make this project AOT-compatible"**
- **"Fix trimming warnings"**
- **"Resolve IL warnings"**

## When Not to Use This Skill

Do not use this skill when the project exclusively targets .NET Framework (net4x), which does not support the trim/AOT analyzers.

## Non-Goals

This skill resolves trim/AOT analyzer warnings but does not cover publishing a native AOT binary, optimizing binary size, or replacing reflection-heavy libraries with alternatives.

## Prerequisites

An existing .NET project targeting net8.0 or later (or multi-targeting with at least one net8.0+ TFM) and the corresponding .NET SDK installed.

## Background: What AOT Compatibility Means

Native AOT and the IL trimmer perform static analysis to determine what code is reachable. Reflection can break this analysis because the trimmer can't see what types/members are accessed at runtime. The `IsAotCompatible` property enables analyzers that flag these issues as build warnings (ILXXXX codes).

## Critical Rules

### Never suppress warnings

Every IL warning represents a real code path that WILL break at runtime under trimming or AOT. There are no "false positives" — if the analyzer warns, the trimmer will fail or produce broken code. Suppressions just move the failure from build time to runtime.

- **NEVER** use `#pragma warning disable` for IL warnings. It hides warnings from the Roslyn analyzer at build time, but the IL linker and AOT compiler still see the issue. The code will fail at trim/publish time.
- **NEVER** use `[UnconditionalSuppressMessage]`. It tells both the analyzer AND the linker to ignore the warning, meaning the trimmer cannot verify safety. Raising an error at build time is always preferable to hiding the issue and having it silently break at runtime.
- If your first instinct for a warning is to suppress it, **stop and look at the "Fix recipes" section below** — there is almost always a real fix.
- **Prefer** `[DynamicallyAccessedMembers]` annotations to flow type information through the call chain.
- **Prefer** refactoring to eliminate patterns that break annotation flow (e.g., boxing `Type` through `object[]`).
- **Use** `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` / `[RequiresAssemblyFiles]` to mark methods as fundamentally incompatible with trimming, propagating the requirement to callers. This surfaces the issue clearly rather than hiding it — callers must explicitly acknowledge the incompatibility.

### Annotation flow is key

The trimmer tracks `[DynamicallyAccessedMembers]` annotations through assignments, parameter passing, and return values. If this flow is broken (e.g., by boxing a `Type` into `object`, storing in an untyped collection, or casting through interfaces), the trimmer loses track and warns. The fix is to preserve the flow, not suppress the warning.

## Step-by-Step Procedure

> **Do not explore the codebase up-front.** The build warnings tell you exactly which files and lines need changes. Follow a tight loop: **build → pick a warning → open that file at that line → apply the fix recipe → rebuild**. Reading or analyzing source files beyond what a specific warning points you to is wasted effort and leads to timeouts. Let the compiler guide you.

### Step 1: Enable AOT analysis in the .csproj

Add `IsAotCompatible`. If the project doesn't exclusively target net8.0+, add a TFM condition (AOT analysis requires net8.0+):

```xml
<PropertyGroup>
  <IsAotCompatible Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">true</IsAotCompatible>
</PropertyGroup>
```

This automatically sets `EnableTrimAnalyzer=true` and `EnableAotAnalyzer=true` for compatible TFMs. For multi-targeting projects (e.g., `netstandard2.0;net8.0`), the condition ensures no `NETSDK1210` warnings on older TFMs.

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

### Step 3: Fix warnings iteratively (innermost first)

Work from the **innermost** reflection call outward. Each fix may cascade new warnings to callers.

**Stay warning-driven.** For each warning, open only the file and line the compiler reported, identify the pattern, apply the matching fix recipe below, and move on. Do not scan the codebase for similar patterns or try to understand the full architecture — fix what the compiler tells you, rebuild, and let new warnings guide the next change. Fix a small batch of warnings (5-10), then rebuild immediately to check progress.

**Use sub-agents when available.** If you have the ability to launch sub-agents (e.g., via a `task` tool), use them to fix individual warnings or small batches of warnings in the same file. Keep the main loop focused on building, parsing warnings, and dispatching — delegate the actual file edits and fix reasoning to sub-agents. This prevents context from filling up with file contents and fix logic, which leads to timeouts on large projects. A good sub-agent prompt includes: the exact warning text (code, file, line), the relevant fix recipe from this skill, and an instruction to make the minimal edit.

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

When you annotate a parameter, **all callers** must now pass properly annotated types. This cascades outward — follow each caller and annotate or refactor as needed.

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

#### Strategy C: `[RequiresUnreferencedCode]` (last resort)

When a method fundamentally requires arbitrary reflection that cannot be statically described:

```csharp
[RequiresUnreferencedCode("Loads plugins by name using Assembly.Load")]
public void LoadPlugin(string assemblyName) {
    var asm = Assembly.Load(assemblyName);
    // ...
}
```

This propagates to callers — they must also be annotated with `[RequiresUnreferencedCode]`. Use sparingly; it marks the entire call chain as trim-incompatible.

### Fix recipes for commonly-mishandled warnings

These warning types are frequently "fixed" via suppression. Here are the actual fixes:

#### IL2050: P/invoke with COM marshalling

**Wrong**: Suppress with `#pragma` or `[UnconditionalSuppressMessage]`.

**Right**: Migrate from `[DllImport]` to `[LibraryImport]` with a source generator. The `LibraryImport` source generator produces AOT-compatible marshalling code at compile time. For COM-typed `out` parameters, use `[MarshalAs(UnmanagedType.Interface)]` or a custom marshaller. See the `dotnet-pinvoke` skill for detailed guidance. If the P/Invoke is Windows-only, conditionally gate it and mark the containing method `[RequiresDynamicCode]` so callers know.

#### IL3000: `Assembly.Location`

**Wrong**: Suppress because "the code already handles empty string".

**Right**: `Assembly.Location` returns empty in AOT/single-file — this is a real behavioral difference. Replace with one of:
- `AppContext.BaseDirectory` if you need the app's directory
- `typeof(T).Assembly.GetName().Name` if you need the assembly name for logging
- `[RequiresAssemblyFiles("Uses Assembly.Location")]` to propagate the requirement to callers

If the location is used purely for diagnostic/logging purposes and the empty string is acceptable, mark the method with `[RequiresAssemblyFiles]`.

#### IL3050: `Enum.GetValues(Type)`

**Wrong**: Suppress the warning.

**Right**: Use `Enum.GetValues<TEnum>()` (available since net5.0). If the method's generic constraint is `where T : struct` and cannot be changed to `where T : struct, Enum`, change the constraint — the `Enum` constraint has been available since C# 7.3.

#### IL2091: Generic argument missing annotation

**Wrong**: Suppress because "we never actually construct T via reflection".

**Right**: Propagate `[DynamicallyAccessedMembers]` to the generic type parameter:

```csharp
// Before (warns IL2091):
public static T EnsureInitialized<T>(ref T? target) where T : class
    => LazyInitializer.EnsureInitialized<T>(ref target!);

// After (clean):
public static T EnsureInitialized<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
    ref T? target) where T : class
    => LazyInitializer.EnsureInitialized<T>(ref target!);
```

This may require a polyfill of `DynamicallyAccessedMembersAttribute` for older TFMs (see Polyfills section).

#### IL2026: Calling `[RequiresUnreferencedCode]` methods

**Wrong**: Suppress the warning at the call site.

**Right**: Mark your method with `[RequiresUnreferencedCode]` too, propagating the requirement. If your method is an entry point or API boundary, this is the correct signal — it tells consumers that this code path is not trim-safe. For assembly loading (`AssemblyLoadContext.LoadFromAssemblyPath`, `Assembly.Load`, etc.), the entire call chain should propagate `[RequiresUnreferencedCode]` because loading arbitrary assemblies is fundamentally trim-incompatible.

### Step 4: Rebuild and repeat

After each small batch of fixes (5-10 warnings), rebuild with `--no-incremental` and check for new warnings. **Do not attempt to fix all warnings before rebuilding** — frequent rebuilds catch mistakes early and reveal cascading warnings. Fixes cascade — annotating an inner method may surface warnings in its callers. Repeat until `0 Warning(s)`.

### Step 5: Validate all TFMs

Build all target frameworks to ensure:
- **0 IL warnings** on net8.0+ TFMs
- **No NETSDK1210 warnings** (the `IsAotCompatible` condition handles this)
- **Clean builds** on older TFMs (netstandard2.0, net472, etc.)

```bash
dotnet build <project.csproj>  # builds all TFMs
```

## Polyfills for Older TFMs

`DynamicallyAccessedMembersAttribute` shipped in .NET 5. For projects targeting netstandard2.0 or net472, you need a polyfill. The trimmer recognizes the attribute by name, so a local copy works:

```csharp
#if !NET
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.ReturnValue |
                    AttributeTargets.GenericParameter | AttributeTargets.Parameter |
                    AttributeTargets.Property, Inherited = false)]
    internal sealed class DynamicallyAccessedMembersAttribute : Attribute
    {
        public DynamicallyAccessedMembersAttribute(DynamicallyAccessedMemberTypes memberTypes)
            => MemberTypes = memberTypes;
        public DynamicallyAccessedMemberTypes MemberTypes { get; }
    }

    [Flags]
    internal enum DynamicallyAccessedMemberTypes
    {
        None = 0,
        PublicParameterlessConstructor = 0x0001,
        PublicConstructors = 0x0002 | PublicParameterlessConstructor,
        NonPublicConstructors = 0x0004,
        PublicMethods = 0x0008,
        NonPublicMethods = 0x0010,
        PublicFields = 0x0020,
        NonPublicFields = 0x0040,
        PublicNestedTypes = 0x0080,
        NonPublicNestedTypes = 0x0100,
        PublicProperties = 0x0200,
        NonPublicProperties = 0x0400,
        PublicEvents = 0x0800,
        NonPublicEvents = 0x1000,
        Interfaces = 0x2000,
        All = ~None // Discouraged — prefer specific flags
    }
}
#endif
```

Similarly for `RequiresUnreferencedCodeAttribute` and `UnconditionalSuppressMessageAttribute` if needed on older TFMs.

## Common Gotchas

4. **Serialization libraries**: Most reflection-based serializers (e.g., `Newtonsoft.Json`, `XmlSerializer`) are not AOT-compatible. Migrate to a source-generation-based serializer such as `System.Text.Json` with a `JsonSerializerContext`. If migration is not feasible, mark the serialization call site with `[RequiresUnreferencedCode]`.

5. **Shared projects / projitems**: When source is shared between multiple projects via `<Import>`, annotations added to shared code affect ALL consuming projects. Verify that all consumers still build cleanly.

## Checklist

- [ ] Added `<IsAotCompatible>` with TFM condition to .csproj
- [ ] Built with AOT analyzers enabled (net8.0+ TFM)
- [ ] Fixed all IL warnings via annotations or refactoring
- [ ] No `#pragma warning disable` or `[UnconditionalSuppressMessage]` used for any IL warning
- [ ] Polyfills present for older TFMs if needed
- [ ] All target frameworks build with 0 warnings
- [ ] Verified shared/linked source doesn't break sibling projects
