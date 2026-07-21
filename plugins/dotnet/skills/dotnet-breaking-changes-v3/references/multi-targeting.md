# Multi-targeting and #if — satisfy every target, preserve intentional divergence

## Contents

- Why this is a hidden surface
- Inspect every branch (including inactive ones)
- Preserve intentional divergence
- Re-gate every target

## Why this is a hidden surface

A project with `<TargetFrameworks>` (plural) compiles once per TFM, and code under `#if` compiles
differently per TFM, per platform, and per custom symbol. Your editor and a default `dotnet build`
usually show/exercise **one** active branch. An edit that is correct there can leave another target
uncompilable or behaviorally different — and CI (which builds all of them) is where it surfaces.

Common conditional symbols: framework (`NET8_0_OR_GREATER`, `NETFRAMEWORK`, `NETSTANDARD2_0`), platform
(`WINDOWS`, `LINUX`, `OSX`), runtime flavor (`CORECLR`, `MONO`, `NATIVEAOT`), and repo-defined symbols
(`FEATURE_*`, `PRIVATE_*`) declared via `<DefineConstants>`.

## Inspect every branch (including inactive ones)

Before editing a symbol used under `#if`:

- Find **all** its declarations/uses across branches — including branches that are inactive for the
  current TFM. A grep for the symbol crosses `#if` boundaries; the compiler for the active TFM does not.
- If the symbol has the same meaning in every branch, apply the equivalent change to each.
- Watch for symbols that **only exist** in some branches (e.g. an API available on `net8.0` but polyfilled
  or absent on `netstandard2.0`).

## Preserve intentional divergence

Conditional branches often differ **on purpose** (a fast path on new runtimes, a polyfill on old ones, a
platform-specific implementation). Do **not** homogenize them into one shape to "clean up." Preserve the
intended per-target behavior; only unify what is genuinely duplicated with identical intent.

## Re-gate every target

After the edit, build **and** test each target, not just the default:

```bash
dotnet build                         # builds every TargetFramework
dotnet build -f net472               # force a specific TFM
dotnet test -f net8.0                # test a specific TFM
# platform/RID-specific code: build/test on (or cross-target for) each supported RID
```

A green default build is **not** proof the other targets are green.
