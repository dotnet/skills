## Detecting OutputPath Clashes with Text-Log Replay

### Step 1: Replay the binlog

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log
```

> **PowerShell:** `-flp:"v=diag;logfile=full.log"`

### Step 2: List all projects

```bash
grep -i 'done building project\|Building project' full.log | grep -oP '"[^"]+\.csproj"' | sort -u
```

### Step 3: Check for multiple evaluations per project

```bash
grep 'Evaluation started.*\.csproj' full.log
```

Multiple evaluations for the same project indicate multi-targeting or multiple build configurations.

### Step 4: Check global properties for each evaluation

```bash
grep -i 'TargetFramework\|Configuration\|Platform\|RuntimeIdentifier' full.log | head -40
```

Also check solution-related properties for multi-solution builds:
```bash
grep -i "SolutionFileName\|CurrentSolutionConfigurationContents" full.log | head -20
```

Look for extra global properties that don't affect output paths:
```bash
grep -i "PublishReadyToRun\|BuildProjectReferences\|MSBuildRestoreSessionId" full.log | head -20
```

### Step 5: Get output paths

```bash
grep -i 'OutputPath\s*=\|IntermediateOutputPath\s*=\|BaseOutputPath\s*=\|BaseIntermediateOutputPath\s*=' full.log | head -40
```

Or query specific projects directly:
```bash
dotnet msbuild MyProject.csproj -getProperty:OutputPath
dotnet msbuild MyProject.csproj -getProperty:IntermediateOutputPath
```

### Step 6: Identify clashes

Compare OutputPath and IntermediateOutputPath values. Normalize paths and group by value — any group with more than one evaluation is a clash.

### Step 7: Verify via CopyFilesToOutputDirectory (optional)

```bash
grep 'Target "CopyFilesToOutputDirectory"' full.log
grep 'Copying file from\|SkipUnchangedFiles' full.log | head -30
```

### Step 8: Check CoreCompile patterns (optional)

```bash
grep 'Target "CoreCompile"' full.log
```

The instance with long duration is the primary build; skipped instances are redundant.
