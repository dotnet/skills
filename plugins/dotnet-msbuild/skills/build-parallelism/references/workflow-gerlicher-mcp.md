## Analyzing Parallelism with BinlogMCP

### Step 1: Get parallelism analysis

Call `GetParallelismAnalysis` with the binlog path. This returns a comprehensive analysis of build parallelism including concurrent operations, sequential bottlenecks, and utilization metrics.

### Step 2: Identify parallelism blockers

Call `GetParallelismBlockers` to find serialization points and dependency bottlenecks. This shows what is preventing better parallelism.

### Step 3: Get project dependency graph

Call `GetProjectDependencies` to see the project dependency graph, build order, and parallel execution info. Look for deep chains that serialize the build.

### Step 4: Get per-project timing

Call `GetProjectPerformance` to see per-project timing rollup. Identify which projects are slowest.

### Step 5: Get critical path

Call `GetCriticalPath` to identify the targets on the critical path that determine the overall build duration.

### Step 6: Assess parallelism

- `GetParallelismAnalysis` directly reports utilization percentage
- `GetParallelismBlockers` identifies the specific serialization points to fix
- `GetCriticalPath` shows which targets determine minimum build time
- Consider: splitting projects on the critical path, reducing dependency depth, using `/graph` mode
