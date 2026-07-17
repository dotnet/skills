---
name: csharp-refactoring
description: "Performs safe, behavior-preserving refactoring of C#/.NET code, verified with build, tests, and analyzers. USE FOR: any request to rename, move, extract, split, modernize, or otherwise restructure C# code without changing behavior, including small requests like 'rename X to Y': rename a symbol/type/file across a solution; move a type or static members to another file/namespace/project; extract a method, interface, or base class; pull members up; inline a method or local; split a large class/file; consolidate or de-duplicate copy-pasted code; sync namespaces to folders; modernize to current C# idioms (file-scoped namespaces, primary constructors, collection expressions, target-typed new, pattern matching); or enable nullable reference annotations. DO NOT USE FOR: adding features, fixing bugs, writing new tests, upgrading frameworks or NuGet versions (use dotnet-upgrade), or formatting-only passes (use dotnet format)."
license: MIT
---

# C# Refactoring (behavior-preserving)

Refactor C#/.NET code so the structure improves but the observable behavior does **not** change. Work in
small, named, Roslyn-aware steps, each followed by a build/test gate sized to the change's blast radius.
If a gate fails, stop and revert — a refactor that changes behavior is a bug, not a refactor.

## Right-size the rigor to the blast radius

The safety *goal* never changes — behavior and the relevant contract stay identical — but the *amount* of
gating scales with reach. A full multi-TFM baseline and a public-surface hazard sweep on a one-line local
rename spends time and tokens without adding safety.

- **Local / private** (method-local or `private` member, single file, one target framework, no public
  surface, no `partial`/generated/`#if` involvement): **verify once.** Let the compiler catch missed
  references and run the *relevant* tests a single time after the edit. Skip a separate "before" baseline
  when the tree is already known-green, and skip the compatibility-hazard sweep.
- **Cross-boundary** (public/shipped symbol, multi-targeted project, `#if`/platform branches, or
  `partial`/generated code): apply the **full** contract below — baseline, per-TFM re-gate, and the
  compatibility-hazards check.

Unsure which tier applies? Do one cheap inspection first, then take the lighter tier if the heavier
signals are absent.

## Operations

Rename · Move (to another file / namespace / project) · Extract (method / class / interface) · Inline
(method / local / constant) · Consolidate / de-duplicate copy-pasted code · Split a large class / file ·
Pull up / push down a member · Sync namespace to folder · Modernize to current C# idioms (the repo's
analyzers decide the idiom, not taste) · Enable nullable annotations.

Each is a single named operation — compose them one step at a time (see Procedure). For the Roslyn
provider mapping and representative real PRs, see
[references/operation-catalog.md](references/operation-catalog.md).

## Find true references, then edit semantically

The #1 way a "rename" silently corrupts code is editing textual matches instead of *bindings*. Before a
rename / move / inline, find every **binding** reference — not comments, strings, or unrelated overloads.

Prefer the C# LSP (Roslyn language server) the [`dotnet` plugin declares](https://github.com/dotnet/skills/blob/main/plugins/dotnet/lsp.json)
over grep: **findReferences** (the rename/move safety net), **goToDefinition**, **incomingCalls**,
**workspaceSymbol**, and **hover** give exact, binding-aware answers. Use the strongest tool actually
available, in order:

1. An IDE / Roslyn workspace refactoring (rename, move, extract, inline, pull-up) — updates all
   references, `using`s, and `partial` declarations correctly.
2. C# LSP semantic edits driven by **findReferences** / **goToDefinition** / **incomingCalls** (some
   builds also expose a `textDocument/rename` code action that rewrites all references at once).
3. Analyzer code-fixes / `dotnet format analyzers` / **Roslynator** for idiom modernization and de-dup.
4. Compiler-validated edits (common headless fallback): enumerate candidates (LSP, or grep if no LSP) →
   edit only the true bindings → rebuild. The compiler flags anything missed or wrongly edited.
5. Plain find/replace ONLY when scope is provably tiny and every reference is verified.

Never skip the build/test gate to compensate for a weaker tool.

## Procedure (the safety contract)

1. **Know the baseline is green.** If the tree is not already known-green, build the affected projects and
   run the relevant tests once — never refactor on a red baseline. If it is already known-green (e.g. a
   fresh checkout that builds), don't repeat a full baseline; go to the edit and rely on the post-edit
   gate in step 3.
2. **One operation per step.** Refactors compose, but each step is a single named operation from the
   catalog. Never mix a refactor with a behavior change in the same step.
3. **Re-gate after every step — proportional to the blast radius.** Rebuild + re-run the relevant tests;
   if red, revert this step and reassess. The diff must be behavior-neutral: tests still green, no new
   warnings (nullable work is the deliberate exception), no new analyzer/API-compat diagnostics, and the
   intended public contract unchanged. A single-target, private-scope change needs one build + the
   relevant tests; a **multi-targeted or public** change must build/test **each** TFM — a green default
   build can still be broken on another target.
4. **Keep the relevant contract stable — which contract depends on the project type:** library / shipped
   package → preserve the .NET public API (move public types across assemblies via `[TypeForwardedTo]`; a
   *rename* needs an `[Obsolete]` shim, not a forwarder); application / service → preserve the *external*
   contract (HTTP routes, config keys, DB schema, CLI args); small / private → preserve behavior + tests.
5. **One refactor per commit** for libraries / large repos (keeps review + `git bisect` meaningful);
   relax for small private codebases.

**Use the repo's own build/test workflow when it documents one.** Check `README` / `CONTRIBUTING`, build
scripts (`build.sh` / `build.cmd`, an `eng/`, `build/`, or `scripts/` directory), `global.json`, or the CI
workflows under `.github/workflows`, and run the gate the way the repo does — the repo's instructions win
over any generic command. Fall back to the generic commands below only when the repo documents none:

```bash
dotnet build   # 0 errors, on every TargetFramework (add -f <tfm> to check one)
dotnet test    # must stay green; same pass count as baseline
git restore .  # if the gate fails, revert THIS step and reassess
```

## .NET compatibility hazards (cross-boundary changes)

A behavior-preserving edit can still break in _.NET-specific_ ways a green test run won't catch. **When
the change touches — or might touch — a public symbol, a multi-targeted project, or `partial`/generated
code, search the repo first** for the surfaces that govern the symbol (don't assume or recite):

- Public-API gate: `PublicAPI.Shipped/Unshipped.txt` (PublicApiAnalyzers) and/or `ApiCompat` /
  `<EnablePackageValidation>` — these are _not_ interchangeable.
- `<TargetFrameworks>` and `#if` / platform branches; `partial` and generated (`*.g.cs`) declarations;
  `InternalsVisibleTo` friend/test assemblies.

Refactor reminders: move a public type via a `[TypeForwardedTo]` forwarder (a *rename* needs an
`[Obsolete]` shim); include _every_ `partial` declaration in a rename/move; edit the generator input, not
generated output. **For a provably local/private change, skip this sweep.** For the full per-surface
playbook, load the companion **dotnet-breaking-changes** skill — it adds depth on each surface (and
applies to any change, not just refactors) but does **not** replace this skill's safety contract.

## Stop and escalate (do not silently proceed)

Halt and ask the user before continuing when:

- The **baseline is red** (build or tests already failing) — you can't prove you preserved behavior.
- A **public/shipped API** would change and you cannot verify compatibility (no type-forwarder/shim path,
  and no PublicApiAnalyzers/ApiCompat/package-validation gate to catch a break).
- The request is **not actually behavior-preserving** — it asks you to upgrade a framework/package, add a
  feature, or fix a bug. Do **not** carry it out under this skill, and **never label such a change
  "behavior-preserving."** Say it is out of scope and redirect (upgrades → `dotnet-upgrade`;
  features/fixes → normal dev flow). If a refactor is genuinely a prerequisite, do only that, as a
  separate step, and stop.
- The edit touches **generated, designer, or migration files** (`*.g.cs`, `*.Designer.cs`, EF migrations,
  source-generator output) — hand-edits there are **overwritten on the next build**; change the source of
  generation, not the output.
- Semantic equivalence depends on **runtime behavior not covered by tests** (reflection, DI wiring,
  serialization, `dynamic`, P/Invoke) — flag the risk; tests alone won't catch a regression.

## Anti-patterns to avoid

- Renaming via text replace (hits strings, comments, unrelated overloads).
- "Refactor + small fix" in one step (a behavior change masquerading as a refactor).
- Moving a public type without a forwarder/shim, or refactoring a public surface with no
  PublicApiAnalyzers/ApiCompat gate to catch a break.
- Editing only the TFM/`#if` branch your editor shows; renaming a `partial` type without its other
  declarations; editing generated (`*.g.cs`) output instead of the generator input.
- Skipping verification entirely — shipping a change with no compiler or test confirmation at all.
  (Right-sizing the gate to the change is expected; skipping it is not.)

## Reference files

- **[references/operation-catalog.md](references/operation-catalog.md)** — the full operation taxonomy
  with Roslyn providers and representative real PRs. **Load when** you need the provider for an operation.

For .NET compatibility depth (public API, multi-targeting/`#if`, source-generated/partial code,
`InternalsVisibleTo`), load the companion **dotnet-breaking-changes** skill and its `references/`.
