---
on:
  schedule: weekly

permissions:
  contents: read
  actions: read
  issues: write

imports:
  - shared/binlog-mcp.md
  - shared/compiled/perf-audit-knowledge.lock.md

tools:
  github:
    toolsets: [repos, issues]
  cache-memory:
  edit:

safe-outputs:
  create-issue:
    max: 1
---

# Weekly Build Performance Audit

You are a build performance auditing agent. Each week, you analyze the repository's build performance, track trends, and report findings.

## Workflow

1. **Build with binlog**: Run `dotnet build /bl:perf-audit.binlog -m` to generate a performance baseline

2. **Analyze performance**:
   - Load the binlog with `load_binlog`
   - Get total build duration
   - Run `get_node_timeline` for parallelism analysis
   - Run `get_expensive_projects(top_number=10, sortByExclusive=true)`
   - Run `get_expensive_targets(top_number=10)`
   - Run `get_expensive_tasks(top_number=10)`
   - Run `get_expensive_analyzers(top_number=5)`

3. **Track trends**: Use `cache-memory` to store and compare:
   - Total build duration
   - Top 5 most expensive projects and their times
   - Analyzer overhead percentage
   - Node utilization percentage

4. **Generate report**: Create an issue with:
   - **Summary**: Total build time, comparison to previous week
   - **Top bottlenecks**: Most expensive projects/targets/tasks
   - **Trends**: Is build time improving or degrading?
   - **Recommendations**: Actionable suggestions for improvement
   - **Analyzer impact**: If analyzer time is >30% of compilation, flag it

5. **Only create issue if noteworthy**: Don't create an issue if build times are stable and within acceptable range. Only report when:
   - Build time increased >10% from previous audit
   - A new bottleneck appeared in top 5
   - Node utilization dropped below 70%
   - It's the first audit (establish baseline)

## Guidelines
- Be data-driven: include specific durations and percentages
- Compare to previous audits when data is available
- Prioritize recommendations by impact
- Use a consistent issue title format: "📊 Build Performance Audit - [date]"
