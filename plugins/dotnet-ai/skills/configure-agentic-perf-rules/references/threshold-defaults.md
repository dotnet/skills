# Threshold Defaults

Each numeric threshold in the managed block has a default value and a rationale. These
defaults are starting points, not absolutes — projects with different shapes (e.g. very
simple two-agent workflows, or complex tool-heavy pipelines) should adjust.

| Threshold | Default | Rationale |
|-----------|---------|-----------|
| `agent_count_max` | `3` | Most workflows that need more than 3 agents are better served by tools-on-fewer-agents. Real-world frustration: 5+ specialist workflows with free-form LLM routing dramatically slow per-turn latency and confuse handoff decisions. |
| `llm_routed_edges_max_per_turn` | `2` | Each LLM-routed edge is an extra LLM call. Two routed decisions per user turn (e.g. "router → specialist", "specialist → done") is the practical ceiling before latency becomes user-visible. |
| `per_turn_input_token_warn` | `8000` | Modern reasoning models can take 100K+, but most chat-class models start showing meaningful latency and cost above ~8K input tokens. Projects with retrieval/RAG legitimately exceed this — override locally. |
| `per_turn_output_token_warn` | `2000` | Output tokens are usually 4-10x more expensive than input on a per-token basis. 2000 is a reasonable "are you sure?" threshold; long-form generation tasks should override. |
| `baseline_token_increase_warn_pct` | `20` | A 20% increase per turn meaningfully changes monthly bills at scale. Small tweaks under 20% are noise; over 20% is worth surfacing. |
| `unbounded_history_warn` | `true` | Default-on. Sending full history forever is the single most common token-bloat pattern. Disable only if the workflow has already implemented a windowing/summarization strategy and the warning is now noise. |

## Adjusting thresholds

Users override defaults inside the managed block's `thresholds:` YAML map. The skill
preserves these overrides on update (it merges new defaults underneath, so user values
win for any key they set).

Examples of legitimate overrides:

- A RAG-heavy workflow with retrieval that routinely sends 20K input tokens — set
  `per_turn_input_token_warn: 25000`.
- A long-form drafting tool that generates 5K outputs per turn — set
  `per_turn_output_token_warn: 6000`.
- A workflow that has implemented summarization-at-N-turns — set
  `unbounded_history_warn: false`.
