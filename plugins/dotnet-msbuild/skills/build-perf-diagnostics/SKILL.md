---
name: build-perf-diagnostics
description: "Reference knowledge for diagnosing MSBuild build performance issues. Only activate in MSBuild/.NET build context. Use when builds are slow, to identify bottlenecks using binary log analysis. Covers timeline analysis, node utilization, expensive targets/tasks, Roslyn analyzer impact, RAR performance, and critical path identification. Works with binlog replay to text logs for data-driven analysis."
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
- **Reduce transitive references**: Set `<DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>` to avoid pulling in the full transitive closure. Use `ReferenceOutputAssembly="false"` on ProjectReferences that are only needed at build time (not API surface). Trim unused PackageReferences.

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
- **Root causes**: copying thousands of files, copying across network drives, accidentally batched Copy tasks (runs once per item instead of batch — see dotnet/msbuild#12884)
- **Fixes**: use hardlinks (`<CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>true</CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>`), reduce CopyToOutputDirectory items, use `<UseCommonOutputDirectory>true</UseCommonOutputDirectory>` when appropriate, set `<SkipCopyUnchangedFiles>true</SkipCopyUnchangedFiles>`, consider `--artifacts-path` (.NET 8+) for centralized output layout
- **Dev Drive**: On Windows, switching to a Dev Drive (ReFS with copy-on-write and reduced Defender scans) dramatically reduces file I/O overhead. OrchardCore with 7257 Copy tasks shows significant speedup. Enable via https://aka.ms/devdrive — recommend for both dev machines and self-hosted CI agents.

### 5. Evaluation Overhead

- **Symptoms**: build starts slow before any compilation
- **Root causes**: complex Directory.Build.props, wildcard globs scanning large directories, NuGetSdkResolver overhead (adds 180-400ms per project evaluation even when restored — see dotnet/msbuild#4025)
- **Fixes**: reduce Directory.Build.props complexity, use `<EnableDefaultItems>false</EnableDefaultItems>` for legacy projects with explicit file lists, avoid NuGet-based SDK resolvers if possible
- See: `eval-performance` skill for detailed guidance

### 6. NuGet Restore in Build

- **Symptoms**: restore runs every build even when unnecessary
- **Fixes**:
  - Separate restore from build: `dotnet restore` then `dotnet build --no-restore`
  - Enable static graph evaluation: `<RestoreUseStaticGraphEvaluation>true</RestoreUseStaticGraphEvaluation>` in Directory.Build.props — can save 20s+ in large (~200s) builds

### 7. Large Project Count and Graph Shape

- **Symptoms**: many small projects, each takes minimal time but overhead adds up; deep dependency chains serialize the build
- **Consider**: project consolidation, or use `/graph` mode for better scheduling
- **Graph shape matters**: a wide dependency graph (few levels, many parallel branches) builds much faster than a deep one (many levels, serialized). Measured improvements: **40% faster clean builds, 20% faster incremental builds** when refactoring from deep to wide.
- **Actions**: look for unnecessary project dependencies, consider splitting a bottleneck project into two, or merging small leaf projects

### 7a. MSBuild Server and CLI Caching

- **Symptoms**: small incremental builds from CLI are slower than expected
- **Fix**: enable MSBuild Server by setting environment variable `MSBUILDUSESERVER=1` — provides better caching for incremental CLI builds

### 7b. Inline Task Overhead

- **Symptoms**: tasks show surprisingly high overhead (>1s) for simple operations
- **Root cause**: inline tasks (defined in `.targets` files with `<UsingTask TaskFactory="RoslynCodeTaskFactory">`) are compiled at runtime, adding ~1s overhead vs ~3ms for pre-compiled tasks
- **Fix**: convert frequently-executed inline tasks to compiled task assemblies

### 8. Misleading ResolveProjectReferences Time

- **Symptoms**: ResolveProjectReferences appears as the most expensive target in the performance summary
- **Root cause**: the reported time includes waiting for dependent projects to build while the node is yielded (see dotnet/msbuild#3135). The node may be doing useful work on other projects during this wait.
- **Diagnosis**: focus on the **self-time** of actual tasks (Csc, RAR, Copy) rather than the total time of wrapper targets like ResolveProjectReferences. Look at Task Performance Summary instead of Target Performance Summary for a more accurate picture.
- **Not a fix target**: don't optimize ResolveProjectReferences directly — optimize the targets/tasks it's waiting on.

### 9. Incrementality Anti-patterns

- **Symptoms**: builds take the same time on second run as first, targets never skip even when sources haven't changed
- **Targets generating Items via Tasks** (see dotnet/msbuild#13206): when a Target with `Inputs`/`Outputs` invokes a Task that generates Items (e.g., `<Output TaskParameter="ExcludedFiles" ItemName="_FilesExcludedFromBundle"/>`), and the target is skipped because outputs are up-to-date, those Items disappear. Downstream targets depending on those Items will fail or behave incorrectly.
- **Fix**: separate computation (always-run, no Inputs/Outputs) from execution targets. The computation target discovers/lists items; the execution target has Inputs/Outputs and does the actual work.
- See: `incremental-build` skill for comprehensive guidance

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
- [ ] Enable hardlinks for Copy (`<CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>true`)
- [ ] Disable analyzers conditionally in dev inner loop (`<RunAnalyzers Condition="'$(ContinuousIntegrationBuild)' != 'true'">false</RunAnalyzers>`)
- [ ] Enable reference assemblies (`<ProduceReferenceAssembly>true</ProduceReferenceAssembly>`) — especially for older non-SDK-style projects
- [ ] Check for broken incremental builds (see `incremental-build` skill)
- [ ] Check for bin/obj clashes (see `check-bin-obj-clash` skill)
- [ ] Use graph build (`/graph`) for multi-project solutions
- [ ] Disable packing for non-package projects (`<IsPackable>false</IsPackable>`)
- [ ] Use `--artifacts-path` (.NET 8+) for centralized output layout
- [ ] Set `<Deterministic>true</Deterministic>` as a prerequisite for build caching
- [ ] Enable Dev Drive (ReFS) on Windows dev machines and self-hosted CI (`https://aka.ms/devdrive`)
- [ ] Enable MSBuild Server for CLI builds (`MSBUILDUSESERVER=1`)
- [ ] Run `dotnet build /check` for built-in BuildCheck diagnostics

## Impact Categorization

When reporting findings, categorize by impact to help prioritize fixes:

- 🔴 **HIGH IMPACT** (do first): Items consuming >10% of total build time, or a single target >50% of build time
- 🟡 **MEDIUM IMPACT**: Items consuming 2-10% of build time
- 🟢 **QUICK WINS**: Easy changes with modest impact (e.g., property flags in Directory.Build.props)
