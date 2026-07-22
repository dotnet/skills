---
name: check-bin-obj-clash
description: "Detects MSBuild projects with conflicting OutputPath or IntermediateOutputPath. USE FOR: builds failing with 'Cannot create a file when that file already exists', 'The process cannot access the file because it is being used by another process', intermittent build failures that succeed on retry, or missing/overwritten outputs in multi-project or multi-targeting builds where bin/obj (or project.assets.json) collide. Common causes: shared OutputPath, missing AppendTargetFrameworkToOutputPath, extra global properties (e.g. PublishReadyToRun), or SetTargetFramework on a ProjectReference to a single-targeting project. DO NOT USE FOR: file access errors unrelated to MSBuild (OS-level locking), single-project single-TFM builds, non-MSBuild build systems."
license: MIT
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

## Primary workflow — binlog MCP

The MCP server exposes structured tools for inspecting a `.binlog` without
parsing text logs. Call them directly instead of replaying the binlog to a text
file. Call `tools/list` for the MCP first if you are unsure which tools are available.

**Important constraints:**
- The `.binlog` file is a **binary format** — do NOT try to `cat`, `head`, `strings`, or read it directly. Use only the MCP tools to query it.
- **Synthesize findings as you go.** Do not spend all available time investigating — once you have enough evidence, present your conclusions.

### Step 2: Get an overview and list projects

Use the MCP overview and projects tools to understand the build and list all projects that participated.

### Step 3: Check evaluations and global properties

Use the MCP `evaluations` and `evaluation_global_properties` tools to find all evaluations per project. Look for:
- Multiple evaluations for the same project (indicates multi-targeting or multiple build configurations)
- Differing global properties between evaluations (`TargetFramework`, `Configuration`, `RuntimeIdentifier`, `SolutionFileName`, `PublishReadyToRun`, etc.)

### Step 4: Get output paths for each evaluation

Use the MCP properties tool to query `OutputPath`, `IntermediateOutputPath`, `BaseOutputPath`, and `BaseIntermediateOutputPath` for each project evaluation.

### Step 5: Check for double writes

Use the MCP double_writes tool if available — it directly detects files written by multiple project instances.

### Step 6: Identify clashes

Compare the `OutputPath` and `IntermediateOutputPath` values across all evaluations:
1. **Normalize paths** - Convert to absolute paths and normalize separators
2. **Group by path** - Find evaluations that share the same OutputPath or IntermediateOutputPath
3. **Filter out non-build evaluations** - Exclude `BuildProjectReferences=false` instances (P2P queries)
4. **Report clashes** - Any group with more than one evaluation indicates a clash

## Fallback workflow — text-log replay (when MCP is unavailable)

Use this only when the MCP server cannot be started.

Replay the binlog to a diagnostic text log, then grep for the same signals the MCP tools surface:

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log
```

Then extract the clash signals:

- **Projects & evaluations** — list evaluation starts and count them per project: `grep 'Evaluation started' full.log | grep -oiE '"[^"]+\.[a-z]+proj"' | sort | uniq -c`. Matching the full **quoted path** keeps same-named projects in different directories distinct (and tolerates spaces in paths); a path with a count ≥ 2 was evaluated more than once (multi-targeting or extra global properties). (`grep -c` alone only totals evaluations across the whole log, so it can't reveal per-project duplication.)
- **Output paths** — `grep -iE 'OutputPath[[:space:]]*=|IntermediateOutputPath[[:space:]]*=|BaseOutputPath[[:space:]]*=|BaseIntermediateOutputPath[[:space:]]*=' full.log | sort -u`, or query a project directly: `dotnet msbuild MyProject.csproj -getProperty:OutputPath` (and `IntermediateOutputPath`, `BaseIntermediateOutputPath`).
- **Distinguishing global properties** — `grep -iE 'TargetFramework|Configuration|Platform|RuntimeIdentifier|SolutionFileName|PublishReadyToRun' full.log`. See the [Global Properties to Check](#global-properties-to-check-when-comparing-evaluations) table for which affect the path and which just fork a redundant instance.
- **Corroborating evidence (optional)** — `grep 'Target "CopyFilesToOutputDirectory"' full.log` plus `grep 'SkipUnchangedFiles' full.log` show a second instance writing (or skipping a masked write) to the same path; a long vs ~0 ms `CoreCompile` distinguishes the real build from a redundant instance.

**Then identify clashes:** normalize paths to absolute, group evaluations by `OutputPath` and by `IntermediateOutputPath`, and exclude `BuildProjectReferences=false` (P2P queries) — plus, for `OutputPath` only, `MSBuildRestoreSessionId` restore evaluations. Any group with more than one remaining evaluation is a clash.

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

### Explicit `<MSBuild>` Build/Publish with extra global properties (self or cross-project)

**Problem:** A target uses the `<MSBuild>` task to build or publish a project with an extra global property, most commonly a "publish-on-build" target. The offending call can be in the target project itself **or in another project** that consumes it (e.g. a test or layout project publishing a tool):

```xml
<!-- (a) same project (publish-on-build) -->
<Target Name="PublishOnBuild" AfterTargets="Build">
  <MSBuild Projects="$(MSBuildProjectFullPath)" Targets="Publish" Properties="_IsPublishing=true" />
</Target>

<!-- (b) project A publishes project B that it consumes -->
<MSBuild Projects="..\tool\tool.csproj" Targets="Publish" Properties="_IsPublishing=true" />
```

Either way this forks a distinct instance of the target project (`path` + `{_IsPublishing=true}`) that shares the same `OutputPath`/`IntermediateOutputPath` as the instance the solution/graph already builds. Both write the same files — for NativeAOT this includes the `*.sourcelink` intermediate, which produces `SourceLinkWriter` / "file in use" failures under parallel builds.

**How to detect:** Follow the Primary workflow above — the `evaluations` and `evaluation_global_properties` tools surface two evaluations of the target project that share the same `OutputPath`/`IntermediateOutputPath` but differ only by a path-neutral publish flag such as `_IsPublishing`, and the `double_writes` tool flags the resulting shared-file writes directly. To tell case (a) from (b), see which project the extra `{_IsPublishing=true}` evaluation runs *under* in the build tree (from the overview/projects tools): the target project itself for (a), or a consumer project that invoked the `<MSBuild>` task for (b).

**Fix:** Depends on where the call lives:

- **Same project (a):** you can't strip the property with `RemoveGlobalProperties` (the project injects it on itself). Set the flag as a **static** (non-global) property and run the target in the **same** instance via `DependsOnTargets`/`CallTarget`, with a guard against a target cycle when publish is the entry point:

```xml
<PropertyGroup>
  <_PublishWasInvokedDirectly Condition="'$(_IsPublishing)' == 'true'">true</_PublishWasInvokedDirectly>
  <_IsPublishing>true</_IsPublishing>
</PropertyGroup>
<Target Name="PublishOnBuild"
        AfterTargets="Build"
        DependsOnTargets="Publish"
        Condition="'$(_PublishWasInvokedDirectly)' != 'true'" />
```

- **Cross-project (b):** the consumer must not fork the producer with path-neutral global properties. Make the producer publish as part of its own build (the (a) fix in *its* project), then have the consumer **sequence** it and read its output instead of re-publishing it:

```xml
<ItemGroup>
  <ProjectReference Include="..\tool\tool.csproj" ReferenceOutputAssembly="false" />
</ItemGroup>
<!-- consumer reads tool's publish dir; it does NOT invoke Publish on tool -->
```

See the `msbuild-antipatterns` skill (AP-22) for the authoring-time smell and rationale.

### `SetTargetFramework` re-injecting a single-targeting project's own TFM on a `ProjectReference`

**Problem:** A `ProjectReference` sets `SetTargetFramework="TargetFramework=<tfm>"` metadata pointing at a **single-targeting** project (one that uses singular `<TargetFramework>`, not `<TargetFrameworks>`), where the injected `<tfm>` **equals the TFM the project already targets**. `SetTargetFramework` injects `TargetFramework` as a **global property** on the referenced project's build.

```xml
<!-- BAD: Tool.csproj single-targets net8.0 and we inject that SAME net8.0 -->
<ProjectReference Include="..\Tool\Tool.csproj" SetTargetFramework="TargetFramework=net8.0" />
```

Injecting the TFM the project already targets is **path-neutral** — the project already resolves to `bin\<config>\net8.0\` and `obj\<config>\net8.0\` on its own. So it doesn't change the output path; it only forks a distinct instance `(project, {TargetFramework=net8.0})`. The solution/graph builds the very same project as `(project, {})`. Both share the same `OutputPath`/`IntermediateOutputPath`, so the project is **built twice** to the same location — a bin/obj clash under parallel builds.

**How to detect:** Follow the Primary workflow above. The `evaluations` and `evaluation_global_properties` tools surface two evaluations of the referenced project that share the same `OutputPath`/`IntermediateOutputPath` and differ only by a `TargetFramework` global property, while the project itself is single-targeting (its own `TargetFramework` already equals the injected value). The `double_writes` tool flags the resulting shared-file writes directly.

**Note:** The P2P protocol itself does **not** inject `TargetFramework` for a non-multi-targeting reference — the clash comes specifically from the explicit `SetTargetFramework` metadata overriding that safe default.

**Fix:** Remove the redundant `SetTargetFramework` when it just restates the project's own single TFM:

```xml
<!-- GOOD -->
<ProjectReference Include="..\Tool\Tool.csproj" />
```

**When `SetTargetFramework` is legitimate (not a clash):**

- **Multi-targeting reference** — the referenced project uses `<TargetFrameworks>` and you need one specific TFM. Each TFM has a distinct output path, so no clash.
- **Overriding to a *different* TFM** — you may use `SetTargetFramework` on a single-targeting project to build it under a TFM *other than* the one it declares. Because the injected TFM then changes the output path (`obj\<config>\<different-tfm>\`), the instance no longer collides with `(project, {})`. Only the *same-TFM* case is path-neutral and clashing.
- **Framework-incompatible reference** — whenever the referencing and referenced projects target **incompatible frameworks** (e.g. a `.NETFramework` project referencing a `.NETCoreApp` project, or vice-versa) — **regardless of single- or multi-targeting on either side** — set `SkipGetTargetFrameworkProperties="true"` (the P2P `GetTargetFrameworkProperties` negotiation would otherwise fail) and `ReferenceOutputAssembly="false"` (an assembly built for an incompatible framework can't be consumed as a reference — you only want to trigger/sequence the build):

  ```xml
  <ProjectReference Include="..\Tool\Tool.csproj"
                    SkipGetTargetFrameworkProperties="true"
                    ReferenceOutputAssembly="false" />
  ```

  With `SkipGetTargetFrameworkProperties="true"`, the negotiation no longer stops the **referencing** project's own `TargetFramework` global property (present when it builds for a specific TFM, e.g. it is multi-targeting) from flowing into the referenced project. For a **single-targeting** referenced project that would force it to build under the wrong TFM / output path. Prevent it by either setting `SetTargetFramework="TargetFramework=<tfm>"` (pin the TFM) **or** `UndefineProperties="TargetFramework"` (strip the inherited global property so the project builds as it declares) — use one, not both:

  ```xml
  <ProjectReference Include="..\Tool\Tool.csproj"
                    SkipGetTargetFrameworkProperties="true"
                    UndefineProperties="TargetFramework"
                    ReferenceOutputAssembly="false" />
  ```

See the `msbuild-antipatterns` skill (AP-23) for the authoring-time smell and rationale.

## Tips

- The SDK default paths include `$(TargetFramework)` — clashes often occur when projects override these defaults; normalize relative paths to absolute before comparing.
- **Cross-project `IntermediateOutputPath` clashes cannot be fixed with `AppendTargetFrameworkToOutputPath`** — files like `project.assets.json` are written directly to the intermediate path. For multi-targeting clashes *within the same project*, `AppendTargetFrameworkToOutputPath=true` is the correct fix.
- Error messages that indicate a path clash: `Cannot create a file when that file already exists` (NuGet restore), `The process cannot access the file because it is being used by another process`, or intermittent failures that succeed on retry.

### Global Properties to Check When Comparing Evaluations

When multiple evaluations share an output path, compare these global properties to understand why:

| Property | Affects OutputPath? | Notes |
|----------|---------------------|-------|
| `TargetFramework` | Yes | Different TFMs should have different paths. **Exception:** re-injecting a *single-targeting* project's own TFM (e.g. via `SetTargetFramework` with the same value) is path-neutral — it forks a redundant instance sharing the output path (see "`SetTargetFramework` re-injecting...") |
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
| `_IsPublishing` | No | Publish flag; an `<MSBuild>` Build/Publish call with this set (in this project or another that consumes it) forks a publish instance sharing the build output path (see "Explicit `<MSBuild>` Build/Publish with extra global properties") |

## Testing Fixes

After making changes to fix path clashes, clean and rebuild to verify. See the `binlog-generation` skill's "Cleaning the Repository" section on how to clean the repository while preserving binlog files.
