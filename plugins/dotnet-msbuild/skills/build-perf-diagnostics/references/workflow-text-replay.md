## Performance Analysis with Text-Log Replay

### Step 1: Replay with performance summary

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary
```

> **PowerShell:** `-flp:"v=diag;logfile=full.log;performancesummary"`

### Step 2: Read target/task performance summaries

```bash
grep "Target Performance Summary\|Task Performance Summary" -A 50 full.log
```

This shows all targets and tasks sorted by cumulative time.

### Step 3: Find per-project build times

```bash
grep "done building project\|Project Performance Summary" full.log
```

### Step 4: Check parallelism (multi-node scheduling)

```bash
grep -i "node.*assigned\|RequiresLeadingNewline\|Building with" full.log | head -30
```

### Step 5: Check analyzer overhead

```bash
grep -i "Total analyzer execution time\|analyzer.*elapsed\|CompilerAnalyzerDriver" full.log
```

### Step 6: Drill into a specific slow target

```bash
grep 'Target "CoreCompile"\|Target "ResolveAssemblyReferences"' full.log
```
