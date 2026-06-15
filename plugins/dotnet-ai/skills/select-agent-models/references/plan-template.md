# Plan template

The exact Markdown layout written to
`.copilot/perf-reports/model-plan-<timestamp>.md` and
`latest-model-plan.md`.

```markdown
# Model selection plan — {{ project_name }}

Run: {{ utc_timestamp }}
Project: {{ relative_project_path }}
Mode: recommend | apply ({{ confirmed_at | "n/a" }})

## Per-agent recommendations

| Agent      | Role       | Current model    | Recommended model | Δ          | Rationale                                  |
|------------|------------|------------------|-------------------|------------|--------------------------------------------|
| router     | router     | gpt-4o           | gpt-4o-mini       | downgrade  | One-shot classification; latency dominates |
| planner    | planner    | gpt-4o-mini      | o4-mini           | upgrade    | Plan quality drives N downstream calls     |
| worker     | worker     | gpt-4o           | gpt-4o-mini       | downgrade  | Most calls; latency dominates              |

## Aggregate notes

- **Cost:** ↓ (downgrades on router and worker outweigh planner upgrade)
- **Latency:** ↓ (router and worker shrink; planner runs once per turn)
- **Quality risk:** validate planner upgrade and worker downgrade with
  `setup-maf-evals` quality mode before promoting.

## Apply preview (only present in apply mode)

Files to be modified:

- `MyApp.AppHost/appsettings.json`
- `MyApp.AppHost/Program.cs` (parameter declarations only)

Diff:

```diff
- "worker-model":  "gpt-4o",
+ "worker-model":  "gpt-4o-mini",
```

## Next steps

- Run `setup-maf-evals` quality mode against the new assignments.
- Re-run `audit-agentic-app-perf` after evals confirm parity.
- If quality regresses, revert the affected agent only via this skill.
```

## Empty-plan contract

If the inventory finds < 2 agents, the skill aborts and does not write
a plan file. The chat output explains why.
