## Performance Analysis with SQLite Logger

Requires: build was run with `-logger:"SqliteLogger,SqliteLogger.dll;LogFile=build.sqlite"`

### Step 1: Get expensive targets

```sql
SELECT * FROM ExpensiveTargets LIMIT 10;
```

### Step 2: Get expensive tasks

```sql
SELECT * FROM ExpensiveTasks LIMIT 10;
```

### Step 3: Get expensive projects

```sql
SELECT * FROM ExpensiveProjects LIMIT 10;
```

### Step 4: Check analyzer overhead

```sql
-- Csc task durations per project
SELECT t.ProjectFile, t.Name, t.DurationMs
FROM Tasks t
WHERE t.Name = 'Csc'
ORDER BY t.DurationMs DESC
LIMIT 10;
```

### Step 5: Check node utilization

```sql
SELECT NodeId,
       COUNT(*) AS TargetCount,
       SUM(DurationMs) AS TotalWorkMs,
       MIN(StartTimeMs) AS FirstStart,
       MAX(EndTimeMs) AS LastEnd
FROM NodeTimeline
GROUP BY NodeId
ORDER BY TotalWorkMs DESC;
```

### Step 6: Drill into a specific slow target

```sql
-- Get tasks within a specific target
SELECT tk.Name AS TaskName, tk.DurationMs, tk.TaskAssembly
FROM Tasks tk
JOIN Targets t ON tk.TargetId = t.TargetId
WHERE t.Name = 'CoreCompile' AND t.ProjectId = <id>
ORDER BY tk.DurationMs DESC;
```
