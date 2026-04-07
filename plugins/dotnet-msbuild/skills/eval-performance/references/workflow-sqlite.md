## Diagnosing Evaluation Performance with SQLite Logger

Requires: build was run with `-logger:"SqliteLogger,SqliteLogger.dll;LogFile=build.sqlite"`

### Step 1: Find slowest evaluations

```sql
SELECT EvaluationId, ProjectFile, DurationMs
FROM Evaluations
ORDER BY DurationMs DESC
LIMIT 10;
```

### Step 2: Check for multiple evaluations per project

```sql
SELECT ProjectFile, COUNT(*) AS EvalCount, SUM(DurationMs) AS TotalMs
FROM Evaluations
GROUP BY ProjectFile
HAVING COUNT(*) > 1
ORDER BY TotalMs DESC;
```

Any project with `EvalCount > 1` per TFM is being over-evaluated.

### Step 3: Compare global properties across evaluations

```sql
-- For a project with multiple evaluations, see what differs
SELECT e.EvaluationId, ep.Name, ep.Value
FROM EvaluationProperties ep
JOIN Evaluations e ON ep.EvaluationId = e.EvaluationId
WHERE e.ProjectFile LIKE '%MyProject.csproj'
AND ep.Name IN ('TargetFramework', 'Configuration', 'Platform', 'RuntimeIdentifier')
ORDER BY e.EvaluationId, ep.Name;
```

### Step 4: Check glob patterns

```sql
-- Count Compile items per evaluation to detect overly broad globs
SELECT e.EvaluationId, e.ProjectFile, COUNT(*) AS CompileCount
FROM EvaluationItems ei
JOIN Evaluations e ON ei.EvaluationId = e.EvaluationId
WHERE ei.ItemType = 'Compile'
GROUP BY e.EvaluationId
ORDER BY CompileCount DESC
LIMIT 10;
```

### Step 5: Inspect imported files

```sql
SELECT FilePath, LENGTH(Content) AS ContentBytes
FROM Files
WHERE FilePath LIKE '%.props' OR FilePath LIKE '%.targets'
ORDER BY ContentBytes DESC
LIMIT 20;
```

Deep import chains with large files indicate heavy evaluation overhead.
