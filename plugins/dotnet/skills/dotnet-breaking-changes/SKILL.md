---
name: dotnet-breaking-changes
description: >
  Protects the observable .NET/C# contract when you edit code: the contract is wider than the file
  you touch, and additions count too. USE FOR: shipped libraries/NuGet packages, or cross-assembly,
  multi-targeted, partial, or source-generated code — adding a public/protected member, overload, or
  target framework; widening what a public method accepts or returns; renaming, moving, or removing a
  member that is public/protected, used outside its assembly (incl. InternalsVisibleTo), referenced
  from another #if/target-framework branch, or referenced by generated or partial code; changing a
  shipped member's signature, nullability, or attribute; or answering "is this a breaking change?".
  Applies to features, fixes, and refactors. DO NOT USE FOR: intra-method changes, local-variable
  renames, or private/internal details not exposed across public API, assembly, #if/target-framework,
  or generated/partial code — even when the project multi-targets; framework/SDK/NuGet upgrades (use
  dotnet-upgrade); or formatting.
license: MIT
---

# .NET breaking changes: the contract is wider than the file

When you edit .NET code, the observable contract is usually **larger than the snippet you are
looking at**. A change that compiles locally and keeps tests green can still break a downstream
consumer, another target framework, a friend assembly, or regenerate away on the next build. This
skill names the four hidden surfaces, tells you how to find which ones a repo actually uses, and
points to a reference file for each. It is deliberately _not_ refactoring-specific — the same
hazards apply to a feature or a bug fix, and to **additions** (a new public member, overload, or
target framework) as much as to removals: adding surface creates a permanent contract obligation,
and adding behavior to a multi-targeted or partial type must stay correct on every target and
survive the next regeneration. For the behavior-preserving refactoring **process**
(green baseline → one named op → re-gate), use the `csharp-refactoring` skill; this skill is the
compatibility knowledge it (and any other change) taps into.

## Inspect first — find which surfaces this repo actually has

Do not recite these checks or assume; **search the repo and adapt to what exists.** The markers
present dictate the validation plan; markers absent tell you a surface is not in play.

```bash
# Public-API gate (which one? they are not interchangeable)
git ls-files "**/PublicAPI.Shipped.txt" "**/PublicAPI.Unshipped.txt"   # PublicApiAnalyzers
grep -rl "EnablePackageValidation\|ApiCompat\|Microsoft.DotNet.ApiCompat" --include=*.props --include=*.targets --include=*.csproj .
# Multi-targeting and conditional code
grep -rl "<TargetFrameworks>" --include=*.csproj --include=*.props .
grep -rn "#if " --include=*.cs .        # NET*, platform, and custom symbols
# Generated / partial code
git ls-files "*.g.cs" "*.generated.cs" ; grep -rln "partial class\|partial record\|partial struct" --include=*.cs .
# Friend assemblies
grep -rn "InternalsVisibleTo" --include=*.cs --include=*.csproj --include=*.props .
```

Then read only the reference(s) for the surfaces you actually found.

## The four hidden surfaces (summary — depth in references/)

Public API (1) is the **default** surface to check on any library edit; the other three are
**conditional** — pursue them only when the inspect-first markers above show they are in play.

1. **Public API surface.** In a shipped library, renaming/moving/removing/re-signing a public
   member — or changing nullability, a generic constraint, or a trimming/AOT attribute — is a
   breaking change a green test run will not catch. Repos gate this **two different, non-
   interchangeable ways**: source-level (`PublicApiAnalyzers` + `PublicAPI.*.txt`, which you
   maintain) and binary/package-level (`ApiCompat` / `<EnablePackageValidation>`). Run/respect the
   gate that exists; if none exists, review the surface by hand and flag it — do **not** bolt on
   analyzer infrastructure as a side effect. See `references/public-api.md`.

2. **Multi-targeting and #if.** Code that multi-targets (`<TargetFrameworks>`) or compiles
   conditionally (`#if NET8_0_OR_GREATER`, platform/CoreCLR/Mono/NativeAOT branches) can build for
   the target your editor shows and break one it never invoked. Inspect every branch — including
   inactive ones — preserve intentional divergence, and re-gate each TFM/RID. See
   `references/multi-targeting.md`.

3. **Source-generated and partial code.** A type is often `partial` across several files, and part
   may be **generated** (source generators, Razor, `*.g.cs`). Include every partial declaration;
   treat generated files as derived and change the generator input/template unless the repo checks
   the output in as source. See `references/source-generation.md`.

4. **InternalsVisibleTo.** `internal` is not private across a solution: test and friend assemblies
   bind to internals (and, when strong-named, to a public key). An internal rename/move/removal can
   break a consumer with no reference in the declaring project. See
   `references/internals-visible-to.md`.

## Stop and escalate (do not silently proceed)

- A **public/shipped API** would change and you cannot verify compatibility — no shim/forwarder
  path and no PublicApiAnalyzers/ApiCompat/package-validation gate to catch a break.
- The edit changes an **observable annotation** (nullability, `[DynamicallyAccessedMembers]`,
  `[RequiresUnreferencedCode]`, generic constraints) on a public member.
- The change can only be made consistent across **some but not all** target frameworks or platforms.
- The only way to apply it is editing **generated output** that the next build will overwrite.

## When to use

- "Is this a breaking change?" / "Will this break the public API / the NuGet package?"
- Editing, renaming, or removing a public or `internal` member in a library
- Changing a signature, nullability, generic constraint, or trimming/AOT attribute
- Editing a multi-targeted project, code under `#if`, or a platform-specific branch
- Touching a `partial` type or source-generated code

## When NOT to use

- Framework/SDK/NuGet **version upgrades** → the `dotnet-upgrade` plugin (`migrate-*`,
  `dotnet-aot-compat`, `migrate-nullable-references`)
- Pure formatting/whitespace → `dotnet format`
- A single-target private app with no public or cross-assembly surface (no hidden contract to break)

## Reference Files

- **[references/public-api.md](references/public-api.md)** — the two API gates and why they are not
  interchangeable, nullable/trimming/AOT as observable API, `[Obsolete]` shims and type-forwarders,
  suppression baselines. **Load when** editing a public/`internal` member in a library, changing a
  signature/nullability/attribute, or asked whether something is a breaking change.
- **[references/multi-targeting.md](references/multi-targeting.md)** — TFMs, `#if` and platform
  symbols, RIDs, and how to inspect and re-gate every target. **Load when** the project has
  `<TargetFrameworks>` or the code uses `#if`.
- **[references/source-generation.md](references/source-generation.md)** — partial types, generated
  output vs checked-in source, editing the generator input. **Load when** the symbol is `partial`
  or lives in `*.g.cs`/generated files. (See also `dotnet-msbuild/including-generated-files`.)
- **[references/internals-visible-to.md](references/internals-visible-to.md)** — friend/test
  assemblies and strong-name keys. **Load when** the repo has `InternalsVisibleTo` and you touch an
  `internal` member.

## Related skills

- `csharp-refactoring` — the behavior-preserving refactoring process that consumes this knowledge.
  This guide _adds_ compatibility analysis; it does **not** replace that skill's green-baseline → one
  named op → re-gate → revert-on-red workflow.
- `dotnet-upgrade/migrate-nullable-references`, `dotnet-upgrade/dotnet-aot-compat` — for _adopting_
  nullable/AOT, not preserving an existing surface.
- `dotnet-msbuild/including-generated-files` — MSBuild wiring for generated files.
