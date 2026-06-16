# Prompt-weight checks

Detect oversized system prompts and per-agent prompt cost.

## Checks

### PW1. System prompt > 2K tokens (warn) / > 4K tokens (critical)

**Detect:** count tokens (or chars / 4 as approximation) in each agent's
`Instructions` or system prompt string.

**Why:** every turn pays this cost. A 4K-token system prompt at $0.005/1K
input tokens × 1000 turns/day = $20/day per agent on prompt overhead alone.

**Next:** "Move static rules into a tool the agent can call when needed,
or split the prompt into a short policy section and a separate few-shot
example doc retrieved on demand."

### PW2. Few-shot examples in prompt > 3 (warn)

**Detect:** count "Example:", "User:", "Assistant:" turn pairs in the
prompt string.

**Why:** few-shot examples scale linearly with token cost. After 3
examples the marginal accuracy gain is usually < 1%.

**Next:** "Keep the 2 strongest examples; move the rest behind a
`getExample(category)` tool."

### PW3. Identical preamble duplicated across agents (warn)

**Detect:** two or more agents share the same > 100-token block at the
start or end of their system prompts.

**Why:** the same tokens are billed N times per turn (once per agent).

**Next:** "Lift the shared block into a single deterministic preprocessor
or attach it as a tool result rather than a system prompt repeat."

## Token estimation

If a real tokenizer is not available, approximate as `chars / 4`. Mark
findings using approximation as `(estimated)` in the evidence.
