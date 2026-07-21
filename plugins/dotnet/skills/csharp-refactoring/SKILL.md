---
name: csharp-refactoring
description: "Performs safe, behavior-preserving refactoring of C#/.NET code, verified with build, tests, and analyzers. USE FOR: any request to rename, move, extract, split, modernize, or otherwise restructure C# code without changing behavior, including small requests like 'rename X to Y': rename a symbol/type/file across a solution; move a type or static members to another file/namespace/project; extract a method, interface, or base class; pull members up; inline a method or local; split a large class/file; consolidate or de-duplicate copy-pasted code; sync namespaces to folders; modernize to current C# idioms (file-scoped namespaces, primary constructors, collection expressions, target-typed new, pattern matching); or enable nullable reference annotations. DO NOT USE FOR: adding features, fixing bugs, writing new tests, upgrading frameworks or NuGet versions (use dotnet-upgrade), or formatting-only passes (use dotnet format)."
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

- **Framework / NuGet version bump** → not a refactor; redirect to the `dotnet-upgrade` skills.
- **New feature** (e.g. add a pricing tier, a flag, an endpoint) → a feature, not a refactor. If asked,
  build it as a feature *with its own tests*; don't claim behavior is preserved.
- **Bug fix or "simplification" that changes output** (e.g. always charge shipping, bump a discount) →
  a behavior **change**. It is a legitimate task — do it as an explicit, tested change and update the
  tests that lock in the new behavior — but keep it **separate** from any refactor and never label it
  behavior-preserving. Do not stall or report "nothing to change."
- **A rename/move with a behavior tweak smuggled in** ("rename X, and while you're there bump the rate")
  → split it: do the rename as a behavior-preserving refactor, and treat the tweak as its own tested
  change, or flag it and defer.

Only when the request is genuinely structure-only do you proceed as a refactor.

## Rename / move by bindings, not text

The #1 way a "rename" silently corrupts code is editing textual matches (comments, strings, unrelated
overloads) instead of real **bindings**. Find every binding reference first, then edit semantically. Use
the strongest tool available: an IDE/Roslyn workspace refactoring, then the C# LSP the
[`dotnet` plugin declares](https://github.com/dotnet/skills/blob/main/plugins/dotnet/lsp.json)
(`findReferences`, `goToDefinition`, `incomingCalls`, `rename` code action), then analyzer code-fixes /
Roslynator, then compiler-validated edits (edit the true bindings, rebuild, let the compiler flag misses).
Plain find/replace only when scope is provably tiny and every hit is verified. Include **every** `partial`
declaration, and edit the generator input, never generated (`*.g.cs`) output.

For the operation → Roslyn-provider mapping and representative PRs, see
[references/operation-catalog.md](references/operation-catalog.md).

## Verify proportionally

Confirm behavior is preserved after the edit — scaled to blast radius, not a fixed ceremony:

- **Local / private** (method-local or `private` member, one file, single target framework, no public
  surface, no `partial`/generated/`#if`): build once and run the **relevant** tests once after the edit.
  If the tree is already known-green, don't burn a second full "before" baseline — rely on the post-edit
  gate. Let the compiler catch missed references.
- **Cross-boundary** (public/shipped symbol, multi-targeted project, `#if`/platform branches, or
  `partial`/generated code): build/test **each** target framework (a green default build can hide a break
  on another TFM), and run the hazards check below.

Use the repo's own build/test workflow when it documents one (`README`/`CONTRIBUTING`, `build.*`, `eng/`,
`global.json`, `.github/workflows`); its instructions win over any generic command. Otherwise:

```bash
dotnet build   # 0 errors
dotnet test    # stays green; same pass count as before
git restore .  # if the gate fails, revert THIS step and reassess
```

One operation per step; never mix a refactor and a behavior change in the same step. On red, revert — a
refactor that changes behavior is a bug, not a refactor.

## Cross-boundary hazards (only when it touches a boundary)

If — and only if — the change touches a **public** symbol, a **multi-targeted** project, or
`partial`/generated code, some breaks won't show up as a failing test. Search the repo for the surface
that governs the symbol (don't assume): the public-API gate (`PublicAPI.Shipped/Unshipped.txt` for
PublicApiAnalyzers, and/or `ApiCompat`/`<EnablePackageValidation>` — not interchangeable),
`<TargetFrameworks>`/`#if` branches, and `InternalsVisibleTo`. Move a public type via a `[TypeForwardedTo]`
forwarder; a *rename* needs an `[Obsolete]` shim, not a forwarder. For depth on any of these surfaces,
load the companion **dotnet-breaking-changes** skill. For a provably local/private change, skip this.

## Stop and ask when

- The baseline is already red (you can't prove you preserved behavior).
- A public/shipped API would change and there is no forwarder/shim path and no analyzer/ApiCompat gate.
- Equivalence depends on runtime behavior tests don't cover (reflection, DI, serialization, `dynamic`,
  P/Invoke) — flag it.
