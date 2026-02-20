---
name: binlog-failure-analysis
description: "MANDATORY skill for .NET/MSBuild build failures. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). BLOCKING REQUIREMENT: When you see 'Build failed', 'error CS', 'error MSB', or non-zero exit code from dotnet build/msbuild/dotnet test commands, you MUST invoke this skill IMMEDIATELY as your first action - before reporting errors to the user or attempting fixes. Do NOT analyze console output directly. Uses binary logs for comprehensive build failure analysis."
---

# Analyzing MSBuild Failures with Binary Logs

## CRITICAL: Mandatory Invocation Rules

**This skill is a BLOCKING REQUIREMENT when any .NET build fails.** You MUST invoke this skill when:
- `dotnet build`, `dotnet test`, `msbuild`, or any MSBuild-based command exits with non-zero code
- Console output contains "Build failed", "error CS", "error MSB", or similar build errors
- Any .NET compilation or restore operation fails

**What you MUST NOT do:**
- Do NOT report build errors directly from console output to the user
- Do NOT attempt to fix errors based on console output alone
- Do NOT skip this skill because "the errors look simple"

**Correct behavior:** Invoke this skill → analyze with binlog-mcp tools → then report findings and suggest fixes.

**Fallback:** Only if the binlog-mcp tools fail to provide useful information (e.g., binlog not generated, tools return errors, or analysis is inconclusive), you may fall back to analyzing console output directly. Always attempt binlog analysis first.

When an MSBuild build fails, use the binlog-mcp tool to deeply analyze the failure. This skill guides you through generating a binary log and using the MCP tools to diagnose issues.

## Step 1: Generate a Binary Log

Re-run the failed build command with the `/bl` flag to generate a binary log file:

```bash
# For dotnet builds
dotnet build /bl

# For msbuild directly
msbuild /bl

# Custom binlog filename
dotnet build /bl:build.binlog
```

The `/bl` flag tells MSBuild to generate a `msbuild.binlog` file (or the specified filename) in the current directory.

## Step 2: Load the Binary Log

Use the `load_binlog` tool to load the generated binlog file:

```
load_binlog with path: "<absolute-path-to-binlog>"
```

This must be called before using any other binlog analysis tools. Returns `InterestingBuildData` containing total duration in milliseconds and node count.

## Step 3: Analyze the Failure

### Get Diagnostics (Errors and Warnings)

Use `get_diagnostics` to extract all errors and warnings:

```
get_diagnostics with:
  - binlog_file: "<path>"
  - includeErrors: true
  - includeWarnings: true
  - includeDetails: true
  - projectIds: [optional array of project IDs to filter]
  - targetIds: [optional array of target IDs to filter]
  - taskIds: [optional array of task IDs to filter]
  - maxResults: [optional max number of diagnostics]
```

Returns `DiagnosticAnalysisResult` with severity classification (Error, Warning, Info), source locations, file paths, line numbers, and context information.

### Search for Specific Issues

Use `search_binlog` with the powerful query language to find specific issues:

```
search_binlog with:
  - binlog_file: "<path>"
  - query: "error CS1234"        # Find specific error codes
  - query: "$task Csc"           # Find all C# compilation tasks
  - query: "under($project MyProject)"  # Find nodes under a specific project
  - maxResults: 300              # Default limit
  - includeDuration: true        # Include timing info
  - includeContext: true         # Include project/target/task IDs
```

### Investigate Expensive Operations

If the build is slow or timing out:

```
get_expensive_targets with binlog_file and top_number: 10
get_expensive_tasks with binlog_file and top_number: 10
get_expensive_projects with binlog_file, top_number: 10, sortByExclusive: true
```

The `get_expensive_projects` tool supports:
- `excludeTargets`: Array of target names to exclude (e.g., ['Copy', 'CopyFilesToOutputDirectory'])
- `sortByExclusive`: Sort by exclusive time (true) or inclusive time (false)

### Analyze Roslyn Analyzers

If compilation is slow, check analyzer performance:

```
get_expensive_analyzers with binlog_file and top_number: 10
```

Returns aggregated analyzer data with execution count, total/average/min/max durations.

For specific Csc task analyzer details:
```
get_task_analyzers with binlog_file, projectId, targetId, taskId
```

## Available Tools Reference

### Binlog Loading
| Tool | Description |
|------|-------------|
| `load_binlog` | Load a binlog file (required before other tools) |

### Diagnostic Analysis
| Tool | Description |
|------|-------------|
| `get_diagnostics` | Extract errors/warnings with optional filtering |

### Search Analysis
| Tool | Description |
|------|-------------|
| `search_binlog` | Powerful freetext search with MSBuild Log Viewer syntax |

### Project Analysis
| Tool | Description |
|------|-------------|
| `list_projects` | List all projects in the build |
| `get_expensive_projects` | Get N most expensive projects |
| `get_project_build_time` | Get build time for a specific project |
| `get_project_target_list` | List all targets for a project |
| `get_project_target_times` | Get all target times for a project in one call |

### Target Analysis
| Tool | Description |
|------|-------------|
| `get_expensive_targets` | Get N most expensive targets |
| `get_target_info_by_id` | Get target details by ID (more efficient) |
| `get_target_info_by_name` | Get target details by name |
| `search_targets_by_name` | Find all executions of a target across projects |

### Task Analysis
| Tool | Description |
|------|-------------|
| `get_expensive_tasks` | Get N most expensive tasks |
| `get_task_info` | Get detailed task invocation info |
| `list_tasks_in_target` | List all tasks in a target |
| `search_tasks_by_name` | Find all invocations of a task |

### Analyzer Analysis
| Tool | Description |
|------|-------------|
| `get_expensive_analyzers` | Get N most expensive Roslyn analyzers/generators |
| `get_task_analyzers` | Get analyzer data from a specific Csc task |

### Evaluation Analysis
| Tool | Description |
|------|-------------|
| `list_evaluations` | List all evaluations for a project |
| `get_evaluation_global_properties` | Get global properties for an evaluation |
| `get_evaluation_properties_by_name` | Get specific properties by name |
| `get_evaluation_items_by_name` | Get items by type (Compile, PackageReference, etc.) |

### File Analysis
| Tool | Description |
|------|-------------|
| `list_files_from_binlog` | List embedded source files |
| `get_file_from_binlog` | Get content of an embedded file |

### Timeline Analysis
| Tool | Description |
|------|-------------|
| `get_node_timeline` | Get active/inactive time for build nodes |

## Common Analysis Workflows

### Build Error Investigation
1. `load_binlog` - Load the binlog
2. `get_diagnostics` with `includeErrors: true` - Get all errors
3. `search_binlog` with the error code - Find context around the error
4. `list_projects` - Identify which projects are involved
5. `get_file_from_binlog` - View source files embedded in the binlog

### Performance Investigation
1. `load_binlog` - Load the binlog
2. `get_expensive_targets` - Find slow targets
3. `get_expensive_tasks` - Find slow tasks
4. `get_expensive_analyzers` - Check if analyzers are slow
5. `search_targets_by_name` - Find all executions of a specific target
6. `get_node_timeline` - Analyze parallelism and node utilization

### Dependency/Evaluation Issues
1. `load_binlog` - Load the binlog
2. `list_projects` - See all projects in the build
3. `list_evaluations` - Check for multiple evaluations (indicates overbuilding)
4. `get_evaluation_global_properties` - Compare properties between evaluations
5. `get_evaluation_items_by_name` - Inspect PackageReference, Compile items

## Query Language Reference

The `search_binlog` tool supports powerful query syntax from MSBuild Structured Log Viewer:

### Basic Search
| Query | Description |
|-------|-------------|
| `text` | Find nodes containing text |
| `"exact phrase"` | Exact string matching |
| `term1 term2` | Multiple terms (AND logic) |

### Node Type Filtering
| Query | Description |
|-------|-------------|
| `$task TaskName` | Find tasks by name |
| `$target TargetName` | Find targets by name |
| `$project ProjectName` | Find project nodes |
| `$csc` | Shortcut for `$task Csc` |
| `$rar` | Shortcut for `$task ResolveAssemblyReference` |

### Property Matching
| Query | Description |
|-------|-------------|
| `name=value` | Match nodes where name equals value |
| `value=text` | Match nodes where value equals text |

### Hierarchical Search
| Query | Description |
|-------|-------------|
| `under($project X)` | Find nodes under project X |
| `notunder($target Y)` | Exclude nodes under target Y |
| `project($query)` | Find nodes within matching projects |
| `not($query)` | Exclude matching nodes |

### Time-based Filtering
| Query | Description |
|-------|-------------|
| `start<"2023-01-01 09:00:00"` | Nodes started before time |
| `start>"2023-01-01 09:00:00"` | Nodes started after time |
| `end<"datetime"` | Nodes ended before time |
| `end>"datetime"` | Nodes ended after time |

### Special Properties
| Query | Description |
|-------|-------------|
| `skipped=true` | Find skipped targets |
| `skipped=false` | Find executed targets |
| `height=N` or `height=max` | Filter by tree depth |
| `$123` | Find node by index |

### Result Enhancement
| Query | Description |
|-------|-------------|
| `$time` or `$duration` | Include timing in results |
| `$start` or `$starttime` | Include start time |
| `$end` or `$endtime` | Include end time |

## Cross-Reference: Related Knowledge Base Skills

After identifying errors from binlog analysis, consult these specialized skills for in-depth guidance:

### By Error Code Prefix
| Error Prefix | Skill to Consult | Typical Issues |
|-------------|-----------------|----------------|
| `CS####` | `common-build-errors` | C# compiler errors — missing types, namespaces, type mismatches |
| `MSB####` | `common-build-errors` | MSBuild engine errors — missing imports, SDK issues, conflicts |
| `NU####` | `common-build-errors` + `nuget-restore-failures` | NuGet errors — package resolution, feed issues, version conflicts |
| `NETSDK####` | `common-build-errors` + `sdk-workload-resolution` | SDK/workload errors — missing SDK, TFM issues |
| `FS####` | `common-build-errors` | F# compiler errors |
| `BC####` | `common-build-errors` | VB compiler errors |

### By Failure Category
| Category | Skill to Consult |
|----------|-----------------|
| NuGet restore failures | `nuget-restore-failures` |
| SDK or workload not found | `sdk-workload-resolution` |
| TFM compatibility / multi-targeting | `multitarget-tfm-issues` |
| Output path conflicts / intermittent failures | `check-bin-obj-clash` |
| Slow builds (not errors, but performance) | `build-perf-diagnostics` |
| Incremental build broken (rebuilds everything) | `incremental-build` |

### Common Error Patterns Quick-Lookup
When binlog analysis reveals these patterns, here's the fast path:

1. **"Package X could not be found"** → Check `nuget-restore-failures` for feed/auth issues
2. **"The imported project was not found" (MSB4019)** → Check `sdk-workload-resolution` for SDK install
3. **"Reference assemblies not found" (MSB3644)** → Missing targeting pack, check `sdk-workload-resolution`
4. **"Found conflicts between different versions" (MSB3277)** → Check `common-build-errors` for binding redirect guidance
5. **"Package downgrade detected" (NU1605)** → Check `nuget-restore-failures` for version resolution
6. **Multiple evaluation of same project** → Check `eval-performance` for overbuilding diagnosis
7. **Build succeeds but is very slow** → Use `build-perf-diagnostics` and the `build-perf` agent

### Decision Tree: When to Generate a New Binlog

- **Existing binlog is available and recent** → Load and analyze it first
- **Existing binlog is stale** (code or config changed since) → Generate fresh binlog
- **No binlog exists** → Generate one using `binlog-generation` skill conventions
- **Binlog analysis is inconclusive** → Regenerate with higher verbosity: `dotnet build /bl /v:diag`
- **Multiple build configurations failing** → Generate separate binlogs per configuration

## Tips

- The binlog contains embedded source files - use `list_files_from_binlog` and `get_file_from_binlog` to view them
- Use `maxResults` parameter to limit large result sets
- Use `get_target_info_by_id` instead of `get_target_info_by_name` when you have the ID for better performance
- Use `get_project_target_times` to get all target times in one call instead of querying individually
- Results from `get_expensive_projects` and `get_project_build_time` are cached for performance
- The binlog captures the complete build state, making it ideal for reproducing and diagnosing issues
