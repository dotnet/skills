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
