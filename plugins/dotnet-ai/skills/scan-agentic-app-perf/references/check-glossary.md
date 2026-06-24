# Check glossary

This file is the source of truth for the human-readable check card
that `scan-agentic-app-perf` writes alongside every report.

When the skill runs, it copies the "Reference card" section verbatim
to `.copilot/perf-reports/check-glossary.md` so a first-time report
reader can see the full check catalog without opening the skill repo.

## Reference card

> **Categories:** `topology` (agent graph shape) · `tools` (per-agent
> tool list) · `history` (chat history strategy) · `prompt` (system
> prompt size + reuse) · `parallel` (concurrent agent invocations) ·
> `otel` (instrumentation coverage) · `model` (per-agent model
> selection).
>
> **Severity:** `critical` = likely to break a flow or blow the
> budget · `warn` = measurable cost/perf regression · `info` =
> observation only.

| Check                              | Sev      | What it catches |
|------------------------------------|----------|-----------------|
| `topology.cycle`                   | critical | Directed cycle in the agent handoff graph |
| `topology.deep-single-leaf`        | warn     | 3+ hops to reach a single terminal agent |
| `tools.duplicate`                  | warn     | Same tool description on >1 agent |
| `tools.dead`                       | info     | Tool registered but never referenced |
| `tools.description-too-long`       | warn     | `[Description]` attribute > 200 chars |
| `history.full-share`               | critical | Entire chat history passed to a downstream agent |
| `history.unbounded`                | warn     | No `MaxMessages` / reducer / summarizer wired |
| `history.through-deterministic`    | warn     | Full history given to a deterministic agent |
| `prompt.oversized`                 | warn     | System prompt > 2K tokens (critical at > 4K) |
| `prompt.duplicate-preamble`        | warn     | Same > 100-token block shared across agents |
| `parallel.independent-handoffs`    | warn     | Sequential `await`s on agents that don't share data |
| `parallel.hidden-tool-fanout`      | info     | Tool internally serializes 3+ API calls |
| `otel.missing-sdk`                 | critical | No `AddOpenTelemetry` / Aspire dashboard wiring |
| `otel.no-aspire-dashboard`         | warn     | Aspire dashboard not declared |
| `otel.no-token-cost`               | warn     | No `gen_ai.usage.*` tags surfaced |
| `otel.no-per-agent-source`         | info     | All agents share one `ActivitySource` |
| `model.same-default`               | warn     | All agents use the same model id |
| `model.reasoning-on-deterministic` | warn     | Frontier model on a formatter / validator |
| `model.cheap-on-planner`           | warn     | Small model on the planner, larger on workers |
| `model.hardcoded`                  | info     | Model id literal in agent service `.cs` |

## Cross-skill routes embedded in `ref:` fields

| `ref:` value                         | Skill to run next                                          |
|--------------------------------------|------------------------------------------------------------|
| `skill:configure-agentic-perf-rules` | Install/update the always-on perf rules (rule #3 covers role-aware model selection) |
| `skill:setup-maf-evals`              | Wire eval reports + telemetry                              |

## Notes for skill implementation

- When generating a report, write a copy of the "Reference card" section
  (the table and the legend immediately above it) to
  `.copilot/perf-reports/check-glossary.md`. Overwrite each run; the
  content is static within a skill version and does not depend on findings.
- The glossary file is per-repo (one file regardless of how many runs);
  the timestamped `scan-<ts>.md` reports link to it relatively.
- Keep this table in lockstep with the per-category reference files.
  Adding a new check in any `*-checks.md` file REQUIRES a row here.
