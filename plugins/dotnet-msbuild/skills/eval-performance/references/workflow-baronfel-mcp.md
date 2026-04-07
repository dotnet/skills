## Diagnosing Evaluation Performance with baronfel.binlog.mcp

### Step 1: Load the binlog

Call `load_binlog` with the path to the binlog file.

### Step 2: List all evaluations

Call `list_projects` to get all project file paths, then call `list_evaluations(projectFilePath=<path>)` for each project. This returns evaluation IDs with durations in milliseconds.

Sort by duration to find the slowest evaluations.

### Step 3: Check for multiple evaluations

If a project has more than one evaluation per TFM, it is being over-evaluated. Call `get_evaluation_global_properties(evaluationId=<id>)` for each evaluation to see what differs between them (different global properties trigger separate evaluations).

### Step 4: Inspect evaluation properties

For slow evaluations, call `get_evaluation_properties_by_name(evaluationId=<id>, propertyNames=["DefaultItemExcludes", "EnableDefaultItems"])` to check glob configuration.

### Step 5: Inspect evaluation items

Call `get_evaluation_items_by_name(evaluationId=<id>, itemTypeNames=["Compile"])` to see how many Compile items were evaluated. An unexpectedly large count suggests overly broad globs.

### Step 6: Check import chain

Call `list_files_from_binlog` and filter for `.props` and `.targets` files to understand the import chain depth.
