# Report template

The exact Markdown layout written to
`.copilot/perf-reports/audit-<timestamp>.md` and `latest-audit.md`.

```markdown
# Agentic perf audit — {{ project_name }}

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

### [critical] {{ title }}
- **Category:** {{ category }}
- **File:** `{{ file }}:{{ line }}`
- **Evidence:**
  ```csharp
  {{ snippet }}
  ```
- **Why:** {{ paragraph }}
- **Next:** {{ action }}
- **Cross-ref:** {{ skill: ... | omit if none }}

(... repeat per finding, ordered: critical → warn → info, then by category ...)

## Next steps

- If you want to fix the model assignments above, run `select-agent-models`.
- If you want to capture token/quality numbers before vs after, run
  `setup-maf-evals`.
- If you do not yet have always-on rules to prevent regressions, run
  `configure-agentic-perf-rules`.
```

## Empty-report contract

If there are zero findings, the `## Findings` section still appears with the
literal text `_No findings._`. The `## Summary` section shows zeros.
