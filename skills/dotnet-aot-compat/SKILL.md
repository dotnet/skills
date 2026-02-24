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

### Never suppress warnings incorrectly

- **NEVER** use `#pragma warning disable` for IL warnings. It hides warnings from the Roslyn analyzer at build time, but the IL linker and AOT compiler still see the issue. The code will fail at trim/publish time.
- **NEVER** use `[UnconditionalSuppressMessage]`. It tells both the analyzer AND the linker to ignore the warning, meaning the trimmer cannot verify safety. Raising an error at build time is always preferable to hiding the issue and having it silently break at runtime.
- **Prefer** `[DynamicallyAccessedMembers]` annotations to flow type information through the call chain.
- **Prefer** refactoring to eliminate patterns that break annotation flow (e.g., boxing `Type` through `object[]`).
- **Use** `[RequiresUnreferencedCode]` to mark methods as fundamentally incompatible with trimming, propagating the requirement to callers. This surfaces the issue clearly rather than hiding it — callers must explicitly acknowledge the incompatibility.

### Annotation flow is key

The trimmer tracks `[DynamicallyAccessedMembers]` annotations through assignments, parameter passing, and return values. If this flow is broken (e.g., by boxing a `Type` into `object`, storing in an untyped collection, or casting through interfaces), the trimmer loses track and warns. The fix is to preserve the flow, not suppress the warning.

## Step-by-Step Procedure

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
- **IL3050**: Calling a method marked `[RequiresDynamicCode]`

### Step 3: Fix warnings iteratively (innermost first)

Work from the **innermost** reflection call outward. Each fix may cascade new warnings to callers.

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

### Step 4: Rebuild and repeat

After each round of fixes, rebuild with `--no-incremental` and check for new warnings. Fixes cascade — annotating an inner method may surface warnings in its callers. Repeat until `0 Warning(s)`.

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

6. **Shared projects / projitems**: When source is shared between multiple projects via `<Import>`, annotations added to shared code affect ALL consuming projects. Verify that all consumers still build cleanly.

## Checklist

- [ ] Added `<IsAotCompatible>` with TFM condition to .csproj
- [ ] Built with AOT analyzers enabled (net8.0+ TFM)
- [ ] Fixed all IL warnings via annotations or refactoring
- [ ] No `#pragma warning disable` or `[UnconditionalSuppressMessage]` used for any IL warning
- [ ] Polyfills present for older TFMs if needed
- [ ] All target frameworks build with 0 warnings
- [ ] Verified shared/linked source doesn't break sibling projects
