## Analyzing Parallelism with SQLite Logger

Requires: build was run with `-logger:"SqliteLogger,SqliteLogger.dll;LogFile=build.sqlite"`

### Step 1: Check node utilization

```sql
SELECT NodeId,
       COUNT(*) AS TargetCount,
       SUM(DurationMs) AS TotalWorkMs,
       MIN(StartTimeMs) AS FirstStart,
       MAX(EndTimeMs) AS LastEnd,
       MAX(EndTimeMs) - MIN(StartTimeMs) AS WallClockMs
FROM NodeTimeline
GROUP BY NodeId
ORDER BY TotalWorkMs DESC;
```

Compare `TotalWorkMs` to `WallClockMs` per node. If `TotalWorkMs << WallClockMs`, the node was idle much of the time.

### Step 2: Get expensive projects

```sql
SELECT * FROM ExpensiveProjects LIMIT 10;
```

### Step 3: Identify the critical path

```sql
-- Find the longest project chains
SELECT p.ProjectFile, p.DurationMs, p.StartTimeMs, p.EndTimeMs,
       parent.ProjectFile AS ParentProject
FROM Projects p
LEFT JOIN Projects parent ON p.ParentProjectId = parent.ProjectId
WHERE p.DurationMs IS NOT NULL
ORDER BY p.DurationMs DESC
LIMIT 15;
```

### Step 4: Find idle gaps between targets on each node

```sql
-- Detect gaps > 100ms between consecutive targets on the same node
SELECT a.NodeId, a.TargetName AS PrevTarget, b.TargetName AS NextTarget,
       b.StartTimeMs - a.EndTimeMs AS GapMs
FROM NodeTimeline a
JOIN NodeTimeline b ON a.NodeId = b.NodeId AND a.EndTimeMs < b.StartTimeMs
WHERE b.StartTimeMs - a.EndTimeMs > 100
  AND NOT EXISTS (
    SELECT 1 FROM NodeTimeline c
    WHERE c.NodeId = a.NodeId AND c.StartTimeMs > a.EndTimeMs AND c.StartTimeMs < b.StartTimeMs
  )
ORDER BY GapMs DESC
LIMIT 20;
```

### Step 5: Assess parallelism

- Many idle gaps → projects are serialized due to dependency chains
- One node doing most of the work → build graph is too deep
- All nodes evenly loaded → parallelism is already good
