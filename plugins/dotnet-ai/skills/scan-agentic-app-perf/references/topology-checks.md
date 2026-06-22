# Topology checks

Detect structural issues in the agent graph that drive latency or runaway loops.

## Checks

### T1. Agent count > 3 (warn) / > 6 (critical)

**Detect:** count distinct `ChatClientAgent` / `AddAgent(...)` registrations
in the AppHost and agent service projects.

**Why:** more agents = more LLM hops per turn. Every additional agent that
can be routed to costs at least one extra round trip.

**Next:** "Collapse `<names>` into a single agent with two tool calls instead
of two agents."

**Ref:** `skill:configure-agentic-perf-rules` if the project has no rules
file installed yet.

### T2. LLM-routed handoff edges per turn > 2 (warn) / > 4 (critical)

**Detect:** edges where the *destination* agent is selected by an LLM (not
deterministic code). Look for `Handoff` builders, `RoutingAgent`, or
`switch`/`if` blocks that select an agent based on a string returned by a
chat completion.

**Why:** LLM-routed edges multiply tail latency. Two LLM hops to pick the
next agent before the work even starts is the most common cause of "why
is my agent so slow".

**Next:** "Replace the LLM router between `<A>` and `<B>` with a deterministic
intent classifier or a tool call on the source agent."

### T3. Cycles in the agent graph (critical)

**Detect:** any directed cycle in the static handoff graph.

**Why:** cycles risk infinite loops if the loop-break condition is
LLM-judged. Even with a turn cap, a cycle burns budget on retries.

**Next:** "Break the cycle `<A>` → `<B>` → `<A>` by making `<B>`'s exit condition
deterministic."

### T4. Single-leaf graph with > 2 hops (warn)

**Detect:** graph that always ends at one agent but routes through 3+
agents to reach it.

**Why:** the intermediate hops are usually classification or routing that
could be one tool call.

**Next:** "Move the routing logic into a tool on the entry agent and call
`<leaf>` directly."

## Out of scope here

- Tool counts → see `tool-inventory-checks.md`.
- Per-agent model selection → see `model-assignment-checks.md`.
