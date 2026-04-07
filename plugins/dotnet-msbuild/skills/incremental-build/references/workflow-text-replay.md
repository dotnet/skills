## Diagnosing Incremental Build Issues with Text-Log Replay

### Step 1: Build twice with binlogs

```shell
dotnet build /bl:first.binlog
dotnet build /bl:second.binlog
```

The first build establishes the baseline. The second build is the one you want to be incremental. Analyze `second.binlog`.

### Step 2: Replay the second binlog

```shell
dotnet msbuild second.binlog -noconlog -fl -flp:v=diag;logfile=second-full.log;performancesummary
```

> **PowerShell:** `-flp:"v=diag;logfile=second-full.log;performancesummary"`

### Step 3: Find non-skipped targets

```bash
grep 'Building target\|Target.*was not skipped' second-full.log
```

In a perfectly incremental build, most targets should be skipped.

### Step 4: Look for key messages

- `"Building target 'X' completely"` — MSBuild found no outputs or all outputs are missing
- `"Building target 'X' incrementally"` — some (but not all) outputs are out of date
- `"Skipping target 'X' because all output files are up-to-date"` — target was correctly skipped

### Step 5: Find the triggering file

```bash
grep "is newer than output" second-full.log
```

This reveals exactly which input file's timestamp caused MSBuild to consider the target out of date.

### Step 6: Check performance summary

```bash
grep "Target Performance Summary" -A 30 second-full.log
```

Targets that consumed time in the second build are your optimization targets.

### Additional techniques

- Compare `first.binlog` and `second.binlog` side by side in the MSBuild Structured Log Viewer to see what changed.
- Check for targets with zero-duration that still ran — they may have unnecessary dependencies.
