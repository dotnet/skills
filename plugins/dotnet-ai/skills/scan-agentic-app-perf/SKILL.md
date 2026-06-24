---
name: scan-agentic-app-perf
description: |
  Scan a .NET agentic application (Microsoft Agent Framework + Aspire + Foundry) for performance, cost, and reliability issues across seven check categories: topology, tool inventory, message-history strategy, prompt weight, parallelism, OTel coverage, and per-agent model assignment. Produces a Markdown report at .copilot/perf-reports/scan-<timestamp>.md (plus latest-scan.md) with severity-tagged findings (critical/warn/info), file:line citations, evidence, and concrete next actions that can route into configure-agentic-perf-rules or setup-maf-evals. WHEN: user asks "why is my agent slow", "scan my agentic app", "audit my agentic app", "find perf issues", "is my topology too complex", or has just modified an agent topology. NOT-WHEN: user wants to install always-on rules (use configure-agentic-perf-rules), or wire up evaluations (use setup-maf-evals); not for non-agentic .NET apps. Read-only — never edits source files.
---

# scan-agentic-app-perf

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
check:    one of the slugs listed in references/check-glossary.md
          (e.g. model.same-default, history.full-share, otel.missing-sdk)
title:    short imperative phrase
file:     path relative to repo root
line:     1-based line number, or null
evidence: 1-3 line code snippet or measurement (must be present in the cited file)
why:      one paragraph explaining the impact
next:     concrete action the developer can take next
ref:      optional cross-skill route (e.g. "skill:configure-agentic-perf-rules")
```

The `check` field is a dotted slug: `<category>.<descriptor>`. The
prefix before the dot encodes the category — there is no separate
`category` field. Category card (also rendered at the top of the
report's `## Findings` section):

| Category   | Coverage                | Reference                              |
|------------|-------------------------|----------------------------------------|
| `topology` | agent graph shape       | `references/topology-checks.md`        |
| `tools`    | per-agent tool list     | `references/tool-inventory-checks.md`  |
| `history`  | chat history strategy   | `references/message-history-checks.md` |
| `prompt`   | system prompt size/reuse| `references/prompt-weight-checks.md`   |
| `parallel` | concurrent invocations  | `references/parallelism-checks.md`     |
| `otel`     | instrumentation         | `references/otel-coverage-checks.md`   |
| `model`    | per-agent model id      | `references/model-assignment-checks.md`|

See `references/check-glossary.md` for the full slug→description table.

**Evidence gate** — before adding a finding to the report, re-open the
cited file and confirm the `evidence` snippet is present at or near
the cited line. Drop any finding that fails this check. Findings whose
evidence is "absence of X" must list the exact files and patterns
that were searched in the `evidence` field instead of a snippet.

Severity rules:

- **critical** — likely to break a user-visible flow, blow the token budget,
  or cause a cost spike. Always surfaced in chat.
- **warn** — measurable perf or cost regression, but app still works.
- **info** — observation worth knowing, no action required.

### 4. Aggregate and write the report

Sort findings by severity (critical → warn → info), then by `check`
slug (stable lexical order so `history.*` < `model.*` < `otel.*` <
`parallel.*` < `prompt.*` < `tools.*` < `topology.*`).

Write a **single file**: `.copilot/perf-reports/scan.md`, overwritten
on every run. Git provides whatever history you want; the skill does
not maintain timestamped copies or `latest-*` mirrors.

The category legend (one-liners describing what each prefix covers) is
inlined into the report itself — see `references/report-template.md`.
There is no separate glossary file. The slug encodes the check (e.g.
`history.full-share`, `topology.cycle`); readers don't need a lookup
table.

Create `.copilot/perf-reports/` if it does not exist.

This skill never touches `.gitignore`. If the user wants the report
ignored, recommend in chat that they add `.copilot/perf-reports/` to
their `.gitignore` themselves; do not edit it from this skill.

### 5. Surface top findings in chat

Print:

1. Total counts (critical / warn / info).
2. The first up to 3 critical findings with title + `check` slug + file:line + next action.
3. The full report path.
4. If any findings have a `ref:` field, list the suggested follow-up skills.

Do not paste the entire report into chat.

### 6. Offer to route into follow-up skills

After surfacing the top findings, **ask the user once** whether they
want to act on the routed follow-ups. The skill itself never edits
source — this step only routes into a sibling skill that owns its own
diff-and-confirm flow.

1. Aggregate the unique `ref:` values across all findings.
2. If the set is non-empty, print one prompt of the form:
   > Want me to follow up on any of these?
   > - **A.** Install/update perf rules via `configure-agentic-perf-rules` to enforce role-aware model selection on future code.
   > - **B.** Run `setup-maf-evals` to capture token/quality numbers.
   > - **C.** Run `configure-agentic-perf-rules` to install always-on rules.
   > - **D.** No — just leave the report.
   Render only the lettered options that correspond to refs actually
   present in the findings (skip letters whose target skill is not
   referenced).
3. Wait for the user's response. If they pick a letter, hand off to the
   named skill with the audit report path as context. If they pick
   "no" or anything else, stop.
4. Do **not** infer intent from the original audit-request prompt
   ("audit and fix it" is *intent* — the follow-up skill must still
   present its own diff and obtain its own confirmation before any
   write). This skill's job ends at the routing offer.

### 7. Stop

This skill never edits source. If the user declined the offer in
step 6, or if there were no `ref:` fields in any finding, the skill
ends here.

## Validation

After running:

- A file exists at `.copilot/perf-reports/scan.md` (overwritten if it
  was there before).
- The report has a `## Findings` section, even if empty (containing
  `_No findings._`).
- The summary counts in the report match the chat output.

## Common pitfalls

- **Editing source code.** This skill is read-only. The ONLY write path
  it owns is `.copilot/perf-reports/scan.md`. Never edit `.gitignore`,
  source files, config files, or anything else. If a check tempts you
  to fix the issue inline, stop and add it as a finding instead.
- **Hallucinating findings.** Every finding must cite a real file and
  (where applicable) a real line. Before adding a finding to the
  report, re-open the cited file and verify the snippet exists at the
  cited line. If you cannot point to evidence, drop the finding.
- **Burying critical findings.** Always lift the top 3 critical
  findings into chat. Do not say "see the report" without surfacing
  the worst issues.
- **Confusing this with rules install.** If the user wants the rules
  themselves embedded into their instructions file, run
  `configure-agentic-perf-rules` instead.
- **Running on non-agentic apps.** If no agent registrations are found,
  abort cleanly. Do not invent an audit for a plain web API.
- **Recommending a model downgrade without an eval gate.** Any MA*
  finding that says "downgrade Agent X from gpt-4o to gpt-4o-mini"
  must be paired in the `next:` field with "validate via
  `setup-maf-evals` quality mode before shipping". Apparent free wins
  on cost frequently regress quality on edge cases — the eval gate
  protects against that.

## References

- `references/topology-checks.md` — agent count, handoff edges, cycles.
- `references/tool-inventory-checks.md` — tools per agent, redundancy, dead tools.
- `references/message-history-checks.md` — full-history sharing, summarization.
- `references/prompt-weight-checks.md` — system-prompt size, per-agent token cost.
- `references/parallelism-checks.md` — sequential calls that could fan out.
- `references/otel-coverage-checks.md` — Aspire dashboard, token/cost telemetry.
- `references/model-assignment-checks.md` — single-model defaulting, role mismatch.
- `references/check-glossary.md` — dev-facing catalog of all check slugs (NOT copied to user repos; for skill maintainers).
- `references/report-template.md` — exact Markdown layout for `scan.md`.
