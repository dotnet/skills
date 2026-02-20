<!-- AUTO-GENERATED — DO NOT EDIT. Regenerate with: node src/dotnet-msbuild/build.js -->

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

---

---
name: build-perf-diagnostics
description: "Reference knowledge for diagnosing MSBuild build performance issues. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when builds are slow, to identify bottlenecks using binary log analysis. Covers timeline analysis, node utilization, expensive targets/tasks, Roslyn analyzer impact, RAR performance, and critical path identification. Works with the binlog MCP tools for data-driven analysis."
---

## Performance Analysis Methodology

1. **Generate a binlog**: `dotnet build /bl`
2. **Load with binlog MCP**: `load_binlog`
3. **Get overall picture**: `get_expensive_projects`, `get_expensive_targets`, `get_expensive_tasks`
4. **Analyze node utilization**: `get_node_timeline`
5. **Drill into bottlenecks**: `get_project_target_times`, `search_targets_by_name`
6. **Check analyzers**: `get_expensive_analyzers`

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

### 2. Roslyn Analyzers and Source Generators

- **Symptoms**: Csc task takes much longer than expected for file count (>2× clean compile time)
- **Diagnosis**: `get_expensive_analyzers` → identify top offenders, compare Csc duration with and without analyzers
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

- **Symptoms**: `get_node_timeline` shows most nodes idle while one works
- **Common culprits**: targets without proper dependency declaration, single project on critical path
- **Fixes**: split large projects, optimize the critical path project, ensure proper `BuildInParallel`

### 4. Excessive File I/O (Copy tasks)

- **Symptoms**: Copy task shows high aggregate time
- **Root causes**: copying thousands of files, copying across network drives
- **Fixes**: use hardlinks (`<CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>true</CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>`), reduce CopyToOutputDirectory items, use `<UseCommonOutputDirectory>true</UseCommonOutputDirectory>` when appropriate

### 5. Evaluation Overhead

- **Symptoms**: build starts slow before any compilation
- See: `eval-performance` skill for detailed guidance

### 6. NuGet Restore in Build

- **Symptoms**: restore runs every build even when unnecessary
- **Fix**: separate restore from build: `dotnet restore` then `dotnet build --no-restore`

### 7. Large Project Count

- **Symptoms**: many small projects, each takes minimal time but overhead adds up
- **Consider**: project consolidation, or use `/graph` mode for better scheduling

## Using Binlog MCP Tools for Performance Analysis

Step-by-step workflow with the actual MCP tool calls:

1. `load_binlog` → note total duration and node count
2. `get_expensive_projects(top_number=5, sortByExclusive=true)` → find where time is spent
3. `get_expensive_targets(top_number=10)` → which targets dominate
4. `get_expensive_tasks(top_number=10)` → which tasks dominate
5. `get_node_timeline()` → check parallelism utilization
6. `get_expensive_analyzers(top_number=10)` → analyzer overhead
7. For a specific slow project: `get_project_target_times(projectId=X)` → target breakdown
8. Deep dive: `search_binlog(query="$time $target TargetName")` → timing for specific targets

## Quick Wins Checklist

- [ ] Use `/maxcpucount` (or `-m`) for parallel builds
- [ ] Separate restore from build (`--no-restore`)
- [ ] Enable hardlinks for Copy
- [ ] Disable analyzers in dev inner loop
- [ ] Check for broken incremental builds (see `incremental-build` skill)
- [ ] Check for bin/obj clashes (see `check-bin-obj-clash` skill)
- [ ] Use graph build (`/graph`) for multi-project solutions

---

---
name: incremental-build
description: "Guide for optimizing MSBuild incremental builds. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when builds are slower than expected on subsequent runs, when 'nothing changed but it rebuilds anyway', or when diagnosing why incremental builds are broken. Covers Inputs/Outputs on targets, FileWrites tracking, up-to-date checks, and diagnosing unnecessary rebuilds via binlog analysis."
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

### Step-by-step using binlog

1. **Build twice with binlogs** to capture the incremental build behavior:
   ```shell
   dotnet build /bl:first.binlog
   dotnet build /bl:second.binlog
   ```
   The first build establishes the baseline. The second build is the one you want to be incremental. Analyze `second.binlog`.

2. **Load the second binlog** and search for targets that actually executed:
   Use `search_binlog` with query `skipped=false` to find all targets that were not skipped. In a perfectly incremental build, most targets should be skipped.

3. **Inspect non-skipped targets** using `get_target_info_by_name` to see why they ran. Check the `Reason` field — it tells you whether MSBuild determined the target was out of date and why.

4. **Look for key messages** in the binlog:
   - `"Building target 'X' completely"` — means MSBuild found no outputs or all outputs are missing; this is a full target execution.
   - `"Building target 'X' incrementally"` — means some (but not all) outputs are out of date.
   - `"Skipping target 'X' because all output files are up-to-date"` — target was correctly skipped.

5. **Search for "is newer than output"** messages to find the specific input file that triggered the rebuild:
   ```
   search_binlog with query: "is newer than output"
   ```
   This reveals exactly which input file's timestamp caused MSBuild to consider the target out of date.

### Additional diagnostic techniques

- Compare `first.binlog` and `second.binlog` side by side in the MSBuild Structured Log Viewer to see what changed.
- Use `get_project_target_times` to see which targets consumed the most time in the second build — these are your optimization targets.
- Check for targets with zero-duration that still ran — they may have unnecessary dependencies causing them to execute.

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

---
name: build-parallelism
description: "Guide for optimizing MSBuild build parallelism and multi-project scheduling. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when builds are not utilizing all CPU cores, when looking to speed up multi-project builds, or when evaluating graph build mode. Covers /maxcpucount, project dependency graphs, graph build (/graph), BuildInParallel, and reducing unnecessary project references."
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
- Diagnosis: `get_node_timeline()` in binlog MCP → shows per-node activity/idle time
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
- See `multithreaded-task-migration` skill for migration guide
- Tasks must declare thread-safety via `[MSBuildMultiThreadableTask]`

## Analyzing Parallelism with Binlog

Step-by-step:

1. `get_node_timeline()` → see active vs idle time per node
2. Ideal: all nodes busy most of the time
3. If nodes are idle: too many serial dependencies, or one slow project blocking others
4. `get_expensive_projects(sortByExclusive=true)` → find the bottleneck project
5. Consider splitting large projects or optimizing the critical path

## CI/CD Parallelism Tips

- Use `-m` in CI (many CI runners have multiple cores)
- Consider splitting solution into build stages for extreme parallelism
- Use build caching (see `build-caching` skill) to avoid rebuilding unchanged projects
- `dotnet build /graph` works well with structured CI pipelines

---

## build-caching

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

### Why

[truncated]