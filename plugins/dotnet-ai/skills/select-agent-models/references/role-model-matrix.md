# Role → model matrix

Recommendations are model-family-neutral where possible, with concrete
defaults for OpenAI/Foundry. Names below are illustrative; substitute the
deployment the user actually has access to.

## Matrix

| Role        | Recommended (primary) | Acceptable alternatives           | Avoid                          | Rationale (latency / quality / cost)                                                                 |
|-------------|-----------------------|------------------------------------|--------------------------------|------------------------------------------------------------------------------------------------------|
| router      | gpt-4o-mini           | o4-mini, gpt-4.1-mini              | frontier reasoning models      | One-shot classification. Latency dominates; a small fast model is correct. Quality differential is negligible for ≤ 5-way routes. |
| planner     | o4-mini (reasoning)   | gpt-4o, o3-mini                    | gpt-3.5-turbo, gpt-4o-mini     | Plan quality drives N downstream calls. Reasoning model pays back in fewer worker turns.             |
| decomposer  | o4-mini               | gpt-4o, gpt-4.1                    | small chat-only models         | Similar to planner, but typically smaller output. Reasoning-medium is enough.                        |
| worker      | gpt-4o-mini           | gpt-4.1-mini, gpt-4o               | frontier models for bulk work  | Most calls happen here; latency dominates. Bumping every worker to a frontier model is the most common cost mistake. |
| validator   | gpt-4o-mini           | gpt-4.1-mini                       | reasoning models               | Yes/no/score; small input, small output, deterministic. A small model with a tight rubric beats a large one with a fuzzy prompt. |
| formatter   | gpt-4o-mini           | gpt-4.1-mini                       | reasoning models               | Structured-output transformation. Quality plateau is hit quickly.                                    |
| summarizer  | gpt-4o-mini           | gpt-4.1-mini, gpt-4o               | reasoning models for hot loops | Runs every turn (or near it). Latency and cost matter more than peak quality.                        |

## Provider notes

- **Public OpenAI:** model id is the canonical OpenAI name
  (`gpt-4o-mini`, `o4-mini`, etc.). Apply mode writes the id directly.
- **Foundry / Azure OpenAI:** the model id stored in `appsettings.json`
  is the **deployment alias** (e.g. `my-prod-mini`), not the OpenAI
  public id. Apply mode must NOT write a public id like `gpt-4o-mini`
  into an Azure project's `appsettings.json` — that will break at
  runtime. Instead:
  - Recommend the model *family* in the plan (e.g. "use a
    small/fast model for the router").
  - In apply mode, ask the user which deployment alias maps to each
    recommended role, or recommend creating a new deployment.
  - If no suitable deployment exists, the plan's `delta` should be
    `unmapped` and apply mode must abort for that agent.
- **Anthropic / Bedrock:** map "reasoning-strong" → Claude Sonnet,
  "small/fast" → Claude Haiku.
- **Local / Ollama:** map "small/fast" → Llama 3.2 / Phi-3, "reasoning"
  → Llama 3.3 70B or DeepSeek-R1; expect higher latency than hosted
  reasoning models and re-eval quality.

## Router sub-types

The default `router → gpt-4o-mini` recommendation assumes a *simple
classifier*. Promote to a stronger model when **any** of these apply:

- The router generates **tool-call arguments** (not just selects a
  destination).
- The router performs **schema validation** or policy checks on user
  input.
- The router chooses among **more than 5 destinations** with
  overlapping descriptions.
- A misroute is **expensive** (e.g. routes to a long-running workflow).

In those cases, recommend `gpt-4o` / `o4-mini` instead and note the
upgrade in the plan's rationale.

## Planner: `o4-mini` vs `gpt-4o`

Recommend a reasoning model (`o4-mini`, `o3-mini`) when:

- The plan has multi-step dependencies between worker outputs.
- The user task is open-ended and the planner must choose what to do
  before how.

Recommend `gpt-4o` / `gpt-4.1` when:

- Latency dominates (interactive UX with strict p95 budget).
- The plan output is mostly structured (JSON shape known in advance).
- Planning depth is shallow (≤ 3 steps).

## Multi-role agents

When an agent fits more than one role, classify by the **highest-
consequence output downstream agents consume**:

| Combination                       | Classify as                  |
|-----------------------------------|------------------------------|
| router + shallow input validation | router                       |
| router + tool-arg generation      | reasoning router (see above) |
| planner + output formatting       | planner                      |
| validator + scoring               | validator                    |
| worker + summarizer               | worker                       |

If the role is genuinely unclear after applying these rules, mark as
`unclear` in the plan and ask the user to classify before apply mode.

## When to deviate

- **Strict-latency interactive UX (≤ 1.5s p95):** override planner to
  `gpt-4o-mini` and accept a small quality hit. Validate with evals.
- **High-stakes single-shot (e.g. legal summarization):** override
  worker to a frontier model for the critical step only; keep the rest
  on small models.
- **Strict cost budget (≤ $X / 1K turns):** start every role at
  small/fast, then upgrade only the role that fails the eval-quality
  bar.
