---
name: build-caching
description: "Guide for MSBuild build caching and avoiding redundant work across builds. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when optimizing CI/CD build times, implementing NuGet restore caching, or leveraging compiler server and deterministic builds. Covers restore caching, VBCSCompiler, CI/CD cache strategies, and deterministic build configuration."
---

## Separating Restore from Build

Run `dotnet restore` separately, then `dotnet build --no-restore` to split network I/O (restore) from CPU-bound work (build). This separation enables better caching because the restore step only needs to re-run when dependencies change.

The `--no-restore` flag is available on `build`, `test`, `pack`, and `publish` commands and skips the implicit restore that normally runs first.

**CI pattern:** restore once at the start of your pipeline, then run build/test/pack without re-restoring:

```yaml
- run: dotnet restore
- run: dotnet build --no-restore
- run: dotnet test --no-restore
- run: dotnet pack --no-restore
```

Use `RestoreLockedMode=true` with a `packages.lock.json` lock file for reproducible restores:

```xml
<PropertyGroup>
  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
</PropertyGroup>
```

Then in CI:

```shell
dotnet restore --locked-mode
```

This ensures the restore exactly matches what was committed, failing if the lock file is out of date.

## NuGet Cache Locations and Caching

NuGet uses several cache locations:

| Cache | Location | Purpose |
|-------|----------|---------|
| Global packages folder | `~/.nuget/packages` (`%USERPROFILE%\.nuget\packages` on Windows) | Extracted package contents |
| HTTP cache | `~/.local/share/NuGet/http-cache` | Raw HTTP responses from feeds |
| Temp folder | System temp directory | Temporary extraction during restore |

List all cache locations with:

```shell
dotnet nuget locals all --list
```

### CI Caching Strategy

Cache `~/.nuget/packages` between CI runs, keyed on the lock file or project file hash.

**GitHub Actions:**

```yaml
- uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: nuget-${{ hashFiles('**/packages.lock.json') }}
    restore-keys: |
      nuget-
```

Or if not using lock files, key on project files:

```yaml
    key: nuget-${{ hashFiles('**/*.csproj') }}
```

**Azure DevOps:**

```yaml
- task: Cache@2
  inputs:
    key: 'nuget | "$(Agent.OS)" | **/packages.lock.json'
    restoreKeys: |
      nuget | "$(Agent.OS)"
    path: $(NUGET_PACKAGES)
```

## Compiler Server (VBCSCompiler)

VBCSCompiler is a long-running background process that keeps the Roslyn compiler warm between compilations. The first compilation pays a cold-start cost (JIT, loading assemblies), but subsequent compilations reuse the warm compiler process for significantly faster builds.

Benefits:
- Avoids repeated JIT compilation of the Roslyn compiler
- Keeps compiler assemblies loaded in memory
- Noticeably faster for multi-project solutions built sequentially

Shared compilation is enabled by default in most scenarios:

```xml
<PropertyGroup>
  <UseSharedCompilation>true</UseSharedCompilation>
</PropertyGroup>
```

### Troubleshooting

If builds hang or files remain locked after a build, VBCSCompiler may be holding file locks. Shut down the compiler server with:

```shell
dotnet build-server shutdown
```

### CI Considerations

In CI environments with ephemeral (short-lived) build agents, the compiler server may not help because each build starts fresh. Consider disabling it to avoid process leaks:

```xml
<PropertyGroup Condition="'$(CI)' == 'true'">
  <UseSharedCompilation>false</UseSharedCompilation>
</PropertyGroup>
```

Related but different: `/nodeReuse:false` prevents MSBuild worker nodes from persisting between builds. This is also recommended in CI:

```shell
dotnet build /nodeReuse:false
```

## Deterministic Builds

Deterministic builds ensure that the same source code compiled with the same compiler produces identical binary output, byte for byte.

```xml
<PropertyGroup>
  <Deterministic>true</Deterministic>
</PropertyGroup>
```

This is the default in SDK-style projects.

### Why Determinism Matters

- **Reproducible builds:** verify that a binary was produced from a specific source commit
- **Binary caching:** identical inputs produce identical outputs, enabling cache hits
- **Source Link:** requires deterministic builds for reliable source mapping

### What Affects Determinism

Without deterministic mode, several things introduce non-determinism:
- Timestamps embedded in PE headers
- Random GUIDs (MVID) generated per compilation
- Absolute file paths embedded in PDBs

Deterministic mode removes timestamps, uses content-based GUIDs, and normalizes paths.

### CI Configuration

Enable `ContinuousIntegrationBuild` in CI to normalize file paths in PDBs (replacing local paths with source control paths):

```xml
<PropertyGroup Condition="'$(CI)' == 'true'">
  <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
</PropertyGroup>
```

For Source Link support, also embed untracked sources:

```xml
<PropertyGroup>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
</PropertyGroup>
```

## CI/CD Build Caching Strategies

### Cache the NuGet Packages Folder

The simplest and safest caching strategy. Key the cache on a hash of dependency-related files:

- `packages.lock.json` (best — exact dependency versions)
- `Directory.Packages.props` + all `*.csproj` files (if not using lock files)

Restore only needs to run when the cache key changes.

### Cache obj/ Folders (Intermediate Outputs)

A more aggressive strategy that caches compilation results in the `obj/` directories.

- **Key:** hash of source files + project files
- **Benefit:** can skip recompilation entirely if sources haven't changed
- **Risk:** stale cache can cause subtle build issues. Clear the cache periodically or on dependency changes.

Consider using `<BaseIntermediateOutputPath>` to control the `obj/` location for easier cache management:

```xml
<PropertyGroup>
  <BaseIntermediateOutputPath>$(MSBuildProjectDirectory)\..\.cache\obj\$(MSBuildProjectName)\</BaseIntermediateOutputPath>
</PropertyGroup>
```

### Incremental CI Builds

For maximum build speed in CI:

- Don't clean on every CI run — let incremental build do its job
- Use persistent (non-ephemeral) build agents with warm caches
- Combine with deterministic builds to validate that cached outputs are correct
- Trust MSBuild's up-to-date checks to skip unnecessary work

## MSBuild Output Caching (Advanced)

MSBuild supports output caching via build plugins, allowing target results to be cached and reused across builds.

- The `/outputResultsCache` flag enables result caching (experimental in some versions)
- Allows caching of target-level results so unchanged projects can skip execution entirely
- This feature is evolving — check the latest MSBuild documentation for current status and plugin availability

## Avoiding Redundant Work — Quick Checklist

- [ ] Separate restore from build
- [ ] Cache NuGet packages in CI
- [ ] Use `--no-restore` on build/test/pack
- [ ] Enable deterministic builds
- [ ] Don't clean on every CI run (use incremental)
- [ ] Use compiler server for local dev
- [ ] Disable compiler server in ephemeral CI if process leaks are a concern
- [ ] Use `dotnet build /graph` for better scheduling
