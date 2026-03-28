---
name: migrate-nullable-references
description: >
  Enable nullable reference types in a C# project and systematically resolve all warnings.
  USE FOR: adopting NRTs in existing codebases, file-by-file or project-wide migration,
  fixing CS8602/CS8618/CS86xx warnings, annotating APIs for nullability, cleaning up
  null-forgiving operators, upgrading dependencies with new nullable annotations.
  DO NOT USE FOR: projects already fully migrated with zero warnings (unless auditing
  suppressions), fixing a handful of nullable warnings in code that already has NRTs enabled,
  suppressing warnings without fixing them, C# 7.3 or earlier projects.
  INVOKES: Get-NullableReadiness.ps1 scanner script.
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

> 🛑 **Zero runtime behavior changes.** NRT migration is strictly a metadata and annotation exercise. The generated IL must not change — no new branches, no new null checks, no changed control flow, no added or removed method calls. The only acceptable changes are nullable annotations (`?`), nullable attributes (`[NotNullWhen]`, etc.), `!` operators (metadata-only), and `#nullable` directives. If you discover a missing runtime null guard or a latent bug during migration, **do not fix it inline**. Instead, offer to insert a `// TODO: Consider adding ArgumentNullException.ThrowIfNull(param)` comment at the site so the user can address it as a separate change. Never mix behavioral fixes into an annotation commit.

> **Commit strategy:** Commit at each logical boundary — after enabling `<Nullable>` (Step 2), after fixing dereference warnings (Step 3), after annotating declarations (Step 4), after applying nullable attributes (Step 5), and after cleaning up suppressions (Step 6). This keeps each commit focused and reviewable, and prevents losing work if a later step reveals a design issue that requires rethinking. For file-by-file migrations, commit each file or batch of related files individually.

### Step 1: Evaluate readiness

> **Optional:** Run `scripts/Get-NullableReadiness.ps1 -Path <project-or-solution>` to automate the checks below. The script reports `<Nullable>`, `<LangVersion>`, `<TargetFramework>`, `<WarningsAsErrors>` settings and counts `#nullable disable` directives, `!` operators, and `#pragma warning disable CS86xx` suppressions. Use `-Json` for machine-readable output.

1. Identify how the project is built and tested. Look for build scripts (e.g., `build.cmd`, `build.sh`, `Makefile`), a `.sln` file, or individual `.csproj` files. If the repo uses a custom build script, use it instead of `dotnet build` throughout this workflow.
2. Run `dotnet --version` to confirm the SDK is installed. Nullable reference types (NRTs) require C# 8.0+ (`.NET Core 3.0` / `.NET Standard 2.1` or later).
3. Open the `.csproj` (or `Directory.Build.props` if properties are set at the repo level) and check the `<LangVersion>` and `<TargetFramework>`. If the project multi-targets, note all TFMs.

> **Stop if the language version or target framework is insufficient.** If `<LangVersion>` is below 8.0, or the project targets a framework that defaults to C# 7.x (e.g., `.NET Framework 4.x` without an explicit `<LangVersion>`), NRTs cannot be enabled as-is. Inform the user explicitly: explain what needs to change (set `<LangVersion>8.0</LangVersion>` or higher, or retarget to `.NET Core 3.0+` / `.NET 5+`), and ask whether they want to make that update and continue, or abort the migration. Do not silently proceed or assume the update is acceptable.
4. Check whether `<Nullable>` is already set. If it is set to `enable`, skip to Step 5 to audit remaining warnings.
5. Determine the project type — this shapes annotation priorities throughout the migration:
   - **Library**: Focus on public API contracts first. Every `?` on a public parameter or return type is a contract change that consumers depend on. Be precise and conservative.
   - **Application (web, console, desktop)**: Focus on null safety at boundaries — deserialization, database queries, user input, external API responses. Internal plumbing can be annotated more liberally.
   - **Test project**: Lower priority for annotation precision. Use `!` more freely on test setup and assertions where null is never expected. Focus on ensuring test code compiles cleanly.

### Step 2: Choose a rollout strategy

Pick one of the following strategies based on codebase size and activity level. Recommend the strategy to the user and confirm before proceeding.

> **Multi-project solutions:** Migrate in dependency order — shared libraries and core projects first, then projects that consume them. Annotating a dependency first eliminates cascading warnings in its consumers and prevents doing work twice.

Regardless of strategy, **start at the center and work outward**:begin with core domain models, DTOs, and shared utility types that have few dependencies but are used widely. Annotating these first eliminates cascading warnings across the codebase and gives the biggest return on effort. Then move on to higher-level services, controllers, and UI code that depend on the core types. This approach minimizes the number of warnings at each step and prevents getting overwhelmed by a flood of warnings from a large project-wide enable. Prefer to create at least one PR per project, or per layer, to keep changesets reviewable and focused. If there are relatively few annotations needed, a single project-wide enable and single PR may be appropriate.

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
| CS8602 | Dereference of a possibly null reference | Prefer annotation-only fixes: make the upstream type nullable (`T?`) if null is valid, or use `!` if you can verify the value is never null at this point. Adding a null check or `?.` changes runtime behavior — reserve those for a separate commit (see zero-behavior-change rule above) |
| CS8600 | Converting possible null to non-nullable type | Add `?` to the target type if null is valid, or use `!` if you can verify the value is never null. Adding a null guard changes runtime behavior |
| CS8603 | Possible null reference return | Change the return type to nullable (`T?`) if the method can genuinely return null. **Do not suppress with `!` if the method can genuinely return null** — fix the return type instead. This is the single most important rule in NRT migration: a non-nullable return type is a promise to every caller that null will never be returned |
| CS8604 | Possible null reference argument | Mark the parameter as nullable if null is valid, or use `!` if the argument is verifiably non-null. Adding a null check before passing changes runtime behavior |

> ❌ **Do not use `?.` as a quick fix for dereference warnings.** Replacing `obj.Method()` with `obj?.Method()` silently changes runtime behavior — the call is skipped instead of throwing. Only use `?.` when you intentionally want to tolerate null.

> ❌ **Do not sprinkle `!` to silence warnings.** Each `!` is a claim that the value is never null. If that claim is wrong, you have hidden a `NullReferenceException`. Add a null check or make the type nullable instead.

> ❌ **Never use `return null!` to keep a return type non-nullable.** If a method returns `null`, the return type must be `T?`. Writing `return null!` hides a null behind a non-nullable signature — callers trust the signature, skip null checks, and get `NullReferenceException` at runtime. This applies to `null!`, `default!`, and any cast that makes the compiler accept null in a non-nullable position. The only acceptable use of `!` on a return value is when the value is **provably never null** but the compiler cannot see why.

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
- Guard clause libraries (e.g., Ardalis.GuardClauses, Dawn.Guard) often decorate parameters with `[NotNull]`, which narrows null state after the guard call. After `Guard.Against.NullOrEmpty(value, nameof(value))`, the compiler already narrows `string?` to `string` — do not add a redundant `!` at the subsequent assignment. Check whether the guard method uses `[NotNull]` before assuming the compiler needs help.
- When a method legitimately returns null, change the return type to `T?` — do not hide nulls behind a non-nullable signature.
- `Debug.Assert(x != null)` acts as a null-state hint to the compiler just like an `if` check. Use it at the top of a method or block to inform the flow analyzer about invariants and eliminate subsequent `!` operators in that scope. Note: `Debug.Assert` informs the compiler but is stripped from Release builds — it does not protect against null at runtime. For public API boundaries, prefer an explicit null check or `ArgumentNullException`.
- If you find yourself adding `!` at every call site of an internal method, consider making that parameter nullable instead. Reserve `!` for cases where the compiler genuinely cannot prove non-nullness.
- When a boolean-returning helper method's result guarantees a nullable parameter is non-null (e.g., `if (IsValid(x))` implies `x != null`), prefer adding `[NotNullWhen(true)]` to the helper's parameter over using `!` at every call site. This is a metadata-only change (no behavior change) that eliminates `!` operators downstream while giving the compiler real flow information.
- For fields that are always set after construction (e.g., by a framework, an `Init()` method, or a builder pattern), prefer `= null!` on the field declaration over adding `!` at every use site. A field accessed 50 times should have one `= null!`, not fifty `field!` assertions. This keeps the field non-nullable in the type system while acknowledging the late initialization. Pair with `[MemberNotNull]` on the initializing method when possible.
- For generic methods returning `default` on an unconstrained type parameter (e.g., `FirstOrDefault<T>`), use `[return: MaybeNull] T` rather than `T?`. Writing `T?` on an unconstrained generic changes value-type signatures to `Nullable<T>`, altering the method signature and binary layout. `[return: MaybeNull]` preserves the original signature while communicating that the return may be null for reference types.
- LINQ's `Where(x => x != null)` does not narrow `T?` to `T` — the compiler cannot track nullability through lambdas passed to generic methods. Use `source.OfType<T>()` to filter nulls with correct type narrowing.

> **Build checkpoint:** After fixing dereference warnings, build and confirm zero CS8602/CS8600/CS8603/CS8604 warnings remain before moving to annotation warnings.

### Step 4: Annotate declarations

Start by deciding the **intended nullability** of each member based on its design purpose — should this parameter accept null? Can this return value ever be null? Annotate accordingly, then address any resulting warnings. Do not let warnings drive your annotations; that leads to over-annotating with `?` or scattering `!` to silence the compiler.

> **When to ask the user:** Do not guess API contracts. Never infer nullability intent from usage frequency or naming conventions alone — if intent is not explicit in code or documentation, ask the user. Specifically, ask before: (1) changing a public method's return type to nullable or adding `?` to a public parameter — this changes the API contract consumers depend on; (2) deciding whether a property should be nullable vs. required when the design intent is unclear; (3) choosing between a null check and `!` when you cannot determine from context whether null is a valid state. For internal/private members where the answer is obvious from usage, proceed without asking.

> ❌ **Do not let warnings drive annotations.** Decide the intended nullability of each member first, then annotate. Adding `?` everywhere to make warnings disappear defeats the purpose — callers must then add unnecessary null checks. Adding `!` everywhere hides bugs.

> ⚠️ **Return types must reflect semantic nullability, not just compiler satisfaction.** A common mistake is removing `?` from a return type because the implementation uses `default!` or a cast that satisfies the compiler. If the method can return null by design, its return type must be nullable — regardless of whether the compiler warns. Key patterns:
> - Methods named `*OrDefault` (`FirstOrDefault`, `SingleOrDefault`, `FindOrDefault`) → return type must be nullable (`T?`, `object?`, `dynamic?`) because "or default" means "or null" for reference types.
> - `ExecuteScalar` and similar database methods → return type must be `object?` because the result can be `DBNull.Value` or null when no rows match.
> - `Find`, `TryGet*` (out parameter), and lookup methods → return type should be nullable when the item may not exist.
> - Any method documented or designed to return null on failure, not-found, or empty-input → nullable return type.
>
> The compiler cannot catch a *missing* `?` on a return type when the implementation hides null behind `!` or `default!`. This makes the annotation wrong for consumers — they trust the non-nullable signature and skip null checks, leading to `NullReferenceException` at runtime.

> ⚠️ **Do not remove existing `ArgumentNullException` checks.** A non-nullable parameter annotation is a compile-time hint only — it does not prevent null at runtime. Callers using older C# versions, other .NET languages, reflection, or `!` can still pass null.

> ⚠️ **Flag public API methods missing runtime null validation — but do not add checks.** While annotating, check each `public` and `protected` method: if a parameter is non-nullable (`T`, not `T?`), there should be a runtime null check (e.g., `ArgumentNullException.ThrowIfNull(param)` or `if (param is null) throw new ArgumentNullException(...)`). Without one, a null passed at runtime causes a `NullReferenceException` deep in the method body instead of a clear `ArgumentNullException` at the entry point. Adding a null guard is a runtime behavior change and must not be part of the NRT migration. Instead, ask the user whether they want a `// TODO: Consider adding ArgumentNullException.ThrowIfNull(param)` comment inserted at the site. This is especially important for libraries where callers may not have NRTs enabled.

> **Methods with defined behavior for null should accept nullable parameters.** If a method handles null input gracefully — returning null, returning a default, or returning a failure result instead of throwing — the parameter should be `T?`, not `T`. The BCL follows this convention: `Path.GetPathRoot(string?)` returns null for null input, while `Path.GetFullPath(string)` throws. Only use a non-nullable parameter when null causes an exception. Marking a parameter as non-nullable when the method actually tolerates null forces callers to add unnecessary null checks before calling.
>
> **Gray areas:** When a parameter is neither validated, sanitized, nor documented for null, consider: (1) Is null ever passed in your own codebase? If yes → nullable. (2) Is null likely used as a "default" or no-op placeholder by callers? If yes → nullable. (3) Do similar methods in the same area accept null? If yes → nullable for consistency. (4) If the method is largely oblivious to null and just happens to work, but null makes no semantic sense for the API's purpose → non-nullable. When in doubt between nullable and non-nullable for a parameter, prefer nullable — it is safer and can be tightened later.

After dereference warnings are resolved, address annotation warnings:

| Warning | Meaning | Typical fix |
|---------|---------|-------------|
| CS8618 | Non-nullable field/property not initialized in constructor | Initialize the member, make it nullable (`?`), or use `required` (C# 11+). For fields that are always set after construction but outside the constructor (e.g., by a framework lifecycle method, an `Init()` call, or a builder pattern), use `= null!` to declare intent while keeping the field non-nullable at every use site. If a helper method initializes fields, decorate it with `[MemberNotNull(nameof(field))]` so the compiler knows the field is non-null after the call |
| CS8625 | Cannot convert null literal to non-nullable type | Make the target nullable or provide a non-null value |
| CS8601 | Possible null reference assignment | Same techniques as CS8600 |

For each type, decide: **should this member ever be null?**

- **Yes** → add `?` to its declaration.
- **No** → ensure it is initialized in every constructor path, or mark it `required` (C# 11+).
- **No, but it is set after the constructor** (e.g., by a framework method, a builder, or a two-phase init pattern) → use `= null!` on the field declaration. This keeps the field's type non-nullable everywhere it is used, while telling the compiler "I guarantee this will be set before access." This is far preferable to adding `!` at every use site — a field accessed 50 times would need 50 `!` operators instead of one `= null!`. If the initialization is done by a specific method, also consider `[MemberNotNull(nameof(field))]` on that method.

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

- **DTOs vs domain models**: Apply different nullability strategies depending on the role of the class. **DTOs and serialization models** cross trust boundaries (JSON, forms, external APIs) — their properties should be nullable by default unless enforced by the serializer, because deserialized data can always be null regardless of the declared type. Use `required` (C# 11+), `[JsonRequired]` (.NET 7+), or runtime validation to enforce non-null constraints. **Domain models** represent internal invariants — prefer non-nullable properties with constructor enforcement, making invalid state unrepresentable. This distinction is where migrations most often go wrong: treating a DTO as a domain model leads to runtime `NullReferenceException`; treating a domain model as a DTO leads to unnecessary null checks everywhere.
- **Event handlers and delegates**: The pattern `EventHandler? handler = SomeEvent; handler?.Invoke(...)` is idiomatic.
- **Struct reference-type fields**: Reference-type fields in structs are null when using `default(T)`. If `default` is valid usage for the struct, those fields must be nullable. If `default` is never expected (the struct is only created by specific APIs), keep them non-nullable to avoid burdening every consumer with unnecessary null checks.
- **Post-Dispose state**: If a field or property is non-null for the entire useful lifetime of the object but may become null after `Dispose`, keep it non-nullable. Using an object after disposal is a contract violation — do not weaken annotations for that case.
- **Overrides and interface implementations**: An override can return a stricter (non-nullable) type than the base method declares. If your implementation never returns null but the base/interface returns `T?`, you can declare the override as returning `T`. Parameter types must match the base exactly.
- **Widely-overridden virtual return types**: For virtual/abstract methods that many classes override, consider whether existing overrides actually return null. If they commonly do (like `Object.ToString()`), annotate the return as `T?` — callers need to know. If null overrides are vanishingly rare (like `Exception.Message`), annotate as `T`. When in doubt for broadly overridden virtuals, prefer `T?`.
- **`IEquatable<T>` and `IComparable<T>`**: Reference types should implement `IEquatable<T?>` and `IComparable<T?>` (with nullable `T`), because callers commonly pass null to `Equals` and `CompareTo`.
- **`Equals(object?)` overrides**: Add `[NotNullWhen(true)]` to the parameter of `Equals(object? obj)` overrides — if `Equals` returns `true`, the argument is guaranteed non-null. This lets callers skip redundant null checks after an equality test.

> **Build checkpoint:** After annotating declarations, build and confirm zero CS8618/CS8625/CS8601 warnings remain before moving to nullable attributes.

### Step 5: Apply nullable attributes for advanced scenarios

When a simple `?` annotation cannot express the null contract, apply attributes from `System.Diagnostics.CodeAnalysis` — see [references/nullable-attributes.md](references/nullable-attributes.md) for the full attribute table (`[NotNullWhen]`, `[MaybeNullWhen]`, `[MemberNotNull]`, `[AllowNull]`, `[DisallowNull]`, `[DoesNotReturn]`, etc.) with usage guidance for each.

> **Build checkpoint:** After applying nullable attributes, build to verify the attributes resolved the targeted warnings and did not introduce new ones.

### Step 6: Clean up suppressions

> **Optional:** Re-run `scripts/Get-NullableReadiness.ps1` to get current counts of `#nullable disable` directives, `!` operators, and `#pragma warning disable CS86xx` suppressions across the project.

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
| Serialization bypasses constructors — non-nullable ≠ runtime safety | Serializers create objects without calling constructors, so non-nullable DTO properties can still be null at runtime. See "DTOs vs domain models" in Step 4 for detailed guidance |
| Generated code produces warnings | Generated files are excluded from nullable analysis automatically if they contain `<auto-generated>` comments. If warnings persist, add `#nullable disable` at the top of the generated file or configure `.editorconfig` with `generated_code = true` |
| Multi-target projects and older TFMs | NRT annotations compile on older TFMs (e.g., .NET Standard 2.0) with C# 8.0+, but nullable attributes like `[NotNullWhen]` may not exist. Use a polyfill package such as `Nullable` from NuGet, or define the attributes internally |
| Warnings reappear after upgrading a dependency | The dependency added nullable annotations. This is expected and beneficial — fix the new warnings as in Steps 3–5 |
| Accidentally changing behavior while annotating | Adding `?` to a type or `!` to an expression is metadata-only and does not change generated IL. But replacing `obj.Method()` with `obj?.Method()` (null-conditional) changes runtime behavior — the call is silently skipped instead of throwing. Only use `?.` when you intentionally want to tolerate null, not as a quick fix for a warning |
| Adding `?` to a value type (enum, struct) | For reference types, `?` is a metadata annotation with no runtime effect. For value types like `int` or an enum, `?` changes the type to `Nullable<T>`, altering the method signature, binary layout, and boxing behavior. Double-check that you are only adding `?` to reference types unless you truly intend to make a value type nullable |
| Removing existing null argument validation | Non-nullable annotations are compile-time only — callers can still pass null at runtime. Keep existing `ArgumentNullException` checks. See Step 4 for details |
| `var` infers nullability from the assigned expression | When using `var`, the inferred type includes nullability from the assigned expression, which can be surprising compared to explicitly declaring `T` vs `T?`. Flow analysis determines the actual null-state from that point forward, but the inferred declaration type may carry nullability you did not expect. If precise nullability at the declaration matters, use an explicit type instead of `var` |
| Consuming unannotated (nullable-oblivious) libraries | When a dependency has not opted into nullable annotations, the compiler treats all its types as "oblivious" — you get no warnings for dereferencing or assigning null. This gives a false sense of safety. Treat return values from oblivious APIs as potentially null, especially for methods that could conceptually return null (dictionary lookups, `FirstOrDefault`-style calls). Upgrade dependencies or wrap calls when possible |

## Entity Framework Core Considerations

If the project uses EF Core, see [references/ef-core.md](references/ef-core.md) — enabling NRTs can change database schema inference and migration output.

## ASP.NET Core Considerations

If the project uses ASP.NET Core, see [references/aspnet-core.md](references/aspnet-core.md) — enabling NRTs can change MVC model validation and JSON serialization behavior.

## More Info

- [Nullable reference types](https://learn.microsoft.com/dotnet/csharp/nullable-references) — overview of the feature, nullable contexts, and compiler analysis
- [Nullable reference types (C# reference)](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/nullable-reference-types) — language reference for nullable annotation and warning contexts
- [Nullable migration strategies](https://learn.microsoft.com/dotnet/csharp/nullable-migration-strategies)
- [Embracing Nullable Reference Types](https://devblogs.microsoft.com/dotnet/embracing-nullable-reference-types/) — Mads Torgersen's guidance on adoption timing and ecosystem considerations
- [Resolve nullable warnings](https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/nullable-warnings)
- [Attributes for nullable static analysis](https://learn.microsoft.com/dotnet/csharp/language-reference/attributes/nullable-analysis)
- [! (null-forgiving) operator](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/null-forgiving) — language reference for the operator and when to use it
- [EF Core and nullable reference types](https://learn.microsoft.com/ef/core/miscellaneous/nullable-reference-types)
- [.NET Runtime nullable annotation guidelines](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/api-guidelines/nullability.md) — the annotation principles used when annotating the .NET libraries themselves
