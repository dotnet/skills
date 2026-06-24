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
check_id: T1 | T2 | T3 | T4 | TI1 | TI2 | TI3 | TI4 | MH1 | MH2 | MH3 | PW1 | PW2 | PW3 | P1 | P2 | P3 | O1 | O2 | O3 | O4 | MA1 | MA2 | MA3 | MA4
title:    short imperative phrase
file:     path relative to repo root
line:     1-based line number, or null
evidence: 1-3 line code snippet or measurement (must be present in the cited file)
why:      one paragraph explaining the impact
next:     concrete action the developer can take next
ref:      optional cross-skill route (e.g. "skill:configure-agentic-perf-rules")
```

The `check_id` prefix encodes the category — there is no separate
`category` field. Prefix glossary (also rendered at the top of the
report's `## Findings` section):

| Prefix | Category                | Reference                              |
|--------|-------------------------|----------------------------------------|
| `T`    | topology                | `references/topology-checks.md`        |
| `TI`   | tool inventory          | `references/tool-inventory-checks.md`  |
| `MH`   | message history         | `references/message-history-checks.md` |
| `PW`   | prompt weight           | `references/prompt-weight-checks.md`   |
| `P`    | parallelism             | `references/parallelism-checks.md`     |
| `O`    | OTel coverage           | `references/otel-coverage-checks.md`   |
| `MA`   | model assignment        | `references/model-assignment-checks.md`|

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

Sort findings by severity (critical → warn → info), then by `check_id`
(stable lexical order so `T1` < `T2` < `TI1` < `MH1` < ...). Write to:

- `.copilot/perf-reports/scan-<UTC-timestamp>.md` (timestamped, kept)
- `.copilot/perf-reports/latest-scan.md` (overwritten each run)
- `.copilot/perf-reports/check-id-glossary.md` (overwritten each run)
  — a one-line-per-code reference card so first-time readers of a
  report can decode `T1`/`TI3`/`MA4` without opening the skill repo.
  Source: copy the "Reference card" section verbatim from
  `references/check-id-glossary.md`.

See `references/report-template.md` for the exact layout.

Create `.copilot/perf-reports/` if it does not exist.

This skill never touches `.gitignore`. If the user wants the report
folder ignored, recommend in chat that they add
`.copilot/perf-reports/` to their `.gitignore` themselves; do not edit
it from this skill.

### 5. Surface top findings in chat

Print:

1. Total counts (critical / warn / info).
2. The first up to 3 critical findings with title + check_id + file:line + next action.
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

- A new file exists at `.copilot/perf-reports/scan-<timestamp>.md`.
- `latest-scan.md` exists in the same folder and matches the timestamped
  file byte-for-byte.
- The report has a `## Findings` section, even if empty (containing
  `_No findings._`).
- The summary counts in the report match the chat output.

## Common pitfalls

- **Editing source code.** This skill is read-only and only writes to
  `.copilot/perf-reports/`. Never edit `.gitignore`, source files,
  config files, or anything else. If a check tempts you to fix the
  issue inline, stop and add it as a finding instead.
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
- `references/check-id-glossary.md` — the reference card written alongside each report.
- `references/report-template.md` — exact Markdown layout for the report.
