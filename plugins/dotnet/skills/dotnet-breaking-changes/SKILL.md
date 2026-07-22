---
name: dotnet-breaking-changes
description: >
  Keep the observable .NET/C# contract intact when editing: additions count too, and the
  contract is wider than the file you are editing. Covers the public API surface and the
  DIFFERENT gates repos use for it (PublicApiAnalyzers vs ApiCompat/package validation),
  nullable/trimming/AOT annotations as API, multi-targeting and #if branches (behavior per
  target framework), source-generated and partial code, and InternalsVisibleTo.
  USE FOR: any edit to a shipped library/NuGet package or cross-assembly/multi-targeted code —
  adding a public/protected member, overload, or target framework; widening what a public method
  accepts or returns; renaming, moving, or removing a member; changing a signature, nullability,
  or attribute; touching a partial or source-generated type; or answering "is this a breaking
  change?". Applies to features, fixes, and refactors alike.
  DO NOT USE FOR: framework/SDK/NuGet upgrades (use dotnet-upgrade skills), pure formatting, or a
  single-target private app with no public/cross-assembly surface.
license: MIT
---

# .NET breaking changes: the contract is wider than the file

When you edit .NET code, the observable contract is usually **larger than the snippet in front of you**.
A change that compiles and keeps tests green can still break a downstream consumer, another target
framework, a friend assembly, or regenerate away on the next build. This applies to a feature or a bug
fix as much as a refactor, and to **additions** — a new public member, overload, or target framework — as
much as removals: added surface is a permanent contract obligation, and added behavior on a multi-targeted
or partial type must stay correct on every target and survive the next regeneration. (For the
behavior-preserving refactoring *process*, use the `csharp-refactoring` skill; this skill is the
compatibility knowledge it — and any other change — taps into.)

## Inspect first — find which surfaces this repo actually has

The most valuable move is to **look before you leap**: find which hidden surfaces exist here, then check
only those. Don't recite these or assume — the markers present dictate the plan; markers absent tell you a
surface is not in play.

```bash
# Public-API gate (which one? they are NOT interchangeable)
git ls-files "**/PublicAPI.Shipped.txt" "**/PublicAPI.Unshipped.txt"                 # PublicApiAnalyzers
grep -rl "EnablePackageValidation\|ApiCompat" --include=*.props --include=*.targets --include=*.csproj .
# Multi-targeting and conditional code
grep -rl "<TargetFrameworks>" --include=*.csproj --include=*.props . ; grep -rn "#if " --include=*.cs .
# Generated / partial code
git ls-files "*.g.cs" "*.generated.cs" ; grep -rln "partial class\|partial record\|partial struct" --include=*.cs .
# Friend assemblies
grep -rn "InternalsVisibleTo" --include=*.cs --include=*.csproj --include=*.props .
```

Then read only the reference(s) for the surfaces you actually found.

## The hidden surfaces (depth in references/)

Public API (1) is the **default** surface to check on any library edit; the other three are
**conditional** — pursue them only when the inspect-first markers above show they are in play.

1. **Public API.** Renaming/moving/removing/re-signing a public member — or changing nullability, a
   generic constraint, or a trimming/AOT attribute — is a breaking change a green test run will not catch.
   Repos gate this **two non-interchangeable ways**: source-level (`PublicApiAnalyzers` + `PublicAPI.*.txt`,
   which you maintain) and binary/package-level (`ApiCompat` / `<EnablePackageValidation>`). Respect the
   gate that exists; if none exists, review the surface by hand and flag it — do **not** bolt on analyzer
   infrastructure as a side effect. Move a public type via a `[TypeForwardedTo]` forwarder; a *rename*
   needs an `[Obsolete]` shim, not a forwarder. See `references/public-api.md`.

2. **Multi-targeting and `#if`.** Code that multi-targets (`<TargetFrameworks>`) or compiles conditionally
   (`#if NET8_0_OR_GREATER`, platform/CoreCLR/Mono/NativeAOT branches) can build for the target your editor
   shows and break one it never invoked. Inspect every branch — including inactive ones — preserve
   intentional divergence, and re-gate each TFM/RID. See `references/multi-targeting.md`.

3. **Source-generated and partial code.** A type is often `partial` across several files, and part may be
   **generated** (source generators, Razor, `*.g.cs`). Include every partial declaration; treat generated
   files as derived and change the generator input/template unless the repo checks the output in as source.
   See `references/source-generation.md`.

4. **InternalsVisibleTo.** `internal` is not private across a solution: test and friend assemblies bind to
   internals (and, when strong-named, to a public key). An internal rename/move/removal can break a
   consumer with no reference in the declaring project. See `references/internals-visible-to.md`.

## Stop and escalate (do not silently proceed)

- A **public/shipped API** would change and you cannot verify compatibility — no shim/forwarder path and
  no PublicApiAnalyzers/ApiCompat/package-validation gate to catch a break.
- The edit changes an **observable annotation** (nullability, `[DynamicallyAccessedMembers]`,
  `[RequiresUnreferencedCode]`, generic constraints) on a public member.
- The change can be made consistent across **some but not all** target frameworks or platforms.
- The only way to apply it is editing **generated output** that the next build will overwrite.

## Reference files

Load a reference only for a surface the inspect-first step actually found:

- **[references/public-api.md](references/public-api.md)** — the two API gates and why they are not
  interchangeable, nullable/trimming/AOT as observable API, `[Obsolete]` shims and type-forwarders,
  suppression baselines.
- **[references/multi-targeting.md](references/multi-targeting.md)** — TFMs, `#if` and platform symbols,
  RIDs, and how to inspect and re-gate every target.
- **[references/source-generation.md](references/source-generation.md)** — partial types, generated output
  vs checked-in source, editing the generator input. (See also `dotnet-msbuild/including-generated-files`.)
- **[references/internals-visible-to.md](references/internals-visible-to.md)** — friend/test assemblies
  and strong-name keys.
