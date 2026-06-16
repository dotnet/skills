---
name: select-agent-models
description: |
  Recommend a per-agent model assignment for a .NET agentic application (Microsoft Agent Framework + Aspire + Foundry). Reads the existing topology, classifies each agent (router, planner, decomposer, worker, validator, formatter, summarizer), then maps each role to a model from a curated role-model matrix balancing latency, quality, and per-call cost. Two modes: read-only "recommend" (default, writes a plan to .copilot/perf-reports/model-plan-<timestamp>.md) and "apply" (diff-preview-and-confirm, edits AppHost connection strings and per-agent IChatClient registrations). Never applies without explicit confirmation. WHEN: user asks "which model should each agent use", "audit my model selection", "everyone defaults to gpt-4o-mini", or has just received an MA finding from scan-agentic-app-perf. NOT-WHEN: user is comparing providers, tuning prompts, or has only one agent (run scan-agentic-app-perf first).
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

**Confirmation contract** — the initial apply request only enables
preview mode. The actual write requires a *second* user response after
the diff is shown. Wording in the initial request like "apply and
confirm", "yes do it", or "proceed" is treated as *intent*, not as
*confirmation* — the agent must still show the diff and ask.

Steps:

1. **Provider resolution.** Detect whether the project uses public
   OpenAI ids, Azure OpenAI deployment aliases, Foundry deployments,
   or another provider:
   - Check the AppHost connection string type (`AddAzureOpenAI` vs
     `AddOpenAI` vs others).
   - Check `appsettings.json` for keys ending in `-deployment-name`,
     `-deployment`, or values that look like custom names (not the
     OpenAI public id pattern).
   - If Azure is detected, refuse to write a public OpenAI id like
     `gpt-4o-mini` directly. Map the recommendation to an existing
     deployment alias by:
     a. enumerating deployment aliases from `appsettings.json` /
        AppHost parameters, and
     b. asking the user to pick which alias maps to each role, or
     c. recommending creating a new deployment if no suitable alias
        exists (and stopping apply mode in that case).
2. **Pre-write validation.** For every file that would be modified,
   verify it parses (JSON / C# compiles via `dotnet build` dry-run on
   the AppHost). If any target file is unparseable, abort apply mode
   before any write.
3. **Diff preview.** Show a unified diff of every change you intend to
   make:
   - AppHost connection-string updates / parameter values
   - Per-agent `IChatClient` registrations
   - `appsettings.json` model-id keys
4. **Confirm.** Ask the user to confirm. Only on `yes` / explicit
   confirmation, proceed.
5. **Atomic write.** Write all changes. If any write fails midway,
   restore *all* touched files from their pre-write content. Do not
   leave the project in a partially-applied state.
6. **Build.** Run `dotnet build` on the touched projects.
   - On success: record the apply timestamp and per-agent old → new
     mapping in the plan file.
   - On failure: revert all changes from step 5, surface the build
     output, and report `apply: failed (build)`. Do not declare
     success on a failing build.
7. **Recommend follow-up.** After a successful apply, recommend running
   `setup-maf-evals` quality mode to validate no quality regression.

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
