## Diagnosing Incremental Build Issues with baronfel.binlog.mcp

### Step 1: Build twice with binlogs

```shell
dotnet build /bl:first.binlog
dotnet build /bl:second.binlog
```

### Step 2: Load the second binlog

Call `load_binlog` with the path to `second.binlog`.

### Step 3: Find non-skipped targets

Call `search_binlog(query="Building target")` to find targets that executed instead of being skipped. In a perfectly incremental build, most targets should be skipped.

Also search for: `search_binlog(query="is newer than output")` to find the specific input files that triggered rebuilds.

### Step 4: Check expensive targets in second build

Call `get_expensive_targets(top_number=10)` to see which targets consumed the most time in the second build. These are your optimization targets.

### Step 5: Inspect specific targets

For each non-skipped target, call `get_target_info_by_name(projectId=<id>, targetName="<name>")` to see:
- The build reason (why it ran)
- Duration
- Messages (including "is newer than output" details)

### Step 6: Compare with first build (manual)

Load `first.binlog` separately and compare the target execution lists. Targets that ran in both builds despite no code changes indicate broken incrementality.

> **Note:** baronfel.binlog.mcp does not have built-in build comparison. If build comparison is critical, consider the AndyGerlicher/BinlogMCP backend which has `CompareBinlogs` and `DiffTargetExecution` tools.
