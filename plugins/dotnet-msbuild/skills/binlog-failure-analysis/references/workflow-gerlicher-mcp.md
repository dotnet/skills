## Failure Analysis with BinlogMCP

### Step 1: Get build summary

Call `GetBuildSummary` to get an overview: build result, duration, error/warning counts, and project list.

### Step 2: Get automated diagnosis

Call `GetFailureDiagnosis` with the binlog path. This tool categorizes errors, identifies root causes, and suggests fixes. For many failures, this single call provides a complete diagnosis without further investigation.

### Step 3: Get structured error list

If more detail is needed, call `GetErrors` to get all errors with file, line, column, code, and message.

### Step 4: Examine project dependencies

Call `GetProjectDependencies` to understand the build graph and trace cascading failures through the dependency chain. Projects downstream of a failed project are cascading failures.

### Step 5: Examine project files for root causes

Use file tools to read the `.csproj` of the root-cause project identified by `GetFailureDiagnosis` or `GetErrors`.
