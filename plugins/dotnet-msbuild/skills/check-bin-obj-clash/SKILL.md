---
name: check-bin-obj-clash
description: "Detects MSBuild projects with conflicting OutputPath or IntermediateOutputPath. Only activate in MSBuild/.NET build context. USE FOR: builds failing with 'Cannot create a file when that file already exists', 'The process cannot access the file because it is being used by another process', intermittent build failures that succeed on retry, missing outputs in multi-project builds, multi-targeting builds where project.assets.json conflicts. Diagnoses when multiple projects or TFMs write to the same bin/obj directories due to shared OutputPath, missing AppendTargetFrameworkToOutputPath, or extra global properties like PublishReadyToRun creating redundant evaluations. DO NOT USE FOR: file access errors unrelated to MSBuild (OS-level locking), single-project single-TFM builds, non-MSBuild build systems. INVOKES: binlog MCP tools when available, otherwise dotnet msbuild binlog replay with grep."
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

## Step 2: Analyze the Binary Log

Follow the workflow for the backend specified in the user's request:

| Backend ID | Name | Workflow |
|---|---|---|
| `baronfel-mcp` | baronfel.binlog.mcp | [baronfel.binlog.mcp workflow](references/workflow-baronfel-mcp.md) |
| `gerlicher-mcp` | AndyGerlicher BinlogMCP | [BinlogMCP workflow](references/workflow-gerlicher-mcp.md) |
| `sqlite` | SQLite Logger | [SQLite workflow](references/workflow-sqlite.md) |
| `text-replay` | Text-log replay | [Text replay workflow](references/workflow-text-replay.md) |

### What to collect for each evaluation

Regardless of backend, collect for each project evaluation:
- Project file path
- Evaluation ID
- TargetFramework (if multi-targeting)
- Configuration
- OutputPath
- IntermediateOutputPath
- Key global properties (SolutionFileName, BuildProjectReferences, MSBuildRestoreSessionId, PublishReadyToRun)

### Clash detection logic

```
For each unique OutputPath:
  - If multiple evaluations share it → CLASH

For each unique IntermediateOutputPath:
  - If multiple evaluations share it → CLASH
```

### Filter rules

1. **For OutputPath clashes**: Exclude restore-phase evaluations (where `MSBuildRestoreSessionId` is set). These don't write to output directories.
2. **For IntermediateOutputPath clashes**: Include restore-phase evaluations, as NuGet restore writes `project.assets.json` to the intermediate output path.
3. **Always exclude `BuildProjectReferences=false`**: These are P2P metadata queries, not actual builds that write files.

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

## Tips

- Use `grep -i 'OutputPath\s*=' full.log | sort -u` to quickly find all OutputPath property assignments
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