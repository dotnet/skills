# Baseline Comparison

Verify the CPM conversion is version-neutral by comparing resolved package versions before and after conversion using `dotnet package list`. Binlogs are also captured as artifacts for manual inspection or troubleshooting.

## Capturing package lists

Use `dotnet package list` to snapshot resolved versions. Always build from a clean state first to ensure accurate resolution.

### Baseline (before conversion, step 2)

```bash
dotnet clean
dotnet build -bl:baseline.binlog
dotnet package list --format json > baseline-packages.json
```

### Post-conversion (after all changes, step 8)

```bash
dotnet clean
dotnet build -bl:after-cpm.binlog
dotnet package list --format json > after-cpm-packages.json
```

If `--format json` is not available (requires .NET 8 SDK+), use the default tabular output:

```bash
dotnet package list > baseline-packages.txt
```

For solution-scoped conversions, pass the solution file to all commands.

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

MSBuild binary logs (binlogs) capture the full structured build event stream, including every resolved package reference, property evaluation, and target execution. They are produced alongside the package list captures as supplementary artifacts. Inform the user they are available for manual review:

- `baseline.binlog` — Build state before CPM conversion
- `after-cpm.binlog` — Build state after CPM conversion

The user can open these `.binlog` files in the [MSBuild Structured Log Viewer](https://msbuildlog.com/) for detailed inspection of the full build tree, including target execution, property evaluation, and item resolution.

```bash
# Install the viewer on Windows
winget install KirillOsenkov.MSBuildStructuredLogViewer

# Or download from https://msbuildlog.com/
```

## When comparison reveals unexpected differences

If the post-conversion package list resolves different versions than expected (beyond intentional changes like version conflict alignment or `VersionOverride`), investigate:

- Missing `<PackageVersion>` entries causing fallback behavior
- Conditional `<PackageVersion>` entries not matching the project's target framework
- Import order issues where a property referenced in `Directory.Packages.props` is not yet defined
- Transitive dependency resolution differences from version alignment
- Packages unexpectedly added or removed due to conditional ItemGroup changes

The binlogs can help diagnose these issues by showing the full MSBuild evaluation and package resolution. Flag any unexpected differences to the user before considering the conversion complete.
