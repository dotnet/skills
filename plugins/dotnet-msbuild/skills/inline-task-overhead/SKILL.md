---
name: inline-task-overhead
description: "Diagnose and fix performance overhead from MSBuild inline tasks. Activate when tasks defined with RoslynCodeTaskFactory or CodeTaskFactory show unexpectedly high execution time (>1s for simple operations). Covers runtime compilation overhead of inline tasks vs pre-compiled task assemblies. Do not activate for general build performance issues — use build-perf-diagnostics instead."
---

## Inline Task Performance Overhead

### Symptoms

- A task shows >1s execution time for a trivially simple operation
- Task Performance Summary reveals unexpected overhead in custom tasks
- Build logs show `RoslynCodeTaskFactory` or `CodeTaskFactory` compilation messages

### Root Cause

Inline tasks defined in `.targets` files using `<UsingTask TaskFactory="RoslynCodeTaskFactory">` are **compiled at runtime** on every build. This adds approximately **~1s overhead per unique inline task**, compared to **~3ms** for a pre-compiled task assembly.

Example of an inline task (slow):

```xml
<UsingTask TaskName="MyTask" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll">
  <ParameterGroup>
    <InputFile ParameterType="System.String" Required="true" />
    <Result Output="true" ParameterType="System.String" />
  </ParameterGroup>
  <Task>
    <Code Type="Fragment" Language="cs">
      Result = System.IO.File.ReadAllText(InputFile).Trim();
    </Code>
  </Task>
</UsingTask>
```

### Fix

Convert frequently-executed inline tasks to pre-compiled task assemblies:

1. Create a class library project targeting `netstandard2.0` (for broadest MSBuild compatibility)
2. Reference `Microsoft.Build.Utilities.Core` and `Microsoft.Build.Framework`
3. Implement `Microsoft.Build.Utilities.Task`
4. Reference the compiled DLL in your `.targets` file:

```xml
<UsingTask TaskName="MyTask" AssemblyFile="path/to/MyTasks.dll" />
```

### When to Keep Inline Tasks

- Tasks that run rarely (once per build, not per project)
- Prototyping / temporary tasks during development
- Tasks in repos where adding a compiled task project is too much overhead
