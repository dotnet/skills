---
name: binlog-failure-analysis
description: "Skill for .NET/MSBuild *.binlog files and complicated build failures. Only activate in MSBuild/.NET build context. This skill uses binary logs for comprehensive build failure analysis."
---

# Analyzing MSBuild Failures with Binary Logs

You have binlog MCP tool calls available — call them directly like any other tool. Do not use bash to install or run binlog tools.

## Build Error Investigation (Primary Workflow)

1. Call `load_binlog` with the absolute path to the `.binlog` file
2. Call `get_diagnostics` with `includeErrors: true, includeDetails: true` — returns all errors with file paths, line numbers, and project context
3. Call `list_projects` — shows all projects and their build status
4. Call `get_file_from_binlog` to retrieve `.csproj` files for projects that had errors — binlogs embed all source files
5. Call `get_evaluation_items_by_name` with `PackageReference` or `ProjectReference` to check dependencies
6. If needed, call `search_binlog` with the error code (e.g., `"error CS0246"`) for additional context
7. To detect cascading failures: call `search_binlog` with `under($project ProjectName) $target CoreCompile` — projects that never reached CoreCompile failed due to a dependency, not their own code

**Write your diagnosis as soon as you have enough information.** Do not over-investigate.

## Additional Workflows

### Performance Investigation
1. `load_binlog` → `get_expensive_targets` → `get_expensive_tasks` → `get_expensive_analyzers` → `search_targets_by_name` → `get_node_timeline`

### Dependency/Evaluation Issues
1. `load_binlog` → `list_projects` → `list_evaluations` → `get_evaluation_global_properties` → `get_evaluation_items_by_name`

## ❌ DO NOT use bash for binlog analysis

- Do not install `binlogtool`, `dotnet-script`, or any CLI tool — you already have tool calls
- Do not write C# scripts to parse binlogs — the tool calls are faster and more reliable
- Do not use `dotnet msbuild -flp` — use `get_diagnostics` and `search_binlog` instead

## Key tools

| Tool call | Purpose |
|-----------|---------|
| `load_binlog` | **Call first.** Load a binlog before using any other tool |
| `get_diagnostics` | Get all errors/warnings with source locations |
| `list_projects` | List projects and build status |
| `get_file_from_binlog` | Read any embedded file (source, csproj, props) |
| `list_files_from_binlog` | List all embedded files |
| `search_binlog` | Freetext search with query syntax |
| `get_evaluation_items_by_name` | Get PackageReference, ProjectReference, etc. |
| `get_project_target_list` | List targets executed for a project |

## Search query syntax (for `search_binlog`)

- `"error CS0246"` — find specific errors
- `$task Csc` — find C# compilation tasks
- `under($project MyProject)` — scope to a project
- `under($project X) $target CoreCompile` — check if project reached compilation
- `$target TargetName` — find target executions
- `not($query)` — exclude matches

## Generating a binlog (only if none exists)

```bash
dotnet build /bl:build.binlog
```

## Common error patterns

1. **CS0246 / "type not found"** → Missing PackageReference — check the .csproj
2. **MSB4019 / "imported project not found"** → SDK install or global.json issue
3. **NU1605 / "package downgrade"** → Version conflict in package graph
4. **MSB3277 / "version conflicts"** → Binding redirect or version alignment issue
5. **Project failed at ResolveProjectReferences** → Cascading failure from a dependency
