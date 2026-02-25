<!-- AUTO-GENERATED — DO NOT EDIT. Regenerate with: node src/dotnet-msbuild/build.js -->

# Analyzing MSBuild Failures with Binary Logs

When an MSBuild build fails, use the binlog-mcp tool to deeply analyze the failure. This skill guides you through generating a binary log and using the MCP tools to diagnose issues.
Requires 'binlog-mcp' MCP server tools to perform the binlog analysis.

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

The `/bl` flag tells MSBuild to generate a `msbuild.binlog` file (or the specified filename) in the current directory. Use `/bl:{}` (or `/bl:{{}}` in powershell for escaping) to generate unique binlog filename on each run.

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

---

# Detecting OutputPath and IntermediateOutputPath Clashes

## Overview

This skill helps identify when multiple MSBuild project evaluations share the same `OutputPath` or `IntermediateOutputPath`. This is a common source of build failures including:

- File access conflicts during parallel builds
- Missing or overwritten output files
- Intermittent build failures
- "File in use" errors
- **NuGet restore errors like `Cannot create a file when that file already exists`** - this strongly indicates multiple projects share the same `IntermediateOutputPath` where `project.assets.json` is written

Clashes can occur between:
- **Different projects** sharing the same output directory
- **Multi-targeting builds** (e.g., `TargetFrameworks=net8.0;net9.0`) where the path doesn't include the target framework
- **Multiple solution builds** where the same project is built from different solutions in a single build

**Note:** Project instances with `BuildProjectReferences=false` should be **ignored** when analyzing clashes - these are P2P reference resolution builds that only query metadata (via `GetTargetPath`) and do not actually write to output directories.

## When to Use This Skill

**Invoke this skill immediately when you see:**
- `Cannot create a file when that file already exists` during NuGet restore
- `The process cannot access the file because it is being used by another process`
- Intermittent build failures that succeed on retry
- Missing output files or unexpected overwriting

## Step 1: Generate a Binary Log

Use the `binlog-generation` skill to generate a binary log with the correct naming convention.

## Step 2: Load the Binary Log

```
load_binlog with path: "<absolute-path-to-build.binlog>"
```

## Step 3: List All Projects

```
list_projects with binlog_file: "<path>"
```

This returns all projects with their IDs and file paths.

## Step 4: Get Evaluations for Each Project

For each unique project file path, list its evaluations:

```
list_evaluations with:
  - binlog_file: "<path>"
  - projectFilePath: "<project-file-path>"
```

Multiple evaluations for the same project indicate multi-targeting or multiple build configurations.

## Step 5: Check Global Properties for Each Evaluation

For each evaluation, get the global properties to understand the build configuration:

```
get_evaluation_global_properties with:
  - binlog_file: "<path>"
  - evaluationId: <evaluation-id>
```

Look for properties like `TargetFramework`, `Configuration`, `Platform`, and `RuntimeIdentifier` that should differentiate output paths.

Also check **solution-related properties** to identify multi-solution builds:
- `SolutionFileName`, `SolutionName`, `SolutionPath`, `SolutionDir`, `SolutionExt` — differ when a project is built from multiple solutions
- `CurrentSolutionConfigurationContents` — the number of project entries reveals which solution an evaluation belongs to (e.g., 1 project vs ~49 projects)

Look for **extra global properties that don't affect output paths** but create distinct MSBuild project instances:
- `PublishReadyToRun` — a publish setting that doesn't change `OutputPath` or `IntermediateOutputPath`, but MSBuild treats it as a distinct project instance, preventing result caching and causing redundant target execution (e.g., `CopyFilesToOutputDirectory` running again)
- Any other global property that differs between evaluations but doesn't contribute to path differentiation

### Filter Out Non-Build Evaluations

When analyzing clashes, filter evaluations based on the type of clash you're investigating:

1. **For OutputPath clashes**: Exclude restore-phase evaluations (where `MSBuildRestoreSessionId` global property is set). These don't write to output directories.

2. **For IntermediateOutputPath clashes**: Include restore-phase evaluations, as NuGet restore writes `project.assets.json` to the intermediate output path.

3. **Always exclude `BuildProjectReferences=false`**: These are P2P metadata queries, not actual builds that write files.

## Step 6: Get Output Paths for Each Evaluation

For each evaluation, retrieve the `OutputPath` and `IntermediateOutputPath`:

```
get_evaluation_properties_by_name with:
  - binlog_file: "<path>"
  - evaluationId: <evaluation-id>
  - propertyNames: ["OutputPath", "IntermediateOutputPath", "BaseOutputPath", "BaseIntermediateOutputPath", "TargetFramework", "Configuration", "Platform"]
```

## Step 7: Identify Clashes

Compare the `OutputPath` and `IntermediateOutputPath` values across all evaluations:

1. **Normalize paths** - Convert to absolute paths and normalize separators
2. **Group by path** - Find evaluations that share the same OutputPath or IntermediateOutputPath
3. **Report clashes** - Any group with more than one evaluation indicates a clash

## Step 8: Verify Clashes via CopyFilesToOutputDirectory (Optional)

As additional evidence for OutputPath clashes, check if multiple project builds execute the `CopyFilesToOutputDirectory` target to the same path. Note that not all clashes manifest here - compilation outputs and other targets may also conflict.

```
search_binlog with:
  - binlog_file: "<path>"
  - query: "$target CopyFilesToOutputDirectory project(<project-name>.csproj)"
```

Then for each project ID that ran this target, examine the Copy task messages:

```
list_tasks_in_target with:
  - binlog_file: "<path>"
  - projectId: <project-id>
  - targetId: <target-id-of-CopyFilesToOutputDirectory>
```

Look for evidence of clashes in the messages:
- `Copying file from "..." to "..."` - Active file writes
- `Did not copy from file "..." to file "..." because the "SkipUnchangedFiles" parameter was set to "true"` - Indicates a second build attempted to write to the same location

The `SkipUnchangedFiles` skip message often masks clashes - the build succeeds but is vulnerable to race conditions in parallel builds.

## Step 9: Check CoreCompile Execution Patterns (Optional)

To understand which project instance did the actual compilation vs redundant work, check `CoreCompile`:

```
search_binlog with:
  - binlog_file: "<path>"
  - query: "$target CoreCompile project(<project-name>.csproj)"
```

Compare the durations:
- The instance with a long `CoreCompile` duration (e.g., seconds) is the **primary build** that did the actual compilation
- Instances where `CoreCompile` was skipped (duration ~0-10ms) are **redundant builds** — they didn't recompile but may still run other targets like `CopyFilesToOutputDirectory` that write to the same output directory

This helps distinguish the "real" build from redundant instances created by extra global properties or multi-solution builds.

### Caveat: `under()` Search in Multi-Solution Builds

When using `search_binlog` with `under($project SolutionName)` to determine which solution a project instance belongs to, be aware that `under()` matches through the **entire build hierarchy**. If both solutions share a common ancestor (e.g., Arcade SDK's `Build.proj`), all project instances will appear "under" both solutions.

Instead, use `get_evaluation_global_properties` and compare the `SolutionFileName` / `CurrentSolutionConfigurationContents` properties to reliably determine which solution an evaluation belongs to.

### Expected Output Structure

For each evaluation, collect:
- Project file path
- Evaluation ID
- TargetFramework (if multi-targeting)
- Configuration
- OutputPath
- IntermediateOutputPath

### Clash Detection Logic

```
For each unique OutputPath:
  - If multiple evaluations share it → CLASH
  
For each unique IntermediateOutputPath:
  - If multiple evaluations share it → CLASH
```

## Common Causes and Fixes

### Multi-targeting without TargetFramework in path

**Problem:** Project uses `TargetFrameworks` but OutputPath doesn't vary by framework.

```xml
<!-- BAD: Same path for all frameworks -->
<OutputPath>bin\$(Configuration)\</OutputPath>
```

**Fix:** Include TargetFramework in the path:

```xml
<!-- GOOD: Path varies by framework -->
<OutputPath>bin\$(Configuration)\$(TargetFramework)\</OutputPath>
```

Or rely on SDK defaults which handle this automatically:

```xml
<AppendTargetFrameworkToOutputPath>true</AppendTargetFrameworkToOutputPath>
<AppendTargetFrameworkToIntermediateOutputPath>true</AppendTargetFrameworkToIntermediateOutputPath>
```

### Shared output directory across projects (CANNOT be fixed with AppendTargetFramework)

**Problem:** Multiple projects explicitly set the same `BaseOutputPath` or `BaseIntermediateOutputPath`.

```xml
<!-- Project A - Directory.Build.props -->
<BaseOutputPath>..\SharedOutput\</BaseOutputPath>
<BaseIntermediateOutputPath>..\SharedObj\</BaseIntermediateOutputPath>

<!-- Project B - Directory.Build.props -->
<BaseOutputPath>..\SharedOutput\</BaseOutputPath>
<BaseIntermediateOutputPath>..\SharedObj\</BaseIntermediateOutputPath>
```

**IMPORTANT:** Even with `AppendTargetFrameworkToOutputPath=true`, this will still clash! .NET writes certain files directly to the `IntermediateOutputPath` without the TargetFramework suffix, including:

- `project.assets.json` (NuGet restore output)
- Other NuGet-related files

This causes errors like `Cannot create a file when that file already exists` during parallel restore.

**Fix:** Each project MUST have a unique `BaseIntermediateOutputPath`. Do not share intermediate output directories across projects:

```xml
<!-- Project A -->
<BaseIntermediateOutputPath>..\obj\ProjectA\</BaseIntermediateOutputPath>

<!-- Project B -->
<BaseIntermediateOutputPath>..\obj\ProjectB\</BaseIntermediateOutputPath>
```

Or simply use the SDK defaults which place `obj` inside each project's directory.

### RuntimeIdentifier builds clashing

**Problem:** Building for multiple RIDs without RID in path.

**Fix:** Ensure RuntimeIdentifier is in the path:

```xml
<AppendRuntimeIdentifierToOutputPath>true</AppendRuntimeIdentifierToOutputPath>
```

### Multiple solutions building the same project

**Problem:** A single build invokes multiple solutions (e.g., via MSBuild task or command line) that include the same project. Each solution build evaluates and builds the project independently, with different `Solution*` global properties that don't affect the output path.

**How to detect:** Compare `SolutionFileName` and `CurrentSolutionConfigurationContents` across evaluations for the same project. Different values indicate multi-solution builds. For example:

| Property | Eval from Solution A | Eval from Solution B |
|---|---|---|
| `SolutionFileName` | `BuildAnalyzers.sln` | `Main.slnx` |
| `CurrentSolutionConfigurationContents` | 1 project entry | ~49 project entries |
| `OutputPath` | `bin\Release\netstandard2.0\` | `bin\Release\netstandard2.0\` ← **clash** |

**Example:** A repo build script builds `BuildAnalyzers.sln` then `Main.slnx`, and both solutions include `SharedAnalyzers.csproj`. Both builds write to `bin\Release\netstandard2.0\`. The first build compiles; the second skips compilation but still runs `CopyFilesToOutputDirectory`.

**Fix:** Options include:
1. **Consolidate solutions** - Ensure each project is only built from one solution in a single build
2. **Use different configurations** - Build solutions with different `Configuration` values that result in different output paths
3. **Exclude duplicate projects** - Use solution filters or conditional project inclusion to avoid building the same project twice

### Extra global properties creating redundant project instances

**Problem:** A project is built multiple times within the same solution due to extra global properties (e.g., `PublishReadyToRun=false`) that create distinct MSBuild project instances. These properties don't affect output paths but prevent MSBuild from caching results across instances, causing redundant target execution.

**How to detect:** Compare global properties across evaluations for the same project within the same solution (same `SolutionFileName`). Look for properties that differ but don't contribute to path differentiation:

| Property | Eval A (from Razor.slnx) | Eval B (from Razor.slnx) |
|---|---|---|
| `PublishReadyToRun` | *(not set)* | `false` |
| `OutputPath` | `bin\Release\netstandard2.0\` | `bin\Release\netstandard2.0\` ← **clash** |

This is particularly wasteful for projects where the extra property has no effect (e.g., `PublishReadyToRun` on a `netstandard2.0` class library that doesn't use ReadyToRun compilation).

**Fix:** Options include:
1. **Remove the extra global property** - Investigate which parent target/task is injecting the property and prevent it from being passed to projects that don't need it
2. **Use `RemoveGlobalProperties` metadata** - On `ProjectReference` items, use `RemoveGlobalProperties="PublishReadyToRun"` to strip the property before building the referenced project
3. **Condition the property** - Only set the property on projects that actually use it (e.g., only for executable projects, not class libraries)

## Example Workflow

```
1. load_binlog with path: "C:\repo\build.binlog"

2. list_projects → Returns projects with IDs

3. For project "MyLib.csproj":
   list_evaluations → Returns evaluation IDs 1, 2 (net8.0, net9.0)

4. get_evaluation_properties_by_name for evaluation 1:
   - TargetFramework: "net8.0"
   - OutputPath: "bin\Debug\net8.0\"
   - IntermediateOutputPath: "obj\Debug\net8.0\"

5. get_evaluation_properties_by_name for evaluation 2:
   - TargetFramework: "net9.0"
   - OutputPath: "bin\Debug\net9.0\"
   - IntermediateOutputPath: "obj\Debug\net9.0\"

6. Compare paths → No clash (paths differ by TargetFramework)
```

## Tips

- Use `search_binlog` with query `"OutputPath"` to quickly find all OutputPath property assignments
- Check `BaseOutputPath` and `BaseIntermediateOutputPath` as they form the root of output paths
- The SDK default paths include `$(TargetFramework)` - clashes often occur when projects override these defaults
- Remember that paths may be relative - normalize to absolute paths before comparing
- **Cross-project IntermediateOutputPath clashes cannot be fixed with `AppendTargetFrameworkToOutputPath`** - files like `project.assets.json` are written directly to the intermediate path
- For multi-targeting clashes within the same project, `AppendTargetFrameworkToOutputPath=true` is the correct fix
- Common error messages indicating path clashes:
  - `Cannot create a file when that file already exists` (NuGet restore)
  - `The process cannot access the file because it is being used by another process`
  - Intermittent build failures that succeed on retry

### Global Properties to Check When Comparing Evaluations

When multiple evaluations share an output path, compare these global properties to understand why:

| Property | Affects OutputPath? | Notes |
|----------|---------------------|-------|
| `TargetFramework` | Yes | Different TFMs should have different paths |
| `RuntimeIdentifier` | Yes | Different RIDs should have different paths |
| `Configuration` | Yes | Debug vs Release |
| `Platform` | Yes | AnyCPU vs x64 etc. |
| `SolutionFileName` | No | Identifies which solution built the project — different values indicate multi-solution clash |
| `SolutionName` | No | Solution name without extension |
| `SolutionPath` | No | Full path to the solution file |
| `SolutionDir` | No | Directory containing the solution file |
| `CurrentSolutionConfigurationContents` | No | XML with project entries — count of entries reveals which solution |
| `BuildProjectReferences` | No | `false` = P2P query, not a real build - ignore these |
| `MSBuildRestoreSessionId` | No | Present = restore phase evaluation |
| `PublishReadyToRun` | No | Publish setting, doesn't change build output path but creates distinct project instances |

## Testing Fixes

After making changes to fix path clashes, clean and rebuild to verify. See the `binlog-generation` skill's "Cleaning the Repository" section on how to clean the repository while preserving binlog files.