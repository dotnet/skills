---
name: incremental-build
description: "Guide for optimizing MSBuild incremental builds. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when builds are slower than expected on subsequent runs, when 'nothing changed but it rebuilds anyway', or when diagnosing why incremental builds are broken. Covers Inputs/Outputs on targets, FileWrites tracking, up-to-date checks, and diagnosing unnecessary rebuilds via binlog analysis."
---

## How MSBuild Incremental Build Works

MSBuild's incremental build mechanism allows targets to be skipped when their outputs are already up to date, dramatically reducing build times on subsequent runs.

- **Targets with `Inputs` and `Outputs` attributes**: MSBuild compares the timestamps of all files listed in `Inputs` against all files listed in `Outputs`. If every output file is newer than every input file, the target is skipped entirely.
- **Without `Inputs`/`Outputs`**: The target runs every time the build is invoked. This is the default behavior and the most common cause of slow incremental builds.
- **`Incremental` attribute on targets**: Targets can explicitly opt in or out of incremental behavior. Setting `Incremental="false"` forces the target to always run, even if `Inputs` and `Outputs` are specified.
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

### Step-by-step using binlog

1. **Build twice with binlogs** to capture the incremental build behavior:
   ```shell
   dotnet build /bl:first.binlog
   dotnet build /bl:second.binlog
   ```
   The first build establishes the baseline. The second build is the one you want to be incremental. Analyze `second.binlog`.

2. **Load the second binlog** and search for targets that actually executed:
   Use `search_binlog` with query `skipped=false` to find all targets that were not skipped. In a perfectly incremental build, most targets should be skipped.

3. **Inspect non-skipped targets** using `get_target_info_by_name` to see why they ran. Check the `Reason` field — it tells you whether MSBuild determined the target was out of date and why.

4. **Look for key messages** in the binlog:
   - `"Building target 'X' completely"` — means MSBuild found no outputs or all outputs are missing; this is a full target execution.
   - `"Building target 'X' incrementally"` — means some (but not all) outputs are out of date.
   - `"Skipping target 'X' because all output files are up-to-date"` — target was correctly skipped.

5. **Search for "is newer than output"** messages to find the specific input file that triggered the rebuild:
   ```
   search_binlog with query: "is newer than output"
   ```
   This reveals exactly which input file's timestamp caused MSBuild to consider the target out of date.

### Additional diagnostic techniques

- Compare `first.binlog` and `second.binlog` side by side in the MSBuild Structured Log Viewer to see what changed.
- Use `get_project_target_times` to see which targets consumed the most time in the second build — these are your optimization targets.
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
- **Avoid volatile data in build** — Don't embed timestamps, random values, or build counters in file paths or generated content unless you have a deliberate strategy for managing staleness. If you must use volatile data, isolate it to a single file with minimal downstream impact.
- **Use `Returns` instead of `Outputs` when you need to pass items without creating incremental build dependency** — `Outputs` serves double duty: it defines the incremental check AND the items returned from the target. If you only need to pass items to calling targets without affecting incrementality, use `Returns` instead:
  ```xml
  <!-- Outputs: affects incremental check AND return value -->
  <Target Name="GetFiles" Outputs="@(DiscoveredFiles)">...</Target>

  <!-- Returns: only affects return value, no incremental check -->
  <Target Name="GetFiles" Returns="@(DiscoveredFiles)">...</Target>
  ```
