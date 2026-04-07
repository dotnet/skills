## Performance Analysis with baronfel.binlog.mcp

### Step 1: Load the binlog

Call `load_binlog` with the path to the binlog file.

### Step 2: Get expensive targets

Call `get_expensive_targets(top_number=10)`. This returns targets sorted by cumulative execution time, including execution count, skipped count, inclusive and exclusive durations.

### Step 3: Get expensive tasks

Call `get_expensive_tasks(top_number=10)`. This returns tasks sorted by total duration, with execution count, average/min/max durations.

### Step 4: Get expensive projects

Call `get_expensive_projects(top_number=10)` to see per-project build times with both exclusive and inclusive durations.

### Step 5: Check analyzer overhead

Call `get_expensive_analyzers(top_number=5)` to see which Roslyn analyzers and source generators consume the most build time.

For more detail on a specific Csc task, use `search_tasks_by_name(taskName="Csc")` to find task IDs, then `get_task_analyzers(projectId=<id>, targetId=<id>, taskId=<id>)` for per-analyzer timing.

### Step 6: Check node utilization

Call `get_node_timeline` to see per-node work data. Look for uneven utilization indicating serialization bottlenecks.

### Step 7: Drill into specific targets

For a slow target, call `get_target_info_by_name(projectId=<id>, targetName="CoreCompile")` to see its duration, build reason, and messages. Then `list_tasks_in_target` to see which tasks within the target are expensive.
