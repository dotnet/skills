---
name: copy-to-output-directory
description: "Choosing an MSBuild CopyToOutputDirectory / CopyToPublishDirectory mode: Never, PreserveNewest, Always, and IfDifferent (MSBuild 17.13+), plus $(SkipUnchangedFilesOnCopyAlways). USE FOR: removing the per-build Always copy perf hit; resetting output files mutated between builds. DO NOT USE FOR: general incremental-build diagnosis (use incremental-build); non-MSBuild build systems."
license: MIT
---

# Choosing a CopyToOutputDirectory Mode

## Overview

The `CopyToOutputDirectory` metadata (and its publish counterpart `CopyToPublishDirectory`) controls whether an item — `Content`, `None`, `EmbeddedResource`, or `Compile` — is copied next to your build output, and under what conditions the copy happens. Picking the wrong mode causes either stale files in `bin/` or an unnecessary per-build performance hit.

As of **MSBuild 17.13 / .NET SDK 9.0.2xx** there are four values:

| Mode | Copies when… | Incremental cost | Typical use |
| --- | --- | --- | --- |
| `Never` (default) | Never | None | Files not needed at runtime |
| `PreserveNewest` | Source is **newer** than destination (or destination missing) | Cheap (timestamp check) | The common case — source files you edit |
| `Always` | **Every build**, unconditionally | Expensive — copies on every build even in no-op builds | Legacy workaround; avoid (see below) |
| `IfDifferent` | Source **differs** from destination in either direction (newer **or** older, or size differs, or destination missing) | Cheap (timestamp + size check) | Destination may be mutated between builds |

```xml
<ItemGroup>
  <None Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  <None Include="testdata\seed.db"  CopyToOutputDirectory="IfDifferent" />
</ItemGroup>
```

You can use either the attribute form shown above or the child-element form:

```xml
<None Include="testdata\seed.db">
  <CopyToOutputDirectory>IfDifferent</CopyToOutputDirectory>
</None>
```

## Why `Always` is usually the wrong choice

`Always` re-copies the file on **every** build, including otherwise-clean incremental/no-op builds. On projects with many or large content files this is a measurable, recurring cost and a common cause of "why is my no-op build not instant?" reports.

Historically `Always` was the only way to handle a specific scenario: **the destination file can change between builds** — for example an SQLite database, a storage/state file, or a config file that a test run mutates. With `PreserveNewest`, if the destination is modified (making its timestamp *newer* than the source) MSBuild will *not* restore the original source file, because the source is no longer newer. People reached for `Always` to force the file back into a known-good state — paying the copy cost on every build as a side effect.

## `IfDifferent`: copy when different, in either direction

`IfDifferent` is the targeted fix for that scenario. It copies the source over the destination whenever MSBuild considers the two **different** — whether the source is newer *or* older than the destination, whether the size differs, or the destination is missing — and skips the copy when the destination is unchanged per MSBuild's heuristic.

Under the hood the `_CopyDifferingSourceItemsToOutputDirectory` target uses the `Copy` task with `SkipUnchangedFiles="true"`. That "unchanged" check is a **heuristic**: it compares **last-write timestamp and file size only** — not a content hash — so a destination that was edited to the same size and timestamp as the source is treated as unchanged and is *not* re-copied. In practice this restores a mutated destination back to the source version on the next build (the reason people reached for `Always`) while avoiding the unconditional per-build copy.

Use `IfDifferent` when:

- A test run or the app itself writes to the copied file (databases, caches, state/storage files, editable config) and you want each build to reset it to the source version.
- You were using `Always` purely as a "keep the output in sync with the source" mechanism, not because you truly need a copy on every single build.

```xml
<ItemGroup>
  <!-- Reset the fixture DB to the source copy whenever it has drifted,
       but don't pay a copy on every no-op build. -->
  <None Include="fixtures\catalog.db" CopyToOutputDirectory="IfDifferent" />
</ItemGroup>
```

## Globally softening `Always` with `$(SkipUnchangedFilesOnCopyAlways)`

If you have an existing codebase full of `CopyToOutputDirectory="Always"` items and want the performance benefit without editing every item, set the property:

```xml
<PropertyGroup>
  <SkipUnchangedFilesOnCopyAlways>true</SkipUnchangedFilesOnCopyAlways>
</PropertyGroup>
```

This makes the `_CopyOutOfDateSourceItemsToOutputDirectoryAlways` target pass `SkipUnchangedFiles="true"` to its `Copy` task, so `Always` items are only copied when they actually differ — effectively giving `Always` the same skip-unchanged behavior as `IfDifferent`.

- Default is `false` for backwards compatibility (classic `Always` = copy every build).
- Set it in `Directory.Build.props` to opt an entire repo in at once.
- Prefer converting individual items to `IfDifferent` when you can; use this property when a bulk, non-invasive opt-in is more practical.

## How the modes flow through the build

`GetCopyToOutputDirectoryItems` buckets each item by its `CopyToOutputDirectory` value. Three copy targets then do the work as dependencies of `_CopySourceItemsToOutputDirectory` (which is itself invoked by `CopyFilesToOutputDirectory`):

- `_CopyOutOfDateSourceItemsToOutputDirectory` — `PreserveNewest` items (incremental via `Inputs`/`Outputs` timestamp comparison).
- `_CopyOutOfDateSourceItemsToOutputDirectoryAlways` — `Always` items (unconditional copy unless `$(SkipUnchangedFilesOnCopyAlways)` is `true`).
- `_CopyDifferingSourceItemsToOutputDirectory` — `IfDifferent` items (`SkipUnchangedFiles="true"`).

All copied files are registered in `FileWrites`, so `dotnet clean` removes them.

**Transitive copy:** items marked `Always`, `PreserveNewest`, or `IfDifferent` also flow to referencing projects through `ProjectReference` (via `_CopyToOutputDirectoryTransitiveItems`). `Never` items do not. `IfDifferent` participates in ClickOnce publish item collection alongside `Always`/`PreserveNewest`.

## Version requirement

`IfDifferent` and `$(SkipUnchangedFilesOnCopyAlways)` require **MSBuild 17.13 or later** (**.NET SDK 9.0.2xx+ / Visual Studio 2022 17.13+**). On older toolsets the value is not recognized: it will not match the `Always`/`PreserveNewest`/`IfDifferent` conditions in the common targets, so the item is silently **not copied**. Gate usage on the toolset if you must support older SDKs, or require the minimum SDK via `global.json`.

## Quick decision guide

- Don't need the file at runtime → `Never` (or omit — it's the default).
- Normal source file you edit → `PreserveNewest`.
- Destination gets mutated between builds and must be reset to the source → `IfDifferent`.
- You truly need a fresh copy on literally every build → `Always` (rare).
- Stuck with lots of legacy `Always` and want the perf win without edits → keep `Always` but set `$(SkipUnchangedFilesOnCopyAlways)=true`.
