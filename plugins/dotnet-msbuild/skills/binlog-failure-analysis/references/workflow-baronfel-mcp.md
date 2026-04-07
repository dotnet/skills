## Failure Analysis with baronfel.binlog.mcp

### Step 1: Load the binlog

Call `load_binlog` with the path to the binlog file.

### Step 2: Get all diagnostics

Call `get_diagnostics(includeErrors=true, includeWarnings=false)` to get structured error data. Each error includes file path, line number, error code, message, and project context — no parsing needed.

To also see warnings: `get_diagnostics(includeErrors=true, includeWarnings=true)`.

### Step 3: Identify affected projects

Call `list_projects` to see all projects and their entry targets. Cross-reference with the diagnostics to identify which projects have direct errors.

### Step 4: Detect cascading failures

Call `search_binlog(query="$target CoreCompile")` to find which projects ran `CoreCompile`. Projects with errors but no `CoreCompile` execution are cascading failures — they failed because a dependency failed.

### Step 5: Investigate specific errors

For errors in specific projects, use `search_binlog` with targeted queries:

- `search_binlog(query="error CS0246")` — find specific error codes with context
- `search_binlog(query="$target CoreCompile under($project MyProject)")` — check if a specific project compiled

### Step 6: Examine project files for root causes

Use file tools to read the `.csproj` of the first project with direct errors. Check `PackageReference` and `ProjectReference` entries.
