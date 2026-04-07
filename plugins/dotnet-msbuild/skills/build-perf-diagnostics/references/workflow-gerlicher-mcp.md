## Performance Analysis with BinlogMCP

### Step 1: Get comprehensive performance report

Call `GetPerformanceReport` with the binlog path. This returns a comprehensive analysis including bottlenecks, slow targets/tasks, and optimization hints — often sufficient as a single call.

### Step 2: Get compiler performance details

Call `GetCompilerPerformance` for detailed C#/VB/F# compilation timing analysis, including per-project Csc task durations.

### Step 3: Check parallelism

Call `GetParallelismAnalysis` for build parallelism efficiency — concurrent operations, sequential bottlenecks, and utilization metrics.

### Step 4: Check slow I/O operations

Call `GetSlowOperations` to analyze slow file I/O operations (Copy, Move, Delete, Exec).

### Step 5: Get per-project timing

Call `GetProjectPerformance` for per-project timing rollup. Identify which projects are slowest.

### Step 6: Identify the critical path

Call `GetCriticalPath` to identify targets on the critical path that determine overall build duration.

### Step 7: Drill into specific targets

Call `AnalyzeTarget(targetName="CoreCompile")` for a deep dive into a specific target — tasks, parameters, I/O, and timing breakdown.
