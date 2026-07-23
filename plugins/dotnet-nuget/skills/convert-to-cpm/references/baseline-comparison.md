# Baseline Comparison

Verify the CPM conversion is version-neutral by comparing resolved package versions before and after conversion using `dotnet package list`. Binlogs are also captured as artifacts for manual inspection or troubleshooting.

## Capturing package lists

Use the same explicit project or solution target for every command. Always build from a clean state first.

Select the package-list syntax once instead of discovering it through failed commands:

- If `dotnet package list --help` includes `--project`, use `dotnet package list --project <scope> --format json --no-restore`.
- Otherwise use the legacy form `dotnet list <scope> package --format json --no-restore`.
- For a single project when the working directory contains exactly that project, the target may be omitted.

### Baseline (before conversion)

```bash
dotnet clean <scope>
dotnet build <scope> -bl:baseline.binlog
dotnet package list --project <scope> --format json --no-restore > baseline-packages.json
```

### Post-conversion (after all changes)

```bash
dotnet clean <scope>
dotnet build <scope> -bl:after-cpm.binlog
dotnet package list --project <scope> --format json --no-restore > after-cpm-packages.json
```

If `--format json` is not available (requires .NET 8 SDK+), use the default tabular output:

```bash
dotnet package list > baseline-packages.txt
```

For older SDKs, replace each noun-first package-list command above with the legacy form. Do not try both forms after the SDK capability has been determined.

Keep normal output small:

- Redirect routine build output to a log or suppress it. On success, report only status and artifact paths.
- On failure, inspect the relevant error lines or a short tail rather than loading the full build output.
- Never read a binlog as text.
- Preserve package JSON, but use a JSON parser to extract only project path, framework, package ID, requested version, and resolved version. Do not print or read the raw JSON when a compact extraction is available.

## Producing the comparison

Compare `baseline-packages.json` and `after-cpm-packages.json` per project. For each project, identify:

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
| Legacy.csproj | System.Text.Json | 8.0.4 | 9.0.0 | Aligned to highest version |
| Core.csproj | System.Text.Json | 9.0.0 | 9.0.0 | VersionOverride |
| Shared.csproj | Azure.Identity | 1.10.0 | 1.10.0 | VersionOverride |
```

**Unchanged:**

```
| Project | Package | Version |
|---------|---------|---------|
| Api.csproj | System.Text.Json | 10.0.1 |
| Api.csproj | Azure.Storage.Blobs | 12.24.0 |
| Web.csproj | OpenTelemetry.Extensions.Hosting | 1.15.0 |
| Tests.csproj | xunit | 2.9.3 |
```

If there are no changes at all, state that the conversion is fully version-neutral and present only the unchanged table.

## Binlog artifacts

MSBuild binary logs (binlogs) are captured alongside the package list snapshots as supplementary artifacts. Inform the user they are available for manual validation and troubleshooting if needed:

- `baseline.binlog` — Build state before CPM conversion
- `after-cpm.binlog` — Build state after CPM conversion

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
