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
- Re-run `scan-agentic-app-perf` after evals confirm parity.
- If quality regresses, revert the affected agent only via this skill.
```

## Plan mode (greenfield / design-time) layout

Written to `.copilot/perf-reports/model-plan-design-<timestamp>.md` and
`latest-model-plan-design.md`. The inventory section is replaced with
the user-declared topology, and a deployment-shape + verify-checklist
section is appended.

```markdown
# Model selection plan (design) — {{ project_name }}

Run: {{ utc_timestamp }}
Mode: plan (greenfield, no source scan)
Quality priority: {{ cost | latency | quality }}
Provider constraint: {{ foundry | azure-openai | openai | any }}

## Declared topology

| Agent              | Intended role | Purpose (user-declared) |
|--------------------|---------------|--------------------------|
| diff_summarizer    | worker        | Summarize a git diff for downstream review |
| style_critic       | worker        | Markdown code review over the summary |

## Per-agent recommendations

| Agent              | Role   | Recommended model | Rationale                                       |
|--------------------|--------|-------------------|--------------------------------------------------|
| diff_summarizer    | worker | gpt-4o-mini       | Matrix-primary for worker; latency dominates    |
| style_critic       | worker | gpt-4o-mini       | Matrix-primary for worker; candidate-upgrade if quality bar missed |

## Deployment shape

| Alias  | Model id     | Used by                          |
|--------|--------------|----------------------------------|
| chat   | gpt-4o-mini  | diff_summarizer, style_critic    |

Use stable alias names (`chat`, `chat-high`, `chat-reasoning`) in
AppHost — never the model id directly.

## Verify checklist (run after wiring)

- [ ] Re-run `select-agent-models` in **recommend** mode against the built app.
- [ ] Confirm every agent shows `Δ: same`.
- [ ] Any `upgrade`/`downgrade` means the source-classified role
      differs from what was declared here. Reconcile before continuing.
```

## Empty-plan contract

In `recommend` mode, if the inventory finds < 2 agents, the skill aborts
and does not write a plan file. The chat output explains why and routes
the user to `plan` mode instead.

In `plan` mode there is no such guard — single-agent design plans are
valid.
