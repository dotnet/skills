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
- [ ] Separate restore from build (`--no-restore`)
- [ ] Enable hardlinks for Copy
- [ ] Disable analyzers in dev inner loop
- [ ] Check for broken incremental builds (see `incremental-build` skill)
- [ ] Check for bin/obj clashes (see `check-bin-obj-clash` skill)
- [ ] Use graph build (`/graph`) for multi-project solutions
