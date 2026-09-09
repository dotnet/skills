---
name: build-parallelism
description: "Analyze and improve MSBuild scheduling across two or more projects. USE FOR: solution or project-graph throughput, `-m` node utilization, BuildInParallel on an MSBuild task with multiple Projects, serial ProjectReference critical paths, graph builds, and solution filters. Requires multiple projects and a scheduling or throughput question. DO NOT USE for non-MSBuild build systems."
license: MIT
---

## Diagnose a slow parallel build (start here)

Work this checklist in order — it targets the usual root cause (a serial
dependency chain that no number of cores can parallelize):

1. **Confirm parallelism is even on.** Rebuild with `dotnet build -m /bl:{}`
   (PowerShell: `dotnet build -m '-bl:{}'`). `-m` with no number uses all logical
   processors; without `-m` MSBuild runs a single node (sequential).
2. **Find the critical path.** From the binlog, read per-project timings and the
   node timeline. If total build time ≈ the sum of the projects on one
   dependency chain, that chain — not CPU count — is the bottleneck.
3. **Name the chain explicitly**, e.g. `Core → Api → Web → Tests`. A long serial
   chain stays serial no matter how large `-m` is, because each project waits on
   its predecessor.
4. **Look for unnecessary `ProjectReference` edges** that lengthen the chain — a
   reference that only needs build order (not the output assembly), or one that
   could be a `PackageReference`, forces serialization it doesn't need.
5. **Preserve real dependencies.** Never remove a critical-path reference only
   because it is slow. First prove that no source, runtime, or artifact dependency
   needs it. If every edge is valid, optimize or split the slow project instead.
6. **Recommend flattening only false dependencies** so independent projects build
   concurrently, and consider `/graph` for better scheduling.

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
- Diagnosis: replay binlog to diagnostic log with `performancesummary` and check Project Performance Summary — shows per-project time; grep for `node.*assigned` to check scheduling
- Wide graphs (many independent projects) parallelize well; deep graphs (long chains) don't

## Graph Build Mode (`/graph`)

- `dotnet build /graph` or `msbuild /graph`
- What it changes: MSBuild constructs the full project dependency graph BEFORE building
- Benefits: better scheduling, avoids redundant evaluations, enables isolated builds
- Limitations: all projects must use `<ProjectReference>` (no programmatic MSBuild task references)
- When to use: large solutions with many projects, CI builds
- When NOT to use: projects that dynamically discover references at build time

## Optimizing Project References

- Reduce a `<ProjectReference>` only after proving it is unnecessary; a slow but
  valid dependency must stay in the graph
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

### Primary: binlog MCP (preferred)

Use the **binlog MCP server** (`Microsoft.AITools.BinlogMcp`, exposed under the `binlog` MCP namespace):

1. Use expensive_projects tool → find the slowest projects and compare individual vs total build time
2. Use expensive_targets tool → find bottleneck targets
3. Use project_target_times tool → drill into a specific project's target-level timing
4. Ideal: build time should be much less than sum of project times (parallelism)
5. If build time ≈ sum of project times: too many serial dependencies, or one slow project blocking others

### Fallback: text-log replay (when MCP is unavailable)

Step-by-step:

1. Replay the binlog: `dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary`
2. Check Project Performance Summary at the end of `full.log`
3. Ideal: build time should be much less than sum of project times (parallelism)
4. If build time ≈ sum of project times: too many serial dependencies, or one slow project blocking others
5. `grep 'Target Performance Summary' -A 30 full.log` → find the bottleneck targets
6. Consider splitting large projects or optimizing the critical path

## CI/CD Parallelism Tips

- Use `-m` in CI (many CI runners have multiple cores)
- Consider splitting solution into build stages for extreme parallelism
- Use build caching (NuGet lock files, deterministic builds) to avoid rebuilding unchanged projects
- `dotnet build /graph` works well with structured CI pipelines
