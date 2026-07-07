---
name: csharp-refactoring
description: "Performs safe, behavior-preserving refactoring of C#/.NET code, verified with build, tests, and analyzers. Use when the user wants to refactor, rename, restructure, clean up, modernize, or reorganize C# code WITHOUT changing behavior: rename a symbol/type/file across a solution; move a type or static members to another file/namespace/project; extract a method, interface, or base class; pull members up; inline a method or local; split a large class/file; consolidate or de-duplicate copy-pasted code; sync namespaces to folders; modernize to current C# idioms (file-scoped namespaces, primary constructors, collection expressions, target-typed new, pattern matching); or enable nullable reference annotations. Prefers Roslyn-backed edits over text find/replace, and defers .NET compatibility hazards to the companion dotnet-breaking-changes skill. DO NOT USE FOR: adding features, fixing bugs, writing new tests, upgrading frameworks or NuGet versions (use dotnet-upgrade), or formatting-only passes (use dotnet format)."
license: MIT
---

# C# Refactoring (behavior-preserving)

Refactor C#/.NET code so the structure improves but the observable behavior does **not** change. Every
refactor is a sequence of small, named, Roslyn-aware transformations, each followed by a build/test
gate. If the gate fails, stop and revert — a refactor that changes behavior is a bug, not a refactor.

## Operation catalog (what "refactoring" means in C#)

The canonical C# refactoring operations, aligned with Roslyn's IDE refactoring providers:

- **Rename** a symbol / type / file
- **Move** a type / member / file (to another file, namespace, or project)
- **Consolidate / de-duplicate** copy-pasted code
- **Modernize / simplify** to current C# idioms (the repo's analyzers decide the idiom, not taste)
- **Split** a large class / file / assembly
- **Extract** a method / class / interface
- **Enable nullable** reference annotations
- **Inline** a method / local / constant
- **Pull up / push down** a member
- **Sync namespace** to folder

Each is a single named operation; compose them one step at a time (see Procedure). For the Roslyn
provider mapping and representative real PRs, see
[references/operation-catalog.md](references/operation-catalog.md).

## Tooling — C# LSP (Roslyn language server)

A headless CLI agent usually can't invoke IDE code actions, but it **can** get Roslyn-quality
_semantic navigation_ from the C# language server the [`dotnet` plugin in `dotnet/skills`](https://github.com/dotnet/skills/blob/main/plugins/dotnet/lsp.json)
declares. It launches through the .NET CLI (`dnx roslyn-language-server --yes --prerelease -- --stdio
--autoLoadProjects`) over `.cs`, `.razor`, and `.cshtml` files (prerequisite: a .NET 10 SDK with
`dotnet` on `PATH`).

When the LSP is available, prefer its binding-aware operations over textual grep/glob for every
reference-finding step in a refactor:

- Where a symbol is defined → **goToDefinition**
- All _binding_ references to a symbol → **findReferences** (the reliable rename/move safety net)
- What calls a method → **incomingCalls**
- Find symbols by name across the workspace → **workspaceSymbol**
- A symbol's type / signature / docs → **hover**

These are exact, binding-aware answers — unlike grep they don't match comments, string literals, or
unrelated overloads. Reach for the LSP first; fall back to grep only when it is unavailable.

## .NET compatibility hazards — inspect first, then load `dotnet-breaking-changes`

Behavior-preserving edits fail in _.NET-specific_ ways a green test run won't catch. Before you edit,
**search the repo** for the surfaces that govern the symbol — don't assume or recite:

- Public-API gate: `PublicAPI.Shipped/Unshipped.txt` (PublicApiAnalyzers) and/or `ApiCompat` /
  `<EnablePackageValidation>` — these are _not_ interchangeable.
- `<TargetFrameworks>` and `#if` / platform branches.
- `partial` declarations and generated (`*.g.cs`) files.
- `InternalsVisibleTo` friend/test assemblies.

The four bullets above (and the reminders below) are the **floor**: apply them even if the guide skill
is not installed. When it _is_ available, **load the `dotnet-breaking-changes` skill** for the full
per-surface playbook (it applies to any change, not just refactors, and holds the depth on each
surface); it _adds_ compatibility analysis but does **not** replace this skill's safety contract.
Refactor-specific reminders: move a public type across assemblies via a `[TypeForwardedTo]` forwarder
(a _rename_ needs an `[Obsolete]` shim, not a forwarder); include _every_ `partial` declaration in a
rename/move; edit the generator input, not generated output; and treat friend-assembly `internal`
members with public-API care.

## Stop and escalate (do not silently proceed)

Halt and ask the user before continuing when:

- The **baseline is red** (build or tests already failing) — you can't prove you preserved behavior.
- A **public/shipped API** would change and you cannot verify compatibility (no type-forwarder/shim
  path, and no PublicApiAnalyzers/ApiCompat/package-validation gate available to catch a break).
- The request is **not actually behavior-preserving** — it asks you to upgrade a framework/package,
  add a feature, or fix a bug. Do **not** carry it out under this skill, and **never label such a
  change "behavior-preserving."** Say it is out of scope and redirect (upgrades → `dotnet-upgrade`;
  features/fixes → normal dev flow). If a refactor is genuinely a prerequisite, do only that, as a
  separate step, and stop.
- The edit touches **generated, designer, or migration files** (`*.g.cs`, `*.Designer.cs`, EF
  migrations, source-generator output) — hand-edits there are **overwritten on the next build**;
  change the source of generation (template/generator input), not the output.
- Semantic equivalence depends on **runtime behavior not covered by tests** (reflection, DI wiring,
  serialization, `dynamic`, P/Invoke) — flag the risk; tests alone won't catch a regression.
- The operation can't be done with any tool on the fallback ladder and would require a wide,
  unverifiable text replace.

## When to use

- "Rename this method/type everywhere safely" / "rename across the solution"
- "Extract this block into a method" / "extract an interface from this class"
- "Move this type into its own file / into project X / into namespace Y"
- "This class is too big — split it" / "consolidate these two near-identical implementations"
- "Modernize this file to current C#" / "use file-scoped namespaces and primary constructors"
- "Turn on nullable annotations for this project and fix the warnings"
- Any request to **restructure / clean up / reorganize** code while keeping behavior identical

## When NOT to use

- The change is meant to alter behavior, add a feature, or fix a bug (not a refactor)
- Framework/SDK/NuGet version upgrades → `dotnet-upgrade`
- Pure formatting/whitespace → `dotnet format`
- Writing brand-new tests → `writing-mstest-tests` / `code-testing-agent`

## Procedure (the safety contract)

1. **Establish a green baseline.** Build the affected projects and run the relevant tests. Never
   refactor on a red baseline — you won't be able to tell what you broke.
2. **Pick ONE operation from the catalog.** Refactors compose, but each step is a single named
   operation. Never mix a refactor with a behavior change in the same step.
3. **Find the true references first (for rename/move/inline).** Before editing, locate every _binding_
   reference to the symbol — not textual matches. Prefer the C# LSP: **findReferences** /
   **goToDefinition** / **incomingCalls** (see **Tooling**) return only true bindings. If the LSP is
   unavailable, a headless grep finds candidates but also false positives (test method names, comments,
   strings, unrelated overloads); treat grep as a _candidate list_, then keep only true bindings and let
   the compiler catch any you missed. Skipping this is the #1 way a "rename" silently corrupts code.
4. **Prefer semantics-aware edits over text edits.** The principle is _semantic verification_, not a
   specific API — use the strongest tool actually available, in this order (fallback ladder):
   1. An IDE / Roslyn workspace refactoring (rename, move, extract, inline, pull-up) when available —
      updates all references, `using`s, and partial declarations correctly.
   2. **The C# LSP (Roslyn language server) from `dotnet/skills`** (see **Tooling**) for a headless
      agent: drive edits from **findReferences** / **goToDefinition** / **incomingCalls** /
      **workspaceSymbol** so every touched reference is a true binding, not a textual guess. Some
      language-server builds also expose a `textDocument/rename` code action that rewrites all
      references at once — use it when present.
   3. Analyzer code-fixes / `dotnet format analyzers` / **Roslynator** for idiom modernization and
      de-duplication, when installed.
   4. **Compiler-validated, reference-tracked edits** (the common headless fallback): use the LSP (or
      grep, if no LSP) to enumerate candidates → edit only the true binding references from step 3 →
      rebuild. The compiler is your safety net: any missed or wrongly-edited reference becomes a build
      error, not a silent bug.
   5. Plain find/replace ONLY when scope is provably tiny and every reference is verified — it
      silently corrupts strings, comments, and unrelated overloads.
   A CLI agent often won't have a loaded MSBuildWorkspace, but it **can** load the C# LSP above; prefer
   its semantic answers over grep, and never skip the build/test gate to compensate.
5. **Re-gate after every step — across every target framework.** Rebuild + re-run tests; if red, revert
   this step and reassess. The diff must be behavior-neutral: tests still green, no new warnings (nullable
   work is the deliberate exception), no new analyzer/API-compat diagnostics, and the intended public
   contract unchanged. On a multi-targeted project, build/test **each** TFM — a green default build can
   still be broken on another target.

   ```bash
   dotnet build   # 0 errors, on every TargetFramework (add -f <tfm> to check one)
   dotnet test    # must stay green; same pass count as baseline
   # if the gate fails, revert THIS step and reassess:
   git restore .          # or: git checkout -- <changed files>
   ```
6. **Keep the relevant contract stable — and which contract depends on the project type:**
   - **Library / shipped package:** preserve the .NET public API; move public types across assembly
     boundaries via type-forwarders or `[Obsolete]` shims, not breaking moves.
   - **Application / service:** preserve the _external_ contract (HTTP routes, config keys, DB schema,
     CLI args); internal type surface can move freely.
   - **Small / private codebase:** preserve behavior + tests; commit granularity can relax.
7. **One refactor per commit** for libraries/large repos (keeps review + `git bisect` meaningful);
   relax for small private codebases.

## Inputs

- Target scope: a symbol, file, type, project, or directory.
- The operation (from the catalog) — or infer it from the request.
- How to build and test the affected projects (solution/proj path, test command).

## Outputs

- The applied refactoring as a minimal, behavior-preserving diff.
- Proof of safety: before/after build + test results.
- A short rationale naming the operation(s) applied.

## Anti-patterns to avoid

- Renaming via text replace (hits strings, comments, unrelated overloads).
- "Refactor + small fix" in one step (a behavior change masquerading as a refactor).
- Moving a public type without a forwarder/shim (silent breaking change) — or refactoring a public
  surface with no PublicApiAnalyzers/ApiCompat gate to catch a break.
- Editing only the TFM/`#if` branch your editor shows, leaving other targets broken.
- Renaming a `partial` type without its other declarations, or editing generated (`*.g.cs`) output
  instead of the generator input.
- Skipping the test gate "because it's just a rename."

## Reference Files

- **[references/operation-catalog.md](references/operation-catalog.md)** — the full operation taxonomy
  with Roslyn providers and representative real PRs.
  **Load when** you need the provider for an operation or more detail on the catalog.

For .NET compatibility hazards (public API, multi-targeting/`#if`, source-generated/partial code,
`InternalsVisibleTo`), load the companion `dotnet-breaking-changes` skill and its `references/` — it
holds the depth on each surface and applies to any change, not just refactors.
