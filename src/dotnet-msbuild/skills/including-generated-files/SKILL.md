---
name: including-generated-files
description: "Proper handling of files generated during the MSBuild's build process. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals)."
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
