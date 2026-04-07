## Detecting OutputPath Clashes with baronfel.binlog.mcp

### Step 1: Load the binlog

Call `load_binlog` with the path to the binlog file.

### Step 2: List all projects

Call `list_projects` to get all project file paths and IDs.

### Step 3: List evaluations per project

For each unique project file path, call `list_evaluations(projectFilePath=<path>)`. Multiple evaluations for the same project indicate multi-targeting or multiple build configurations.

### Step 4: Check global properties for each evaluation

For evaluations of the same project, call `get_evaluation_global_properties(evaluationId=<id>)` to see what differs between them. Look for:
- `TargetFramework` — should produce different output paths
- `SolutionFileName` — different values indicate multi-solution builds
- `PublishReadyToRun` — extra property that doesn't affect output paths
- `BuildProjectReferences` — if `false`, this is a P2P query (ignore)
- `MSBuildRestoreSessionId` — if present, this is a restore-phase evaluation

### Step 5: Get output paths for each evaluation

Call `get_evaluation_properties_by_name(evaluationId=<id>, propertyNames=["OutputPath", "IntermediateOutputPath", "BaseOutputPath", "BaseIntermediateOutputPath"])` for each evaluation.

### Step 6: Identify clashes

Compare the property values across evaluations:
- Normalize paths to absolute paths
- Group evaluations by OutputPath and IntermediateOutputPath
- Any group with multiple evaluations (after filtering out P2P queries and restore-only evals) is a clash

### Step 7: Verify via target execution (optional)

Call `search_binlog(query="$target CopyFilesToOutputDirectory")` to check which project instances ran file copy operations to the same output path.

Call `search_targets_by_name(targetName="CoreCompile")` to distinguish primary builds (long duration) from redundant builds (skipped or near-zero duration).
