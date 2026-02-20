---
mcp-servers:
  binlog-mcp:
    command: "dnx"
    args: ["-y", "baronfel.binlog.mcp@0.0.13"]
tools:
  bash: ["dotnet", "cat", "grep", "head", "tail", "find", "ls"]
---

<!-- Shared: MSBuild Binary Log MCP Server -->
<!-- Import this in agentic workflows that need binlog analysis capabilities -->

When analyzing MSBuild binary logs, use the binlog-mcp tools:
1. First load the binlog: `load_binlog` with the absolute path
2. Get diagnostics: `get_diagnostics` for errors and warnings
3. Search: `search_binlog` for specific patterns
4. Performance: `get_expensive_projects`, `get_expensive_targets`, `get_expensive_tasks`
5. Analyzers: `get_expensive_analyzers` for Roslyn analyzer overhead
6. Timeline: `get_node_timeline` for parallelism analysis
