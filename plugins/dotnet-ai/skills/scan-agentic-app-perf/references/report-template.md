# Report template

The exact Markdown layout written to `.copilot/perf-reports/scan.md`.
This file is **overwritten on every run**. Git provides history; the
skill does not maintain timestamped copies or `latest-*` mirrors.

```markdown
# Agentic perf scan — {{ project_name }}

Run: {{ utc_timestamp }}
Project: {{ relative_project_path }}

## Inventory

- AppHost: `{{ apphost_path }}`
- Agents: {{ agent_count }} ({{ agent_list }})
- Handoff edges: {{ edge_count }}
- Tools (total): {{ tool_count }}
- Distinct models: {{ model_set }}
- OTel wired: {{ true | false }}

## Summary

- critical: {{ count }}
- warn:     {{ count }}
- info:     {{ count }}

## Findings

> **Slug format:** `<category>.<descriptor>` — categories are
> `topology` (graph shape) · `tools` (per-agent tool list) ·
> `history` (chat history strategy) · `prompt` (system prompt
> size/reuse) · `parallel` (concurrent invocations) · `otel`
> (instrumentation) · `model` (per-agent model id). Severities:
> `critical` (likely to break a flow or blow the budget) ·
> `warn` (measurable cost/perf regression) · `info` (observation).

### [critical] [`{{ check }}`] {{ title }}
- **File:** `{{ file }}:{{ line }}`
- **Evidence:**
  ```csharp
  {{ snippet }}
  ```
- **Why:** {{ paragraph }}
- **Next:** {{ action }}
- **Cross-ref:** {{ skill: ... | omit if none }}

(... repeat per finding, ordered: critical → warn → info, then by `check` slug ...)

## Next steps

- If you want to fix the model assignments above, see rule #3 in `.github/copilot-instructions.md` (managed by `configure-agentic-perf-rules`).
- If you want to capture token/quality numbers before vs after, run
  `setup-maf-evals`.
- If you do not yet have always-on rules to prevent regressions, run
  `configure-agentic-perf-rules`.
```

## Empty-report contract

If there are zero findings, the `## Findings` section still appears
(after the legend blockquote) with the literal text `_No findings._`.
The `## Summary` section shows zeros.
