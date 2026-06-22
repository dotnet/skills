# Check-ID glossary

This file is the source of truth for the check-ID reference card that
`scan-agentic-app-perf` writes alongside every report (parallel to the
metrics-glossary pattern in `setup-maf-evals`).

When the skill runs, it copies this file's "Reference card" section
verbatim to `.copilot/perf-reports/check-id-glossary.md` so a first-time
report reader can decode the codes without opening the skill repo.

## Reference card

> **Prefixes:** `T*` topology · `TI*` tool inventory · `MH*` message
> history · `PW*` prompt weight · `P*` parallelism · `O*` OTel · `MA*`
> model assignment.
>
> **Severity:** `critical` = likely to break a flow or blow the budget ·
> `warn` = measurable cost/perf regression · `info` = observation only.

| ID  | Sev      | Title                                                       | Full check |
|-----|----------|-------------------------------------------------------------|------------|
| T1  | warn     | Agent count > 3                                             | `topology-checks.md#t1` |
| T2  | warn     | LLM-routed handoff edges per turn > 2                       | `topology-checks.md#t2` |
| T3  | critical | Cycles in the agent graph                                   | `topology-checks.md#t3` |
| T4  | warn     | Single-leaf graph with > 2 hops                             | `topology-checks.md#t4` |
| TI1 | warn     | Tools per agent > 8                                         | `tool-inventory-checks.md#ti1` |
| TI2 | warn     | Duplicate tool functionality across agents                  | `tool-inventory-checks.md#ti2` |
| TI3 | info     | Dead tools (declared, never invoked)                        | `tool-inventory-checks.md#ti3` |
| TI4 | warn     | Tool description > 200 chars                                | `tool-inventory-checks.md#ti4` |
| MH1 | critical | Full chat history shared with every agent                   | `message-history-checks.md#mh1` |
| MH2 | warn     | No history cap (unbounded growth)                           | `message-history-checks.md#mh2` |
| MH3 | warn     | History passed through deterministic agents                 | `message-history-checks.md#mh3` |
| PW1 | warn     | System prompt > 2K tokens                                   | `prompt-weight-checks.md#pw1` |
| PW2 | warn     | Few-shot examples in prompt > 3                             | `prompt-weight-checks.md#pw2` |
| PW3 | warn     | Identical preamble duplicated across agents                 | `prompt-weight-checks.md#pw3` |
| P1  | warn     | Sequential awaits over independent inputs                   | `parallelism-checks.md#p1` |
| P2  | warn     | Sequential agent handoffs that don't share context          | `parallelism-checks.md#p2` |
| P3  | info     | Tool fan-out behind a single tool wrapper                   | `parallelism-checks.md#p3` |
| O1  | critical | No `AddOpenTelemetry` call                                  | `otel-coverage-checks.md#o1` |
| O2  | warn     | No Aspire dashboard reference                               | `otel-coverage-checks.md#o2` |
| O3  | warn     | Token / cost surfacing missing                              | `otel-coverage-checks.md#o3` |
| O4  | info     | Per-agent activity source missing                           | `otel-coverage-checks.md#o4` |
| MA1 | warn     | All agents on the same model                                | `model-assignment-checks.md#ma1` |
| MA2 | warn     | Reasoning-strong model on a deterministic agent             | `model-assignment-checks.md#ma2` |
| MA3 | warn     | Cheap model on a planner / decomposer                       | `model-assignment-checks.md#ma3` |
| MA4 | info     | Hard-coded model id outside config                          | `model-assignment-checks.md#ma4` |

## Cross-skill routes embedded in `ref:` fields

| `ref:` value                      | Skill to run next                |
|-----------------------------------|----------------------------------|
| `skill:select-agent-models`       | Per-agent model recommendations  |
| `skill:setup-maf-evals`           | Wire eval reports + telemetry    |
| `skill:configure-agentic-perf-rules` | Install always-on rules block |

## Notes for skill implementation

- When generating a report, write a copy of the "Reference card" section
  (the table and the prefix/severity legend immediately above it) to
  `.copilot/perf-reports/check-id-glossary.md`. Overwrite each run; the
  content is static within a skill version and does not depend on findings.
- The glossary file is per-repo (one file regardless of how many runs);
  the timestamped `scan-<ts>.md` reports link to it relatively.
- Keep this table in lockstep with the per-category reference files. Adding
  a new check ID in `topology-checks.md` REQUIRES a row here.
