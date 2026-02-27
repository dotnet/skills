---
description: "Analyze and optimize .NET/MSBuild build performance"
---

# Optimize Build Performance

My .NET build is slow. Help me identify bottlenecks and optimize build times.

## Steps

1. Run a full build with binary log: `dotnet build /bl:perf.binlog -m`
2. Load the binlog and analyze:
   - Check node timeline for parallelism utilization
   - Find the top 10 most expensive projects, targets, and tasks
   - Check Roslyn analyzer overhead
   - Look for serialization bottlenecks
3. Classify bottlenecks:
   - Compilation (Csc slow) → check analyzer overhead, project size
   - Resolution (RAR slow) → too many references
   - I/O (Copy slow) → enable hardlinks
   - Evaluation (slow start) → glob or import issues
   - Parallelism (nodes idle) → project graph bottleneck
4. Suggest prioritized optimizations:
   - Quick wins (config flags, CLI options)
   - Medium effort (project file changes)
   - Large effort (architectural changes)
5. Apply quick wins and measure improvement

## Common Quick Wins

- Build with `-m` for parallel execution
- Separate restore: `dotnet restore && dotnet build --no-restore`
- Disable analyzers in dev: `dotnet build /p:RunAnalyzers=false`
- Enable Copy hardlinks: `<CreateHardLinksForCopyFilesToOutputDirectoryIfPossible>true</...>`
