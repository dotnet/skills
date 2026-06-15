# Managed Block Template

This is the exact content the skill writes into the target instructions file. Variables
in `<UPPER_SNAKE>` are filled in at install time.

```markdown
<!-- BEGIN: managed by configure-agentic-perf-rules v<SKILL_VERSION> -->

## Agentic Performance Rules

> These rules are managed by the `configure-agentic-perf-rules` skill. Do not edit prose
> inside this managed block — your changes will be overwritten on the next update.
> Numeric defaults can be overridden in the `thresholds` block below; user-edited values
> are preserved across updates.

```yaml
# thresholds — edit values below to override per-project defaults
thresholds:
  agent_count_max: 3
  llm_routed_edges_max_per_turn: 2
  per_turn_input_token_warn: 8000
  per_turn_output_token_warn: 2000
  baseline_token_increase_warn_pct: 20
  unbounded_history_warn: true
```

When working in this codebase, apply each rule below by default. Each rule is in the
form **"Before X, justify Y."** When you cannot justify, prefer the safer alternative
(do not add the agent, do not add the edge, etc.) and surface the trade-off to the user.

### 1. Agent count

Before adding a new agent to a workflow, justify why the new responsibility cannot be a
tool call on an existing agent. Default ceiling: **`agent_count_max`** agents per
workflow. If the workflow already has that many agents, do not add another without
explicit user direction.

### 2. Handoff edges

Before adding an LLM-routed handoff edge (e.g. via
`AgentWorkflowBuilder.CreateHandoffBuilderWith`), justify why a deterministic edge or a
conditional `WorkflowBuilder` branch will not work. Default ceiling:
**`llm_routed_edges_max_per_turn`** LLM-routed edges traversed per user turn.

### 3. Model selection

Before defaulting to a frontier model (e.g. `gpt-4o`), name the agent's role and pick
from the role→model matrix in the `select-agent-models` skill. Routers, classifiers,
and summarizers usually want a smaller/faster model; reasoning steps may want a
reasoning-class model.

### 4. Message-history strategy

Before sending the full conversation history to an agent, state the bound — turn count,
token cap, summarization point, or retrieval strategy. Unbounded full-history sends in
a multi-turn workflow are flagged when **`unbounded_history_warn`** is true.

### 5. Token / cost surfacing

Before implementing a non-trivial change to an agent's prompt, tools, or model, estimate
per-turn token cost. Default warnings:

- More than **`per_turn_input_token_warn`** input tokens projected per turn
- More than **`per_turn_output_token_warn`** output tokens projected per turn
- Any change that adds more than **`baseline_token_increase_warn_pct`**% to a measured
  baseline

When any of these trips, surface the projection to the user before implementing.

### 6. Post-change measurement

After a non-trivial change to a workflow (new agent, new edge, model swap, prompt
rewrite), propose running `setup-maf-evals` (or an existing `.Evals` project) to confirm
the change is net-positive on the metrics that matter — or explicitly note why
measurement is not warranted (e.g. cosmetic refactor with no behavioral change).

<!-- END: managed by configure-agentic-perf-rules -->
```

## Notes for the skill implementation

- The fenced YAML block uses three backticks. When the skill renders this template into
  a markdown file, it must escape or otherwise preserve those backticks correctly.
- The `<SKILL_VERSION>` placeholder is filled in from the skill's own version, embedded
  via the install step. Use semver (e.g. `v0.1.0`).
- The threshold frontmatter is intentionally inside the managed block (not top-of-file
  YAML) so it does not interfere with any other YAML frontmatter the project may have.
- Update mode preserves user-edited threshold values by parsing the existing block's
  `thresholds:` map, then merging onto the new defaults map (user values win).
