## Detecting OutputPath Clashes with BinlogMCP

### Step 1: Get build summary

Call `GetBuildSummary` to see all projects in the build.

### Step 2: Get properties for each project

Call `GetProperties` with a filter for output path properties. This returns MSBuild properties including `OutputPath`, `IntermediateOutputPath`, `BaseOutputPath`, and `BaseIntermediateOutputPath`.

### Step 3: Get evaluated project view

For suspected clashing projects, call `GetEvaluatedProject` to see the flattened project view with all final properties, items, and imports.

### Step 4: Trace output path origin

Call `GetPropertyOrigin(property="OutputPath")` to trace where the output path was set — which file, in what order, and the final value. This helps understand WHY two projects share the same path.

Also try `TraceProperty(property="OutputPath")` for a full trace of every assignment from initial to final value.

### Step 5: Diff properties across configurations (if applicable)

If the build includes multiple configurations or solutions, call `DiffProperties` with two build binlogs to compare property values side by side.

### Step 6: Check for duplicate file writes

Call `GetDuplicateFileWrites` to find files that were written multiple times during the build. This directly identifies the consequences of OutputPath clashes.

### Step 7: Check redundant operations

Call `GetRedundantOperations` to find tasks that ran with identical inputs — often a symptom of extra global properties creating redundant project instances.
