## Diagnosing Evaluation Performance with Text-Log Replay

### Step 1: Replay the binlog

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log
```

### Step 2: Search for evaluation events

```bash
grep -i "Evaluation started\|Evaluation finished" full.log
```

Multiple evaluations for the same project indicate overbuilding. Look for timestamps to measure evaluation duration.

### Step 3: Check evaluation counts per project

```bash
grep -i "Evaluation started" full.log | grep -oP '"[^"]+\.(csproj|vbproj|fsproj)"' | sort | uniq -c | sort -rn
```

Any project with count > 1 per TFM is being over-evaluated.

### Step 4: Preprocess to analyze imports

```bash
dotnet msbuild -pp:full.xml MyProject.csproj
```

Search the preprocessed output for `<!-- Importing` comments to see the import tree depth. Large preprocessed output (>10K lines) indicates heavy evaluation.

### Step 5: Check evaluation time in performance summary

```bash
dotnet msbuild build.binlog -noconlog -fl -flp:v=diag;logfile=full.log;performancesummary
grep "Evaluation Performance Summary\|Evaluation started\|Evaluation finished" full.log | head -30
```
