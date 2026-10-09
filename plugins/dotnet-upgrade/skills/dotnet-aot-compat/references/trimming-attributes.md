# Trimming and AOT Attributes Reference

Attributes in `System.Diagnostics.CodeAnalysis` describe trimming/AOT contracts the linker's static analysis cannot infer on its own. Apply them to express which members reflection needs, which code paths are unsafe, and which suppressions are provably correct.

```csharp
using System.Diagnostics.CodeAnalysis;
```

## Attribute Catalog

| Attribute | Purpose |
| --- | --- |
| `[RequiresUnreferencedCode]` | The member needs members the linker cannot see (reflection by name, `Assembly.Load*`, `Type.GetType(string)`). |
| `[RequiresDynamicCode]` | The member needs a JIT (emitting, dynamic dispatch, `Type.MakeGenericType` on unknown types). |
| `[DynamicallyAccessedMembers]` | Declares which members of a `Type`/`string` must be preserved for reflection. |
| `[UnconditionalSuppressMessage]` | IL-persisted suppression of a specific warning, with a mandatory justification. |
| `[DynamicDependency]` | Keeps named members reachable; does **not** silence warnings on its own. |

---

## `[RequiresUnreferencedCode]`

**Intent:** Mark a member whose body requires members the linker cannot prove are referenced. The warning is suppressed *inside* the member and emitted (`IL2026`) at every call site instead.

**Signature:**

```csharp
[RequiresUnreferencedCode("This method uses Assembly.Load, which the linker cannot analyze.")]
public void LoadPlugins(string path) { /* Assembly.Load ... */ }
```

Valid on `class`, `constructor`, `method` (not on properties, fields, or events).

**Rules:**
- Use when the requirement cannot be expressed more precisely: `Assembly.Load*`, `Type.GetType(string)` with a non-constant name, `XmlSerializer`, or reflection by a runtime-built name. Constant-name reflection (for example, `type.GetMethod("name")`) is usually a `[DynamicallyAccessedMembers]` case, not a `[RequiresUnreferencedCode]` case.
- Give a message that tells the *caller* what breaks, not what the method does. Optionally set `Url` to docs.
- Propagate the warning: if your method calls a `[RequiresUnreferencedCode]` API, either annotate your method too (correct for public API) or remove the reflection.
- Annotating a `virtual`/interface member forces every override/implementation to carry the same annotation (`IL2046`). Prefer annotating the concrete implementation or the whole type instead.

---

## `[RequiresDynamicCode]`

**Intent:** The AOT-only analog of `[RequiresUnreferencedCode]`. Marks members that need the JIT. Emits `IL3050` at call sites; only produced under AOT (or when `EnableAotAnalyzer` is on).

**Signature:**

```csharp
[RequiresDynamicCode("Emits a dynamic proxy type at runtime, which requires a JIT.")]
public Type CreateProxy(Type interfaceType) => /* Reflection.Emit ... */;
```

Valid on `method`, `constructor`, and (since .NET 7) `class`.

**Rules:**
- Use for `Reflection.Emit`, `Type.MakeGenericType(...)` with a runtime-constructed argument, `dynamic` dispatch, and `Expression.Compile()` (which may fall back to interpretation but is not guaranteed).
- Trimming tolerates some of these (for example, `Reflection.Emit` is trimmed but still runs); AOT does not. A trimmable-but-not-AOT library is fine — just do not set `IsAotCompatible`.
- `[RequiresDynamicCode]` means the feature *cannot be guaranteed* under AOT — not that every listed operation always fails. Runtime generic construction can succeed when the instantiation is statically available, and reflection over preserved members still works.
- As with `[RequiresUnreferencedCode]`, avoid putting it on `virtual`/interface members unless you intend every implementation to inherit the requirement.

---

## `[DynamicallyAccessedMembers]`

**Intent:** Declare that specific members of a `Type` (or of the type named by a `string`) must be preserved because reflection will access them. The requirement flows **backward** from the reflection site to wherever the `Type`/`string` came from. This is the primary way to make reflective code trimming-safe *without* removing the reflection.

**Signature:**

```csharp
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
private readonly Type _serviceType;
```

Valid on `class`, `struct`, `interface`, `method`, `field`, `property`, `parameter`, `return value`, and `generic parameter`. It applies only to values of type `Type` or `string`.

- On a **generic type parameter**: `void Register<[DynamicallyAccessedMembers(PublicConstructors)] T>()` — the members of the type argument `T` are preserved.
- On a **parameter** (including `this` of an extension method): the members of the argument are preserved for the call.
- On a **method** (instance method of a `Type`-derived type): applies to the implicit `this`, not the return value.
- On a **return value**: `[return: DynamicallyAccessedMembers(...)]` — the members of the returned `Type` are preserved for callers.
- On a **property**: propagates to the backing field.

**Rules:**
- Use the **narrowest** member set for the reflection performed — see the selection table in `SKILL.md`.
- Propagate the annotation through the entire call chain: generic parameter → parameter → field → return. If a `Type` flows through a method, that method's parameter/return must carry the annotation, or the trimmer reports `IL2067`/`IL2070`/`IL2072`/`IL2077`.
- When a method just forwards a `Type` through, re-declare the same annotation on its own parameter/return.
- Boxing drops the flow: storing a `Type` in `object`, `object[]`, or an untyped collection loses the annotation. Pass the `Type` through its own annotated parameter.

### Full `DynamicallyAccessedMemberTypes` list

`[Flags]` enum; combine with `|`. Core values:

| Value | Members preserved |
| --- | --- |
| `None` (0) | Nothing. |
| `PublicParameterlessConstructor` (1) | Public parameterless constructors. |
| `PublicConstructors` (3) | All public constructors. |
| `NonPublicConstructors` (4) | Non-public constructors. |
| `PublicMethods` (8) | Public methods. |
| `NonPublicMethods` (16) | Non-public methods. |
| `PublicFields` (32) | Public fields. |
| `NonPublicFields` (64) | Non-public fields. |
| `PublicNestedTypes` (128) | Public nested types. |
| `NonPublicNestedTypes` (256) | Non-public nested types. |
| `PublicProperties` (512) | Public properties. |
| `NonPublicProperties` (1024) | Non-public properties. |
| `PublicEvents` (2048) | Public events. |
| `NonPublicEvents` (4096) | Non-public events. |
| `Interfaces` (8192) | Interfaces implemented by the type. |
| `All` (-1) | Everything. |

Named composites also exist — `AllConstructors`, `AllMethods`, `AllFields`, `AllProperties`, `AllEvents`, `AllNestedTypes`, and the `*WithInherited` variants (`PublicConstructorsWithInherited`, `NonPublicMethodsWithInherited`, and so on). Name reusable sets in a constant:

```csharp
internal const DynamicallyAccessedMemberTypes CreatorMembersRequired =
    DynamicallyAccessedMemberTypes.PublicConstructors |
    DynamicallyAccessedMemberTypes.NonPublicConstructors;
```

**Avoid `All`.** Preserving everything makes far more code reachable, which can cascade into *new* warnings from preserved members that themselves have `[RequiresUnreferencedCode]` (`IL2112`/`IL2113`).

---

## Scoping attributes to property accessors

Trimming attributes do not have to cover a whole property. Placing an attribute directly on an accessor scopes it to one part — the setter, the getter, or the backing field — so the rest of the property stays unannotated.

- Place the attribute directly on the accessor (the setter/getter) — accessors are methods, so the attribute applies to that method only.
- `[field: ...]` — the backing field only.
- `[return: ...]` — the return value only (of a method or a getter).

This matters when only one accessor is unsafe. If a setter routes by the runtime type of the value but the getter is a plain field read, annotate only the setter so readers are not warned:

```csharp
public sealed class ProcessingContext
{
    private object _payload;

    public object Payload
    {
        get => _payload; // unannotated — reading is safe.

        [RequiresUnreferencedCode("Routes by the runtime type of the value, which trimming cannot analyze.")]
        set => _payload = value;
    }
}
```

`RequiresUnreferencedCode`, `RequiresDynamicCode`, and `UnconditionalSuppressMessage` target methods, so placing them directly on an accessor works (accessors compile to methods). Use the attribute directly on the accessor, or the explicit `[method:]` target. `[return: DynamicallyAccessedMembers(...)]` preserves only the returned `Type`; `[field: DynamicallyAccessedMembers(...)]` puts the requirement on the backing field without touching the property surface.

Rule of thumb: annotate the narrowest part that actually needs it, so callers only get a warning or contract for the operation that is affected.

---

## `[UnconditionalSuppressMessage]`

**Intent:** Suppress one specific warning at one location, persisted into IL so the linker respects it. This is the *only* suppression mechanism that works for trimming/AOT warnings — `#pragma warning disable` and `[SuppressMessage]` are stripped before linking and do nothing.

**Signature:**

```csharp
[UnconditionalSuppressMessage("Trimming", "IL2072",
    Justification = "The dictionary is only populated through the annotated Register<T>() method.")]
```

Constructor: `UnconditionalSuppressMessage(string category, string checkId)`. Named properties: `Justification`, `MessageId`, `Scope`, `Target` — `Justification` is optional at the API level but required under this skill's policy.

**Category** identifies the warning family:
- `"Trimming"` — `IL2xxx` (trimming). Some documentation and older code use `"ReflectionAnalysis"` for the same warnings.
- `"AOT"` — `IL305x` (dynamic-code warnings).
- `"SingleFile"` — `IL3000`–`IL3003`.

`checkId` is the warning code, optionally with the method suffix, for example `"IL2072"` or `"IL2026:RequiresUnreferencedCode"`.

**Rules:**
- **Always set `Justification`** (mandatory under this skill's policy) and make it state the invariant that makes the suppressed warning safe — not "to make the build green".
- Suppress at the narrowest scope (a private helper or a leaf), not a public API, so the unsafety stays contained.
- The suppression is only valid if the reflected member is a **genuine reflection target elsewhere** (for example, preserved through an annotated registration API). A member reachable only by a normal non-reflective call is not enough — it can be inlined, renamed, or moved. Example of an invalid suppression, straight from the docs:

```csharp
// INVALID: "the app uses this property" does not make it a reflection target.
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2063",
    Justification = "*INVALID* Only need to serialize properties used by the app.")]
public string Serialize(object o)
{
    foreach (var property in o.GetType().GetProperties()) { /* ... */ }
}
```

- Prefer `[DynamicallyAccessedMembers]` or a source generator over suppression. Reserve suppression for the invariant-proven leaf after the rest of the design is trimming-safe.

---

## `[DynamicDependency]`

**Intent:** Last resort for keeping a *named* member reachable that annotations cannot express (for example, reflection using a name built at runtime).

```csharp
[DynamicDependency("Start", typeof(MyService))]
public void RegisterByName() { /* finds "Start" by string */ }
```

**Rules:**
- `[DynamicDependency]` keeps the member but does **not** silence the warning on the reflection site; you still need a `[RequiresUnreferencedCode]` or `[UnconditionalSuppressMessage]` if reflection by name remains.
- Prefer `[DynamicallyAccessedMembers]` whenever the access is by `Type`, and reserve `[DynamicDependency]` for genuinely string-named, assembly-scoped access.

---

## Warning Code Table

### IL2xxx — trimming (reachability / DAM dataflow)

| Code | Meaning |
| --- | --- |
| `IL2026` | Calling a member annotated `[RequiresUnreferencedCode]`. |
| `IL2046` | `[RequiresUnreferencedCode]` mismatch between a base member and an override/implementation. |
| `IL2057` | `Type.GetType(string)` (or similar) with a non-constant type name. |
| `IL2067` | A parameter without DAM is passed to a parameter that requires it. |
| `IL2070` | The `this` argument (an unannotated `Type`, for example `type.GetMethods()`) does not satisfy the DAM requirement; annotate the source parameter. |
| `IL2072` | A method's unannotated **return value** is passed to a parameter that requires DAM. |
| `IL2075` | A method's unannotated **return value** is used as the reflection receiver (`this`). |
| `IL2077` | An unannotated **field** is passed to a parameter that requires DAM. |
| `IL2087` | `typeof(TSource)` (a generic parameter) is passed to a parameter that requires DAM; annotate the generic parameter. |
| `IL2088` | An unannotated generic parameter (`typeof(TSource)`) is returned from a method whose return requires DAM. |
| `IL2091` | A generic type argument does not satisfy the DAM requirement of a generic parameter (constraint mismatch). |
| `IL2104` | Collapsed summary: "Assembly produced trim warnings." Set `TrimmerSingleWarn=false` to expand. |
| `IL2112` / `IL2113` | A member preserved by DAM itself has `[RequiresUnreferencedCode]`. |
| `IL2125` | A referenced assembly lacks `IsTrimmable` metadata (requires `VerifyReferenceTrimCompatibility=true`). |

The `IL2067`–`IL2091` band is *source → target* dataflow: a value without DAM (a parameter, return value, field, `this`, or generic parameter) flows into a location that requires it.

### IL3xxx — AOT and single-file

| Code | Meaning |
| --- | --- |
| `IL3050` | Calling a member annotated `[RequiresDynamicCode]`. |
| `IL3051` | `[RequiresDynamicCode]` mismatch between a base member and an override/implementation. |
| `IL3053` | Collapsed summary: "Assembly produced AOT warnings." |
| `IL3000`–`IL3003` | Single-file incompatibility (for example, `Assembly.Location` is empty in single-file/`RequiresAssemblyFiles`). |

AOT analysis runs *on top of* trim analysis (AOT implies trimming), so an AOT publish reports both families.

---

## Summary

- `[RequiresUnreferencedCode]` → the member needs members the linker cannot see (trimming, `IL2026` at call sites).
- `[RequiresDynamicCode]` → the member needs a JIT (AOT, `IL3050` at call sites).
- `[DynamicallyAccessedMembers]` → preserve the narrowest member set; flow it backward through generic parameters, parameters, fields, and returns.
- `[UnconditionalSuppressMessage]` → IL-persisted suppression with a mandatory, invariant-stating justification; the only working suppression for trim/AOT warnings.
- `[DynamicDependency]` → keep a named member reachable (does not silence warnings).
- Never use `#pragma warning disable` or `[SuppressMessage]` for `IL2xxx`/`IL3xxx` — they are not persisted in IL.
