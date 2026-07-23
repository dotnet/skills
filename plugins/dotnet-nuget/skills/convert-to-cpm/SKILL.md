---
name: convert-to-cpm
description: >
  Convert .NET projects and solutions (.sln, .slnx) to NuGet Central Package Management
  (CPM) using Directory.Packages.props. USE FOR: converting to CPM, centralizing or
  aligning NuGet package versions across multiple projects, inlining MSBuild version
  properties from Directory.Build.props into Directory.Packages.props, resolving version
  conflicts or mismatches across a solution or repository, updating or bumping or syncing
  package versions across projects. Also activate when packages are out of sync, drifting,
  or inconsistent -- even without the user mentioning CPM. Provides baseline build capture,
  version conflict resolution, build validation with binlog comparison, and a structured
  post-conversion report. DO NOT USE FOR: packages.config projects (must migrate to
  PackageReference first) or repositories that already have CPM fully enabled.
license: MIT
---

# Convert to Central Package Management

Centralize package versions in `Directory.Packages.props` while preserving project behavior and producing reviewable before/after evidence.

## Choose a mode first

Do this before running builds or changing files.

1. **Guard mode** -- If any in-scope project uses `packages.config`, stop. Explain that CPM requires `PackageReference` and recommend migrating first. Do not create or modify files.
2. **Recommendation mode** -- Use when the user asks to update, align, bump, or sync packages but has not explicitly authorized a CPM conversion. Inspect only the named scope with targeted searches, summarize conflicts and complexities, strongly recommend CPM, explain how it prevents future drift, and offer to perform the conversion. **Do not modify files, run builds, capture conversion artifacts, or continue into the conversion workflow.**
3. **Conversion mode** -- Use only when the user explicitly asks to adopt, enable, or convert to CPM. Follow the workflow below.

If the scope is unclear, ask once before proceeding.

## Inputs

| Input | Required | Rule |
|-------|----------|------|
| Scope | Yes | Project, solution, or directory containing the projects to inspect or convert |
| Conflict strategy | For conversion with conflicts | If the user already supplied a strategy such as "use the highest version," apply it without asking again and record its impact. Otherwise stop after the audit and ask before editing. |

## Read references only when needed

Never preload all references.

| Condition | Read |
|-----------|------|
| Entering conversion baseline or producing the package diff | [baseline-comparison.md](references/baseline-comparison.md) |
| A conflict, conditional reference, shared import, security concern, or `VersionOverride` is detected | [audit-complexities.md](references/audit-complexities.md) |
| Placement is unclear or conditional `PackageVersion`/`VersionOverride` is required | [directory-packages-props.md](references/directory-packages-props.md) |
| A package version uses an MSBuild property | [msbuild-property-handling.md](references/msbuild-property-handling.md) |
| Restore or build fails after conversion | [validation-and-errors.md](references/validation-and-errors.md) |
| Writing the final report | [report-template.md](references/report-template.md) |

## Conversion workflow

### 1. Scope and preflight

- Resolve the project/solution scope. For a solution, list its projects. For a directory, search only beneath that directory.
- Check for `packages.config`; if found, switch to Guard mode and stop.
- Check the scope and ancestors for `Directory.Packages.props`. If CPM is already fully enabled, report that and stop. If a partial file exists, preserve it and ask only when its intended scope is ambiguous.
- Do not inspect eval definitions, unrelated projects, or the entire repository when the user supplied a scope.

### 2. Capture the baseline

Read [baseline-comparison.md](references/baseline-comparison.md), select the correct package-list syntax once, and then:

1. Clean and build the scope, writing `baseline.binlog`.
2. Write resolved packages to `baseline-packages.json`.
3. Keep normal command output concise. Save full output to artifacts when useful; inspect only errors on failure and never read the binlog as text.

If the baseline build fails, stop without modifying files. Preserve both baseline artifacts.

### 3. Audit with a targeted checklist

Use the baseline snapshot plus one targeted scan of in-scope project, `.props`, and `.targets` files. Identify:

- Package IDs, resolved versions, and consuming projects
- Version conflicts
- MSBuild property-based versions and their definitions
- Conditional `PackageReference` items
- Imported files containing package references
- Existing `VersionOverride` usage

Do not run `--outdated`, `--deprecated`, or `--vulnerable` scans unless the user requested that information or a known concern must be verified. Do not upgrade beyond the highest version already in scope as part of a CPM conversion.

Present conflicts and their impact. If the user supplied a conflict strategy, proceed. Otherwise ask for the unresolved decisions and stop before editing.

### 4. Create CPM files and update references

- Create or update `Directory.Packages.props` at the correct scope with `ManagePackageVersionsCentrally` set to `true`.
- Add one alphabetically sorted `PackageVersion` per package, preserving required target-framework conditions.
- Remove only `Version` from managed `PackageReference` items in projects and imported files.
- Preserve conditions, whitespace, and all other metadata such as `PrivateAssets`, `IncludeAssets`, `ExcludeAssets`, `GeneratePathProperty`, and `Aliases`.
- Use `VersionOverride` only when the chosen strategy requires it.

For MSBuild version properties, follow [msbuild-property-handling.md](references/msbuild-property-handling.md). Remove an inlined property only after validation and only when it has no remaining references.

### 5. Validate and compare

Using [baseline-comparison.md](references/baseline-comparison.md):

1. Clean and build the converted scope, writing `after-cpm.binlog`.
2. Write resolved packages to `after-cpm-packages.json`.
3. Produce a compact per-project changes/unchanged comparison without printing or rereading the full JSON files.

If restore or build fails, read [validation-and-errors.md](references/validation-and-errors.md), inspect only the relevant error lines, make a targeted correction, and rerun the failed validation. Avoid unrelated debugging.

**Do not run `dotnet test` unless the user explicitly requested tests.** If requested tests fail after a successful build, report the failure separately unless it is clearly caused by the CPM changes; do not expand into open-ended dependency debugging.

### 6. Write the report

Read [report-template.md](references/report-template.md) now, not earlier. Create `convert-to-cpm.md` beside the other artifacts. It must include the six required sections, concrete conflict impacts, the package comparison, risk level, follow-ups, and artifact usage.

## Required conversion artifacts

Preserve all five deliverables; they are not temporary files:

- `baseline.binlog`
- `after-cpm.binlog`
- `baseline-packages.json`
- `after-cpm-packages.json`
- `convert-to-cpm.md`

## Efficiency rules

- Batch independent reads and edits when supported.
- Keep full build logs and package JSON out of the conversation; return compact summaries and artifact paths.
- Do not repeat successful commands or reread successful output.
- Do not perform tests, package upgrades, broad security scans, or unrelated repository exploration unless explicitly requested.

## Validation

- [ ] Baseline and converted builds succeeded and both binlogs exist
- [ ] Every managed `PackageReference` has no `Version`, or intentionally uses `VersionOverride`
- [ ] Every managed package has the correct central `PackageVersion`
- [ ] Conditions and non-version metadata were preserved
- [ ] Before/after package comparison contains no unexplained changes
- [ ] Inlined version properties have no remaining references
- [ ] All five required artifacts exist
