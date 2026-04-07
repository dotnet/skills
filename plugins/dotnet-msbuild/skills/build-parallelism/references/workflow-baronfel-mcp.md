## Analyzing Parallelism with baronfel.binlog.mcp

### Step 1: Load the binlog

Call `load_binlog` with the path to the binlog file.

### Step 2: Get the node timeline

Call `get_node_timeline`. This returns per-node work data showing what each build node was doing and when. Look for idle gaps between target executions on the same node.

### Step 3: Get expensive projects

Call `get_expensive_projects(top_number=10, sortByExclusive=false)` to see which projects took the longest (inclusive time, which includes waiting for dependencies).

### Step 4: Check project-level build times

For suspected bottleneck projects, call `get_project_build_time(projectId=<id>)` to get exclusive vs inclusive time. A large gap between inclusive and exclusive means the project spent most of its time waiting for dependencies.

### Step 5: Get expensive targets

Call `get_expensive_targets(top_number=10)` to find which targets dominate build time across all projects.

### Step 6: Assess parallelism

- If `get_node_timeline` shows uneven node utilization → serialization bottleneck
- If one project has high inclusive time but low exclusive time → it's waiting on dependencies, not doing work
- If one project has high exclusive time → it's the actual bottleneck; consider splitting it
