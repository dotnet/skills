---
name: build-perf-diagnostics
description: "Diagnose MSBuild build performance bottlenecks using binary log analysis. Only activate in MSBuild/.NET build context. USE FOR: identifying why builds are slow by analyzing binlog performance summaries, detecting ResolveAssemblyReference (RAR) taking >5s, Roslyn analyzers consuming >30% of Csc time, single targets dominating >50% of build time, node utilization below 80%, excessive Copy tasks, NuGet restore running every build. Covers timeline analysis, Target/Task Performance Summary interpretation, and 7 common bottleneck categories. Use after build-perf-baseline has established measurements. DO NOT USE FOR: establishing initial baselines (use build-perf-baseline first), fixing incremental build issues (use incremental-build), parallelism tuning (use build-parallelism), non-MSBuild build systems. INVOKES: dotnet msbuild binlog replay with performancesummary, grep for analysis."
license: MIT
---

## Performance Analysis Methodology

1. **Generate a binlog**: `dotnet build /bl:{} -m`
2. **Replay to diagnostic log with performance summary**:
   ```bash
   dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary
   ```
3. **Read the performance summary** (at the end of `full.log`):
   ```bash
   grep "Target Performance Summary\|Task Performance Summary" -A 50 full.log
   ```
4. **Find expensive targets and tasks**: The PerformanceSummary section lists all targets/tasks sorted by cumulative time
5. **Check for node utilization**: grep for scheduling and node messages
   ```bash
   grep -i "node.*assigned\|building with\|scheduler" full.log | head -30
   ```
6. **Check analyzers**: grep for analyzer timing
   ```bash
   grep -i "analyzer.*elapsed\|Total analyzer execution time\|CompilerAnalyzerDriver" full.log
   ```

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

## Using Binlog Replay for Performance Analysis

Step-by-step workflow using text log replay:

1. **Replay with performance summary**:
   ```bash
   dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary
   ```
2. **Read target/task performance summaries** (at the end of `full.log`):
   ```bash
   grep "Target Performance Summary\|Task Performance Summary" -A 50 full.log
   ```
   This shows all targets and tasks sorted by cumulative time — equivalent to finding expensive targets/tasks.
3. **Find per-project build times**:
   ```bash
   grep "done building project\|Project Performance Summary" full.log
   ```
4. **Check parallelism** (multi-node scheduling):
   ```bash
   grep -i "node.*assigned\|RequiresLeadingNewline\|Building with" full.log | head -30
   ```
5. **Check analyzer overhead**:
   ```bash
   grep -i "Total analyzer execution time\|analyzer.*elapsed\|CompilerAnalyzerDriver" full.log
   ```
6. **Drill into a specific slow target**:
   ```bash
   grep 'Target "CoreCompile"\|Target "ResolveAssemblyReferences"' full.log
   ```

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
