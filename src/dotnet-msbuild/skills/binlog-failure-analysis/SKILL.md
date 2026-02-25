---
name: binlog-failure-analysis
description: "Skill for .NET/MSBuild *.binlog files and complicated build failures. Only activate in MSBuild/.NET build context. This skill uses binary logs for comprehensive build failure analysis."
---

# Analyzing MSBuild Failures with Binary Logs

**Use the binlog MCP tools for all binlog analysis.** The MCP server provides structured, efficient access to everything inside a binlog. Do not attempt to parse binlogs manually.

## ❌ DO NOT

- **Do NOT install CLI tools** (`binlogtool`, `dotnet-script`, `msbuild-log`, etc.) — the MCP tools already provide full access
- **Do NOT write C# scripts or programs** to parse binlogs with `MSBuild.StructuredLogger` — this wastes time on compilation errors and produces inferior results
- **Do NOT use `binlogtool savefiles`/`binlogtool reconstruct`** — use `get_file_from_binlog` instead
- **Do NOT use `dotnet msbuild -flp`** to replay binlogs into text logs — use `get_diagnostics` and `search_binlog` instead

## Build Error Investigation (Primary Workflow)

Follow these steps in order. Every step uses an MCP tool — no bash needed for analysis.

1. **Load**: `load_binlog` with the absolute path to the `.binlog` file
2. **Get errors**: `get_diagnostics` with `includeErrors: true, includeDetails: true` — returns all errors with file paths, line numbers, and context
3. **List projects**: `list_projects` — see all projects and their build status to identify which failed
4. **Get source files**: `get_file_from_binlog` — binlogs embed source files; retrieve `.csproj`, `.cs`, or any file directly without needing the original source tree
5. **Check dependencies**: `get_evaluation_items_by_name` with item type `ProjectReference` or `PackageReference` — inspect what each project references
6. **Search for context**: `search_binlog` with the error code (e.g., `"error CS0246"`) — find where errors appear in the build tree
7. **Identify cascading failures**: Compare which projects had `CoreCompile` errors vs which failed at `ResolveProjectReferences` because a dependency failed. Use `search_binlog` with `under($project ProjectName) $target CoreCompile` to check if a project reached compilation.

**After completing analysis, write the diagnosis immediately.** Do not start additional investigation loops — synthesize what you have.

## Additional Workflows

### Performance Investigation
1. `load_binlog` → `get_expensive_targets` → `get_expensive_tasks` → `get_expensive_analyzers` → `search_targets_by_name` → `get_node_timeline`

### Dependency/Evaluation Issues
1. `load_binlog` → `list_projects` → `list_evaluations` → `get_evaluation_global_properties` → `get_evaluation_items_by_name`

## Generating a Binary Log (only if no binlog exists)

If no `.binlog` file is available, re-run the failed build with the `/bl` flag:

```bash
dotnet build /bl:build.binlog
```

Use `/bl:{}` (or `/bl:{{}}` in PowerShell) to generate a unique filename automatically. Then return to the workflow above.

## Detailed Tool Usage

### Get Diagnostics (Errors and Warnings)

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

Returns severity classification, source locations, file paths, line numbers, and context.

### Search for Specific Issues

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

### Get Embedded Files

Binlogs embed all source files from the build. Use this instead of trying to extract files manually:

```
list_files_from_binlog — list all embedded files
get_file_from_binlog with path — retrieve the content of any embedded file
```

This gives you `.csproj` files, `.cs` source, `project.assets.json`, `Directory.Build.props`, and everything else — no need for external tools.

### Check Project References and Packages

```
get_evaluation_items_by_name with:
  - item type "PackageReference" — see all NuGet packages a project references
  - item type "ProjectReference" — see project-to-project dependencies
  - item type "Reference" — see direct assembly references
```

### Investigate Expensive Operations

If the build is slow or timing out:

```
get_expensive_targets with binlog_file and top_number: 10
get_expensive_tasks with binlog_file and top_number: 10
get_expensive_projects with binlog_file, top_number: 10, sortByExclusive: true
```

### Analyze Roslyn Analyzers

```
get_expensive_analyzers with binlog_file and top_number: 10
get_task_analyzers with binlog_file, projectId, targetId, taskId
```

## Available Tools Reference

### Binlog Loading
| Tool | Description |
|------|-------------|
| `load_binlog` | Load a binlog file (**required first, before any other tool**) |

### Diagnostic Analysis
| Tool | Description |
|------|-------------|
| `get_diagnostics` | Extract errors/warnings with optional filtering |

### Search
| Tool | Description |
|------|-------------|
| `search_binlog` | Powerful freetext search with MSBuild Log Viewer query syntax |

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
| `list_files_from_binlog` | List all embedded source files |
| `get_file_from_binlog` | **Get content of any embedded file** (source, csproj, props, etc.) |

### Timeline Analysis
| Tool | Description |
|------|-------------|
| `get_node_timeline` | Get active/inactive time for build nodes |

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

### By Failure Category
| Category | Skill to Consult |
|----------|-----------------|
| Output path conflicts / intermittent failures | `check-bin-obj-clash` |
| Slow builds (not errors, but performance) | `build-perf-diagnostics` |
| Incremental build broken (rebuilds everything) | `incremental-build` |

### Common Error Patterns Quick-Lookup
When binlog analysis reveals these patterns, here's the fast path:

1. **"Package X could not be found"** → Check NuGet feed configuration and authentication
2. **"The imported project was not found" (MSB4019)** → Check SDK install and global.json configuration
3. **"Reference assemblies not found" (MSB3644)** → Missing targeting pack, install the required workload
4. **"Found conflicts between different versions" (MSB3277)** → Check binding redirects and package version alignment
5. **"Package downgrade detected" (NU1605)** → Check package version resolution and constraints
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
