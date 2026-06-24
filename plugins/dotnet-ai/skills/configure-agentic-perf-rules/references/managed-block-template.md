# Managed Block Template

This is the exact content the skill writes into the target instructions file. Variables
in `<UPPER_SNAKE>` are filled in at install time. The outer fence below is **four
backticks** so the inner three-backtick fences in the rendered Markdown are not
interpreted as closing it.

````markdown
<!-- BEGIN: managed by configure-agentic-perf-rules v<SKILL_VERSION> -->

## Agentic Performance Rules

> These rules are managed by the `configure-agentic-perf-rules` skill. Do not edit prose
> inside this managed block — your changes will be overwritten on the next update.
> Numeric defaults can be overridden in the `thresholds` block below; user-edited values
> are preserved across updates.

```yaml
# thresholds — edit values below to override per-project defaults
thresholds:
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
tool call on an existing agent. Each additional agent multiplies the routing surface and
inflates per-turn token cost (system prompts + tool descriptions are paid per agent).
**There is no hard ceiling** — the answer "this needs a clearly different system prompt,
toolset, or output style" is a valid justification. If you cannot articulate one, prefer
adding a tool to an existing agent.

### 2. Handoff edges

Before adding an LLM-routed handoff edge (e.g. via
`AgentWorkflowBuilder.CreateHandoffBuilderWith`), justify why a deterministic edge or a
conditional `WorkflowBuilder` branch will not work. Every LLM-routed edge is an extra LLM
call before the user gets a response. Deterministic routing is faster and cheaper; reserve
LLM routing for decisions that genuinely require reading user intent.

### 3. Model selection

Before defaulting to a frontier model like `gpt-4o`, name the agent's role and pick
from the table below. Routers, validators, formatters, and workers almost never need
a frontier model; defaulting to one is the largest single source of unnecessary spend.

| Role                                | Pick                                                                              |
|-------------------------------------|-----------------------------------------------------------------------------------|
| router / validator / formatter      | small-fast model (e.g. `gpt-4o-mini` or current cheap-fast in your Foundry catalog) |
| worker / summarizer / extraction    | small-fast model, **or** Foundry `model-router` deployment if prompt length varies |
| planner / decomposer / open reasoning | reasoning-class model (e.g. `o4-mini` or current reasoning model) — state *why* in a code comment |
| creative / nuanced generation       | frontier (e.g. `gpt-4o`) — state *why* in a code comment                          |

If unsure which role applies, **stop and ask the user** — do not default to `gpt-4o`.
Specific model ids age fast; check your Foundry catalog for the current cheap-fast,
reasoning-class, and frontier ids before pinning.

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
````

## Notes for the skill implementation

- The outer fence in this file is **four backticks** so the inner three-backtick YAML
  fence in the rendered output is preserved verbatim. When transcribing the template,
  agents must reproduce the inner three-backtick fences exactly.
- The `<SKILL_VERSION>` placeholder is filled from the `version:` field in
  `SKILL.md`'s frontmatter. Render as `v0.1.0` (lowercase `v`, semver triple).
- The threshold frontmatter is intentionally inside the managed block (not top-of-file
  YAML) so it does not interfere with any other YAML frontmatter the project may have.
- Update mode preserves user-edited threshold values per the algorithm in `SKILL.md`
  step 2 ("Threshold preservation algorithm"). Do not duplicate that logic here.
