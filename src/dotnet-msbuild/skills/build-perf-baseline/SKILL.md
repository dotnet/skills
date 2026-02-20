---
name: build-perf-baseline
description: "Establish build performance baselines and apply systematic optimization techniques. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when diagnosing slow builds, establishing before/after measurements, or applying advanced optimization strategies like MSBuild Server, static graph builds, artifacts output, and dependency graph trimming. Start here before diving into specific optimizations from build-perf-diagnostics, incremental-build, or build-parallelism skills. DO NOT use for non-MSBuild build systems."
---

# Build Performance Baseline & Optimization

## Overview

Before optimizing a build, you need a **baseline**. Without measurements, optimization is guesswork. This skill covers how to establish baselines and apply systematic optimization techniques.

**Related skills:**
- `build-perf-diagnostics` — binlog-based bottleneck identification
- `incremental-build` — Inputs/Outputs and up-to-date checks
- `build-parallelism` — parallel and graph build tuning
- `build-caching` — NuGet cache, VBCSCompiler, CI strategies
- `eval-performance` — glob and import chain optimization

---

## Step 1: Establish a Performance Baseline

Measure three scenarios to understand where time is spent:

### Cold Build (First Build)

No previous build output exists. Measures the full end-to-end time including restore, compilation, and all targets.

```bash
# Clean everything first
dotnet clean
# Remove bin/obj to truly start fresh
Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force
# OR on Linux/macOS:
# find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +

# Measure cold build
dotnet build /bl:cold-build.binlog -m
```

### Warm Build (Incremental Build)

Build output exists, some files have changed. Measures how well incremental build works.

```bash
# Build once to populate outputs
dotnet build -m

# Make a small change (touch one .cs file)
# Then rebuild
dotnet build /bl:warm-build.binlog -m
```

### No-Op Build (Nothing Changed)

Build output exists, nothing has changed. This should be nearly instant. If it's slow, incremental build is broken.

```bash
# Build once to populate outputs
dotnet build -m

# Rebuild immediately without changes
dotnet build /bl:noop-build.binlog -m
```

### What Good Looks Like

| Scenario | Expected Behavior |
|----------|------------------|
| Cold build | Full compilation, all targets run. This is your absolute baseline |
| Warm build | Only changed projects recompile. Time proportional to change scope |
| No-op build | < 5 seconds for small repos, < 30 seconds for large repos. All compilation targets should report "Skipping target — all outputs up-to-date" |

**Red flags:**
- No-op build > 30 seconds → incremental build is broken (see `incremental-build` skill)
- Warm build recompiles everything → project dependency chain forces full rebuild
- Cold build has long restore → NuGet cache issues (see `build-caching` skill)

### Recording Baselines

Record baselines in a structured way before and after optimization:

```
| Scenario    | Before  | After   | Improvement |
|-------------|---------|---------|-------------|
| Cold build  | 2m 15s  |         |             |
| Warm build  | 1m 40s  |         |             |
| No-op build | 45s     |         |             |
```

---

## Step 2: MSBuild Server (Persistent Build Process)

The MSBuild server keeps the build process alive between invocations, avoiding JIT compilation and assembly loading overhead on every build.

### Enabling MSBuild Server

```bash
# Enabled by default in .NET 8+ but can be forced
dotnet build /p:UseSharedCompilation=true
```

The MSBuild server is started automatically and reused across builds. The compiler server (VBCSCompiler / `dotnet build-server`) is separate but complementary.

### Managing the Build Server

```bash
# Check if the server is running
dotnet build-server status

# Shut down all build servers (useful when debugging)
dotnet build-server shutdown
```

### When to Restart the Build Server

Restart after:
- Updating the .NET SDK
- Changing MSBuild tooling (custom tasks, props, targets)
- Debugging build infrastructure issues
- Seeing stale behavior in repeated builds

```bash
dotnet build-server shutdown
dotnet build
```

---

## Step 3: Artifacts Output Layout

The `UseArtifactsOutput` feature (introduced in .NET 8) changes the output directory structure to avoid bin/obj clash issues and enable better caching.

### Enabling Artifacts Output

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <UseArtifactsOutput>true</UseArtifactsOutput>
</PropertyGroup>
```

### Before vs After

```
# Traditional layout (before)
src/
  MyLib/
    bin/Debug/net8.0/MyLib.dll
    obj/Debug/net8.0/...
  MyApp/
    bin/Debug/net8.0/MyApp.dll

# Artifacts layout (after)
artifacts/
  bin/MyLib/debug/MyLib.dll
  bin/MyApp/debug/MyApp.dll
  obj/MyLib/debug/...
  obj/MyApp/debug/...
```

### Benefits

- **No bin/obj clash**: Each project+configuration gets a unique path automatically
- **Easier to cache**: Single `artifacts/` directory to cache/restore in CI
- **Cleaner .gitignore**: Just ignore `artifacts/`
- **Multi-targeting safe**: Each TFM gets its own subdirectory

### Customizing

```xml
<!-- Change the artifacts root -->
<PropertyGroup>
  <ArtifactsPath>$(MSBuildThisFileDirectory)output</ArtifactsPath>
</PropertyGroup>
```

---

## Step 4: Deterministic Builds

Deterministic builds produce byte-for-byte identical output given the same inputs. This is essential for build caching and reproducibility.

### Enabling Deterministic Builds

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <!-- Enabled by default in .NET SDK projects since SDK 2.0+ -->
  <Deterministic>true</Deterministic>

  <!-- For full reproducibility, also set: -->
  <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
</PropertyGroup>
```

### What Deterministic Affects

- Removes timestamps from PE headers
- Uses consistent file paths in PDBs
- Produces identical output for identical input

### Why It Matters for Performance

- **Build caching**: If outputs are deterministic, you can cache and reuse them across builds and machines
- **CI optimization**: Skip rebuilding unchanged projects by comparing inputs
- **Distributed builds**: Safe to cache compilation results in shared storage

---

## Step 5: Dependency Graph Trimming

Reducing unnecessary project references shortens the critical path and reduces what gets built.

### Audit the Dependency Graph

```bash
# Visualize the dependency graph
dotnet build /bl:graph.binlog

# In the binlog, check project references and build times
# Look for projects that are referenced but could be trimmed
```

### Techniques

#### Remove Redundant Transitive References

```xml
<!-- BAD: Utils is already referenced transitively via Core -->
<ItemGroup>
  <ProjectReference Include="..\Core\Core.csproj" />
  <ProjectReference Include="..\Utils\Utils.csproj" />
</ItemGroup>

<!-- GOOD: Let transitive references flow automatically -->
<ItemGroup>
  <ProjectReference Include="..\Core\Core.csproj" />
</ItemGroup>
```

#### Build-Order-Only References

When you need a project to build before yours but don't need its assembly output:

```xml
<!-- Only ensures build order, doesn't reference the output assembly -->
<ProjectReference Include="..\CodeGen\CodeGen.csproj"
                  ReferenceOutputAssembly="false" />
```

#### Prevent Transitive Flow

When a dependency is an internal implementation detail that shouldn't flow to consumers:

```xml
<!-- Don't expose this dependency transitively -->
<ProjectReference Include="..\InternalHelpers\InternalHelpers.csproj"
                  PrivateAssets="all" />
```

#### Disable Transitive Project References

For explicit-only dependency management (extreme measure for very large repos):

```xml
<PropertyGroup>
  <DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>
</PropertyGroup>
```

**Caution**: This requires all dependencies to be listed explicitly. Only use in large repos where transitive closure is causing excessive rebuilds.

---

## Step 6: Static Graph Builds (`/graph`)

Static graph mode evaluates the entire project graph before building, enabling better scheduling and isolation.

### Enabling Graph Build

```bash
# Single invocation
dotnet build /graph

# With binary log for analysis
dotnet build /graph /bl:graph-build.binlog
```

### Benefits

- **Better parallelism**: MSBuild knows the full graph upfront and can schedule optimally
- **Build isolation**: Each project builds in isolation (no cross-project state leakage)
- **Caching potential**: With isolation, individual project results can be cached

### When to Use

| Scenario | Recommendation |
|----------|---------------|
| Large multi-project solution (20+ projects) | ✅ Try `/graph` — may see significant parallelism gains |
| Small solution (< 5 projects) | ❌ Overhead of graph evaluation outweighs benefits |
| CI builds | ✅ Graph builds are more predictable and parallelizable |
| Local development | ⚠️ Test both — may or may not help depending on project structure |

### Troubleshooting Graph Build

Graph build requires that all `ProjectReference` items are statically determinable (no dynamic references computed in targets). If graph build fails:

```
error MSB4260: Project reference "..." could not be resolved with static graph.
```

**Fix**: Ensure all `ProjectReference` items are declared in `<ItemGroup>` outside of targets (not dynamically computed inside `<Target>` blocks).

---

## Step 7: Parallel Build Tuning

### MaxCpuCount

```bash
# Use all available cores (default in dotnet build)
dotnet build -m

# Specify explicit core count (useful for CI with shared agents)
dotnet build -m:4

# MSBuild.exe syntax
msbuild /m:8 MySolution.sln
```

### Identifying Parallelism Bottlenecks

In a binlog, look for:
- **Long sequential chains**: Projects that must build one after another due to dependencies
- **Uneven load**: Some build nodes idle while others are overloaded
- **Single-project bottleneck**: One large project on the critical path that blocks everything

Use `get_node_timeline` in binlog analysis to see build node utilization.

### Reducing the Critical Path

The critical path is the longest chain of dependent projects. To shorten it:

1. **Break large projects into smaller ones** that can build in parallel
2. **Remove unnecessary ProjectReferences** (see Step 5)
3. **Use `ReferenceOutputAssembly="false"`** for build-order-only dependencies
4. **Move shared code to a base library** that builds first, then parallelize consumers

---

## Step 8: Additional Quick Wins

### Separate Restore from Build

```bash
# In CI, restore once then build without restore
dotnet restore
dotnet build --no-restore -m
dotnet test --no-build
```

### Skip Unnecessary Targets

```bash
# Skip building documentation
dotnet build /p:GenerateDocumentationFile=false

# Skip analyzers during development (not for CI!)
dotnet build /p:RunAnalyzers=false
```

### Use Project-Level Filtering

```bash
# Build only the project you're working on (and its dependencies)
dotnet build src/MyApp/MyApp.csproj

# Don't build the entire solution if you only need one project
```

### Binary Log for All Investigations

Always start with a binlog:
```bash
dotnet build /bl:perf.binlog -m
```

Then use the `build-perf-diagnostics` skill and binlog tools for systematic bottleneck identification.

---

## Optimization Decision Tree

```
Is your no-op build slow (> 10s per project)?
├── YES → See `incremental-build` skill (fix Inputs/Outputs)
└── NO
    Is your cold build slow?
    ├── YES
    │   Is restore slow?
    │   ├── YES → See `build-caching` skill (NuGet cache, lock files)
    │   └── NO
    │       Is compilation slow?
    │       ├── YES
    │       │   Are analyzers/generators slow?
    │       │   ├── YES → See `analyzer-performance` skill
    │       │   └── NO → Check parallelism, graph build, critical path (this skill + `build-parallelism`)
    │       └── NO → Check custom targets (binlog analysis via `build-perf-diagnostics`)
    └── NO
        Is your warm build slow?
        ├── YES → Projects rebuilding unnecessarily → check `incremental-build` skill
        └── NO → Build is healthy! Consider graph build or UseArtifactsOutput for further gains
```
