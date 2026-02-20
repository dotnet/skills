---
name: check-bin-obj-clash
description: "Detects MSBuild projects with conflicting OutputPath or IntermediateOutputPath. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when builds fail with file access errors, missing outputs, or intermittent failures. Identifies when multiple projects or multi-targeting builds write to the same bin/obj directories."
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

## Samples

The [bin-obj-clash sample](../../samples/bin-obj-clash/) contains a test solution demonstrating both types of clashes:
- **LibraryA & LibraryB** - Cross-project clash with shared `BaseIntermediateOutputPath`
- **MultiTargetLib** - Multi-targeting clash with `AppendTargetFrameworkToOutputPath=false`

See [samples/bin-obj-clash/README.md](../../samples/bin-obj-clash/README.md) for details.

## When to Use This Skill

**Invoke this skill immediately when you see:**
- `Cannot create a file when that file already exists` during NuGet restore
- `The process cannot access the file because it is being used by another process`
- Intermittent build failures that succeed on retry
- Missing output files or unexpected overwriting

## Step 1: Generate a Binary Log

Follow the instructions in the [binlog-generation skill](../binlog-generation/SKILL.md) to generate a binary log with the correct naming convention.

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

After making changes to fix path clashes, clean and rebuild to verify. See the [binlog-generation skill](../binlog-generation/SKILL.md#cleaning-the-repository) for how to clean the repository while preserving binlog files.
