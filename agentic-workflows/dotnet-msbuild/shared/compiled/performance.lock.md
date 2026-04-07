<!-- AUTO-GENERATED — DO NOT EDIT -->

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
- Cold build has long restore → NuGet cache issues

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

Use `grep 'Target Performance Summary' -A 30 full.log` in binlog analysis to see build node utilization.

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
        └── NO → Build is healthy! Consider graph build or UseArtifactsOutput for further gains
```

---

## Performance Analysis Methodology

Follow the workflow for the backend specified in the user's request:

| Backend ID | Name | Workflow |
|---|---|---|
| `baronfel-mcp` | baronfel.binlog.mcp | ## Performance Analysis with baronfel.binlog.mcp

### Step 1: Load the binlog

Call `load_binlog` with the path to the binlog file.

### Step 2: Get expensive targets

Call `get_expensive_targets(top_number=10)`. This returns targets sorted by cumulative execution time, including execution count, skipped count, inclusive and exclusive durations.

### Step 3: Get expensive tasks

Call `get_expensive_tasks(top_number=10)`. This returns tasks sorted by total duration, with execution count, average/min/max durations.

### Step 4: Get expensive projects

Call `get_expensive_projects(top_number=10)` to see per-project build times with both exclusive and inclusive durations.

### Step 5: Check analyzer overhead

Call `get_expensive_analyzers(top_number=5)` to see which Roslyn analyzers and source generators consume the most build time.

For more detail on a specific Csc task, use `search_tasks_by_name(taskName="Csc")` to find task IDs, then `get_task_analyzers(projectId=<id>, targetId=<id>, taskId=<id>)` for per-analyzer timing.

### Step 6: Check node utilization

Call `get_node_timeline` to see per-node work data. Look for uneven utilization indicating serialization bottlenecks.

### Step 7: Drill into specific targets

For a slow target, call `get_target_info_by_name(projectId=<id>, targetName="CoreCompile")` to see its duration, build reason, and messages. Then `list_tasks_in_target` to see which tasks within the target are expensive. |
| `gerlicher-mcp` | AndyGerlicher BinlogMCP | ## Performance Analysis with BinlogMCP

### Step 1: Get comprehensive performance report

Call `GetPerformanceReport` with the binlog path. This returns a comprehensive analysis including bottlenecks, slow targets/tasks, and optimization hints — often sufficient as a single call.

### Step 2: Get compiler performance details

Call `GetCompilerPerformance` for detailed C#/VB/F# compilation timing analysis, including per-project Csc task durations.

### Step 3: Check parallelism

Call `GetParallelismAnalysis` for build parallelism efficiency — concurrent operations, sequential bottlenecks, and utilization metrics.

### Step 4: Check slow I/O operations

Call `GetSlowOperations` to analyze slow file I/O operations (Copy, Move, Delete, Exec).

### Step 5: Get per-project timing

Call `GetProjectPerformance` for per-project timing rollup. Identify which projects are slowest.

### Step 6: Identify the critical path

Call `GetCriticalPath` to identify targets on the critical path that determine overall build duration.

### Step 7: Drill into specific targets

Call `AnalyzeTarget(targetName="CoreCompile")` for a deep dive into a specific target — tasks, parameters, I/O, and timing breakdown. |
| `sqlite` | SQLite Logger | ## Performance Analysis with SQLite Logger

Requires: build was run with `-logger:"SqliteLogger,SqliteLogger.dll;LogFile=build.sqlite"`

### Step 1: Get expensive targets

```sql
SELECT * FROM ExpensiveTargets LIMIT 10;
```

### Step 2: Get expensive tasks

```sql
SELECT * FROM ExpensiveTasks LIMIT 10;
```

### Step 3: Get expensive projects

```sql
SELECT * FROM ExpensiveProjects LIMIT 10;
```

### Step 4: Check analyzer overhead

```sql
-- Csc task durations per project
SELECT t.ProjectFile, t.Name, t.DurationMs
FROM Tasks t
WHERE t.Name = 'Csc'
ORDER BY t.DurationMs DESC
LIMIT 10;
```

### Step 5: Check node utilization

```sql
SELECT NodeId,
       COUNT(*) AS TargetCount,
       SUM(DurationMs) AS TotalWorkMs,
       MIN(StartTimeMs) AS FirstStart,
       MAX(EndTimeMs) AS LastEnd
FROM NodeTimeline
GROUP BY NodeId
ORDER BY TotalWorkMs DESC;
```

### Step 6: Drill into a specific slow target

```sql
-- Get tasks within a specific target
SELECT tk.Name AS TaskName, tk.DurationMs, tk.TaskAssembly
FROM Tasks tk
JOIN Targets t ON tk.TargetId = t.TargetId
WHERE t.Name = 'CoreCompile' AND t.ProjectId = <id>
ORDER BY tk.DurationMs DESC;
``` |
| `text-replay` | Text-log replay | ## Performance Analysis with Text-Log Replay

### Step 1: Replay with performance summary

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary
```

> **PowerShell:** `-flp:"v=diag;logfile=full.log;performancesummary"`

### Step 2: Read target/task performance summaries

```bash
grep "Target Performance Summary\|Task Performance Summary" -A 50 full.log
```

This shows all targets and tasks sorted by cumulative time.

### Step 3: Find per-project build times

```bash
grep "done building project\|Project Performance Summary" full.log
```

### Step 4: Check parallelism (multi-node scheduling)

```bash
grep -i "node.*assigned\|RequiresLeadingNewline\|Building with" full.log | head -30
```

### Step 5: Check analyzer overhead

```bash
grep -i "Total analyzer execution time\|analyzer.*elapsed\|CompilerAnalyzerDriver" full.log
```

### Step 6: Drill into a specific slow target

```bash
grep 'Target "CoreCompile"\|Target "ResolveAssemblyReferences"' full.log
``` |

Regardless of backend, the key analysis steps are:

1. **Find expensive targets** — sorted by cumulative time
2. **Find expensive tasks** — sorted by total duration
3. **Check per-project build times** — identify bottleneck projects
4. **Check analyzer overhead** — compare Csc time with and without analyzers
5. **Check node utilization** — look for serialization bottlenecks
6. **Drill into slow targets** — find root cause within slow targets

## Key Metrics and Thresholds

- **Build duration**: what's "normal" — small project <10s, medium <60s, large <5min
- **Node utilization**: ideal is >80% active time across nodes. Low utilization = serialization bottleneck
- **Single target domination**: if one target is >50% of build time, investigate
- **Analyzer time vs compile time**: analyzers should be <30% of Csc task time. If higher, consider removing expensive analyzers
- **RAR time**: ResolveAssemblyReference >5s is concerning. >15s is pathological

## Common Bottlenecks

### 1. ResolveAssemblyReference (RAR) Slowness

- **Symptoms**: RAR taking >5s per project
- **Root causes**: too many assembly references, network-based reference paths, large assembly search paths
- **Fixes**: reduce reference count, use `<DesignTimeBuild>false</DesignTimeBuild>` for RAR-heavy analysis, set `<ResolveAssemblyReferencesSilent>true</ResolveAssemblyReferencesSilent>` for diagnostic
- **Advanced**: `<DesignTimeBuild>` and `<ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch>`
- **Key insight**: RAR runs unconditionally even on incremental builds because users may have installed targeting packs or GACed assemblies (see dotnet/msbuild#2015). With .NET Core micro-assemblies, the reference count is often very high.
- **Reduce transitive references**: Set `<DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>` to avoid pulling in the full transitive closure (note: projects may need to add direct references for any types they consume). Use `ReferenceOutputAssembly="false"` on ProjectReferences that are only needed at build time (not API surface). Trim unused PackageReferences.

### 2. Roslyn Analyzers and Source Generators

- **Symptoms**: Csc task takes much longer than expected for file count (>2× clean compile time)
- **Diagnosis**: Check the Task Performance Summary in the replayed log for Csc task time; grep for analyzer timing messages; compare Csc duration with and without analyzers (`/p:RunAnalyzers=false`)
- **Fixes**:
  - Conditionally disable in dev: `<RunAnalyzers Condition="'$(ContinuousIntegrationBuild)' != 'true'">false</RunAnalyzers>`
  - Per-configuration: `<RunAnalyzers Condition="'$(Configuration)' == 'Debug'">false</RunAnalyzers>`
  - Code-style only: `<EnforceCodeStyleInBuild Condition="'$(ContinuousIntegrationBuild)' == 'true'">true</EnforceCodeStyleInBuild>`
  - Remove genuinely redundant analyzers from inner loop
  - Severity config in .editorconfig for less critical rules
- **Key principle**: Preserve analyzer enforcement in CI. Never just "remove" analyzers — configure them conditionally.
- **GlobalPackageReference**: Analyzers added via `GlobalPackageReference` in `Directory.Packages.props` apply to ALL projects. Consider if test projects need the same analyzer set as production code.
- **EnforceCodeStyleInBuild**: When set to `true` in `Directory.Build.props`, forces code-style analysis on every build. Should be conditional on CI environment (`ContinuousIntegrationBuild`) to avoid slowing dev inner loop.

### 3. Serialization Bottlenecks (Single-threaded targets)

- **Symptoms**: Performance summary shows most build time concentrated in a single project; diagnostic log shows idle nodes while one works
- **Common culprits**: targets without proper dependency declaration, single project on critical path
- **Fixes**: split large projects, optimize the critical path project, ensure proper `BuildInParallel`

### 4. Excessive File I/O (Copy tasks)

- **Symptoms**: Copy task shows high aggregate time
- **Root causes**: copying thousands of files, copying across network drives, Copy task unintentionally running once per item (per-file) instead of as a single batch (see dotnet/msbuild#12884)
- **Fixes**: use hardlinks (`<CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>true</CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>`), reduce CopyToOutputDirectory items, use `<UseCommonOutputDirectory>true</UseCommonOutputDirectory>` when appropriate, set `<SkipCopyUnchangedFiles>true</SkipCopyUnchangedFiles>`, consider `--artifacts-path` (.NET 8+) for centralized output layout
- **Dev Drive**: On Windows, switching to a Dev Drive (ReFS with copy-on-write and reduced Defender scans) can significantly reduce file I/O overhead for Copy-heavy builds. Recommend for both dev machines and self-hosted CI agents.

### 5. Evaluation Overhead

- **Symptoms**: build starts slow before any compilation
- **Root causes**: complex Directory.Build.props, wildcard globs scanning large directories, NuGetSdkResolver overhead (adds 180-400ms per project evaluation even when restored — see dotnet/msbuild#4025)
- **Fixes**: reduce Directory.Build.props complexity, use `<EnableDefaultItems>false</EnableDefaultItems>` for legacy projects with explicit file lists, avoid NuGet-based SDK resolvers if possible
- See: `eval-performance` skill for detailed guidance

### 6. NuGet Restore in Build

- **Symptoms**: restore runs every build even when unnecessary
- **Fixes**:
  - Separate restore from build: `dotnet restore` then `dotnet build --no-restore`
  - Enable static graph evaluation: `<RestoreUseStaticGraphEvaluation>true</RestoreUseStaticGraphEvaluation>` in Directory.Build.props — can save significant time in large builds (results are workload-dependent)

### 7. Large Project Count and Graph Shape

- **Symptoms**: many small projects, each takes minimal time but overhead adds up; deep dependency chains serialize the build
- **Consider**: project consolidation, or use `/graph` mode for better scheduling
- **Graph shape matters**: a wide dependency graph (few levels, many parallel branches) builds faster than a deep one (many levels, serialized). Refactoring from deep to wide can yield significant improvements in both clean and incremental build times.
- **Actions**: look for unnecessary project dependencies, consider splitting a bottleneck project into two, or merging small leaf projects

## Quick Wins Checklist

- [ ] Use `/maxcpucount` (or `-m`) for parallel builds
- [ ] Separate restore from build (`dotnet restore` then `dotnet build --no-restore`)
- [ ] Enable static graph restore (`<RestoreUseStaticGraphEvaluation>true</RestoreUseStaticGraphEvaluation>`)
- [ ] Enable hardlinks for Copy (`<CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>true</CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>`)
- [ ] Disable analyzers conditionally in dev inner loop: `<RunAnalyzers Condition="'$(ContinuousIntegrationBuild)' != 'true'">false</RunAnalyzers>`
- [ ] Enable reference assemblies (`<ProduceReferenceAssembly>true</ProduceReferenceAssembly>`)
- [ ] Check for broken incremental builds (see `incremental-build` skill)
- [ ] Check for bin/obj clashes (see `check-bin-obj-clash` skill)
- [ ] Use graph build (`/graph`) for multi-project solutions
- [ ] Use `--artifacts-path` (.NET 8+) for centralized output layout
- [ ] Enable Dev Drive (ReFS) on Windows dev machines and self-hosted CI

## Impact Categorization

When reporting findings, categorize by impact to help prioritize fixes:

- 🔴 **HIGH IMPACT** (do first): Items consuming >10% of total build time, or a single target >50% of build time
- 🟡 **MEDIUM IMPACT**: Items consuming 2-10% of build time
- 🟢 **QUICK WINS**: Easy changes with modest impact (e.g., property flags in Directory.Build.props)

---

## How MSBuild Incremental Build Works

MSBuild's incremental build mechanism allows targets to be skipped when their outputs are already up to date, dramatically reducing build times on subsequent runs.

- **Targets with `Inputs` and `Outputs` attributes**: MSBuild compares the timestamps of all files listed in `Inputs` against all files listed in `Outputs`. If every output file is newer than every input file, the target is skipped entirely.
- **Without `Inputs`/`Outputs`**: The target runs every time the build is invoked. This is the default behavior and the most common cause of slow incremental builds.
- **`Incremental` attribute on targets**: Targets can explicitly opt in or out of incremental behavior. Setting `Incremental="false"` forces the target to always run, even if `Inputs` and `Outputs` are specified.
- **Timestamp-based comparison**: MSBuild uses file system timestamps (last write time) to determine staleness. It does not use content hashes. This means touching a file (updating its timestamp without changing content) will trigger a rebuild.

```xml
<!-- This target is incremental: skipped if Output is newer than all Inputs -->
<Target Name="Transform"
        Inputs="@(TransformFiles)"
        Outputs="@(TransformFiles->'$(OutputPath)%(Filename).out')">
  <!-- work here -->
</Target>

<!-- This target always runs because it has no Inputs/Outputs -->
<Target Name="PrintMessage">
  <Message Text="This runs every build" />
</Target>
```

## Why Incremental Builds Break (Top Causes)

1. **Missing Inputs/Outputs on custom targets** — Without both attributes, the target always runs. This is the single most common cause of unnecessary rebuilds.

2. **Volatile properties in Outputs path** — If the output path includes something that changes between builds (e.g., a timestamp, build number, or random GUID), MSBuild will never find the previous output and will always rebuild.

3. **File writes outside of tracked Outputs** — If a target writes files that aren't listed in its `Outputs`, MSBuild doesn't know about them. The target may be skipped (because its declared outputs are up to date), but downstream targets may still be triggered.

4. **Missing FileWrites registration** — Files created during the build but not registered in the `FileWrites` item group won't be cleaned by `dotnet clean`. Over time, stale files can confuse incremental checks.

5. **Glob changes** — When you add or remove source files, the item set (e.g., `@(Compile)`) changes. Since these items feed into `Inputs`, the set of inputs changes and triggers a rebuild. This is expected behavior but can be surprising.

6. **Property changes** — Properties that feed into `Inputs` or `Outputs` paths (e.g., `$(Configuration)`, `$(TargetFramework)`) will cause rebuilds when changed. Switching between Debug and Release is a full rebuild by design.

7. **NuGet package updates** — Changing a package version updates `project.assets.json` and potentially many resolved assembly paths. This changes the inputs to `ResolveAssemblyReferences` and `CoreCompile`, triggering a rebuild.

8. **Build server VBCSCompiler cache invalidation** — The Roslyn compiler server (`VBCSCompiler`) caches compilation state. If the server is recycled (timeout, crash, or manual kill), the next build may be slower even though MSBuild's incremental checks pass, because the compiler must repopulate its in-memory caches.

## Diagnosing "Why Did This Rebuild?"

Use binary logs (binlogs) to understand exactly why targets ran instead of being skipped.

### Step-by-step diagnosis

First, build twice to capture incremental behavior:
```shell
dotnet build /bl:first.binlog
dotnet build /bl:second.binlog
```

The first build establishes the baseline. The second build is the one you want to be incremental. Analyze `second.binlog`.

Then follow the workflow for the backend specified in the user's request:

| Backend ID | Name | Workflow |
|---|---|---|
| `baronfel-mcp` | baronfel.binlog.mcp | ## Diagnosing Incremental Build Issues with baronfel.binlog.mcp

### Step 1: Build twice with binlogs

```shell
dotnet build /bl:first.binlog
dotnet build /bl:second.binlog
```

### Step 2: Load the second binlog

Call `load_binlog` with the path to `second.binlog`.

### Step 3: Find non-skipped targets

Call `search_binlog(query="Building target")` to find targets that executed instead of being skipped. In a perfectly incremental build, most targets should be skipped.

Also search for: `search_binlog(query="is newer than output")` to find the specific input files that triggered rebuilds.

### Step 4: Check expensive targets in second build

Call `get_expensive_targets(top_number=10)` to see which targets consumed the most time in the second build. These are your optimization targets.

### Step 5: Inspect specific targets

For each non-skipped target, call `get_target_info_by_name(projectId=<id>, targetName="<name>")` to see:
- The build reason (why it ran)
- Duration
- Messages (including "is newer than output" details)

### Step 6: Compare with first build (manual)

Load `first.binlog` separately and compare the target execution lists. Targets that ran in both builds despite no code changes indicate broken incrementality.

> **Note:** baronfel.binlog.mcp does not have built-in build comparison. If build comparison is critical, consider the AndyGerlicher/BinlogMCP backend which has `CompareBinlogs` and `DiffTargetExecution` tools. |
| `gerlicher-mcp` | AndyGerlicher BinlogMCP | ## Diagnosing Incremental Build Issues with BinlogMCP

This is the **best backend for incremental build diagnosis** due to its built-in build comparison tools.

### Step 1: Build twice with binlogs

```shell
dotnet build /bl:first.binlog
dotnet build /bl:second.binlog
```

### Step 2: Compare the two builds

Call `CompareBinlogs(baseline="first.binlog", current="second.binlog")` to get a structured diff showing timing changes, new/fixed errors, and target differences between the two builds.

### Step 3: Get incremental build analysis

Call `GetIncrementalBuildAnalysis` with `second.binlog` to analyze incremental build behavior — showing executed vs skipped targets and identifying targets that should have been skipped.

### Step 4: Diff target execution

Call `DiffTargetExecution(baseline="first.binlog", current="second.binlog")` to see exactly which targets ran differently between the two builds.

### Step 5: Check skipped targets

Call `GetSkippedTargets` with `second.binlog` to see all targets that were skipped and the reasons why. Cross-reference with targets that ran but should have been skipped.

### Step 6: Trace specific inputs (if needed)

If a target ran because of a specific file change:
- Call `GetTargetInputsOutputs(targetName="<name>")` to see the target's incremental build inputs and outputs
- Call `TraceItem(item="<filename>")` to track how a specific item flowed through the build

### Step 7: Identify root cause

Call `GetTargetExecutionReasons` to see why targets executed (DependsOnTargets, BeforeTargets, AfterTargets chains). This helps identify the root target that triggered a cascade of rebuilds. (best for incremental build — has build comparison tools) |
| `sqlite` | SQLite Logger | ## Diagnosing Incremental Build Issues with SQLite Logger

Requires: both builds were run with the SQLite logger:
```bash
dotnet build -logger:"SqliteLogger,SqliteLogger.dll;LogFile=first.sqlite"
dotnet build -logger:"SqliteLogger,SqliteLogger.dll;LogFile=second.sqlite"
```

### Step 1: Find non-skipped targets in the second build

```sql
-- Targets that executed (not skipped) in the second build
SELECT t.Name, t.ProjectFile, t.DurationMs, t.BuildReason
FROM Targets t
WHERE t.Skipped = 0
ORDER BY t.DurationMs DESC;
```

### Step 2: Check total work done in second build

```sql
SELECT * FROM ExpensiveTargets LIMIT 15;
```

In a good incremental build, most targets should have `SkippedCount >> RanCount`.

### Step 3: Compare target execution across builds

Use SQLite's `ATTACH` to compare the two databases:

```sql
-- In sqlite3 with second.sqlite open:
ATTACH 'first.sqlite' AS first;

-- Targets that ran in the second build but were skipped in the first
SELECT s.Name, s.ProjectFile, s.DurationMs AS SecondBuildMs
FROM main.Targets s
WHERE s.Skipped = 0
  AND EXISTS (
    SELECT 1 FROM first.Targets f
    WHERE f.Name = s.Name AND f.ProjectFile = s.ProjectFile AND f.Skipped = 1
  );
```

### Step 4: Search for "is newer than output" messages

```sql
SELECT Message, ProjectFile
FROM Messages
WHERE Message LIKE '%is newer than output%'
ORDER BY TimestampMs;
```

### Step 5: Check target inputs/outputs

For targets that should have been skipped, look at their definition using `/pp`:
```bash
dotnet msbuild -pp:full.xml MyProject.csproj
```

Search for the target name to find its `Inputs` and `Outputs` attributes. |
| `text-replay` | Text-log replay | ## Diagnosing Incremental Build Issues with Text-Log Replay

### Step 1: Build twice with binlogs

```shell
dotnet build /bl:first.binlog
dotnet build /bl:second.binlog
```

The first build establishes the baseline. The second build is the one you want to be incremental. Analyze `second.binlog`.

### Step 2: Replay the second binlog

```shell
dotnet msbuild second.binlog -noconlog -fl -flp:v=diag;logfile=second-full.log;performancesummary
```

> **PowerShell:** `-flp:"v=diag;logfile=second-full.log;performancesummary"`

### Step 3: Find non-skipped targets

```bash
grep 'Building target\|Target.*was not skipped' second-full.log
```

In a perfectly incremental build, most targets should be skipped.

### Step 4: Look for key messages

- `"Building target 'X' completely"` — MSBuild found no outputs or all outputs are missing
- `"Building target 'X' incrementally"` — some (but not all) outputs are out of date
- `"Skipping target 'X' because all output files are up-to-date"` — target was correctly skipped

### Step 5: Find the triggering file

```bash
grep "is newer than output" second-full.log
```

This reveals exactly which input file's timestamp caused MSBuild to consider the target out of date.

### Step 6: Check performance summary

```bash
grep "Target Performance Summary" -A 30 second-full.log
```

Targets that consumed time in the second build are your optimization targets.

### Additional techniques

- Compare `first.binlog` and `second.binlog` side by side in the MSBuild Structured Log Viewer to see what changed.
- Check for targets with zero-duration that still ran — they may have unnecessary dependencies. |

### Key messages to look for

Regardless of backend, search for these messages in the second build:

- `"Building target 'X' completely"` — MSBuild found no outputs or all outputs are missing; this is a full target execution.
- `"Building target 'X' incrementally"` — some (but not all) outputs are out of date.
- `"Skipping target 'X' because all output files are up-to-date"` — target was correctly skipped.
- `"is newer than output"` — reveals exactly which input file triggered the rebuild.

## FileWrites and Clean Build

The `FileWrites` item group is MSBuild's mechanism for tracking files generated during the build. It powers `dotnet clean` and helps maintain correct incremental behavior.

- **`FileWrites` item**: Register any file your custom targets create so that `dotnet clean` knows to remove them. Without this, generated files accumulate across builds and may confuse incremental checks.
- **`FileWritesShareable` item**: Use this for files that are shared across multiple projects (e.g., shared generated code). These files are tracked but not deleted if other projects still reference them.
- **If not registered**: Files accumulate in the output and intermediate directories. `dotnet clean` won't remove them, and they may cause stale data issues or confuse up-to-date checks.

### Pattern for registering generated files

Add generated files to `FileWrites` inside the target that creates them:

```xml
<Target Name="MyGenerator" Inputs="..." Outputs="$(IntermediateOutputPath)generated.cs">
  <!-- Generate the file -->
  <WriteLinesToFile File="$(IntermediateOutputPath)generated.cs" Lines="@(GeneratedLines)" />

  <!-- Register for clean -->
  <ItemGroup>
    <FileWrites Include="$(IntermediateOutputPath)generated.cs" />
  </ItemGroup>
</Target>
```

## Visual Studio Fast Up-to-Date Check

Visual Studio has its own up-to-date check (Fast Up-to-Date Check, or FUTDC) that is separate from MSBuild's `Inputs`/`Outputs` mechanism. Understanding the difference is critical for diagnosing "it rebuilds in VS but not on the command line" issues.

- **VS FUTDC is faster** because it runs in-process and checks a known set of items without invoking MSBuild at all. It compares timestamps of well-known item types (Compile, Content, EmbeddedResource, etc.) against the project's primary output.
- **It can be wrong** if your project uses custom build actions, custom targets that generate files, or non-standard item types that FUTDC doesn't know about.
- **Disable FUTDC** to force Visual Studio to use MSBuild's full incremental check:
  ```xml
  <PropertyGroup>
    <DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>
  </PropertyGroup>
  ```
- **Diagnose FUTDC decisions** by viewing the Output window in VS: go to **Tools → Options → Projects and Solutions → SDK-Style Projects** and set **Up-to-date Checks** logging level to **Verbose** or above. FUTDC will log exactly which file it considers out of date.
- **Common VS FUTDC issues**:
  - Custom build actions not registered with the FUTDC system
  - `CopyToOutputDirectory` items that are newer than the last build
  - Items added dynamically by targets that FUTDC doesn't evaluate
  - `Content` or `None` items with `CopyToOutputDirectory="PreserveNewest"` that have been modified

## Making Custom Targets Incremental

The following is a complete example of a well-structured incremental custom target:

```xml
<Target Name="GenerateConfig"
        Inputs="$(MSBuildProjectFile);@(ConfigInput)"
        Outputs="$(IntermediateOutputPath)config.generated.cs"
        BeforeTargets="CoreCompile">
  <!-- Generate file only if inputs changed -->
  <WriteLinesToFile File="$(IntermediateOutputPath)config.generated.cs" Lines="..." />
  <ItemGroup>
    <FileWrites Include="$(IntermediateOutputPath)config.generated.cs" />
    <Compile Include="$(IntermediateOutputPath)config.generated.cs" />
  </ItemGroup>
</Target>
```

**Key points in this example:**

- **`Inputs` includes `$(MSBuildProjectFile)`**: This ensures the target reruns if the project file itself changes (e.g., a property that affects generation is modified).
- **`Inputs` includes `@(ConfigInput)`**: The actual source files that drive generation.
- **`Outputs` uses `$(IntermediateOutputPath)`**: Generated files go in the `obj/` directory, which is managed by MSBuild and cleaned automatically.
- **`BeforeTargets="CoreCompile"`**: The generated file is available before the compiler runs.
- **`FileWrites` registration**: Ensures `dotnet clean` removes the generated file.
- **`Compile` inclusion**: Adds the generated file to the compilation without requiring it to exist at evaluation time.

### Common mistakes to avoid

```xml
<!-- BAD: No Inputs/Outputs — runs every build -->
<Target Name="BadTarget" BeforeTargets="CoreCompile">
  <Exec Command="generate-code.exe" />
</Target>

<!-- BAD: Volatile output path — never finds previous output -->
<Target Name="BadTarget2"
        Inputs="@(Compile)"
        Outputs="$(OutputPath)gen_$([System.DateTime]::Now.Ticks).cs">
  <Exec Command="generate-code.exe" />
</Target>

<!-- GOOD: Stable paths, registered outputs -->
<Target Name="GoodTarget"
        Inputs="@(Compile)"
        Outputs="$(IntermediateOutputPath)generated.cs"
        BeforeTargets="CoreCompile">
  <Exec Command="generate-code.exe -o $(IntermediateOutputPath)generated.cs" />
  <ItemGroup>
    <FileWrites Include="$(IntermediateOutputPath)generated.cs" />
    <Compile Include="$(IntermediateOutputPath)generated.cs" />
  </ItemGroup>
</Target>
```

## Performance Summary and Preprocess

MSBuild provides built-in tools to understand what's running and why.

- **`/clp:PerformanceSummary`** — Appends a summary at the end of the build showing time spent in each target and task. Use this to quickly identify the most expensive operations:
  ```shell
  dotnet build /clp:PerformanceSummary
  ```
  This shows a table of targets sorted by cumulative time, making it easy to spot targets that shouldn't be running in an incremental build.

- **`/pp:preprocess.xml`** — Generates a single XML file with all imports inlined, showing the fully evaluated project. This is invaluable for understanding what targets, properties, and items are defined and where they come from:
  ```shell
  dotnet msbuild /pp:preprocess.xml
  ```
  Search the preprocessed output to find where `Inputs` and `Outputs` are defined for any target, or to understand the full chain of imports.

- Use both together to understand what's running (`PerformanceSummary`) and what's imported (`/pp`), then cross-reference with binlog analysis for a complete picture.

## Common Fixes

- **Always add `Inputs` and `Outputs` to custom targets** — This is the single most impactful change for incremental build performance. Without both attributes, the target runs every time.
- **Use `$(IntermediateOutputPath)` for generated files** — Files in `obj/` are tracked by MSBuild's clean infrastructure and won't leak between configurations.
- **Register generated files in `FileWrites`** — Ensures `dotnet clean` removes them and prevents stale file accumulation.
- **Avoid volatile data in build** — Don't embed timestamps, random values, or build counters in file paths or generated content unless you have a deliberate strategy for managing staleness. If you must use volatile data, isolate it to a single file with minimal downstream impact.
- **Use `Returns` instead of `Outputs` when you need to pass items without creating incremental build dependency** — `Outputs` serves double duty: it defines the incremental check AND the items returned from the target. If you only need to pass items to calling targets without affecting incrementality, use `Returns` instead:
  ```xml
  <!-- Outputs: affects incremental check AND return value -->
  <Target Name="GetFiles" Outputs="@(DiscoveredFiles)">...</Target>

  <!-- Returns: only affects return value, no incremental check -->
  <Target Name="GetFiles" Returns="@(DiscoveredFiles)">...</Target>
  ```

---

## MSBuild Parallelism Model

- `/maxcpucount` (or `-m`): number of worker nodes (processes)
- Default: 1 node (sequential!). Always use `-m` for parallel builds
- Recommended: `-m` without a number = use all logical processors
- Each node builds one project at a time
- Projects are scheduled based on dependency graph

## Project Dependency Graph

- MSBuild builds projects in dependency order (topological sort)
- Critical path: longest chain of dependent projects determines minimum build time
- Bottleneck: if project A depends on B, C, D and B takes 60s while C and D take 5s, B is the bottleneck
- Wide graphs (many independent projects) parallelize well; deep graphs (long chains) don't

## Graph Build Mode (`/graph`)

- `dotnet build /graph` or `msbuild /graph`
- What it changes: MSBuild constructs the full project dependency graph BEFORE building
- Benefits: better scheduling, avoids redundant evaluations, enables isolated builds
- Limitations: all projects must use `<ProjectReference>` (no programmatic MSBuild task references)
- When to use: large solutions with many projects, CI builds
- When NOT to use: projects that dynamically discover references at build time

## Optimizing Project References

- Reduce unnecessary `<ProjectReference>` — each adds to the dependency chain
- Use `<ProjectReference ... SkipGetTargetFrameworkProperties="true">` to avoid extra evaluations
- `<ProjectReference ... ReferenceOutputAssembly="false">` for build-order-only dependencies
- Consider if a ProjectReference should be a PackageReference instead (pre-built NuGet)
- Use `solution filters` (`.slnf`) to build subsets of the solution

## BuildInParallel

- `<MSBuild Projects="@(ProjectsToBuild)" BuildInParallel="true" />` in custom targets
- Without `BuildInParallel="true"`, MSBuild task batches projects sequentially
- Ensure `/maxcpucount` > 1 for this to have effect

## Multi-threaded MSBuild Tasks

- Individual tasks can run multi-threaded within a single project build
- Tasks implementing `IMultiThreadableTask` can run on multiple threads
- Tasks must declare thread-safety via `[MSBuildMultiThreadableTask]`

## Analyzing Parallelism with Binlog

Follow the workflow for the backend specified in the user's request:

| Backend ID | Name | Workflow |
|---|---|---|
| `baronfel-mcp` | baronfel.binlog.mcp | ## Analyzing Parallelism with baronfel.binlog.mcp

### Step 1: Load the binlog

Call `load_binlog` with the path to the binlog file.

### Step 2: Get the node timeline

Call `get_node_timeline`. This returns per-node work data showing what each build node was doing and when. Look for idle gaps between target executions on the same node.

### Step 3: Get expensive projects

Call `get_expensive_projects(top_number=10, sortByExclusive=false)` to see which projects took the longest (inclusive time, which includes waiting for dependencies).

### Step 4: Check project-level build times

For suspected bottleneck projects, call `get_project_build_time(projectId=<id>)` to get exclusive vs inclusive time. A large gap between inclusive and exclusive means the project spent most of its time waiting for dependencies.

### Step 5: Get expensive targets

Call `get_expensive_targets(top_number=10)` to find which targets dominate build time across all projects.

### Step 6: Assess parallelism

- If `get_node_timeline` shows uneven node utilization → serialization bottleneck
- If one project has high inclusive time but low exclusive time → it's waiting on dependencies, not doing work
- If one project has high exclusive time → it's the actual bottleneck; consider splitting it |
| `gerlicher-mcp` | AndyGerlicher BinlogMCP | ## Analyzing Parallelism with BinlogMCP

### Step 1: Get parallelism analysis

Call `GetParallelismAnalysis` with the binlog path. This returns a comprehensive analysis of build parallelism including concurrent operations, sequential bottlenecks, and utilization metrics.

### Step 2: Identify parallelism blockers

Call `GetParallelismBlockers` to find serialization points and dependency bottlenecks. This shows what is preventing better parallelism.

### Step 3: Get project dependency graph

Call `GetProjectDependencies` to see the project dependency graph, build order, and parallel execution info. Look for deep chains that serialize the build.

### Step 4: Get per-project timing

Call `GetProjectPerformance` to see per-project timing rollup. Identify which projects are slowest.

### Step 5: Get critical path

Call `GetCriticalPath` to identify the targets on the critical path that determine the overall build duration.

### Step 6: Assess parallelism

- `GetParallelismAnalysis` directly reports utilization percentage
- `GetParallelismBlockers` identifies the specific serialization points to fix
- `GetCriticalPath` shows which targets determine minimum build time
- Consider: splitting projects on the critical path, reducing dependency depth, using `/graph` mode |
| `sqlite` | SQLite Logger | ## Analyzing Parallelism with SQLite Logger

Requires: build was run with `-logger:"SqliteLogger,SqliteLogger.dll;LogFile=build.sqlite"`

### Step 1: Check node utilization

```sql
SELECT NodeId,
       COUNT(*) AS TargetCount,
       SUM(DurationMs) AS TotalWorkMs,
       MIN(StartTimeMs) AS FirstStart,
       MAX(EndTimeMs) AS LastEnd,
       MAX(EndTimeMs) - MIN(StartTimeMs) AS WallClockMs
FROM NodeTimeline
GROUP BY NodeId
ORDER BY TotalWorkMs DESC;
```

Compare `TotalWorkMs` to `WallClockMs` per node. If `TotalWorkMs << WallClockMs`, the node was idle much of the time.

### Step 2: Get expensive projects

```sql
SELECT * FROM ExpensiveProjects LIMIT 10;
```

### Step 3: Identify the critical path

```sql
-- Find the longest project chains
SELECT p.ProjectFile, p.DurationMs, p.StartTimeMs, p.EndTimeMs,
       parent.ProjectFile AS ParentProject
FROM Projects p
LEFT JOIN Projects parent ON p.ParentProjectId = parent.ProjectId
WHERE p.DurationMs IS NOT NULL
ORDER BY p.DurationMs DESC
LIMIT 15;
```

### Step 4: Find idle gaps between targets on each node

```sql
-- Detect gaps > 100ms between consecutive targets on the same node
SELECT a.NodeId, a.TargetName AS PrevTarget, b.TargetName AS NextTarget,
       b.StartTimeMs - a.EndTimeMs AS GapMs
FROM NodeTimeline a
JOIN NodeTimeline b ON a.NodeId = b.NodeId AND a.EndTimeMs < b.StartTimeMs
WHERE b.StartTimeMs - a.EndTimeMs > 100
  AND NOT EXISTS (
    SELECT 1 FROM NodeTimeline c
    WHERE c.NodeId = a.NodeId AND c.StartTimeMs > a.EndTimeMs AND c.StartTimeMs < b.StartTimeMs
  )
ORDER BY GapMs DESC
LIMIT 20;
```

### Step 5: Assess parallelism

- Many idle gaps → projects are serialized due to dependency chains
- One node doing most of the work → build graph is too deep
- All nodes evenly loaded → parallelism is already good |
| `text-replay` | Text-log replay | ## Analyzing Parallelism with Text-Log Replay

### Step 1: Replay the binlog

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary
```

> **PowerShell:** `-flp:"v=diag;logfile=full.log;performancesummary"`

### Step 2: Check Project Performance Summary

```bash
grep "Project Performance Summary" -A 30 full.log
```

Compare total build wall-clock time against the sum of individual project times. If they are similar, parallelism is low.

### Step 3: Identify bottleneck targets

```bash
grep "Target Performance Summary" -A 30 full.log
```

### Step 4: Check node scheduling

```bash
grep -i "node.*assigned\|Building with" full.log | head -30
```

Look for nodes that are idle while others are busy — this indicates serialization bottlenecks.

### Step 5: Assess parallelism

- Build time << sum of project times → good parallelism
- Build time ≈ sum of project times → too many serial dependencies or one slow project blocking others
- Consider splitting large projects or optimizing the critical path |

Regardless of backend, the key analysis is:

1. Check if build time is much less than the sum of project times (good parallelism) or approximately equal (poor parallelism)
2. Look for idle nodes or uneven node utilization
3. Identify the critical path and bottleneck projects
4. Consider splitting large projects or optimizing the critical path

## CI/CD Parallelism Tips

- Use `-m` in CI (many CI runners have multiple cores)
- Consider splitting solution into build stages for extreme parallelism
- Use build caching (NuGet lock files, deterministic builds) to avoid rebuilding unchanged projects
- `dotnet build /graph` works well with structured CI pipelines

---

## MSBuild Evaluation Phases

For a comprehensive overview of MSBuild's evaluation and execution model, see [Build process overview](https://learn.microsoft.com/en-us/visualstudio/msbuild/build-process-overview).

1. **Initial properties**: environment variables, global properties, reserved properties
2. **Imports and property evaluation**: process `<Import>`, evaluate `<PropertyGroup>` top-to-bottom
3. **Item definition evaluation**: `<ItemDefinitionGroup>` metadata defaults
4. **Item evaluation**: `<ItemGroup>` with `Include`, `Remove`, `Update`, glob expansion
5. **UsingTask evaluation**: register custom tasks

Key insight: evaluation happens BEFORE any targets run. Slow evaluation = slow build start even when nothing needs compiling.

## Diagnosing Evaluation Performance

Follow the workflow for the backend specified in the user's request:

| Backend ID | Name | Workflow |
|---|---|---|
| `baronfel-mcp` | baronfel.binlog.mcp | ## Diagnosing Evaluation Performance with baronfel.binlog.mcp

### Step 1: Load the binlog

Call `load_binlog` with the path to the binlog file.

### Step 2: List all evaluations

Call `list_projects` to get all project file paths, then call `list_evaluations(projectFilePath=<path>)` for each project. This returns evaluation IDs with durations in milliseconds.

Sort by duration to find the slowest evaluations.

### Step 3: Check for multiple evaluations

If a project has more than one evaluation per TFM, it is being over-evaluated. Call `get_evaluation_global_properties(evaluationId=<id>)` for each evaluation to see what differs between them (different global properties trigger separate evaluations).

### Step 4: Inspect evaluation properties

For slow evaluations, call `get_evaluation_properties_by_name(evaluationId=<id>, propertyNames=["DefaultItemExcludes", "EnableDefaultItems"])` to check glob configuration.

### Step 5: Inspect evaluation items

Call `get_evaluation_items_by_name(evaluationId=<id>, itemTypeNames=["Compile"])` to see how many Compile items were evaluated. An unexpectedly large count suggests overly broad globs.

### Step 6: Check import chain

Call `list_files_from_binlog` and filter for `.props` and `.targets` files to understand the import chain depth. |
| `gerlicher-mcp` | AndyGerlicher BinlogMCP | ## Diagnosing Evaluation Performance with BinlogMCP

### Step 1: Get the evaluated project view

Call `GetEvaluatedProject` with the binlog path and project name. This returns the flattened project view showing final properties, items, and imports after evaluation.

### Step 2: Get the import chain

Call `GetImportChain` to see the full import hierarchy of `.props` and `.targets` files. Look for:
- Import depth > 20 levels
- Large numbers of imported files
- Unexpected imports from NuGet packages

### Step 3: Check properties

Call `GetProperties` to see all evaluated properties. Filter for `DefaultItemExcludes`, `EnableDefaultItems`, and glob-related properties.

### Step 4: Check items

Call `GetItems` to see all evaluated items. Look for unexpectedly large `Compile` or `None` item groups that suggest overly broad glob patterns.

### Step 5: Trace specific properties (if needed)

Call `TraceProperty(property="DefaultItemExcludes")` to see how a property was set through the import chain — which file set it, in what order, and what the final value is. |
| `sqlite` | SQLite Logger | ## Diagnosing Evaluation Performance with SQLite Logger

Requires: build was run with `-logger:"SqliteLogger,SqliteLogger.dll;LogFile=build.sqlite"`

### Step 1: Find slowest evaluations

```sql
SELECT EvaluationId, ProjectFile, DurationMs
FROM Evaluations
ORDER BY DurationMs DESC
LIMIT 10;
```

### Step 2: Check for multiple evaluations per project

```sql
SELECT ProjectFile, COUNT(*) AS EvalCount, SUM(DurationMs) AS TotalMs
FROM Evaluations
GROUP BY ProjectFile
HAVING COUNT(*) > 1
ORDER BY TotalMs DESC;
```

Any project with `EvalCount > 1` per TFM is being over-evaluated.

### Step 3: Compare global properties across evaluations

```sql
-- For a project with multiple evaluations, see what differs
SELECT e.EvaluationId, ep.Name, ep.Value
FROM EvaluationProperties ep
JOIN Evaluations e ON ep.EvaluationId = e.EvaluationId
WHERE e.ProjectFile LIKE '%MyProject.csproj'
AND ep.Name IN ('TargetFramework', 'Configuration', 'Platform', 'RuntimeIdentifier')
ORDER BY e.EvaluationId, ep.Name;
```

### Step 4: Check glob patterns

```sql
-- Count Compile items per evaluation to detect overly broad globs
SELECT e.EvaluationId, e.ProjectFile, COUNT(*) AS CompileCount
FROM EvaluationItems ei
JOIN Evaluations e ON ei.EvaluationId = e.EvaluationId
WHERE ei.ItemType = 'Compile'
GROUP BY e.EvaluationId
ORDER BY CompileCount DESC
LIMIT 10;
```

### Step 5: Inspect imported files

```sql
SELECT FilePath, LENGTH(Content) AS ContentBytes
FROM Files
WHERE FilePath LIKE '%.props' OR FilePath LIKE '%.targets'
ORDER BY ContentBytes DESC
LIMIT 20;
```

Deep import chains with large files indicate heavy evaluation overhead. |
| `text-replay` | Text-log replay | ## Diagnosing Evaluation Performance with Text-Log Replay

### Step 1: Replay the binlog

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log
```

### Step 2: Search for evaluation events

```bash
grep -i "Evaluation started\|Evaluation finished" full.log
```

Multiple evaluations for the same project indicate overbuilding. Look for timestamps to measure evaluation duration.

### Step 3: Check evaluation counts per project

```bash
grep -i "Evaluation started" full.log | grep -oP '"[^"]+\.(csproj|vbproj|fsproj)"' | sort | uniq -c | sort -rn
```

Any project with count > 1 per TFM is being over-evaluated.

### Step 4: Preprocess to analyze imports

```bash
dotnet msbuild -pp:full.xml MyProject.csproj
```

Search the preprocessed output for `<!-- Importing` comments to see the import tree depth. Large preprocessed output (>10K lines) indicates heavy evaluation.

### Step 5: Check evaluation time in performance summary

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary
grep "Evaluation Performance Summary\|Evaluation started\|Evaluation finished" full.log | head -30
``` |

Regardless of backend, the key diagnostics are:

1. Find the slowest evaluations and which projects they belong to
2. Check for multiple evaluations per project (overbuilding)
3. Compare global properties across evaluations to understand why duplicates occur
4. Inspect item counts (especially Compile) for overly broad globs
5. Analyze import chain depth

### Using /pp (preprocess)

- `dotnet msbuild -pp:full.xml MyProject.csproj`
- Shows the fully expanded project with ALL imports inlined
- Use to understand: what's imported, import depth, total content volume
- Large preprocessed output (>10K lines) = heavy evaluation

### Using /clp:PerformanceSummary

- Add to build command for timing breakdown
- Shows evaluation time separately from target/task execution

## Expensive Glob Patterns

- Globs like `**/*.cs` walk the entire directory tree
- Default SDK globs are optimized, but custom globs may not be
- Problem: globbing over `node_modules/`, `.git/`, `bin/`, `obj/` — millions of files
- Fix: use `<DefaultItemExcludes>` to exclude large directories
- Fix: be specific with glob paths: `src/**/*.cs` instead of `**/*.cs`
- Fix: use `<EnableDefaultItems>false</EnableDefaultItems>` only as last resort (lose SDK defaults)
- Check: grep for Compile items in the diagnostic log → if Compile items include unexpected files, globs are too broad

## Import Chain Analysis

- Deep import chains (>20 levels) slow evaluation
- Each import: file I/O + parse + evaluate
- Common causes: NuGet packages adding .props/.targets, framework SDK imports, Directory.Build chains
- Diagnosis: `/pp` output → search for `<!-- Importing` comments to see import tree
- Fix: reduce transitive package imports where possible, consolidate imports

## Multiple Evaluations

- A project evaluated multiple times = wasted work
- Common causes: referenced from multiple other projects with different global properties
- Each unique set of global properties = separate evaluation
- Fix: normalize global properties, use graph build (`/graph`)

## TreatAsLocalProperty

- Prevents property values from flowing to child projects via MSBuild task
- Overuse: declaring many TreatAsLocalProperty entries adds evaluation overhead
- Correct use: only when you genuinely need to override an inherited property

## Property Function Cost

- Property functions execute during evaluation
- Most are cheap (string operations)
- Expensive: `$([System.IO.File]::ReadAllText(...))` during evaluation — reads file on every evaluation
- Expensive: network calls, heavy computation
- Rule: property functions should be fast and side-effect-free

## Optimization Checklist

- [ ] Check preprocessed output size: `dotnet msbuild -pp:full.xml`
- [ ] Verify evaluation count: should be 1 per project per TFM
- [ ] Exclude large directories from globs
- [ ] Avoid file I/O in property functions during evaluation
- [ ] Minimize import depth
- [ ] Use graph build to reduce redundant evaluations
- [ ] Check for unnecessary UsingTask declarations