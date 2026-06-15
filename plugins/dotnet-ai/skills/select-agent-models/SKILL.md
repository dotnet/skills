---
name: select-agent-models
description: |
  Recommend a per-agent model assignment for a .NET agentic application (Microsoft Agent Framework + Aspire + Foundry). Reads the existing topology, classifies each agent (router, planner, decomposer, worker, validator, formatter, summarizer), then maps each role to a model from a curated role-model matrix balancing latency, quality, and per-call cost. Two modes: read-only "recommend" (default, writes a plan to .copilot/perf-reports/model-plan-<timestamp>.md) and "apply" (diff-preview-and-confirm, edits AppHost connection strings and per-agent IChatClient registrations). Never applies without explicit confirmation. WHEN: user asks "which model should each agent use", "audit my model selection", "everyone defaults to gpt-4o-mini", or has just received an MA finding from audit-agentic-app-perf. NOT-WHEN: user is comparing providers, tuning prompts, or has only one agent (run audit-agentic-app-perf first).
---

# select-agent-models

Recommend per-agent model assignments based on each agent's role.

## Workflow

### 1. Inventory agents and current models

For each agent in the project, record:

- Agent name
- A short role guess from instructions / tool list / handoff position
- Current model id (from AppHost connection string or per-agent
  `IChatClient` builder)
- Estimated per-turn input tokens (system prompt + history + tool descs)

If fewer than 2 agents are detected, abort. Single-agent apps do not benefit
from this skill.

### 2. Classify each agent's role

Use `references/role-model-matrix.md`. Roles are:

- **router** — picks the next agent or tool. Short prompt, deterministic
  behaviour preferred.
- **planner** — decomposes the task. Reasoning-strong; output drives
  every downstream call.
- **decomposer** — splits work into N parallel items. Reasoning-medium.
- **worker** — does the unit-of-work the planner described. Medium quality;
  most calls happen here so latency dominates.
- **validator** — yes/no/score on a small input. Deterministic, small
  output.
- **formatter** — renders structured output (JSON, Markdown). Deterministic,
  small output.
- **summarizer** — compresses chat history. Medium reasoning, often run
  hot.

If an agent does not cleanly map to one role, mark its role as
`unclear` and recommend that the user review it.

### 3. Look up recommended model per role

`references/role-model-matrix.md` contains the canonical recommendation
table. The matrix has columns:

- Role
- Recommended model (primary)
- Acceptable alternatives
- Avoid
- Rationale (latency / quality / cost trade)

### 4. Build the plan

For each agent, produce a row:

```yaml
agent: <name>
current_model: <model id>
role: <classified role>
recommended_model: <id>
delta: same | upgrade | downgrade
rationale: <one sentence>
```

Aggregate notes:

- Net cost change estimate (qualitative: ↓ / ↔ / ↑)
- Net latency change estimate (qualitative: ↓ / ↔ / ↑)
- Risks to validate (e.g. "downgrading <X> requires a quality eval first")

### 5. Write the recommendation file

Write to:

- `.copilot/perf-reports/model-plan-<UTC-timestamp>.md`
- `.copilot/perf-reports/latest-model-plan.md`

Layout in `references/plan-template.md`.

Surface in chat: per-agent row plus the aggregate notes plus the file path.

### 6. Apply mode (only if user explicitly asks "apply" / "make the
changes")

Apply mode is **off by default**. To run it, the user must say "apply",
"make the changes", "switch to recommended models", or similar.

Steps:

1. Show a unified diff preview of every change you intend to make:
   - AppHost connection-string updates (`builder.AddAzureOpenAI` / `AddOpenAI`
     model parameters)
   - Per-agent `IChatClient` registrations
   - `appsettings.json` model-id keys
2. Ask the user to confirm.
3. Only on `yes` / explicit confirmation, write the changes.
4. After writing, run `dotnet build` on the touched projects and report
   pass/fail. Do not declare success if the build fails.
5. Recommend running `setup-maf-evals` to validate the change does not
   regress quality.

If the user says no or anything other than yes, discard the diff and
leave files untouched.

## Validation

After read-only mode:

- A new file exists at `.copilot/perf-reports/model-plan-<timestamp>.md`.
- `latest-model-plan.md` exists and matches the timestamped file.
- The plan lists every detected agent with a row.

After apply mode:

- The diff was shown and confirmed before any file write.
- All touched projects build (`dotnet build` exit 0).
- The plan file records the apply timestamp and which agents were modified.

## Common pitfalls

- **Applying without confirmation.** The default is recommend-only. Do not
  edit files without an explicit user confirmation in apply mode.
- **Recommending a downgrade with no quality check.** Always pair a
  downgrade recommendation with a `setup-maf-evals` follow-up.
- **Inventing roles.** If an agent's purpose is unclear, say so. Do not
  guess; the user must classify it.
- **Hard-coding model ids in code.** When applying, prefer
  `appsettings.json` over inline string literals so the next swap is a
  config change.
- **Ignoring provider differences.** This skill targets model *selection*
  within an already-chosen provider. If the user wants to compare
  providers, surface that as a follow-up question, not a recommendation.

## References

- `references/role-model-matrix.md` — role → recommended model table.
- `references/apphost-multi-client-template.md` — wiring multiple
  `IChatClient`s in the AppHost with distinct model ids.
- `references/agent-resolution-template.md` — per-agent service
  registration patterns.
- `references/plan-template.md` — exact Markdown layout for the plan file.
