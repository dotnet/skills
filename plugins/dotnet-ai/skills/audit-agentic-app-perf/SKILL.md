---
name: audit-agentic-app-perf
description: |
  Audit a .NET agentic application (Microsoft Agent Framework + Aspire + Foundry) for performance, cost, and reliability issues across seven check categories: topology, tool inventory, message-history strategy, prompt weight, parallelism, OTel coverage, and per-agent model assignment. Produces a Markdown report at .copilot/perf-reports/audit-<timestamp>.md (plus latest-audit.md) with severity-tagged findings (critical/warn/info), file:line citations, evidence, and concrete next actions that can route into select-agent-models, setup-maf-evals, or configure-agentic-perf-rules. WHEN: user asks "why is my agent slow", "audit my agentic app", "review perf of MAF app", "find perf issues", "is my topology too complex", or has just modified an agent topology. NOT-WHEN: user wants to install always-on rules (use configure-agentic-perf-rules), pick models per role (use select-agent-models), or wire up evaluations (use setup-maf-evals); not for non-agentic .NET apps. Read-only — never edits source files.
---

# audit-agentic-app-perf

Run a structured audit of a .NET agentic application and produce a single
Markdown report listing the perf, cost, and reliability issues that matter,
each with a concrete next action.

This skill is **read-only**. It never edits source. The output is a report file
plus a short chat summary of top findings.

## Workflow

### 1. Inventory the app

Detect and record:

- AppHost project (`*.AppHost.csproj`) and agent service projects
- Agent registrations (`AddAgent`, `ChatClientAgent`, `IChatClient` builders)
- Agent count, handoff edges, tool count per agent
- OTel wiring (`AddOpenTelemetry`, Aspire dashboard reference)

If no agentic app is detected, abort and tell the user this skill does not
apply. Do not attempt to audit a non-agentic .NET project.

### 2. Run the seven check classes

Each check class lives in a reference doc and is run in turn. Detection logic
and finding templates are in `references/`:

| # | Category          | Reference                                  |
|---|-------------------|--------------------------------------------|
| 1 | Topology          | `references/topology-checks.md`            |
| 2 | Tool inventory    | `references/tool-inventory-checks.md`      |
| 3 | Message history   | `references/message-history-checks.md`     |
| 4 | Prompt weight     | `references/prompt-weight-checks.md`       |
| 5 | Parallelism       | `references/parallelism-checks.md`         |
| 6 | OTel coverage     | `references/otel-coverage-checks.md`       |
| 7 | Model assignment  | `references/model-assignment-checks.md`    |

For each check, record any findings with the schema in step 3.

### 3. Finding schema

Every finding is a dict with these fields:

```yaml
severity: critical | warn | info
category: topology | tools | history | prompt | parallelism | otel | models
title:    short imperative phrase
file:     path relative to repo root
line:     1-based line number, or null
evidence: 1-3 line code snippet or measurement
why:      one paragraph explaining the impact
next:     concrete action the developer can take next
ref:      optional cross-skill route (e.g. "skill:select-agent-models")
```

Severity rules:

- **critical** — likely to break a user-visible flow, blow the token budget,
  or cause a cost spike. Always surfaced in chat.
- **warn** — measurable perf or cost regression, but app still works.
- **info** — observation worth knowing, no action required.

### 4. Aggregate and write the report

Sort findings by severity (critical → warn → info), then by category. Write
to:

- `.copilot/perf-reports/audit-<UTC-timestamp>.md` (timestamped, kept)
- `.copilot/perf-reports/latest-audit.md` (overwritten each run)

See `references/report-template.md` for the exact layout.

Create `.copilot/perf-reports/` if it does not exist. Add it to `.gitignore`
if a `.gitignore` exists at the repo root and the entry is not already there.

### 5. Surface top findings in chat

Print:

1. Total counts (critical / warn / info).
2. The first up to 3 critical findings with title + file:line + next action.
3. The full report path.
4. If any findings have a `ref:` field, list the suggested follow-up skills.

Do not paste the entire report into chat.

### 6. Stop

This skill never edits source. If the user wants to fix a finding, route to
the appropriate skill named in the `ref:` field.

## Validation

After running:

- A new file exists at `.copilot/perf-reports/audit-<timestamp>.md`.
- `latest-audit.md` exists in the same folder and matches the timestamped
  file byte-for-byte.
- The report has a `## Findings` section, even if empty (containing
  `_No findings._`).
- The summary counts in the report match the chat output.

## Common pitfalls

- **Editing source code.** This skill is read-only. If a check tempts you to
  fix the issue inline, stop and add it as a finding instead.
- **Hallucinating findings.** Every finding must cite a real file and (where
  applicable) a real line. If you cannot point to evidence, drop the finding.
- **Burying critical findings.** Always lift the top 3 critical findings into
  chat. Do not say "see the report" without surfacing the worst issues.
- **Confusing this with rules install.** If the user wants the rules
  themselves embedded into their instructions file, run
  `configure-agentic-perf-rules` instead.
- **Running on non-agentic apps.** If no agent registrations are found,
  abort cleanly. Do not invent an audit for a plain web API.

## References

- `references/topology-checks.md` — agent count, handoff edges, cycles.
- `references/tool-inventory-checks.md` — tools per agent, redundancy, dead tools.
- `references/message-history-checks.md` — full-history sharing, summarization.
- `references/prompt-weight-checks.md` — system-prompt size, per-agent token cost.
- `references/parallelism-checks.md` — sequential calls that could fan out.
- `references/otel-coverage-checks.md` — Aspire dashboard, token/cost telemetry.
- `references/model-assignment-checks.md` — single-model defaulting, role mismatch.
- `references/report-template.md` — exact Markdown layout for the report.
