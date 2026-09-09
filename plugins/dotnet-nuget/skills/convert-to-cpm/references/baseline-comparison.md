# Baseline Comparison

Verify the CPM conversion is version-neutral by comparing resolved package versions before and after conversion using `dotnet package list`. Binlogs are also captured as artifacts for manual inspection or troubleshooting.

## Capturing package lists

Use the same explicit project or solution targets before and after conversion. A directory scope can require multiple targets to cover all projects. Choose one common artifact directory within the resolved scope and use explicit paths into it from every target's command directory. Always build each target from a clean state first.

Create a stable, unique `<target-key>` from each target's path relative to the common artifact directory when more than one target exists. Use it in that target's artifact names so one target cannot overwrite another:

- One target: `baseline.binlog`, `after-cpm.binlog`, `baseline-packages.json`, and `after-cpm-packages.json`.
- Multiple targets: `baseline-<target-key>.binlog`, `after-cpm-<target-key>.binlog`, `baseline-packages-<target-key>.json`, and `after-cpm-packages-<target-key>.json`.

Complete the full baseline sequence for every target before editing any file. Complete the full post-conversion sequence for every target after all edits.

Run `dotnet --version` once from each target's command directory and select that target's package-list syntax by SDK version instead of probing with commands that may fail:

- SDK 10 or later: use `dotnet package list --project <scope> --format json --include-transitive --no-restore`.
- SDK 7.0.200 through 9.x: use `dotnet list <scope> package --format json --include-transitive --no-restore`.
- SDK older than 7.0.200 cannot produce the required JSON snapshots; stop and report that SDK 7.0.200 or later is required for this workflow.
- For a single project when the working directory contains exactly that project, the target may be omitted.
- A `.slnx` scope requires SDK 9.0.201 or later so build, restore, and package-list operations all support the format. If it is unsupported, stop and report the prerequisite.

If `dotnet --version` fails, do not try roll-forward overrides, install an SDK, create a temporary `global.json`, or invoke SDK assemblies directly. Report the SDK required by the existing `global.json` or project and stop.

Set `<baseline-binlog>`, `<after-binlog>`, `<baseline-packages>`, and `<after-packages>` below to explicit paths in the common artifact directory, using the target-keyed names when applicable.

### Baseline for each target (before conversion)

```bash
dotnet clean <scope>
dotnet restore <scope>
dotnet build <scope> --no-restore -bl:<baseline-binlog>
```

Then run exactly one package-list command for the active SDK:

```bash
# SDK 10 or later
dotnet package list --project <scope> --format json --include-transitive --no-restore > <baseline-packages>

# SDK 7.0.200 through 9.x
dotnet list <scope> package --format json --include-transitive --no-restore > <baseline-packages>
```

### Post-conversion for each target (after all changes)

```bash
dotnet clean <scope>
dotnet restore <scope>
dotnet build <scope> --no-restore -bl:<after-binlog>
```

Then run exactly one package-list command for the active SDK:

```bash
# SDK 10 or later
dotnet package list --project <scope> --format json --include-transitive --no-restore > <after-packages>

# SDK 7.0.200 through 9.x
dotnet list <scope> package --format json --include-transitive --no-restore > <after-packages>
```

Do not try both package-list forms after the SDK version has been determined.

Keep normal output small:

- Redirect routine build output to a log or suppress it. On success, report only status and artifact paths.
- On failure, inspect the relevant error lines or a short tail rather than loading the full build output.
- Never read a binlog as text.
- Preserve package JSON, but use a JSON parser to extract only project path, framework, package ID, requested version, and resolved version. Do not print or read the raw JSON when a compact extraction is available.

## Producing the comparison

Compare each target's baseline and post-conversion package files, then aggregate results by project. Deduplicate projects that appeared in overlapping targets. For each project, identify:

1. **Version changes**: Packages whose resolved version differs.
2. **Added packages**: Packages present after conversion but not in the baseline.
3. **Removed packages**: Packages present in the baseline but not after conversion.
4. **VersionOverride entries**: Packages that use `VersionOverride` (their version matches baseline but the mechanism changed).
5. **Transitive changes**: If `CentralPackageTransitivePinningEnabled` was set, note any transitive packages that are now pinned.

### Example comparison tables

Present changes and unchanged packages in separate tables. The **Changes** table highlights anything that differs from baseline — version alignment from conflict resolution, `VersionOverride` entries, and added/removed packages. The **Unchanged** table lists everything else for reference and confidence.

**Changes:**

```
| Project | Package | Before | After | Status |
|---------|---------|--------|-------|--------|
| ProjectA.csproj | PackageA | 1.0.0 | 2.0.0 | Aligned to highest version |
| ProjectB.csproj | PackageA | 1.0.0 | 1.0.0 | VersionOverride |
| ProjectC.csproj | PackageB | — | 3.1.0 | Added |
```

**Unchanged:**

```
| Project | Package | Version |
|---------|---------|---------|
| ProjectA.csproj | PackageB | 3.1.0 |
| ProjectB.csproj | PackageC | 4.2.0 |
```

If there are no changes at all, state that the conversion is fully version-neutral and present only the unchanged table.

## Binlog artifacts

MSBuild binary logs (binlogs) are captured alongside the package list snapshots as supplementary artifacts. Inform the user they are available for manual validation and troubleshooting if needed:

- `baseline.binlog` and `after-cpm.binlog` — Build state before and after a single-target conversion
- Target-keyed binlog pairs — Build state before and after each target in a multi-target conversion

The user can learn more about MSBuild binary logs from:
- [Troubleshoot and create logs for MSBuild problems](https://learn.microsoft.com/visualstudio/ide/msbuild-logs?view=visualstudio#provide-msbuild-binary-logs-for-investigation)
- [Obtaining Build Logs with MSBuild](https://learn.microsoft.com/visualstudio/msbuild/obtaining-build-logs-with-msbuild?view=visualstudio#save-a-binary-log)
- https://github.com/dotnet/msbuild/blob/main/documentation/wiki/Binary-Log.md

## When comparison reveals unexpected differences

If the post-conversion package list resolves different versions than expected (beyond intentional changes like version conflict alignment or `VersionOverride`), investigate:

- Missing `<PackageVersion>` entries causing fallback behavior
- Conditional `<PackageVersion>` entries not matching the project's target framework
- Import order issues where a property referenced in `Directory.Packages.props` is not yet defined
- Transitive dependency resolution differences from version alignment
- Packages unexpectedly added or removed due to conditional ItemGroup changes

The binlogs can help diagnose these issues by showing the full MSBuild evaluation and package resolution. Flag any unexpected differences to the user before considering the conversion complete.
