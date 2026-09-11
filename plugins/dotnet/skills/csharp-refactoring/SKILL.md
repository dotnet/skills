---
name: csharp-refactoring
description: "Performs safe, behavior-preserving refactoring of C#/.NET code, verified with build, tests, and analyzers. USE FOR: requests to rename, move, extract, split, consolidate, de-duplicate, modernize, or otherwise restructure C# code without changing behavior, including 'rename X to Y', generated/partial declarations, public or serialized contracts, multi-targeted code, and mixed requests where a feature, bug fix, package/framework upgrade, nullability change, or other behavior/contract change is presented as a refactor and must be separated or declined. DO NOT USE FOR: ordinary feature or bug-fix requests that are not framed as refactoring; package/framework upgrades after they have been reclassified (use dotnet-upgrade); new tests; or formatting-only passes (use dotnet format)."
license: MIT
---

# C# Refactoring (behavior-preserving)

A refactor changes **structure**, never observable **behavior**. Do the edit with binding-aware tools,
then confirm behavior held with a build + the relevant tests. Keep the effort proportional to the change:
a one-line local rename does not need the ceremony a public multi-targeted change does.

## First, is this actually a refactor?

The most valuable thing this skill does is *not* restructure code you were told to restructure — it is
catching a request that is **not** behavior-preserving before you run it through a refactor's contract.
When the request changes results, decline the refactor framing and handle it honestly:

- **Framework / NuGet version bump** → not a refactor. Stop this workflow without editing project or
  package files, explain the reclassification, and redirect to the `dotnet-upgrade` skills. A successful
  build does not make an upgrade behavior-preserving.
- **New feature** (e.g. add a capability, a flag, or an endpoint) → not a refactor. Stop this
  workflow and route it to the appropriate feature workflow; do not implement it here. If a separately
  authorized feature also has a structural cleanup, keep the two changes distinct in the implementation
  and final report.
- **Bug fix or "simplification" that changes output** (e.g. change a threshold or calculation) →
  a behavior **change**. It is a legitimate task — do it as an explicit, tested change and update the
  tests that lock in the new behavior — but only after it is authorized as a behavior change. Under an
  explicitly behavior-preserving request, leave that edit undone, complete only any separable structural
  operation, and report the deferred change. Never label the behavior change behavior-preserving.
- **A rename/move with a behavior tweak smuggled in** ("rename X, and while you're there change the result")
  → do the rename/move as the behavior-preserving operation and defer the tweak. Perform the tweak only
  after the user separately accepts it as a tested behavior change; do not silently turn one
  "behavior-preserving" task into two edits.

Only when the request is genuinely structure-only do you proceed as a refactor.

## Rename / move by bindings, not text

The #1 way a "rename" silently corrupts code is editing textual matches (comments, strings, unrelated
overloads) instead of real **bindings**. Find every binding reference first, then edit semantically. Use
the strongest tool available: an IDE/Roslyn workspace refactoring, then the configured C# LSP
(`findReferences`, `goToDefinition`, `incomingCalls`, `rename` code action), then analyzer code-fixes /
Roslynator, then compiler-validated edits (edit the true bindings, rebuild, let the compiler flag misses).
Plain find/replace only when scope is provably tiny and every hit is verified. Include **every** `partial`
declaration, and edit the generator input, never generated (`*.g.cs`) output.

For the operation → Roslyn-provider mapping and representative PRs, see
[references/operation-catalog.md](references/operation-catalog.md).

## Consolidate toward the existing source of truth

When de-duplicating, preserve the ownership direction stated by the code or request. If `B` duplicates
an implementation already owned by `A`, keep `A` canonical and make `B` delegate to it; do not invert
the dependency merely because either direction compiles. Preserve public compatibility wrappers when
the duplicate surface is shipped, and migrate only in-repo callers that are safe to move.

## Preserve contracts beyond C# call sites

Compilation proves binding compatibility, not every external contract. Before renaming or moving a
type/member, check whether its name or metadata is observed by serialization, reflection, dependency
injection, configuration binding, source generators, P/Invoke, or `dynamic`.

| Boundary | Required decision |
|---|---|
| Serialized/configuration name | Preserve the external name with the repository's existing mechanism (for example, `JsonPropertyName`) while migrating C# callers; run a focused round-trip or payload test. |
| Public nullable annotation | Treat tightening or loosening nullability as a source-contract change, not a behavior-preserving refactor. Leave it unchanged unless the contract change is explicitly authorized and validated. |
| Uncovered reflection or runtime lookup | Do not guess that a compile-clean rename is safe. Preserve the observed name or stop and report the unverified runtime boundary. |

## Verify proportionally

Confirm behavior is preserved after the edit — scaled to blast radius, not a fixed ceremony:

- **Local / private** (method-local or `private` member, one file, single target framework, no public
  surface, no `partial`/generated/`#if`): skip a separate baseline unless the tree is already suspect.
  Make the edit, then run the narrowest build and relevant tests once. Let the compiler catch missed
  references.
- **Cross-boundary** (public/shipped symbol, multi-targeted project, `#if`/platform branches, or
  `partial`/generated code): establish a baseline, then build/test **each** target framework after the
  edit (a green default build can hide a break on another TFM), and run the hazards check below.

Use the repo's own build/test workflow when it documents one (`README`/`CONTRIBUTING`, `build.*`, `eng/`,
`global.json`, `.github/workflows`); its instructions win over any generic command.

### Typical workflow (one operation)
1. Choose one named refactoring operation and keep the step focused on that operation only.
2. Find true binding references (`findReferences`/`goToDefinition`/rename) and include all `partial` declarations.
3. Establish a baseline first only for a cross-boundary change or a tree not already known green.
4. Apply the change via the most semantics-aware tool available; avoid blind find/replace when possible.
5. Rebuild and run the relevant tests. If the gate goes red, report the failure and repair or reassess only
  your edit; never discard unrelated worktree changes.

Otherwise:
```bash
dotnet build   # 0 errors
dotnet test    # stays green; same pass count as before
```

One operation per step; never mix a refactor and a behavior change in the same step. On red, revert — a
refactor that changes behavior is a bug, not a refactor.

## Cross-boundary hazards (only when it touches a boundary)

If — and only if — the change touches a **public** symbol, a **multi-targeted** project, or
`partial`/generated code, some breaks won't show up as a failing test. Search the repo for the surface
that governs the symbol (don't assume): the public-API gate (`PublicAPI.Shipped/Unshipped.txt` for
PublicApiAnalyzers, and/or `ApiCompat`/`<EnablePackageValidation>` — not interchangeable),
`<TargetFrameworks>`/`#if` branches, and `InternalsVisibleTo`. Moving a public type to another assembly
needs `[TypeForwardedTo]` in the original assembly; a move within one assembly does not. A public
*rename* needs an `[Obsolete]` shim, not a forwarder. For a provably local/private change, skip these
checks.

## Stop and ask when

- The baseline is already red (you can't prove you preserved behavior).
- A public/shipped API would change and there is no forwarder/shim path and no analyzer/ApiCompat gate.
- Equivalence depends on runtime behavior tests don't cover (reflection, DI, serialization, `dynamic`,
  P/Invoke) — flag it.
