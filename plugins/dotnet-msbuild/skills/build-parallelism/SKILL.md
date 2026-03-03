---
name: build-parallelism
description: "Guide for optimizing MSBuild build parallelism and multi-project scheduling. Only activate in MSBuild/.NET build context. Use when builds are not utilizing all CPU cores, when looking to speed up multi-project builds, or when evaluating graph build mode. Covers /maxcpucount, project dependency graphs, graph build (/graph), BuildInParallel, and reducing unnecessary project references."
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
