## Diagnosing Incremental Build Issues with BinlogMCP

This is the **best backend for incremental build diagnosis** due to its built-in build comparison tools.

### Step 1: Build twice with binlogs

```shell
dotnet build /bl:first.binlog
dotnet build /bl:second.binlog
```

### Step 2: Compare the two builds

Call `CompareBinlogs(baseline="first.binlog", current="second.binlog")` to get a structured diff showing timing changes, new/fixed errors, and target differences between the two builds.

### Step 3: Get incremental build analysis

Call `GetIncrementalBuildAnalysis` with `second.binlog` to analyze incremental build behavior — showing executed vs skipped targets and identifying targets that should have been skipped.

### Step 4: Diff target execution

Call `DiffTargetExecution(baseline="first.binlog", current="second.binlog")` to see exactly which targets ran differently between the two builds.

### Step 5: Check skipped targets

Call `GetSkippedTargets` with `second.binlog` to see all targets that were skipped and the reasons why. Cross-reference with targets that ran but should have been skipped.

### Step 6: Trace specific inputs (if needed)

If a target ran because of a specific file change:
- Call `GetTargetInputsOutputs(targetName="<name>")` to see the target's incremental build inputs and outputs
- Call `TraceItem(item="<filename>")` to track how a specific item flowed through the build

### Step 7: Identify root cause

Call `GetTargetExecutionReasons` to see why targets executed (DependsOnTargets, BeforeTargets, AfterTargets chains). This helps identify the root target that triggered a cascade of rebuilds.
