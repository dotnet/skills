---
name: incremental-build
description: "Guide for optimizing MSBuild incremental builds. USE FOR: builds slower than expected on subsequent runs, 'nothing changed but it rebuilds anyway', diagnosing why targets re-execute unnecessarily, fixing broken no-op builds. Covers 8 common causes: missing Inputs/Outputs on custom targets, volatile properties in output paths (timestamps/GUIDs), file writes outside tracked Outputs, missing FileWrites registration, glob changes, Visual Studio Fast Up-to-Date Check (FUTDC) issues. Key diagnostic: look for 'Building target completely' vs 'Skipping target' in binlog. DO NOT USE FOR: first-time build slowness (use build-perf-baseline), parallelism issues (use build-parallelism), evaluation-phase slowness (use eval-performance), non-MSBuild build systems."
license: MIT
---

## Required diagnosis output

For every diagnose or explain request, the final answer must explicitly cover
all of these points, even when the immediate cause is simply missing
`Inputs`/`Outputs`:

- the target and exact root-cause evidence;
- the actual input/output timestamp comparison;
- missing, deleted, cleaned, or rewritten outputs;
- complete inputs, including dynamic or volatile inputs;
- evaluated input/output paths and one-to-one mapping when item lists are batched;
- two identical no-change builds and the expected second-build skip/out-of-date evidence.

Do not edit unless the user asks for a fix. If a check cannot be run, label it
as an unverified check rather than omitting it or claiming success. When
`Inputs` or `Outputs` are absent, report timestamp comparison or one-to-one
mapping as **not applicable — the incremental mechanism is not engaged**, not
as an unverified comparison.

## How MSBuild Incremental Build Works

MSBuild's incremental build mechanism allows targets to be skipped when their outputs are already up to date, dramatically reducing build times on subsequent runs.

- **Targets with `Inputs` and `Outputs` attributes**: MSBuild compares the timestamps of all files listed in `Inputs` against all files listed in `Outputs`. If every output file is newer than every input file, the target is skipped entirely.
- **Without `Inputs`/`Outputs`**: The target runs every time the build is invoked. This is the default behavior and the most common cause of slow incremental builds.
- **Timestamp-based comparison**: MSBuild uses file system timestamps (last write time) to determine staleness. It does not use content hashes. This means touching a file (updating its timestamp without changing content) will trigger a rebuild.

```xml
<!-- This target is incremental: skipped if Output is newer than all Inputs -->
<Target Name="Transform"
        Inputs="@(TransformFiles)"
        Outputs="@(TransformFiles->'$(OutputPath)%(Filename).out')">
  <!-- work here -->
</Target>

<!-- This target always runs because it has no Inputs/Outputs -->
<Target Name="PrintMessage">
  <Message Text="This runs every build" />
</Target>
```

## Why Incremental Builds Break (Top Causes)

1. **Missing Inputs/Outputs on custom targets** — Without both attributes, the target always runs. This is the single most common cause of unnecessary rebuilds.

2. **Volatile properties in Outputs path** — If the output path includes something that changes between builds (e.g., a timestamp, build number, or random GUID), MSBuild will never find the previous output and will always rebuild.

3. **File writes outside of tracked Outputs** — If a target writes files that aren't listed in its `Outputs`, MSBuild doesn't know about them. The target may be skipped (because its declared outputs are up to date), but downstream targets may still be triggered.

4. **Missing FileWrites registration** — Files created during the build but not registered in the `FileWrites` item group won't be cleaned by `dotnet clean`. Over time, stale files can confuse incremental checks.

5. **Glob changes** — When you add or remove source files, the item set (e.g., `@(Compile)`) changes. Since these items feed into `Inputs`, the set of inputs changes and triggers a rebuild. This is expected behavior but can be surprising.

6. **Property changes** — Properties that feed into `Inputs` or `Outputs` paths (e.g., `$(Configuration)`, `$(TargetFramework)`) will cause rebuilds when changed. Switching between Debug and Release is a full rebuild by design.

7. **NuGet package updates** — Changing a package version updates `project.assets.json` and potentially many resolved assembly paths. This changes the inputs to `ResolveAssemblyReferences` and `CoreCompile`, triggering a rebuild.

8. **Build server VBCSCompiler cache invalidation** — The Roslyn compiler server (`VBCSCompiler`) caches compilation state. If the server is recycled (timeout, crash, or manual kill), the next build may be slower even though MSBuild's incremental checks pass, because the compiler must repopulate its in-memory caches.

## Diagnosing "Why Did This Rebuild?"

Use binary logs (binlogs) to understand exactly why targets ran instead of being skipped.

Before recommending a fix, report this compact checklist for each target that reran:

1. **Actual timestamp comparison** — name the newest resolved input and oldest resolved output, with their last-write times, and state which input is newer.
2. **Output existence** — confirm every declared output exists before the second build and is not deleted, cleaned, or rewritten by another target.
3. **Complete inputs** — verify `Inputs` includes every file that should invalidate the output, including volatile or generated dependencies. If a producer touches an input on every build, check whether its content changes. Fix unnecessary rewrites in the producer (for example, write only when content changes); if relevant content really changes, the dependent target must rerun. Exclude a file only with evidence that it cannot affect the output, never just because it changes often.
4. **Stable resolved paths** — expand properties and relative paths for both builds; outputs must resolve to the same location and must not contain timestamps, build numbers, or GUIDs.
5. **One-to-one mapping when batched** — for item transforms, map each input identity to its expected output and report missing, duplicate, or unmatched pairs.
6. **No-change verification** — run the same build twice without edits and cite the second build's skip or out-of-date reason.

For a diagnosis or explanation request, do not edit the project unless the user asks for a fix. If build execution is unavailable, distinguish inspected facts from expected behavior and do not claim the fix was applied or verified.

`Overwrite="true"` on `WriteLinesToFile` is not itself a reason that an
incremental target reruns. The task rewrites the file only after MSBuild has
already scheduled the target; correct `Inputs`/`Outputs` can skip the target
before the task executes.

### Step-by-step using binlog

1. **Build twice without edits** and capture both structured and text evidence
   from the second build:
   ```shell
   dotnet build /bl:first.binlog
   dotnet build /bl:second.binlog -fl "-flp:v=diag;logfile=second-full.log;performancesummary"
   ```
   The first build establishes the baseline. The second build is the one you want to be incremental. Analyze `second.binlog`.

### Primary: binlog MCP (preferred)

Use the **binlog MCP server** (`Microsoft.AITools.BinlogMcp`, exposed under the `binlog` MCP namespace) to analyze the second binlog:

1. Use `load_binlog` to load `second.binlog`.
2. Use `search_binlog` for "Building target completely", "Building target incrementally", "Skipping target", and "is newer than output".
3. Use `get_target_info_by_name` for the target that reran.
4. Use `get_project_target_list` to confirm the target and its project context.
5. Use `get_expensive_targets` only to rank the non-skipped targets after the skip reason is understood.

### Fallback: diagnostic text log (when MCP is unavailable)

2. **Read the diagnostic log captured during the second build**. Do not run
   another build or depend on replay support for the default fallback:
   ```bash
   grep 'Building target\|Target.*was not skipped' second-full.log
   ```
   In a perfectly incremental build, most targets should be skipped.

3. **Inspect non-skipped targets** by looking for their execution messages in the diagnostic log. Check for "out of date" messages that indicate why a target ran.

4. **Look for key messages** in the binlog:
   - `"Building target 'X' completely"` — means MSBuild found no outputs or all outputs are missing; this is a full target execution.
   - `"Building target 'X' incrementally"` — means some (but not all) outputs are out of date.
   - `"Skipping target 'X' because all output files are up-to-date"` — target was correctly skipped.

5. **Search for "is newer than output"** messages to find the specific input file that triggered the rebuild:
   ```bash
   grep "is newer than output" second-full.log
   ```
   This reveals exactly which input file's timestamp caused MSBuild to consider the target out of date.

### Additional diagnostic techniques

- Compare `first.binlog` and `second.binlog` side by side in the MSBuild Structured Log Viewer to see what changed.
- Use `grep 'Target Performance Summary' -A 30 second-full.log` to see which targets consumed the most time in the second build — these are your optimization targets.
- Check for targets with zero-duration that still ran — they may have unnecessary dependencies causing them to execute.

## FileWrites and Clean Build

The `FileWrites` item group is MSBuild's mechanism for tracking files generated during the build. It powers `dotnet clean` and helps maintain correct incremental behavior.

- **`FileWrites` item**: Register any file your custom targets create so that `dotnet clean` knows to remove them. Without this, generated files accumulate across builds and may confuse incremental checks.
- **`FileWritesShareable` item**: Use this for files that are shared across multiple projects (e.g., shared generated code). These files are tracked but not deleted if other projects still reference them.
- **If not registered**: Files accumulate in the output and intermediate directories. `dotnet clean` won't remove them, and they may cause stale data issues or confuse up-to-date checks.

### Pattern for registering generated files

Add generated files to `FileWrites` inside the target that creates them:

```xml
<Target Name="MyGenerator" Inputs="..." Outputs="$(IntermediateOutputPath)generated.cs">
  <!-- Generate the file -->
  <WriteLinesToFile File="$(IntermediateOutputPath)generated.cs" Lines="@(GeneratedLines)" />

  <!-- Register for clean -->
  <ItemGroup>
    <FileWrites Include="$(IntermediateOutputPath)generated.cs" />
  </ItemGroup>
</Target>
```

## Visual Studio Fast Up-to-Date Check

Visual Studio has its own up-to-date check (Fast Up-to-Date Check, or FUTDC) that is separate from MSBuild's `Inputs`/`Outputs` mechanism. Understanding the difference is critical for diagnosing "it rebuilds in VS but not on the command line" issues.

- **VS FUTDC is faster** because it runs in-process and checks a known set of items without invoking MSBuild at all. It compares timestamps of well-known item types (Compile, Content, EmbeddedResource, etc.) against the project's primary output.
- **It can be wrong** if your project uses custom build actions, custom targets that generate files, or non-standard item types that FUTDC doesn't know about.
- **Disable FUTDC** to force Visual Studio to use MSBuild's full incremental check:
  ```xml
  <PropertyGroup>
    <DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>
  </PropertyGroup>
  ```
- **Diagnose FUTDC decisions** by viewing the Output window in VS: go to **Tools → Options → Projects and Solutions → SDK-Style Projects** and set **Up-to-date Checks** logging level to **Verbose** or above. FUTDC will log exactly which file it considers out of date.
- **Common VS FUTDC issues**:
  - Custom build actions not registered with the FUTDC system
  - `CopyToOutputDirectory` items that are newer than the last build
  - Items added dynamically by targets that FUTDC doesn't evaluate
  - `Content` or `None` items with `CopyToOutputDirectory="PreserveNewest"` that have been modified

## Making Custom Targets Incremental

The following is a complete example of a well-structured incremental custom target:

```xml
<Target Name="GenerateConfig"
        Inputs="$(MSBuildProjectFile);@(ConfigInput)"
        Outputs="$(IntermediateOutputPath)config.generated.cs"
        BeforeTargets="CoreCompile">
  <!-- Generate file only if inputs changed -->
  <WriteLinesToFile File="$(IntermediateOutputPath)config.generated.cs" Lines="..." />
  <ItemGroup>
    <FileWrites Include="$(IntermediateOutputPath)config.generated.cs" />
    <Compile Include="$(IntermediateOutputPath)config.generated.cs" />
  </ItemGroup>
</Target>
```

**Key points in this example:**

- **`Inputs` includes `$(MSBuildProjectFile)`**: This ensures the target reruns if the project file itself changes (e.g., a property that affects generation is modified).
- **`Inputs` includes `@(ConfigInput)`**: The actual source files that drive generation.
- **`Outputs` uses `$(IntermediateOutputPath)`**: Generated files go in the `obj/` directory, which is managed by MSBuild and cleaned automatically.
- **`BeforeTargets="CoreCompile"`**: The generated file is available before the compiler runs.
- **`FileWrites` registration**: Ensures `dotnet clean` removes the generated file.
- **`Compile` inclusion**: Adds the generated file to the compilation without requiring it to exist at evaluation time.

### Common mistakes to avoid

```xml
<!-- BAD: No Inputs/Outputs — runs every build -->
<Target Name="BadTarget" BeforeTargets="CoreCompile">
  <Exec Command="generate-code.exe" />
</Target>

<!-- BAD: Volatile output path — never finds previous output -->
<Target Name="BadTarget2"
        Inputs="@(Compile)"
        Outputs="$(OutputPath)gen_$([System.DateTime]::Now.Ticks).cs">
  <Exec Command="generate-code.exe" />
</Target>

<!-- GOOD: Stable paths, registered outputs -->
<Target Name="GoodTarget"
        Inputs="@(Compile)"
        Outputs="$(IntermediateOutputPath)generated.cs"
        BeforeTargets="CoreCompile">
  <Exec Command="generate-code.exe -o $(IntermediateOutputPath)generated.cs" />
  <ItemGroup>
    <FileWrites Include="$(IntermediateOutputPath)generated.cs" />
    <Compile Include="$(IntermediateOutputPath)generated.cs" />
  </ItemGroup>
</Target>
```

## Performance Summary and Preprocess

MSBuild provides built-in tools to understand what's running and why.

- **`/clp:PerformanceSummary`** — Appends a summary at the end of the build showing time spent in each target and task. Use this to quickly identify the most expensive operations:
  ```shell
  dotnet build /clp:PerformanceSummary
  ```
  This shows a table of targets sorted by cumulative time, making it easy to spot targets that shouldn't be running in an incremental build.

- **`/pp:preprocess.xml`** — Generates a single XML file with all imports inlined, showing the fully evaluated project. This is invaluable for understanding what targets, properties, and items are defined and where they come from:
  ```shell
  dotnet msbuild /pp:preprocess.xml
  ```
  Search the preprocessed output to find where `Inputs` and `Outputs` are defined for any target, or to understand the full chain of imports.

- Use both together to understand what's running (`PerformanceSummary`) and what's imported (`/pp`), then cross-reference with binlog analysis for a complete picture.

## Common Fixes

- **Always add `Inputs` and `Outputs` to custom targets** — This is the single most impactful change for incremental build performance. Without both attributes, the target runs every time.
- **Use `$(IntermediateOutputPath)` for generated files** — Files in `obj/` are tracked by MSBuild's clean infrastructure and won't leak between configurations.
- **Register generated files in `FileWrites`** — Ensures `dotnet clean` removes them and prevents stale file accumulation.
- **Avoid unnecessary volatility in producers** — Don't embed timestamps, random values, or build counters in file paths or generated content unless required. Preserve generated-file timestamps when content is unchanged (for example, `Overwrite="true" WriteOnlyWhenDifferent="true"` with `WriteLinesToFile`). Keep real dependencies in `Inputs`; if required volatile content changes, accept the dependent rebuild. Exclude a file only after proving it cannot affect the output.
- **Use `Returns` instead of `Outputs` when you need to pass items without creating incremental build dependency** — `Outputs` serves double duty: it defines the incremental check AND the items returned from the target. If you only need to pass items to calling targets without affecting incrementality, use `Returns` instead:
  ```xml
  <!-- Outputs: affects incremental check AND return value -->
  <Target Name="GetFiles" Outputs="@(DiscoveredFiles)">...</Target>

  <!-- Returns: only affects return value, no incremental check -->
  <Target Name="GetFiles" Returns="@(DiscoveredFiles)">...</Target>
  ```
