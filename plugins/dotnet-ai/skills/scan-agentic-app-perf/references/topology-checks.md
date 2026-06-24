# Topology checks

Detect structural issues in the agent graph that drive latency or runaway loops.

## Checks

### `topology.cycle` (critical)

**Detect:** any directed cycle in the static handoff graph.

**Why:** cycles risk infinite loops if the loop-break condition is
LLM-judged. Even with a turn cap, a cycle burns budget on retries.

**Next:** "Break the cycle `<A>` → `<B>` → `<A>` by making `<B>`'s exit
condition deterministic."

### `topology.deep-single-leaf` (warn)

**Detect:** graph that always ends at one agent but routes through 3+
agents to reach it.

**Why:** the intermediate hops are usually classification or routing
that could be one tool call.

**Next:** "Move the routing logic into a tool on the entry agent and
call `<leaf>` directly."

## Out of scope here

- Tool counts → see `tool-inventory-checks.md`.
- Per-agent model selection → see `model-assignment-checks.md`.

## What used to live here

`topology.agent-count` (was `T1`) and `topology.handoff-fanout` (was
`T2`) were removed in v0.2 — both were taste-based thresholds (>3
agents, >2 handoff edges) that fired on legitimate designs as often
as on real bloat. If you want broad architectural feedback, run
`configure-agentic-perf-rules` so rule #1 (single-agent default) and
rule #2 (handoff justification) can guide the design at scaffold time.
