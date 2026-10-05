---
name: build-perf-baseline
description: "Establish MSBuild/.NET build performance baselines before optimizing. USE FOR: a .NET build or solution that is slow or has build-performance concerns; cold, warm, incremental, or no-op measurements; before/after comparisons; CI build output caching; static graph build decisions; artifacts output; and dependency graph trimming. Start here before build-perf-diagnostics, incremental-build, or build-parallelism. DO NOT USE for webpack, npm, JavaScript bundlers, or other non-MSBuild build systems. For detailed target/task/analyzer bottleneck analysis after baselining, use build-perf-diagnostics."
license: MIT
---

# Build Performance Baseline & Optimization

## Overview

Before optimizing a build, you need a **baseline**. Without measurements, optimization is guesswork. This skill covers how to establish baselines and apply systematic optimization techniques.

**Related skills:**
- `build-perf-diagnostics` — binlog-based bottleneck identification
- `incremental-build` — Inputs/Outputs and up-to-date checks
- `build-parallelism` — parallel and graph build tuning
- `eval-performance` — glob and import chain optimization

---

## Step 1: Establish a Performance Baseline

Measure three scenarios to understand where time is spent. Keep the SDK,
configuration, machine, environment variables, restore state, and build command
consistent. Run each scenario at least three times, repeating its setup before
every measured sample, and report the median plus the observed range; a single
timing is not a baseline. Keep setup outside the timed interval. Save each
sample's binlog under a unique name, outside generated output directories.

### Cold Build (First Build)

No previous build output exists. Measures the full end-to-end time including restore, compilation, and all targets.

Before **every cold sample**, restore the same source state, run `dotnet clean`
with the measured configuration, and remove only the confirmed, disposable
output and intermediate directories for the measured projects. Include custom
artifact paths, not just `bin` and `obj`, and obtain approval before deletion.
Verify those outputs are absent before timing the next build.

This is an **output-cold** build, not necessarily a cold NuGet, OS, or compiler
server cache. Choose and record a consistent cache/server policy for all
samples; do not clear shared caches. If measuring uncached restore, use a
separate, empty package cache for each sample.

```shell
# After repeating the cold setup; use a unique log name for each sample
dotnet build /bl:cold-build-1.binlog -m
```

### Warm Build (Incremental Build)

Build output exists, some files have changed. Measures how well incremental build works.

Before **every warm sample**, restore the same baseline source contents and
build successfully without timing it. Then apply the same small, build-relevant
edit to the same source file and time the build. Keep the changed file set and
edit identical across samples; do not let edits accumulate. Restore the
baseline contents before the next sample and rebuild them outside the timed
interval. Do not clean between that setup build and its measured build.

```shell
# Untimed setup after restoring baseline source contents
dotnet build -m

# Apply the same controlled source edit, then measure with a unique log name
dotnet build /bl:warm-build-1.binlog -m
```

### No-Op Build (Nothing Changed)

Build output exists, nothing has changed. Compilation and correctly incremental
targets should skip; compare timing with this build's other samples.

Before **every no-op sample**, restore the same baseline source contents and
run an untimed setup build successfully. Then measure an identical build
without edits, touching inputs, cleaning outputs, or changing properties.
Keep restore and cache/server policy consistent with the other samples.

```shell
# Untimed setup after restoring baseline source contents
dotnet build -m

# Rebuild immediately without changes; use a unique log name for each sample
dotnet build /bl:noop-build-1.binlog -m
```

### What Good Looks Like

| Scenario | Expected Behavior |
|----------|------------------|
| Cold build | Full compilation, all targets run. This is your absolute baseline |
| Warm build | Only changed projects recompile. Time proportional to change scope |
| No-op build | Compilation and correctly incremental custom targets skip; compare duration with this repo's repeated warm and cold samples |

**Red flags:**
- No-op time is repeatedly close to warm/cold time, or compilation targets rerun → investigate incrementality (see `incremental-build`)
- Warm build recompiles everything → project dependency chain forces full rebuild
- Restore dominates cold samples → measure `dotnet restore` and `dotnet build --no-restore` separately before changing project structure

Do not use universal duration or percentage thresholds to declare a bottleneck.
Rank costs against the controlled samples and the build's own target/task
timings.

### Capture analyzer evidence

A binlog shows compiler/task timing, but granular analyzer timing requires an
analyzer-reporting run. When supported by the SDK/compiler, capture:

```shell
dotnet build /bl:analyzers.binlog /p:ReportAnalyzer=true
```

Open the binlog in MSBuild Structured Log Viewer and inspect the analyzer
summary under the compiler task. If granular timing is unavailable, compare
otherwise identical samples with `/p:RunAnalyzers=false` as an attribution
experiment; do not present disabling analyzers as the fix. Preserve analyzer
enforcement in CI.

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

## Step 2: Artifacts Output Layout

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

## Step 3: Deterministic Builds

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

## Step 4: Dependency Graph Trimming

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

## Step 5: Static Graph Builds (`/graph`)

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

## Step 6: Parallel Build Tuning

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

Use `grep 'Target Performance Summary' -A 30 full.log` in binlog analysis to see build node utilization.

### Reducing the Critical Path

The critical path is the longest chain of dependent projects. To shorten it:

1. **Break large projects into smaller ones** that can build in parallel
2. **Remove unnecessary ProjectReferences** (see Step 5)
3. **Use `ReferenceOutputAssembly="false"`** for build-order-only dependencies
4. **Move shared code to a base library** that builds first, then parallelize consumers

---

## Step 7: Additional Quick Wins

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

# Attribution experiment only: compare against the same build with analyzers
dotnet build /p:RunAnalyzers=false
```

Use these switches to measure contribution before changing configuration.
Do not recommend permanently disabling analyzers from this baseline step;
route measured analyzer bottlenecks to `build-perf-diagnostics` and preserve
CI enforcement.

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
Is your repeated no-op build disproportionately close to warm/cold samples,
or are compile/custom targets rerunning?
├── YES → See `incremental-build` skill (inspect Inputs/Outputs and skip reasons)
└── NO
    Is your cold build slow?
    ├── YES
    │   Is restore slow?
    │   ├── YES → Optimize NuGet restore (use lock files, configure local cache)
    │   └── NO
    │       Is compilation slow?
    │       ├── YES
    │       │   Are analyzers/generators slow?
    │       │   ├── YES → See `build-perf-diagnostics` skill
    │       │   └── NO → Check parallelism, graph build, critical path (this skill + `build-parallelism`)
    │       └── NO → Check custom targets (binlog analysis via `build-perf-diagnostics`)
    └── NO
        Is your warm build slow?
        ├── YES → Projects rebuilding unnecessarily → check `incremental-build` skill
        └── NO → Baseline is healthy; adopt graph build or UseArtifactsOutput only for a measured need
```
