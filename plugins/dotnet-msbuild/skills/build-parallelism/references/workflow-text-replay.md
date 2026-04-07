## Analyzing Parallelism with Text-Log Replay

### Step 1: Replay the binlog

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary
```

> **PowerShell:** `-flp:"v=diag;logfile=full.log;performancesummary"`

### Step 2: Check Project Performance Summary

```bash
grep "Project Performance Summary" -A 30 full.log
```

Compare total build wall-clock time against the sum of individual project times. If they are similar, parallelism is low.

### Step 3: Identify bottleneck targets

```bash
grep "Target Performance Summary" -A 30 full.log
```

### Step 4: Check node scheduling

```bash
grep -i "node.*assigned\|Building with" full.log | head -30
```

Look for nodes that are idle while others are busy — this indicates serialization bottlenecks.

### Step 5: Assess parallelism

- Build time << sum of project times → good parallelism
- Build time ≈ sum of project times → too many serial dependencies or one slow project blocking others
- Consider splitting large projects or optimizing the critical path
