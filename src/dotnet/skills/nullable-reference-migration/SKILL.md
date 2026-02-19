---
name: nullable-reference-migration
description: Enable nullable reference types in a C# project and systematically resolve all warnings. Use when the user wants to adopt NRTs in an existing codebase, migrate file-by-file or project-wide, fix nullable warnings after upgrading a dependency, or resolve CS8602/CS8618 and other CS86xx warnings. Also use when adding #nullable enable, annotating APIs for nullability, or cleaning up null-forgiving operators.
---

# Nullable Reference Migration

Enable C# nullable reference types (NRTs) in an existing codebase and systematically resolve all warnings. The outcome is a project (or solution) with `<Nullable>enable</Nullable>`, zero nullable warnings, and accurately annotated public API surfaces — giving both the compiler and consumers reliable nullability information.

## When to Use

- Enabling nullable reference types in an existing C# project or solution
- Systematically resolving CS86xx nullable warnings after enabling the feature
- Annotating a library's public API surface so consumers get accurate nullability information
- Upgrading a dependency that has added nullable annotations and new warnings appear
- Analyzing suppressions in a code base that has already enabled NRTs to determine whether they can be removed

## When Not to Use

- The project already has `<Nullable>enable</Nullable>` and zero warnings — the migration is done unless the user wants to re-examine suppressions with a view to removing unnecessary ones (see Step 6)
- The user only wants to suppress warnings without fixing them (recommend against this)
- The code targets C# 7.3 or earlier, which does not support nullable reference types

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or build entry point to migrate |
| Migration scope | No | `project-wide` (default) or `file-by-file` — controls the rollout strategy |
| Build command | No | How to build the project (e.g., `dotnet build`, `msbuild`, or a repo-specific build script). Detect from the repo if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`, or a repo-specific test script). Detect from the repo if not provided |

## Workflow

> **Commit strategy:** Commit at each logical boundary — after enabling `<Nullable>` (Step 2), after fixing dereference warnings (Step 3), after annotating declarations (Step 4), after applying nullable attributes (Step 5), and after cleaning up suppressions (Step 6). This keeps each commit focused and reviewable, and prevents losing work if a later step reveals a design issue that requires rethinking. For file-by-file migrations, commit each file or batch of related files individually.

### Step 1: Evaluate readiness

> **Optional:** Run `scripts/Scan-NullableReadiness.ps1 -Path <project-or-solution>` to automate the checks below. The script reports `<Nullable>`, `<LangVersion>`, `<TargetFramework>`, `<WarningsAsErrors>` settings and counts `#nullable disable` directives, `!` operators, and `#pragma warning disable CS86xx` suppressions. Use `-Json` for machine-readable output.

1. Identify how the project is built and tested. Look for build scripts (e.g., `build.cmd`, `build.sh`, `Makefile`), a `.sln` file, or individual `.csproj` files. If the repo uses a custom build script, use it instead of `dotnet build` throughout this workflow.
2. Run `dotnet --version` to confirm the SDK is installed. Nullable reference types (NRTs) require C# 8.0+ (`.NET Core 3.0` / `.NET Standard 2.1` or later).
3. Open the `.csproj` (or `Directory.Build.props` if properties are set at the repo level) and check the `<LangVersion>` and `<TargetFramework>`. If the project multi-targets, note all TFMs.
4. Check whether `<Nullable>` is already set. If it is set to `enable`, skip to Step 5 to audit remaining warnings.
5. Determine the project type — this shapes annotation priorities throughout the migration:
   - **Library**: Focus on public API contracts first. Every `?` on a public parameter or return type is a contract change that consumers depend on. Be precise and conservative.
   - **Application (web, console, desktop)**: Focus on null safety at boundaries — deserialization, database queries, user input, external API responses. Internal plumbing can be annotated more liberally.
   - **Test project**: Lower priority for annotation precision. Use `!` more freely on test setup and assertions where null is never expected. Focus on ensuring test code compiles cleanly.

### Step 2: Choose a rollout strategy

Pick one of the following strategies based on codebase size and activity level. Recommend the strategy to the user and confirm before proceeding.

Regardless of strategy, **start at the center and work outward**: begin with core domain models, DTOs, and shared utility types that have few dependencies but are used widely. Annotating these first eliminates cascading warnings across the codebase and gives the biggest return on effort. Then move on to higher-level services, controllers, and UI code that depend on the core types. This approach minimizes the number of warnings at each step and prevents getting overwhelmed by a flood of warnings from a large project-wide enable. Prefer to create at least one PR per project, or per layer, to keep changesets reviewable and focused. If there are relatively few annotations needed, a single project-wide enable and single PR may be appropriate.

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

> **Build checkpoint:** After enabling `<Nullable>` (or adding `#nullable enable` to the first batch of files), do a **clean build** (e.g., `dotnet build --no-incremental`, or delete `bin`/`obj` first). Incremental builds only recompile changed files and will hide warnings in untouched files. Record the initial warning count — this is the baseline to work down from. Do not proceed to fixing warnings without first confirming the project still compiles. Use clean builds for all subsequent build checkpoints in this workflow.

### Step 3: Fix dereference warnings

> **Prioritization:** Work through files in dependency order — start with core models and shared utilities that other code depends on, then move to higher-level consumers. Within each file, fix public and protected members first (these define the contract), then internal and private members. This order minimizes cascading warnings: fixing a core type's annotations often resolves warnings in its consumers automatically.

Build the project and work through dereference warnings. These are the most common:

| Warning | Meaning | Typical fix |
|---------|---------|-------------|
| CS8602 | Dereference of a possibly null reference | Add a null check, use `?.`, or use a pattern like `if (x is not null)` |
| CS8600 | Converting possible null to non-nullable type | Add `?` to the target type if null is valid, or add a null guard |
| CS8603 | Possible null reference return | Return a non-null value, or change the return type to nullable (`T?`) |
| CS8604 | Possible null reference argument | Check for null before passing, or mark the parameter as nullable |

> ❌ **Do not use `?.` as a quick fix for dereference warnings.** Replacing `obj.Method()` with `obj?.Method()` silently changes runtime behavior — the call is skipped instead of throwing. Only use `?.` when you intentionally want to tolerate null.

> ❌ **Do not sprinkle `!` to silence warnings.** Each `!` is a claim that the value is never null. If that claim is wrong, you have hidden a `NullReferenceException`. Add a null check or make the type nullable instead.

> ⚠️ **Do not add `?` to value types unless you intend to change the runtime type.** For reference types, `?` is metadata-only. For value types (`int`, enums, structs), `?` changes the type to `Nullable<T>`, altering the method signature, binary layout, and boxing behavior.

**Decision flowchart for each warning:**

1. **Is null a valid value here by design?**
   - **Yes** → add `?` to the declaration (make it nullable).
   - **No** → go to step 2.
   - **Unsure** → ask the user before proceeding.
2. **Can you prove the value is never null at this point?**
   - **Yes, with a code path the compiler can't see** → add `!` with a comment explaining why.
   - **Yes, by adding a guard** → add a null check (`if`, `??`, `is not null`).
   - **No** → the type should be nullable (go back to step 1 — the answer is "Yes").

Guidance:

- Prefer explicit null checks (`if`, `is not null`, `??`) over the null-forgiving operator (`!`).
- Use the null-forgiving operator only when you can prove the value is never null but the compiler cannot, and add a comment explaining why.
- When a method legitimately returns null, change the return type to `T?` — do not hide nulls behind a non-nullable signature.
- `Debug.Assert(x != null)` acts as a null-state hint to the compiler just like an `if` check. Use it at the top of a method or block to inform the flow analyzer about invariants and eliminate subsequent `!` operators in that scope.
- If you find yourself adding `!` at every call site of an internal method, consider making that parameter nullable instead. Reserve `!` for cases where the compiler genuinely cannot prove non-nullness.
- For generic methods returning `default` on an unconstrained type parameter (e.g., `FirstOrDefault<T>`), use `[return: MaybeNull] T` rather than `T?`. Writing `T?` on an unconstrained generic changes value-type signatures to `Nullable<T>`, altering the method signature and binary layout. `[return: MaybeNull]` preserves the original signature while communicating that the return may be null for reference types.
- LINQ's `Where(x => x != null)` does not narrow `T?` to `T` — the compiler cannot track nullability through lambdas passed to generic methods. Use a `WhereNotNull()` extension method (see [Helper Extension Methods](#helper-extension-methods) below) or `source.OfType<T>()` to filter nulls with correct type narrowing.

> **Build checkpoint:** After fixing dereference warnings, build and confirm zero CS8602/CS8600/CS8603/CS8604 warnings remain before moving to annotation warnings.

### Step 4: Annotate declarations

Start by deciding the **intended nullability** of each member based on its design purpose — should this parameter accept null? Can this return value ever be null? Annotate accordingly, then address any resulting warnings. Do not let warnings drive your annotations; that leads to over-annotating with `?` or scattering `!` to silence the compiler.

> **When to ask the user:** Do not guess API contracts. Ask the user before: (1) changing a public method's return type to nullable or adding `?` to a public parameter — this changes the API contract consumers depend on; (2) deciding whether a property should be nullable vs. required when the design intent is unclear; (3) choosing between a null check and `!` when you cannot determine from context whether null is a valid state. For internal/private members where the answer is obvious from usage, proceed without asking.

> ❌ **Do not let warnings drive annotations.** Decide the intended nullability of each member first, then annotate. Adding `?` everywhere to make warnings disappear defeats the purpose — callers must then add unnecessary null checks. Adding `!` everywhere hides bugs.

> ⚠️ **Do not remove existing `ArgumentNullException` checks.** A non-nullable parameter annotation is a compile-time hint only — it does not prevent null at runtime. Callers using older C# versions, other .NET languages, reflection, or `!` can still pass null.

> ⚠️ **Flag public API methods missing runtime null validation.** While annotating, check each `public` and `protected` method: if a parameter is non-nullable (`T`, not `T?`), there should be a runtime null check (e.g., `ArgumentNullException.ThrowIfNull(param)` or `if (param is null) throw new ArgumentNullException(...)`). Without one, a null passed at runtime causes a `NullReferenceException` deep in the method body instead of a clear `ArgumentNullException` at the entry point. Flag these to the user and offer to add the guard. This is especially important for libraries where callers may not have NRTs enabled.

> **Methods with defined behavior for null should accept nullable parameters.** If a method handles null input gracefully — returning null, returning a default, or returning a failure result instead of throwing — the parameter should be `T?`, not `T`. The BCL follows this convention: `Path.GetPathRoot(string?)` returns null for null input, while `Path.GetFullPath(string)` throws. Only use a non-nullable parameter when null causes an exception. Marking a parameter as non-nullable when the method actually tolerates null forces callers to add unnecessary null checks before calling.

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

> **Public libraries: track breaking changes.** If the project is a library consumed by others, create a `nullable-breaking-changes.md` file (or equivalent) and record every public API change that could affect consumers. While adding `?` to a reference type is metadata-only and not binary-breaking, it IS source-breaking for consumers who have NRTs enabled — they will get new warnings or errors. Key changes to document:
> - Return types changed from `T` to `T?` (consumers must now handle null)
> - Parameters changed from `T?` to `T` (consumers can no longer pass null)
> - Parameters changed from `T` to `T?` (existing null checks in callers become unnecessary — low impact but worth noting)
> - `?` added to a value type parameter or return (changes `T` to `Nullable<T>` — binary-breaking)
> - New `ArgumentNullException` guards added where none existed
> - Any behavioral changes discovered and fixed during annotation (e.g., a method that silently accepted null now throws)
>
> Present this file to the user for review. It may also serve as the basis for release notes.

Pay special attention to:

- **DTOs and serialization models**: Deserialized properties may be null even if the type says otherwise. Mark them nullable or use `required` / `[JsonRequired]`.
- **Event handlers and delegates**: The pattern `EventHandler? handler = SomeEvent; handler?.Invoke(...)` is idiomatic.
- **Struct reference-type fields**: Reference-type fields in structs are null when using `default(T)`. If `default` is valid usage for the struct, those fields must be nullable. If `default` is never expected (the struct is only created by specific APIs), keep them non-nullable to avoid burdening every consumer with unnecessary null checks.
- **Post-Dispose state**: If a field or property is non-null for the entire useful lifetime of the object but may become null after `Dispose`, keep it non-nullable. Using an object after disposal is a contract violation — do not weaken annotations for that case.
- **Overrides and interface implementations**: An override can return a stricter (non-nullable) type than the base method declares. If your implementation never returns null but the base/interface returns `T?`, you can declare the override as returning `T`. Parameter types must match the base exactly.
- **Widely-overridden virtual return types**: For virtual/abstract methods that many classes override, consider whether existing overrides actually return null. If they commonly do (like `Object.ToString()`), annotate the return as `T?` — callers need to know. If null overrides are vanishingly rare (like `Exception.Message`), annotate as `T`. When in doubt for broadly overridden virtuals, prefer `T?`.
- **`IEquatable<T>` and `IComparable<T>`**: Reference types should implement `IEquatable<T?>` and `IComparable<T?>` (with nullable `T`), because callers commonly pass null to `Equals` and `CompareTo`.
- **`Equals(object?)` overrides**: Add `[NotNullWhen(true)]` to the parameter of `Equals(object? obj)` overrides — if `Equals` returns `true`, the argument is guaranteed non-null. This lets callers skip redundant null checks after an equality test.

> **Build checkpoint:** After annotating declarations, build and confirm zero CS8618/CS8625/CS8601 warnings remain before moving to nullable attributes.

### Step 5: Apply nullable attributes for advanced scenarios

When a simple `?` annotation cannot express the null contract, use attributes from `System.Diagnostics.CodeAnalysis`:

| Attribute | Use case |
|-----------|----------|
| `[NotNullWhen(true/false)]` | `TryGet` or `IsNullOrEmpty` patterns — the argument is not null when the method returns the specified bool. For `Try` methods with a **non-generic** out parameter, declare the parameter nullable and use `[NotNullWhen(true)] out MyType? result` — it is `null` on failure and non-null on success. Also add to `Equals(object? obj)` overrides to indicate the argument is non-null when returning `true` |
| `[MaybeNullWhen(true/false)]` | For `Try` methods with a **generic** out parameter, keep the parameter non-nullable and use `[MaybeNullWhen(false)] out T result` — the value may be `default` (null for reference types) on failure. Using `[NotNullWhen]` with `T?` here would change value-type signatures to `Nullable<T>` |
| `[NotNull]` | A nullable parameter is guaranteed non-null when the method returns (e.g., a `ThrowIfNull` helper) |
| `[MaybeNull]` | A non-nullable generic return might be `default` (null). Rare in practice — prefer `T?` when possible. Reserve for cases like `AsyncLocal<T>.Value` where `T?` is wrong because setting to null is invalid when `T` is non-nullable |
| `[AllowNull]` | A non-nullable property setter accepts null (e.g., falls back to a default value) |
| `[DisallowNull]` | A nullable property should never be explicitly set to null |
| `[MemberNotNull(nameof(...))]` | A helper method guarantees that specific members are non-null after it returns. When initializing multiple fields, prefer multiple `[MemberNotNull("field1")]` `[MemberNotNull("field2")]` attributes over one `[MemberNotNull("field1", "field2")]` — the `params` overload is not CLS-compliant |
| `[NotNullIfNotNull("paramName")]` | The return is non-null if the named parameter is non-null |
| `[DoesNotReturn]` | The method always throws — code after the call is unreachable |

Add `using System.Diagnostics.CodeAnalysis;` where needed.

> **Caution:** The compiler does not warn when nullable attributes are misapplied — for example, `[DisallowNull]` on an already non-nullable parameter or `[MaybeNull]` on a by-value input parameter (not `ref`/`out`) are silently ignored. Verify each attribute is placed where it has an effect.

> **Build checkpoint:** After applying nullable attributes, build to verify the attributes resolved the targeted warnings and did not introduce new ones.

### Step 6: Clean up suppressions

> **Optional:** Re-run `scripts/Scan-NullableReadiness.ps1` to get current counts of `#nullable disable` directives, `!` operators, and `#pragma warning disable CS86xx` suppressions across the project.

1. Search for any `#nullable disable` directives or `!` operators that were added as temporary workarounds.
2. For each one, determine whether the suppression is still needed.
3. Remove suppressions that are no longer necessary. For any that remain, add a comment explaining why.
4. Search for `#pragma warning disable CS86` to find suppressed nullable warnings and evaluate whether the underlying issue can be fixed instead.

> **Build checkpoint:** After removing suppressions, build again — removing a `#nullable disable` or `!` may surface new warnings that need fixing.

### Step 7: Validate

1. Build the project and confirm zero nullable warnings.
2. Add `<WarningsAsErrors>nullable</WarningsAsErrors>` to the project file (or `Directory.Build.props` for the whole repo) to permanently prevent nullable regressions. This is the project-file equivalent of `dotnet build /warnaserror:nullable`.
3. Run existing tests to confirm no regressions.
4. If the project is a library, inspect the public API surface to verify that nullable annotations match the intended contracts (parameters that accept null are `T?`, parameters that reject null are `T`).

> **Verify before claiming the migration is complete.** Zero warnings alone does not mean the migration is correct. Before reporting success: (1) spot-check public API signatures — confirm `?` annotations match actual design intent, not just compiler silence; (2) verify no `?.` operators were added that change runtime behavior (search for `?.` in the diff); (3) confirm no `ArgumentNullException` checks were removed; (4) check that `!` operators are rare and each has a justifying comment.

## Validation

- [ ] Project file(s) contain `<Nullable>enable</Nullable>` (or `#nullable enable` per-file for file-by-file strategy)
- [ ] Build produces zero CS86xx warnings
- [ ] `<WarningsAsErrors>nullable</WarningsAsErrors>` added to project file to prevent regressions
- [ ] Tests pass with no regressions
- [ ] No `#nullable disable` directives remain unless justified with a comment
- [ ] Null-forgiving operators (`!`) are rare, each with a justifying comment
- [ ] Public API signatures accurately reflect null contracts
- [ ] For public libraries: breaking changes documented in `nullable-breaking-changes.md` and reviewed by the user

### Code review checklist

Nullable migration changes require broader review than a typical diff:

1. **Verify no behavior changes**: confirm that `?` and `!` are the only additions — no accidental `?.`, no removed null checks, no new branches. The generated IL should be unchanged except for nullable metadata.
2. **Review explicit annotation changes**: for every `?` added to a parameter or return type, confirm it matches the intended design. Does the method really accept null? Can it really return null?
3. **Review unchanged APIs in scope**: enabling `<Nullable>enable</Nullable>` implicitly makes every unannotated reference type in that scope non-nullable. Scan unchanged public members for parameters that actually do accept null but were not annotated.

## Breaking Changes from NRT Annotations (Libraries)

For libraries, see [references/breaking-changes.md](references/breaking-changes.md) — NRT annotations are part of the public API contract and incorrect annotations are source-breaking changes for consumers.

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Sprinkling `!` everywhere to silence warnings | The null-forgiving operator hides bugs. Add null checks or change the type to nullable instead |
| Marking everything `T?` to eliminate warnings quickly | Over-annotating with `?` defeats the purpose — callers must add unnecessary null checks. Only use `?` when null is a valid value |
| Constructor does not initialize all non-nullable members | Initialize fields and properties in every constructor, use `required` (C# 11+), or make the member nullable |
| Serialization (JSON, XML) bypasses constructors | Deserialized properties can be null regardless of the declared type. Mark DTO properties as nullable or use `required` / `init` with validation |
| Generated code produces warnings | Generated files are excluded from nullable analysis automatically if they contain `<auto-generated>` comments. If warnings persist, add `#nullable disable` at the top of the generated file or configure `.editorconfig` with `generated_code = true` |
| Multi-target projects and older TFMs | NRT annotations compile on older TFMs (e.g., .NET Standard 2.0) with C# 8.0+, but nullable attributes like `[NotNullWhen]` may not exist. Use a polyfill package such as `Nullable` from NuGet, or define the attributes internally |
| Warnings reappear after upgrading a dependency | The dependency added nullable annotations. This is expected and beneficial — fix the new warnings as in Steps 3–5 |
| Accidentally changing behavior while annotating | Adding `?` to a type or `!` to an expression is metadata-only and does not change generated IL. But replacing `obj.Method()` with `obj?.Method()` (null-conditional) changes runtime behavior — the call is silently skipped instead of throwing. Only use `?.` when you intentionally want to tolerate null, not as a quick fix for a warning |
| Adding `?` to a value type (enum, struct) | For reference types, `?` is a metadata annotation with no runtime effect. For value types like `int` or an enum, `?` changes the type to `Nullable<T>`, altering the method signature, binary layout, and boxing behavior. Double-check that you are only adding `?` to reference types unless you truly intend to make a value type nullable |
| Removing existing null argument validation | Do not remove `ArgumentNullException` checks just because a parameter is now non-nullable. Nullable annotations are a compile-time feature only — they do not prevent null at runtime. Callers using older C# versions, other .NET languages, reflection, `dynamic`, or the `!` operator can still pass null. Runtime validation on public APIs remains essential for correctness and security |
| `var` is always considered nullable | The compiler treats `var` as nullable regardless of the assigned expression. Flow analysis determines the actual null-state, but the declared type is `T?`. This can surprise developers who expect `var x = GetNonNullValue()` to behave identically to `string x = ...`. If precise nullability matters, use an explicit type instead of `var` |
| Consuming unannotated (nullable-oblivious) libraries | When a dependency has not opted into nullable annotations, the compiler treats all its types as "oblivious" — you get no warnings for dereferencing or assigning null. This gives a false sense of safety. Treat return values from oblivious APIs as potentially null, especially for methods that could conceptually return null (dictionary lookups, `FirstOrDefault`-style calls). Upgrade dependencies or wrap calls when possible |

## Entity Framework Core Considerations

If the project uses EF Core, see [references/ef-core.md](references/ef-core.md) — enabling NRTs can change database schema inference and migration output.

## ASP.NET Core Considerations

If the project uses ASP.NET Core, see [references/aspnet-core.md](references/aspnet-core.md) — enabling NRTs can change MVC model validation and JSON serialization behavior.

## Helper Extension Methods

See [references/helper-extensions.md](references/helper-extensions.md) for `WhereNotNull` and other helper methods to add during migration.

## More Info

- [Nullable reference types](https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references) — overview of the feature, nullable contexts, and compiler analysis
- [Nullable reference types (C# reference)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/nullable-reference-types) — language reference for nullable annotation and warning contexts
- [Nullable migration strategies](https://learn.microsoft.com/en-us/dotnet/csharp/nullable-migration-strategies)
- [Embracing Nullable Reference Types](https://devblogs.microsoft.com/dotnet/embracing-nullable-reference-types/) — Mads Torgersen's guidance on adoption timing and ecosystem considerations
- [Resolve nullable warnings](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/nullable-warnings)
- [Attributes for nullable static analysis](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis)
- [! (null-forgiving) operator](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/null-forgiving) — language reference for the operator and when to use it
- [EF Core and nullable reference types](https://learn.microsoft.com/en-us/ef/core/miscellaneous/nullable-reference-types)
- [.NET Runtime nullable annotation guidelines](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/api-guidelines/nullability.md) — the annotation principles used when annotating the .NET libraries themselves
