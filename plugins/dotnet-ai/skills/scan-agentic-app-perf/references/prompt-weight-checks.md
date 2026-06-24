# Prompt-weight checks

Detect oversized system prompts and per-agent prompt cost.

## Checks

### `prompt.oversized` (warn at >2K tokens / critical at >4K)

**Detect:** count tokens (or chars / 4 as approximation) in each
agent's `Instructions` or system prompt string.

**Why:** every turn pays this cost. A 4K-token system prompt at
$0.005/1K input tokens × 1000 turns/day = $20/day per agent on prompt
overhead alone.

**Next:** "Move static rules into a tool the agent can call when
needed, or split the prompt into a short policy section and a separate
few-shot example doc retrieved on demand."

### `prompt.duplicate-preamble` (warn)

**Detect:** two or more agents share the same > 100-token block at the
start or end of their system prompts.

**Why:** the same tokens are billed N times per turn (once per agent).

**Next:** "Lift the shared block into a single deterministic
preprocessor or attach it as a tool result rather than a system prompt
repeat."

## Token estimation

If a real tokenizer is not available, approximate as `chars / 4`. Mark
findings using approximation as `(estimated)` in the evidence.

## What used to live here

`prompt.too-many-fewshots` (was `PW2`, fired at >3 few-shot examples)
was a taste-based threshold. Few-shot count alone is not a reliable
signal; `prompt.oversized` already captures the cost dimension.
