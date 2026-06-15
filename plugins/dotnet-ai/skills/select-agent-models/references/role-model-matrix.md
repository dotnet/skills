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

- **Foundry / Azure OpenAI:** model id depends on your deployment name,
  not the OpenAI public id. Use the deployment alias in
  `appsettings.json`.
- **Anthropic / Bedrock:** map "reasoning-strong" → Claude Sonnet,
  "small/fast" → Claude Haiku.
- **Local / Ollama:** map "small/fast" → Llama 3.2 / Phi-3, "reasoning"
  → Llama 3.3 70B or DeepSeek-R1; expect higher latency than hosted
  reasoning models and re-eval quality.

## When to deviate

- **Strict-latency interactive UX (≤ 1.5s p95):** override planner to
  `gpt-4o-mini` and accept a small quality hit. Validate with evals.
- **High-stakes single-shot (e.g. legal summarization):** override
  worker to a frontier model for the critical step only; keep the rest
  on small models.
- **Strict cost budget (≤ $X / 1K turns):** start every role at
  small/fast, then upgrade only the role that fails the eval-quality
  bar.
