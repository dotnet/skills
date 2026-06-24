# Check glossary (dev-facing)

Canonical catalog of every `check` slug the skill can emit. This file
is for **skill maintainers** — adding or renaming a check requires a
row here. It is **not copied** into user repositories; the slugs are
self-describing in the report itself.

If you change this table, also update the matching `*-checks.md`
reference file with the per-check detection logic.

## Catalog

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

## Cross-skill routes

| `ref:` value                         | Skill to run next                                          |
|--------------------------------------|------------------------------------------------------------|
| `skill:configure-agentic-perf-rules` | Install/update the always-on perf rules (rule #3 covers role-aware model selection) |
| `skill:setup-maf-evals`              | Wire eval reports + telemetry                              |
