---
name: convert-to-cpm
description: >
  Convert .NET projects and solutions (.sln, .slnx) to NuGet Central Package Management
  (CPM) using Directory.Packages.props. USE FOR: converting to CPM, centralizing or
  aligning NuGet package versions across multiple projects, inlining MSBuild version
  properties from Directory.Build.props into Directory.Packages.props, resolving version
  conflicts or mismatches across a solution or repository, updating or bumping or syncing
  package versions across projects. Also activate when packages are out of sync, drifting,
  or inconsistent — even without the user mentioning CPM. Provides baseline build capture,
  version conflict resolution, build validation with binlog comparison, and a structured
  post-conversion report. DO NOT USE FOR: packages.config projects (must migrate to
  PackageReference first) or repositories that already have CPM fully enabled.
---

# Convert to Central Package Management

Migrate .NET projects from per-project package versioning to NuGet Central Package Management (CPM). CPM centralizes all package versions into a single `Directory.Packages.props` file, making version governance and upgrades easier across multi-project repositories.

## When to Use

- The user wants to adopt Central Package Management for a .NET repository, solution, or project
- Package versions are scattered across many `.csproj`, `.fsproj`, or `.vbproj` files and the user wants a single source of truth
- The user mentions `Directory.Packages.props`, CPM, or centralizing NuGet versions
- The user wants to update, bump, upgrade, align, or sync a NuGet package version across multiple projects — CPM is the recommended approach for managing shared package versions, so suggest converting to CPM as part of the update if the projects use `PackageReference` and CPM is not already enabled
- Package versions are out of sync, conflicting, or mismatched across projects and the user wants to resolve or unify them

## When Not to Use

- The repository already has CPM fully enabled for all in-scope projects
- The user is working with `packages.config`-based projects (must first migrate to `PackageReference`)
- The user wants to manage versions via a custom MSBuild property file without using CPM

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Scope | Yes | A project file, solution file, or directory containing .NET projects to convert |
| Version conflict strategy | No | How to resolve cases where the same package has different versions across projects. When conflicts are detected, do not assume a default strategy — ask the user which strategy to use or explicitly confirm a proposed strategy before proceeding. |

## Workflow

### Step 1: Determine scope

- **Single project**: User specifies a `.csproj`, `.fsproj`, or `.vbproj`.
- **Solution**: User specifies a `.sln` or `.slnx`. List projects with `dotnet sln list`.
- **Repository/directory**: No specific file given. Find all project files recursively from the first common ancestor directory of all .NET projects in scope.

If the scope is unclear, ask the user.

**Guard: Check for packages.config projects.** Before proceeding, check whether any project in scope uses `packages.config` instead of `PackageReference`. Look for `packages.config` files alongside project files. If any `packages.config` usage is detected, **stop and do not proceed with the conversion**. Inform the user that CPM requires projects with `PackageReference` format and that they must first migrate from `packages.config` to `PackageReference` (e.g., using Visual Studio's built-in migration or the `dotnet migrate` tooling). This skill cannot perform that migration.

### Step 2: Establish baseline build

Before making any changes, verify the scope builds successfully and capture a baseline binlog and package list. Run `dotnet clean`, then `dotnet build -bl:baseline.binlog`, then `dotnet package list --format json > baseline-packages.json`. Read [baseline-comparison.md](references/baseline-comparison.md) for the full procedure and fallback options. If the baseline build fails, stop and inform the user — the scope must build cleanly before conversion. Do not delete `baseline.binlog` or `baseline-packages.json` — they are needed for the post-conversion comparison and report.

### Step 3: Check for existing CPM

Search for any existing `Directory.Packages.props` in scope or ancestor directories. If CPM is already fully enabled, inform the user and stop. If a `Directory.Packages.props` exists without CPM enabled, ask whether to add the property to the existing file or create a new one.

### Step 4: Audit package references

Run `dotnet package list --format json` to get the resolved package references across all in-scope projects. Also scan `<Import>` elements to discover shared `.props`/`.targets` files containing package references.

Check for complexities: version conflicts, MSBuild property-based versions, conditional references, security advisories, and existing `VersionOverride` usage. Read [audit-complexities.md](references/audit-complexities.md) for the full checklist.

Present audit results to the user before proceeding, including:
- A table of each package, its version(s), and which projects use it
- Any version conflicts, security advisories, or complexities requiring decisions

When version conflicts exist, present each one individually with the affected projects, the distinct versions found, and the resolution options (align to highest, use `VersionOverride`, etc.) with their trade-offs. Do not upgrade any package beyond the highest version already in use across the scope — this avoids introducing version incompatibilities or breaking changes that are unrelated to the CPM conversion itself. Note any known security advisories or other upgrade opportunities as follow-up items for the user to address after the conversion is complete. Ask the user to decide on each conflict before proceeding. Read [audit-complexities.md § Same package with different versions](references/audit-complexities.md) for the resolution workflow and presentation format.

### Step 5: Create or update Directory.Packages.props

Create the file with `dotnet new packagesprops` (.NET 8+) or manually. Add a `<PackageVersion>` entry for each unique package sorted alphabetically. For conditional versions or `VersionOverride` patterns, read [directory-packages-props.md](references/directory-packages-props.md).

### Step 6: Update project files

Remove the `Version` attribute from every `<PackageReference>` that now has a corresponding `<PackageVersion>`. Also update any shared `.props`/`.targets` files identified in step 4.

- Preserve all other attributes (`PrivateAssets`, `IncludeAssets`, `ExcludeAssets`, `GeneratePathProperty`, `Aliases`)
- Preserve conditional `<ItemGroup>` elements — only remove the `Version` attribute within them
- Retain each file's existing indentation style (spaces vs. tabs, indentation depth) and blank lines — do not reformat or reorganize unchanged lines
- Use `VersionOverride` (with user confirmation) when a project needs a different version than the central one

### Step 7: Handle MSBuild version properties

For `PackageReference` items that used MSBuild properties for versions, determine whether to inline the resolved value or keep the property reference in `Directory.Packages.props`. After validation succeeds in step 8, remove inlined version properties from `Directory.Build.props` or other files, verifying they have no remaining references. Read [msbuild-property-handling.md](references/msbuild-property-handling.md) for the decision workflow, import order requirements, and cleanup procedure.

### Step 8: Restore and validate

Run a clean restore and build, capturing a post-conversion binlog and package list. Run `dotnet clean`, then `dotnet build -bl:after-cpm.binlog`, then `dotnet package list --format json > after-cpm-packages.json`. Read [baseline-comparison.md](references/baseline-comparison.md) for the full procedure. If errors occur, read [validation-and-errors.md](references/validation-and-errors.md) for NuGet error codes and multi-TFM guidance.

**Do not delete or clean up any artifacts** (`baseline.binlog`, `after-cpm.binlog`, `baseline-packages.json`, `after-cpm-packages.json`). These files must be preserved for the user to inspect after the conversion. They are deliverables, not temporary files.

### Step 9: Post-conversion report

**You must create a `convert-to-cpm.md` file** alongside the binlog and JSON artifacts. Do not skip this step or substitute inline chat output for the file — the user needs a persistent, shareable document. This file should be self-contained and shareable — suitable for a pull request description, a team review, or a record of what was done. Structure the report with the following sections:

#### Section 1: Conversion overview

Summarize what was converted: the scope (project, solution, or repository), number of projects converted, total packages centralized, any projects or packages that were skipped, and any MSBuild properties that were inlined or removed. This gives the reader immediate context.

#### Section 2: Version conflict resolutions

If any version conflicts were encountered, list each one with:

- The package name and all versions that were found across projects
- Which projects used each version
- What the user decided (aligned to highest, used `VersionOverride`, etc.)
- The practical impact: which projects now resolve a different version than before, and which are unchanged

If no conflicts were found, state that all packages had consistent versions across projects — this is a positive signal worth noting.

#### Section 3: Package comparison — baseline vs. result

Compare `baseline-packages.json` and `after-cpm-packages.json` per project. See [baseline-comparison.md](references/baseline-comparison.md) for the comparison procedure. Present two tables:

- **Changes table**: Packages where the resolved version changed, a `VersionOverride` was introduced, or a package was added/removed. Include a status column explaining what changed and why (e.g., "VersionOverride — project retains pinned version", "Aligned to highest version").
- **Unchanged table**: All other packages, confirming they resolve identically to baseline.

If there are no changes at all, state that the conversion is fully version-neutral — this is the ideal outcome and provides reassurance.

#### Section 4: Risk assessment

Provide a clear confidence statement:

- **✅ Low risk** — Conversion is version-neutral; all packages resolve to the same versions as baseline. The build and restore succeeded. Recommend running `dotnet test` as a final check.
- **⚠️ Moderate risk** — Some packages changed versions (e.g., minor/patch alignment). List the affected packages and projects. Recommend reviewing the changes table and running `dotnet test` to verify no regressions.
- **🔴 High risk** — Major version changes were applied, or packages were added/removed unexpectedly. Recommend careful review, running `dotnet test`, and comparing binlogs before merging.

Call out any specific warnings: `VersionOverride` usage that partially undermines centralization, or MSBuild property removal that could affect other build logic.

#### Section 5: Follow-up items

List any items identified during the conversion that the user should address separately after the CPM conversion is complete. These are intentionally out of scope for the conversion itself but important for the user to act on. Common follow-up items include:

- **Security advisories**: If any package versions are known to have security vulnerabilities (detected via `dotnet package list --vulnerable` or noted during the audit), list each advisory with the package name, current version, affected projects, and the minimum patched version. These upgrades are out of scope for the CPM conversion to avoid introducing version incompatibilities or breaking changes.
- **Deprecated packages**: If any packages are deprecated, note the recommended replacement.
- **Version alignment opportunities**: If `VersionOverride` was used to preserve differing versions, note that the user may want to align these in the future once the affected projects can be validated against the central version.
- **Test validation**: Recommend running `dotnet test` to validate runtime behavior beyond build success, especially if any version conflicts were resolved by aligning to the highest version.

Present follow-up items as a numbered checklist so the user can track them.

#### Section 6: Artifacts and how to use them

List the artifacts produced during conversion and explain how to use them:

- **`baseline.binlog`** and **`after-cpm.binlog`** — MSBuild binary logs captured before and after conversion. These are available for manual validation and troubleshooting if needed.
- **`baseline-packages.json`** and **`after-cpm-packages.json`** — Machine-readable snapshots of resolved package versions per project, used to produce the comparison tables above.
- **`convert-to-cpm.md`** — This report file, suitable for use as a pull request description or team review artifact.

Recommend the user run `dotnet test` to validate runtime behavior beyond build success. If any version conflicts were resolved by aligning to the highest version, recommend reviewing the release notes for the affected packages.

## Validation

- [ ] Baseline build succeeded before any changes were made
- [ ] `Directory.Packages.props` exists with `ManagePackageVersionsCentrally` set to `true`
- [ ] Every in-scope `PackageReference` either has no `Version` attribute or uses `VersionOverride`
- [ ] Every referenced package has a corresponding `PackageVersion` entry
- [ ] `dotnet restore` and `dotnet build` complete without errors from a clean state
- [ ] Package list comparison shows no unexpected version changes
- [ ] No orphaned version properties remain (unless intentionally kept)

## More Info

- [Central Package Management documentation](https://github.com/NuGet/docs.microsoft.com-nuget/blob/main/docs/consume-packages/Central-Package-Management.md)
- [Validation and common errors](references/validation-and-errors.md)
