<!-- AUTO-GENERATED -- DO NOT EDIT -->

# Analyzing MSBuild Failures with Binary Logs

Use MSBuild's built-in **binlog replay** to convert binary logs into searchable text logs, then analyze with standard tools (`grep`, `cat`, `head`, `tail`, `find`).

## Build Error Investigation (Primary Workflow)

### Step 1: Replay the binlog to text logs

Replay produces multiple focused log files in one pass:

```bash
dotnet msbuild build.binlog -noconlog \
  -fl  -flp:v=diag;logfile=full.log;performancesummary \
  -fl1 -flp1:errorsonly;logfile=errors.log \
  -fl2 -flp2:warningsonly;logfile=warnings.log
```

> **PowerShell note:** Use `-flp:"v=diag;logfile=full.log;performancesummary"` (quoted semicolons).

### Step 2: Read the errors

```bash
cat errors.log
```

This gives all errors with file paths, line numbers, error codes, and project context.

### Step 3: Search for context around specific errors

```bash
# Find all occurrences of a specific error code with surrounding context
grep -n -B2 -A2 "CS0246" full.log

# Find which projects failed to compile
grep -i "CoreCompile.*FAILED\|Build FAILED\|error MSB" full.log

# Find project build order and results
grep "done building project\|Building with" full.log | head -50
```

### Step 4: Detect cascading failures

Projects that never reached `CoreCompile` failed because a dependency failed, not their own code:

```bash
# List all projects that ran CoreCompile
grep 'Target "CoreCompile"' full.log | grep -oP 'project "[^"]*"'

# Compare against projects that had errors to identify cascading failures
grep "project.*FAILED" full.log
```

### Step 5: Examine project files for root causes

```bash
# Read the .csproj of the failing project
cat path/to/Services/Services.csproj

# Check PackageReference and ProjectReference entries
grep -n "PackageReference\|ProjectReference" path/to/Services/Services.csproj
```

**Write your diagnosis as soon as you have enough information.** Do not over-investigate.

## Additional Workflows

### Performance Investigation
```bash
# The PerformanceSummary is at the end of full.log
tail -100 full.log   # shows target/task timing summary
grep "Target Performance Summary\|Task Performance Summary" -A 50 full.log
```

### Dependency/Evaluation Issues
```bash
# Check evaluation properties
grep -i "OutputPath\|IntermediateOutputPath\|TargetFramework" full.log | head -30
# Check item groups
grep "PackageReference\|ProjectReference" full.log | head -30
```

## Replay reference

| Command | Purpose |
|---------|---------|
| `dotnet msbuild X.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary` | Full diagnostic log with perf summary |
| `dotnet msbuild X.binlog -noconlog -fl -flp:errorsonly;logfile=errors.log` | Errors only |
| `dotnet msbuild X.binlog -noconlog -fl -flp:warningsonly;logfile=warnings.log` | Warnings only |
| `grep -n "PATTERN" full.log` | Search for patterns in the replayed log |
| `dotnet msbuild -pp:preprocessed.xml Proj.csproj` | Preprocess — inline all imports into one file |

## Generating a binlog (only if none exists)

```bash
dotnet build /bl:build.binlog
```

## Common error patterns

1. **CS0246 / "type not found"** → Missing PackageReference — check the .csproj
2. **MSB4019 / "imported project not found"** → SDK install or global.json issue
3. **NU1605 / "package downgrade"** → Version conflict in package graph
4. **MSB3277 / "version conflicts"** → Binding redirect or version alignment issue
5. **Project failed at ResolveProjectReferences** → Cascading failure from a dependency

---

# Generate Binary Logs

**Pass the `/bl` switch when running any MSBuild-based command.** This is a non-negotiable requirement for all .NET builds.

## Commands That Require /bl

You MUST add the `/bl:{}` flag to:
- `dotnet build`
- `dotnet test`
- `dotnet pack`
- `dotnet publish`
- `dotnet restore`
- `msbuild` or `msbuild.exe`
- Any other command that invokes MSBuild

## Preferred: Use `{}` for Automatic Unique Names

> **Note:** The `{}` placeholder requires MSBuild 17.8+ / .NET 8 SDK or later.

The `{}` placeholder in the binlog filename is replaced by MSBuild with a unique identifier, guaranteeing no two builds ever overwrite each other — without needing to track or check existing files.

```bash
# Every invocation produces a distinct file automatically
dotnet build /bl:{}
dotnet test /bl:{}
dotnet build --configuration Release /bl:{}
```

**PowerShell requires escaping the braces:**

```powershell
# PowerShell: escape { } as {{ }}
dotnet build -bl:{{}}
dotnet test -bl:{{}}
```

## Why This Matters

1. **Unique names prevent overwrites** - You can always go back and analyze previous builds
2. **Failure analysis** - When a build fails, the binlog is already there for immediate analysis
3. **Comparison** - You can compare builds before and after changes
4. **No re-running builds** - You never need to re-run a failed build just to generate a binlog

## Examples

```bash
# ✅ CORRECT - {} generates a unique name automatically (bash/cmd)
dotnet build /bl:{}
dotnet test /bl:{}

# ✅ CORRECT - PowerShell escaping
dotnet build -bl:{{}}
dotnet test -bl:{{}}

# ❌ WRONG - Missing /bl flag entirely
dotnet build
dotnet test

# ❌ WRONG - No filename (overwrites the same msbuild.binlog every time)
dotnet build /bl
dotnet build /bl
```

## When a Specific Filename Is Required

If the binlog filename needs to be known upfront (e.g., for CI artifact upload), or if `{}` is not available in the installed MSBuild version, pick a name that won't collide with existing files:

1. Check for existing `*.binlog` files in the directory
2. Choose a name not already taken (e.g., by incrementing a counter from the highest existing number)

```bash
# Example: directory contains 3.binlog — use 4.binlog
dotnet build /bl:4.binlog
```

## Cleaning the Repository

When cleaning the repository with `git clean`, **always exclude binlog files** to preserve your build history:

```bash
# ✅ CORRECT - Exclude binlog files from cleaning
git clean -fdx -e "*.binlog"

# ❌ WRONG - This deletes binlog files (they're usually in .gitignore)
git clean -fdx
```

This is especially important when iterating on build fixes - you need the binlogs to analyze what changed between builds.

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

## Step 2: Replay the Binary Log to Text

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log
```

## Step 3: List All Projects

```bash
grep -i 'done building project\|Building project' full.log | grep -oP '"[^"]+\.csproj"' | sort -u
```

This lists all project files that participated in the build.

## Step 4: Check for Multiple Evaluations per Project

Multiple evaluations for the same project indicate multi-targeting or multiple build configurations:

```bash
# Count how many times each project was evaluated
grep -c 'Evaluation started' full.log
grep 'Evaluation started.*\.csproj' full.log
```

## Step 5: Check Global Properties for Each Evaluation

For each project, query the build properties to understand the build configuration:

```bash
# Search the diagnostic log for evaluated property values
grep -i 'TargetFramework\|Configuration\|Platform\|RuntimeIdentifier' full.log | head -40
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

## Step 6: Get Output Paths for Each Project

Query each project's output path properties:

```bash
# From the diagnostic log - search for OutputPath assignments
grep -i 'OutputPath\s*=\|IntermediateOutputPath\s*=\|BaseOutputPath\s*=\|BaseIntermediateOutputPath\s*=' full.log | head -40

# Or query a specific project directly
dotnet msbuild MyProject.csproj -getProperty:OutputPath
dotnet msbuild MyProject.csproj -getProperty:IntermediateOutputPath
dotnet msbuild MyProject.csproj -getProperty:BaseOutputPath
dotnet msbuild MyProject.csproj -getProperty:BaseIntermediateOutputPath
```

## Step 7: Identify Clashes

Compare the `OutputPath` and `IntermediateOutputPath` values across all evaluations:

1. **Normalize paths** - Convert to absolute paths and normalize separators
2. **Group by path** - Find evaluations that share the same OutputPath or IntermediateOutputPath
3. **Report clashes** - Any group with more than one evaluation indicates a clash

## Step 8: Verify Clashes via CopyFilesToOutputDirectory (Optional)

As additional evidence for OutputPath clashes, check if multiple project builds execute the `CopyFilesToOutputDirectory` target to the same path. Note that not all clashes manifest here - compilation outputs and other targets may also conflict.

```bash
# Search for CopyFilesToOutputDirectory target execution per project
grep 'Target "CopyFilesToOutputDirectory"' full.log

# Look for Copy task messages showing file destinations
grep 'Copying file from\|SkipUnchangedFiles' full.log | head -30
```

Look for evidence of clashes in the messages:
- `Copying file from "..." to "..."` - Active file writes
- `Did not copy from file "..." to file "..." because the "SkipUnchangedFiles" parameter was set to "true"` - Indicates a second build attempted to write to the same location

The `SkipUnchangedFiles` skip message often masks clashes - the build succeeds but is vulnerable to race conditions in parallel builds.

## Step 9: Check CoreCompile Execution Patterns (Optional)

To understand which project instance did the actual compilation vs redundant work, check `CoreCompile`:

```bash
grep 'Target "CoreCompile"' full.log
```

Compare the durations:
- The instance with a long `CoreCompile` duration (e.g., seconds) is the **primary build** that did the actual compilation
- Instances where `CoreCompile` was skipped (duration ~0-10ms) are **redundant builds** — they didn't recompile but may still run other targets like `CopyFilesToOutputDirectory` that write to the same output directory

This helps distinguish the "real" build from redundant instances created by extra global properties or multi-solution builds.

### Caveat: Multi-Solution Builds

When analyzing multi-solution builds, note that the diagnostic log interleaves output from all projects. To determine which solution a project instance belongs to, search for `SolutionFileName` property assignments in the diagnostic log:

```bash
grep -i "SolutionFileName\|CurrentSolutionConfigurationContents" full.log | head -20
```

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

```bash
# 1. Replay the binlog
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log

# 2. List projects
grep 'done building project' full.log | grep -oP '"[^"]+\.csproj"' | sort -u

# 3. Check OutputPath for each evaluation
grep -i 'OutputPath\s*=' full.log | sort -u
# e.g.  OutputPath = bin\Debug\net8.0\
#       OutputPath = bin\Debug\net9.0\

# 4. Check IntermediateOutputPath
grep -i 'IntermediateOutputPath\s*=' full.log | sort -u
# e.g.  IntermediateOutputPath = obj\Debug\net8.0\
#       IntermediateOutputPath = obj\Debug\net9.0\

# 5. Compare paths → No clash (paths differ by TargetFramework)
```

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

---

# Including Generated Files Into Your Build

## Overview

Files generated during the build are generally ignored by the build process. This leads to confusing results such as:
- Generated files not being included in the output directory
- Generated source files not being compiled
- Globs not capturing files created during the build

This happens because of how MSBuild's build phases work.

## Quick Takeaway

For code files generated during the build - we need to add those to `Compile` and `FileWrites` item groups within the target generating the file(s):

```xml
  <ItemGroup>
    <Compile Include="$(GeneratedFilePath)" />
    <FileWrites Include="$(GeneratedFilePath)" />
  </ItemGroup>
```

The target generating the file(s) should be hooked before CoreCompile and BeforeCompile targets - `BeforeTargets="CoreCompile;BeforeCompile"`

## Why Generated Files Are Ignored

For detailed explanation, see [How MSBuild Builds Projects](https://docs.microsoft.com/visualstudio/msbuild/build-process-overview).

### Evaluation Phase

MSBuild reads your project, imports everything, creates Properties, expands globs for Items **outside of Targets**, and sets up the build process.

### Execution Phase

MSBuild runs Targets & Tasks with the provided Properties & Items to perform the build.

**Key Takeaway:** Files generated during execution don't exist during evaluation, therefore they aren't found. This particularly affects files that are globbed by default, such as source files (`.cs`).

## Solution: Manually Add Generated Files

When files are generated during the build, manually add them into the build process. The approach depends on the type of file being generated.

### Use `$(IntermediateOutputPath)` for Generated File Location

Always use `$(IntermediateOutputPath)` as the base directory for generated files. **Do not** hardcode `obj\` or construct the intermediary path manually (e.g., `obj\$(Configuration)\$(TargetFramework)\`). The intermediate output path can be redirected to a different location in some build configurations (e.g., shared output directories, CI environments). Using `$(IntermediateOutputPath)` ensures your target works correctly regardless of the actual path.

### Always Add Generated Files to `FileWrites`

Every generated file should be added to the `FileWrites` item group. This ensures that MSBuild's `Clean` target properly removes your generated files. Without this, generated files will accumulate as stale artifacts across builds.

```xml
<ItemGroup>
  <FileWrites Include="$(IntermediateOutputPath)my-generated-file.xyz" />
</ItemGroup>
```

### Basic Pattern (Non-Code Files)

For generated files that need to be copied to output (config files, data files, etc.), add them to `Content` or `None` items before `BeforeBuild`:

```xml
<Target Name="IncludeGeneratedFiles" BeforeTargets="BeforeBuild">
  
  <!-- Your logic that generates files goes here -->

  <ItemGroup>
    <None Include="$(IntermediateOutputPath)my-generated-file.xyz" CopyToOutputDirectory="PreserveNewest"/>
    
    <!-- Capture all files of a certain type with a glob -->
    <None Include="$(IntermediateOutputPath)generated\*.xyz" CopyToOutputDirectory="PreserveNewest"/>

    <!-- Register generated files for proper cleanup -->
    <FileWrites Include="$(IntermediateOutputPath)my-generated-file.xyz" />
    <FileWrites Include="$(IntermediateOutputPath)generated\*.xyz" />
  </ItemGroup>
</Target>
```

### For Generated Source Files (Code That Needs Compilation)

If you're generating `.cs` files that need to be compiled, use **`BeforeTargets="CoreCompile;BeforeCompile"`**. This is the correct timing for adding `Compile` items — it runs late enough that the file generation has occurred, but before the compiler runs. Using `BeforeBuild` is too early for some scenarios and may not work reliably with all SDK features.

```xml
<Target Name="IncludeGeneratedSourceFiles" BeforeTargets="CoreCompile;BeforeCompile">
  <PropertyGroup>
    <GeneratedCodeDir>$(IntermediateOutputPath)Generated\</GeneratedCodeDir>
    <GeneratedFilePath>$(GeneratedCodeDir)MyGeneratedFile.cs</GeneratedFilePath>
  </PropertyGroup>

  <MakeDir Directories="$(GeneratedCodeDir)" />

  <!-- Your logic that generates the .cs file goes here -->

  <ItemGroup>
    <Compile Include="$(GeneratedFilePath)" />
    <FileWrites Include="$(GeneratedFilePath)" />
  </ItemGroup>
</Target>
```

Note: Specifying both `CoreCompile` and `BeforeCompile` ensures the target runs before whichever target comes first, providing robust ordering regardless of customizations in the build.

## Target Timing

Choose the `BeforeTargets` value based on the type of file being generated:

- **`BeforeTargets="BeforeBuild"`** — For non-code files added to `None` or `Content`. Runs early enough for copy-to-output scenarios.
- **`BeforeTargets="CoreCompile;BeforeCompile"`** — For generated source files added to `Compile`. Ensures the file is included before the compiler runs.
- **`BeforeTargets="AssignTargetPaths"`** — The "final stop" before `None` and `Content` items (among others) are transformed into new items. Use as a fallback if `BeforeBuild` is too early.

## Globbing Behavior

Globs behave according to **when** the glob took place:

| Glob Location | Files Captured |
|---------------|----------------|
| Outside of a target | Only files visible during Evaluation phase (before build starts) |
| Inside of a target | Files visible when the target runs (can capture generated files if timed correctly) |

This is why the solution places the `<ItemGroup>` inside a `<Target>` - the glob runs during execution when the generated files exist.

## Relevant Links

- [How MSBuild Builds Projects](https://docs.microsoft.com/visualstudio/msbuild/build-process-overview)
- [Evaluation Phase](https://docs.microsoft.com/visualstudio/msbuild/build-process-overview#evaluation-phase)
- [Execution Phase](https://docs.microsoft.com/visualstudio/msbuild/build-process-overview#execution-phase)
- [Common Item Types](https://docs.microsoft.com/visualstudio/msbuild/common-msbuild-project-items)
- [How the SDK imports items by default](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.DefaultItems.props)
- [Official docs: Handle generated files](https://learn.microsoft.com/visualstudio/msbuild/customize-your-build#handle-generated-files)