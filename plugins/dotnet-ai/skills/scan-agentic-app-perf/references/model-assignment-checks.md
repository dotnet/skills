# Model-assignment checks

Detect single-model defaulting and role-model mismatch.

## Checks

### MA1. All agents on the same model (warn)

**Detect:** every agent is constructed with the same model id (e.g.
`gpt-4o-mini`). Look at the AppHost connection string or the
`IChatClient` builder per agent service.

**Why:** different agent roles have different latency and quality
needs. A single default usually overspends on cheap roles and
underspends on hard roles.

**Next:** "See rule #3 in `.github/copilot-instructions.md` (managed by
`configure-agentic-perf-rules`) — most apps want a small-fast model
for routers/validators/workers and a reasoning-class model only for
planners. If both agents are workers on the same cheap model, this is
expected and can be downgraded to info."

**Ref:** `skill:configure-agentic-perf-rules`

### MA2. Reasoning-strong model on a deterministic agent (warn)

**Detect:** an agent whose prompt and tool list indicate a
deterministic role (formatter, validator, classifier with ≤3 outputs)
is using a frontier reasoning model.

**Why:** the marginal quality is near-zero; you are paying for unused
capability and per-call latency.

**Next:** "Downgrade `<agent>` to a small-fast model per rule #3 in
`.github/copilot-instructions.md`. Validate via `setup-maf-evals`
quality mode."

**Ref:** `skill:configure-agentic-perf-rules`

### MA3. Cheap model on a planner / decomposer (warn)

**Detect:** the agent that decides the plan or decomposes the task is
on a small model while leaf workers are on a large one.

**Why:** plan-quality drives every downstream call. A bad plan from a
cheap planner makes the expensive workers run more turns.

**Next:** "Promote the planner to a reasoning-class model per rule #3
in `.github/copilot-instructions.md`; consider demoting one or more
workers."

**Ref:** `skill:configure-agentic-perf-rules`

### MA4. Hard-coded model id outside config (info)

**Detect:** model id literal (e.g. `"gpt-4o-mini"`) appears inside an
agent service `.cs` file rather than `appsettings.json` or AppHost
parameters.

**Why:** swapping models for an A/B becomes a code change.

**Next:** "Move model ids into `appsettings.json` and bind them via
`IOptions<...>`."
