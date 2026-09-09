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
2. **Package-maintenance mode** -- A request to update, align, bump, or sync packages authorizes those package edits, not CPM conversion. Audit the named scope, resolve the requested versions, update existing project/shared version declarations, and restore/build every affected CLI target from the directory that establishes its applicable `global.json`. Ask only when the version or alignment policy is ambiguous. Do not create or modify `Directory.Packages.props`, remove versions for CPM, or capture conversion artifacts. Complete the package work, then recommend CPM as the durable follow-up.
3. **Conversion mode** -- Use only when the user explicitly asks to adopt, enable, or convert to CPM. Follow the workflow below.

If the scope is unclear, ask once before proceeding.

### Default execution plan

- **Guard**: use a minimal scoped detection pass, then answer and stop.
- **Package maintenance**: use a compact audit, edit only the requested package versions in their existing locations, validate affected targets, then recommend CPM. Do not read conversion references or enter the conversion workflow.
- **Conversion**: batch the preflight, baseline, audit/mutation, final validation, and report work to avoid redundant turns. Revisit a stage only when new CPM-specific evidence requires a targeted follow-up.

This plan is an efficiency default, not a hard cap. Never omit an in-scope project, imported `.props`/`.targets` file, detected complexity, required validation, or deliverable to save a turn. Batch complete work where practical.

## Inputs

| Input | Required | Rule |
|-------|----------|------|
| Scope | Yes | Project, solution, or directory containing the projects to inspect or convert |
| Conflict strategy | For package maintenance or conversion with conflicts | If the user already supplied a strategy such as "use the highest version," apply it without asking again and record its impact. Otherwise stop after the audit and ask before editing. |

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

- Resolve the project/solution scope. For a solution, list its projects. For a directory, search only beneath that directory and create an explicit target set that covers the full scope: use each applicable `.sln`/`.slnx`, then add each project not covered by a solution. Verify that every in-scope project is covered and avoid duplicate work for projects that occur in more than one target. Ask only when overlapping targets or repository boundaries make the intended coverage ambiguous; never ask the user to select one target when that would omit in-scope projects.
- Determine CPM management scopes separately from CLI targets. Group projects that will share one central version policy and place one `Directory.Packages.props` at each group's first common ancestor, while respecting existing nearest-file boundaries. Multiple CLI targets can share one CPM file; independent project groups can require separate files.
- Check for `packages.config`; if found, switch to Guard mode and stop.
- Check the scope and ancestors for `Directory.Packages.props`. If CPM is already fully enabled, report that and stop. If a partial file exists, preserve it and ask only when its intended scope is ambiguous.
- Choose one common artifact directory within the resolved scope, normally the targets' first common ancestor. Use explicit paths into it for every binlog, package snapshot, and the report.
- Run each target's .NET commands from its solution/project directory or another directory that establishes its applicable `global.json`, not from an unrelated parent workspace.
- Do not inspect unrelated projects or host-tool configuration when the user supplied a scope.

### 2. Capture the baseline

Read [baseline-comparison.md](references/baseline-comparison.md). For each target, determine the active SDK once from that target's command directory and select the documented command syntax for that version. If SDK resolution fails or the SDK cannot process the requested solution format, stop and report the prerequisite; do not alter the host SDK or repository SDK policy unless the user asks.

Then use one command batch to:

1. Clean, restore, and build every explicit target. Use `baseline.binlog` for one target or a unique `baseline-<target-key>.binlog` for each of multiple targets.
2. Write resolved packages for every target without restoring again. Use `baseline-packages.json` for one target or a matching `baseline-packages-<target-key>.json` for each of multiple targets.
3. Keep normal command output concise. Save full output to artifacts when useful; inspect only errors on failure and never read the binlog as text.

Finish every baseline before editing. If any baseline build fails, stop without modifying files and preserve all artifacts already produced.

### 3. Audit with a targeted checklist

Use all baseline snapshots plus one targeted scan of in-scope project, `.props`, and `.targets` files. Identify:

- Package IDs, resolved versions, and consuming projects
- Version conflicts
- MSBuild property-based versions and their definitions
- Conditional `PackageReference` items
- Imported files containing package references
- Existing `VersionOverride` usage

For a complex scope, complete every applicable item above across all projects and imported files; do not stop after finding the first conflict.

Do not run broad `--outdated` or `--deprecated` scans by default. Before editing, attempt a scoped `--vulnerable --include-transitive` query when the user requested security information, a known advisory must be verified, or conflict resolution will move a project across a major package version. Record the compact findings, "no advisories found," or why the check could not run. If a high-risk check is unavailable because of authentication, package-source, or offline constraints, surface the uncertainty and confirm the user's strategy rather than silently treating it as safe. Do not upgrade beyond the highest version already in scope as part of a CPM conversion.

Present conflicts and their impact. Explicitly classify major-version alignment as high risk and minor/patch alignment as moderate risk without performing an extra online scan. If the user supplied a conflict strategy, proceed. Otherwise ask for the unresolved decisions and stop before editing.

### 4. Create CPM files and update references

- Create or update each required `Directory.Packages.props` at its computed management scope with `ManagePackageVersionsCentrally` set to `true`.
- Add one alphabetically sorted `PackageVersion` per package, preserving required target-framework conditions.
- Remove only `Version` from managed `PackageReference` items in projects and imported files.
- Preserve conditions, whitespace, and all other metadata such as `PrivateAssets`, `IncludeAssets`, `ExcludeAssets`, `GeneratePathProperty`, and `Aliases`.
- Use `VersionOverride` only when the chosen strategy requires it.

For MSBuild version properties, follow [msbuild-property-handling.md](references/msbuild-property-handling.md). When the user directs inlining, include both the literal `PackageVersion` and removal of the obsolete property definition in the same mutation batch. Before final validation, verify separately that:

1. No `$(PropertyName)` references remain in scoped project, `.props`, or `.targets` files.
2. No `<PropertyName>...</PropertyName>` definition remains for each property chosen for removal.

Do not rely on a `$()` reference scan to prove that the XML property definition was removed.

### 5. Validate and compare

Using [baseline-comparison.md](references/baseline-comparison.md), validate the final on-disk state after all project, shared-file, and property edits. Use one command batch to:

1. Clean, restore, and build every explicit target after all CPM edits. Use `after-cpm.binlog` for one target or a matching `after-cpm-<target-key>.binlog` for each of multiple targets.
2. Write resolved packages for every target without restoring again. Use `after-cpm-packages.json` for one target or a matching `after-cpm-packages-<target-key>.json` for each of multiple targets.
3. Produce a compact per-project changes/unchanged comparison without printing or rereading the full JSON files.
4. If resolved versions changed and the repository exposes a routine, scoped test command for affected projects, run it with `--no-build --no-restore` and record the result. If tests require substantial setup, broad infrastructure, or user approval, recommend the exact scoped command instead. A version-neutral conversion does not require an automatic test run.

If restore or build fails with a CPM-related error, read [validation-and-errors.md](references/validation-and-errors.md), inspect only the relevant error lines, make a targeted correction, and rerun the affected validation. For SDK, authentication, package-source, file-lock, test-host, or other environmental failures, report the blocker instead of changing the machine or expanding the investigation.

If a test run fails after a successful build, inspect only enough output to determine whether CPM package resolution caused it. Apply a targeted correction only when the evidence clearly identifies a CPM defect; otherwise record the failure and recommended user action without expanding into test-host, SDK, output-directory, or dependency-copy debugging.

### 6. Write the report

Read [report-template.md](references/report-template.md) now, not earlier. Create `convert-to-cpm.md` beside the other artifacts. It must include the six required sections, every explicit target and CPM management scope, concrete conflict impacts, the aggregate package comparison, risk level, follow-ups, artifact usage, and the name of every shared `.props`/`.targets` file inspected or changed. In the final response, mention those shared files, the risk level, and how any conditional references and target frameworks were preserved. Avoid rewriting the report after validation unless verification finds an omission or incorrect evidence.

## Required conversion artifacts

Preserve the report and every target's four evidence files; they are not temporary files. For one target, the five deliverables are:

- `baseline.binlog`
- `after-cpm.binlog`
- `baseline-packages.json`
- `after-cpm-packages.json`
- `convert-to-cpm.md`

For multiple targets, replace the four fixed evidence names with unique target-keyed pairs such as `baseline-api.binlog`, `after-cpm-api.binlog`, `baseline-packages-api.json`, and `after-cpm-packages-api.json`. Keep one aggregate `convert-to-cpm.md`.

## Efficiency rules

- Batch independent reads and edits when supported.
- Keep full build logs and package JSON out of the conversation; return compact summaries and artifact paths.
- Do not repeat successful commands or reread successful output.
- In conversion mode, do not perform package upgrades, broad outdated/deprecated scans, repeated tests, or unrelated repository exploration. The single conditional vulnerability query and test run defined above are part of complete high-risk conversion validation. Package maintenance can perform the requested upgrades and one scoped version-discovery query needed to resolve them.
- Do not install or remove an SDK, create a temporary SDK selector, change roll-forward policy, invoke SDK-internal assemblies, kill unrelated processes, or clean host tooling/temp infrastructure. Report an environment prerequisite and stop.

## Validation

- [ ] Baseline and converted builds succeeded for every explicit target and all target binlogs exist
- [ ] Every managed `PackageReference` has no `Version`, or intentionally uses `VersionOverride`
- [ ] Every managed package has the correct central `PackageVersion`
- [ ] Conditions and non-version metadata were preserved
- [ ] Before/after package comparison contains no unexplained changes
- [ ] Inlined version properties have neither remaining `$()` references nor obsolete XML definitions
- [ ] The report and all per-target baseline and converted artifacts exist
