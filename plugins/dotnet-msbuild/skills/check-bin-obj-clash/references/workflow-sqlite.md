## Detecting OutputPath Clashes with SQLite Logger

Requires: build was run with `-logger:"SqliteLogger,SqliteLogger.dll;LogFile=build.sqlite"`

### Step 1: Find OutputPath clashes

```sql
-- Find OutputPath values shared by multiple evaluations (excluding restore-only and P2P queries)
SELECT ep.Value AS OutputPath, COUNT(DISTINCT ep.EvaluationId) AS EvalCount,
       GROUP_CONCAT(DISTINCT e.ProjectFile) AS Projects
FROM EvaluationProperties ep
JOIN Evaluations e ON ep.EvaluationId = e.EvaluationId
WHERE ep.Name = 'OutputPath'
  AND ep.EvaluationId NOT IN (
    SELECT gp.EvaluationId FROM EvaluationProperties gp
    WHERE gp.Name = 'BuildProjectReferences' AND gp.Value = 'false'
  )
  AND ep.EvaluationId NOT IN (
    SELECT gp.EvaluationId FROM EvaluationProperties gp
    WHERE gp.Name = 'MSBuildRestoreSessionId' AND gp.Value IS NOT NULL
  )
GROUP BY ep.Value
HAVING COUNT(DISTINCT ep.EvaluationId) > 1
ORDER BY EvalCount DESC;
```

### Step 2: Find IntermediateOutputPath clashes

```sql
-- IntermediateOutputPath clashes (include restore evals, they write project.assets.json)
SELECT ep.Value AS IntermediateOutputPath, COUNT(DISTINCT ep.EvaluationId) AS EvalCount,
       GROUP_CONCAT(DISTINCT e.ProjectFile) AS Projects
FROM EvaluationProperties ep
JOIN Evaluations e ON ep.EvaluationId = e.EvaluationId
WHERE ep.Name = 'IntermediateOutputPath'
  AND ep.EvaluationId NOT IN (
    SELECT gp.EvaluationId FROM EvaluationProperties gp
    WHERE gp.Name = 'BuildProjectReferences' AND gp.Value = 'false'
  )
GROUP BY ep.Value
HAVING COUNT(DISTINCT ep.EvaluationId) > 1
ORDER BY EvalCount DESC;
```

### Step 3: Investigate clashing evaluations

```sql
-- For a clashing path, see what global properties differ between evaluations
SELECT e.EvaluationId, e.ProjectFile,
       MAX(CASE WHEN ep.Name = 'TargetFramework' THEN ep.Value END) AS TFM,
       MAX(CASE WHEN ep.Name = 'Configuration' THEN ep.Value END) AS Config,
       MAX(CASE WHEN ep.Name = 'SolutionFileName' THEN ep.Value END) AS Solution,
       MAX(CASE WHEN ep.Name = 'PublishReadyToRun' THEN ep.Value END) AS PubR2R,
       MAX(CASE WHEN ep.Name = 'OutputPath' THEN ep.Value END) AS OutputPath
FROM EvaluationProperties ep
JOIN Evaluations e ON ep.EvaluationId = e.EvaluationId
WHERE ep.Name IN ('TargetFramework', 'Configuration', 'SolutionFileName', 'PublishReadyToRun', 'OutputPath')
  AND e.ProjectFile LIKE '%MyProject.csproj'
GROUP BY e.EvaluationId
ORDER BY e.EvaluationId;
```

### Step 4: Verify via target execution (optional)

```sql
-- Check which instances ran CopyFilesToOutputDirectory
SELECT t.ProjectFile, t.Name, t.Skipped, t.DurationMs
FROM Targets t
WHERE t.Name = 'CopyFilesToOutputDirectory'
ORDER BY t.ProjectFile;

-- Check CoreCompile to distinguish primary from redundant builds
SELECT t.ProjectFile, t.Name, t.Skipped, t.DurationMs
FROM Targets t
WHERE t.Name = 'CoreCompile'
ORDER BY t.DurationMs DESC;
```
