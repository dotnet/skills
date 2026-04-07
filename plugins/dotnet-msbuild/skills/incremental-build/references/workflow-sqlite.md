## Diagnosing Incremental Build Issues with SQLite Logger

Requires: both builds were run with the SQLite logger:
```bash
dotnet build -logger:"SqliteLogger,SqliteLogger.dll;LogFile=first.sqlite"
dotnet build -logger:"SqliteLogger,SqliteLogger.dll;LogFile=second.sqlite"
```

### Step 1: Find non-skipped targets in the second build

```sql
-- Targets that executed (not skipped) in the second build
SELECT t.Name, t.ProjectFile, t.DurationMs, t.BuildReason
FROM Targets t
WHERE t.Skipped = 0
ORDER BY t.DurationMs DESC;
```

### Step 2: Check total work done in second build

```sql
SELECT * FROM ExpensiveTargets LIMIT 15;
```

In a good incremental build, most targets should have `SkippedCount >> RanCount`.

### Step 3: Compare target execution across builds

Use SQLite's `ATTACH` to compare the two databases:

```sql
-- In sqlite3 with second.sqlite open:
ATTACH 'first.sqlite' AS first;

-- Targets that ran in the second build but were skipped in the first
SELECT s.Name, s.ProjectFile, s.DurationMs AS SecondBuildMs
FROM main.Targets s
WHERE s.Skipped = 0
  AND EXISTS (
    SELECT 1 FROM first.Targets f
    WHERE f.Name = s.Name AND f.ProjectFile = s.ProjectFile AND f.Skipped = 1
  );
```

### Step 4: Search for "is newer than output" messages

```sql
SELECT Message, ProjectFile
FROM Messages
WHERE Message LIKE '%is newer than output%'
ORDER BY TimestampMs;
```

### Step 5: Check target inputs/outputs

For targets that should have been skipped, look at their definition using `/pp`:
```bash
dotnet msbuild -pp:full.xml MyProject.csproj
```

Search for the target name to find its `Inputs` and `Outputs` attributes.
