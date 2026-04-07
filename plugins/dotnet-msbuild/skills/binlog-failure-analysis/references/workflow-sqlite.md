## Failure Analysis with SQLite Logger

Requires: build was run with `-logger:"SqliteLogger,SqliteLogger.dll;LogFile=build.sqlite"`

### Step 1: Get all errors

```sql
SELECT Code, Message, File, LineNumber, ColumnNumber, ProjectFile
FROM Errors
ORDER BY ProjectFile, LineNumber;
```

### Step 2: Get failing projects

```sql
SELECT ProjectId, ProjectFile, Succeeded, DurationMs
FROM Projects
WHERE Succeeded = 0
ORDER BY ProjectFile;
```

### Step 3: Detect cascading failures

```sql
SELECT p.ProjectFile,
  CASE WHEN EXISTS (
    SELECT 1 FROM Targets t
    WHERE t.ProjectId = p.ProjectId AND t.Name = 'CoreCompile' AND t.Skipped = 0
  ) THEN 'direct' ELSE 'cascading' END AS FailureType,
  (SELECT COUNT(*) FROM Diagnostics d
   WHERE d.ProjectId = p.ProjectId AND d.Severity = 'Error') AS ErrorCount
FROM Projects p
WHERE p.Succeeded = 0
ORDER BY FailureType, p.ProjectFile;
```

Projects with `FailureType = 'cascading'` failed because a dependency failed, not their own code. Focus on `FailureType = 'direct'` projects first.

### Step 4: Get context for specific errors

```sql
-- Find errors with their target and task context
SELECT d.Code, d.Message, d.File, d.LineNumber, t.Name AS TargetName, tk.Name AS TaskName
FROM Diagnostics d
LEFT JOIN Targets t ON d.TargetId = t.TargetId
LEFT JOIN Tasks tk ON d.TaskId = tk.TaskId
WHERE d.Severity = 'Error'
ORDER BY d.TimestampMs;
```

### Step 5: Examine project files for root causes

Use file tools to read the `.csproj` of projects with direct errors.
