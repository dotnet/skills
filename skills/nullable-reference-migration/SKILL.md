---
name: nullable-reference-migration
description: Enable nullable reference types in a C# project and systematically resolve all warnings. Use when the user wants to adopt NRTs in an existing codebase, migrate file-by-file or project-wide, or fix nullable warnings after upgrading a dependency.
---

# Nullable Reference Migration

## When to Use

- Enabling nullable reference types in an existing C# project or solution
- Systematically resolving CS86xx nullable warnings after enabling the feature
- Annotating a library's public API surface so consumers get accurate nullability information
- Upgrading a dependency that has added nullable annotations and new warnings appear

## When Not to Use

- The project already has `<Nullable>enable</Nullable>` and zero warnings — the migration is done
- The user only wants to suppress warnings without fixing them (recommend against this)
- The code targets C# 7.3 or earlier, which does not support nullable reference types

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj` or `.sln` to migrate |
| Migration scope | No | `project-wide` (default) or `file-by-file` — controls the rollout strategy |

## Workflow

### Step 1: Evaluate readiness

1. Run `dotnet --version` to confirm the SDK is installed. NRTs require C# 8.0+ (`.NET Core 3.0` / `.NET Standard 2.1` or later).
2. Open the `.csproj` and check the `<LangVersion>` and `<TargetFramework>`. If the project multi-targets, note all TFMs.
3. Check whether `<Nullable>` is already set. If it is set to `enable`, skip to Step 5 to audit remaining warnings.
4. Look for any `Directory.Build.props` that might set `<Nullable>` at the repo level.

### Step 2: Choose a rollout strategy

Pick one of the following strategies based on codebase size and activity level. Recommend the strategy to the user and confirm before proceeding.

#### Strategy A — Project-wide enable (small to medium projects)

Best when the project has fewer than roughly 50 source files or the team wants to finish in one pass.

1. Add `<Nullable>enable</Nullable>` to the `<PropertyGroup>` in the `.csproj`.
2. Build and address all warnings at once.

#### Strategy B — Warnings-first, then annotations (large or active projects)

Best when the codebase is large or under active development by multiple contributors.

1. Add `<Nullable>warnings</Nullable>` to the `.csproj`. This enables warnings without changing type semantics.
2. Build, fix all warnings from Step 3 onward.
3. Change to `<Nullable>enable</Nullable>` to activate annotations — this triggers a second wave of warnings.
4. Resolve the annotation-phase warnings from Step 4 onward.

#### Strategy C — File-by-file (very large projects)

Best for large legacy codebases where enabling project-wide would produce an unmanageable number of warnings.

1. Set `<Nullable>disable</Nullable>` (or omit it) at the project level.
2. Add `#nullable enable` at the top of each file as it is migrated.
3. Prioritize files in dependency order: shared utilities and models first, then higher-level consumers.

### Step 3: Fix dereference warnings

Build the project and work through dereference warnings. These are the most common:

| Warning | Meaning | Typical fix |
|---------|---------|-------------|
| CS8602 | Dereference of a possibly null reference | Add a null check, use `?.`, or use a pattern like `if (x is not null)` |
| CS8600 | Converting possible null to non-nullable type | Add `?` to the target type if null is valid, or add a null guard |
| CS8603 | Possible null reference return | Return a non-null value, or change the return type to nullable (`T?`) |
| CS8604 | Possible null reference argument | Check for null before passing, or mark the parameter as nullable |

Guidance:

- Prefer explicit null checks (`if`, `is not null`, `??`) over the null-forgiving operator (`!`).
- Use the null-forgiving operator only when you can prove the value is never null but the compiler cannot, and add a comment explaining why.
- When a method legitimately returns null, change the return type to `T?` — do not hide nulls behind a non-nullable signature.
- `Debug.Assert(x != null)` acts as a null-state hint to the compiler just like an `if` check. Use it at the top of a method or block to inform the flow analyzer about invariants and eliminate subsequent `!` operators in that scope.
- If you find yourself adding `!` at every call site of an internal method, consider making that parameter nullable instead. Reserve `!` for cases where the compiler genuinely cannot prove non-nullness.

### Step 4: Annotate declarations

Start by deciding the **intended nullability** of each member based on its design purpose — should this parameter accept null? Can this return value ever be null? Annotate accordingly, then address any resulting warnings. Do not let warnings drive your annotations; that leads to over-annotating with `?` or scattering `!` to silence the compiler.

After dereference warnings are resolved, address annotation warnings:

| Warning | Meaning | Typical fix |
|---------|---------|-------------|
| CS8618 | Non-nullable field/property not initialized in constructor | Initialize the member, make it nullable (`?`), or use `required` (C# 11+). If a helper method initializes fields, decorate it with `[MemberNotNull(nameof(field))]` so the compiler knows the field is non-null after the call |
| CS8625 | Cannot convert null literal to non-nullable type | Make the target nullable or provide a non-null value |
| CS8601 | Possible null reference assignment | Same techniques as CS8600 |

For each type, decide: **should this member ever be null?**

- **Yes** → add `?` to its declaration.
- **No** → ensure it is initialized in every constructor path, or mark it `required`.

Focus annotation effort on public and protected APIs first — these define the contract that consumers depend on. Internal and private code can tolerate `!` more liberally since it does not affect external callers.

Pay special attention to:

- **DTOs and serialization models**: Deserialized properties may be null even if the type says otherwise. Mark them nullable or use `required` / `[JsonRequired]`.
- **Entity Framework entities**: Navigation properties are often null before loading. Follow the EF Core guidance: declare them as nullable, or use a non-null default with `null!` only if the entity lifecycle guarantees population.
- **Event handlers and delegates**: The pattern `EventHandler? handler = SomeEvent; handler?.Invoke(...)` is idiomatic.
- **Struct reference-type fields**: Reference-type fields in structs are null when using `default(T)`. If `default` is valid usage for the struct, those fields must be nullable. If `default` is never expected (the struct is only created by specific APIs), keep them non-nullable to avoid burdening every consumer with unnecessary null checks.
- **Post-Dispose state**: If a field or property is non-null for the entire useful lifetime of the object but may become null after `Dispose`, keep it non-nullable. Using an object after disposal is a contract violation — do not weaken annotations for that case.
- **Overrides and interface implementations**: An override can return a stricter (non-nullable) type than the base method declares. If your implementation never returns null but the base/interface returns `T?`, you can declare the override as returning `T`. Parameter types must match the base exactly.
- **`IEquatable<T>` and `IComparable<T>`**: Reference types should implement `IEquatable<T?>` and `IComparable<T?>` (with nullable `T`), because callers commonly pass null to `Equals` and `CompareTo`.

### Step 5: Apply nullable attributes for advanced scenarios

When a simple `?` annotation cannot express the null contract, use attributes from `System.Diagnostics.CodeAnalysis`:

| Attribute | Use case |
|-----------|----------|
| `[NotNullWhen(true/false)]` | `TryGet` or `IsNullOrEmpty` patterns — the argument is not null when the method returns the specified bool. For `Try` methods with a non-generic out parameter, use `[NotNullWhen(true)] out T? result`. Also add to `Equals(object? obj)` overrides to indicate the argument is non-null when returning `true` |
| `[MaybeNullWhen(true/false)]` | For `Try` methods with a generic out parameter, use `[MaybeNullWhen(false)] out T result` — the value may be `default` (null) on failure |
| `[NotNull]` | A nullable parameter is guaranteed non-null when the method returns (e.g., a `ThrowIfNull` helper) |
| `[MaybeNull]` | A non-nullable generic return might be `default` (null) |
| `[AllowNull]` | A non-nullable property setter accepts null (e.g., falls back to a default value) |
| `[DisallowNull]` | A nullable property should never be explicitly set to null |
| `[MemberNotNull(nameof(...))]` | A helper method guarantees that specific members are non-null after it returns |
| `[NotNullIfNotNull("paramName")]` | The return is non-null if the named parameter is non-null |
| `[DoesNotReturn]` | The method always throws — code after the call is unreachable |

Add `using System.Diagnostics.CodeAnalysis;` where needed.

### Step 6: Clean up suppressions

1. Search for any `#nullable disable` directives or `!` operators that were added as temporary workarounds.
2. For each one, determine whether the suppression is still needed.
3. Remove suppressions that are no longer necessary. For any that remain, add a comment explaining why.
4. Search for `#pragma warning disable CS86` to find suppressed nullable warnings and evaluate whether the underlying issue can be fixed instead.

### Step 7: Validate

1. Build the project with `dotnet build` and confirm zero nullable warnings.
2. Run `dotnet build /warnaserror:nullable` to enforce that no nullable warnings remain. Consider adding this to CI.
3. Run existing tests with `dotnet test` to confirm no regressions.
4. If the project is a library, inspect the public API surface to verify that nullable annotations match the intended contracts (parameters that accept null are `T?`, parameters that reject null are `T`).

## Validation

- [ ] `.csproj` contains `<Nullable>enable</Nullable>`
- [ ] `dotnet build` produces zero CS86xx warnings
- [ ] `dotnet build /warnaserror:nullable` succeeds
- [ ] `dotnet test` passes with no regressions
- [ ] No `#nullable disable` directives remain unless justified with a comment
- [ ] Null-forgiving operators (`!`) are rare, each with a justifying comment
- [ ] Public API signatures accurately reflect null contracts

### Code review checklist

Nullable migration changes require broader review than a typical diff:

1. **Verify no behavior changes**: confirm that `?` and `!` are the only additions — no accidental `?.`, no removed null checks, no new branches. The generated IL should be unchanged except for nullable metadata.
2. **Review explicit annotation changes**: for every `?` added to a parameter or return type, confirm it matches the intended design. Does the method really accept null? Can it really return null?
3. **Review unchanged APIs in scope**: enabling `<Nullable>enable</Nullable>` implicitly makes every unannotated reference type in that scope non-nullable. Scan unchanged public members for parameters that actually do accept null but were not annotated.

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Sprinkling `!` everywhere to silence warnings | The null-forgiving operator hides bugs. Add null checks or change the type to nullable instead |
| Marking everything `T?` to eliminate warnings quickly | Over-annotating with `?` defeats the purpose — callers must add unnecessary null checks. Only use `?` when null is a valid value |
| Constructor does not initialize all non-nullable members | Initialize fields and properties in every constructor, use `required` (C# 11+), or make the member nullable |
| Serialization (JSON, XML) bypasses constructors | Deserialized properties can be null regardless of the declared type. Mark DTO properties as nullable or use `required` / `init` with validation |
| EF Core navigation properties | Navigation properties are null until loaded. Declare them as nullable, or acknowledge the `null!` pattern with a comment |
| Generated code produces warnings | Generated files are excluded from nullable analysis automatically if they contain `<auto-generated>` comments. If warnings persist, add `#nullable disable` at the top of the generated file or configure `.editorconfig` with `generated_code = true` |
| Multi-target projects and older TFMs | NRT annotations compile on older TFMs (e.g., .NET Standard 2.0) with C# 8.0+, but nullable attributes like `[NotNullWhen]` may not exist. Use a polyfill package such as `Nullable` from NuGet, or define the attributes internally |
| Warnings reappear after upgrading a dependency | The dependency added nullable annotations. This is expected and beneficial — fix the new warnings as in Steps 3–5 |
| Accidentally changing behavior while annotating | Adding `?` to a type or `!` to an expression is metadata-only and does not change generated IL. But replacing `obj.Method()` with `obj?.Method()` (null-conditional) changes runtime behavior — the call is silently skipped instead of throwing. Only use `?.` when you intentionally want to tolerate null, not as a quick fix for a warning |
| Adding `?` to a value type (enum, struct) | For reference types, `?` is a metadata annotation with no runtime effect. For value types like `int` or an enum, `?` changes the type to `Nullable<T>`, altering the method signature, binary layout, and boxing behavior. Double-check that you are only adding `?` to reference types unless you truly intend to make a value type nullable |
| Removing existing null argument validation | Do not remove `ArgumentNullException` checks just because a parameter is now non-nullable. Many callers will not have NRT enabled (older C# versions, other .NET languages, suppressed warnings), so runtime validation remains important |

## More Info

- [Nullable migration strategies](https://learn.microsoft.com/en-us/dotnet/csharp/nullable-migration-strategies)
- [Embracing Nullable Reference Types](https://devblogs.microsoft.com/dotnet/embracing-nullable-reference-types/) — Mads Torgersen's guidance on adoption timing and ecosystem considerations
- [Resolve nullable warnings](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/nullable-warnings)
- [Attributes for nullable static analysis](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis)
- [EF Core and nullable reference types](https://learn.microsoft.com/en-us/ef/core/miscellaneous/nullable-reference-types)
