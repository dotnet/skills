# Tool inventory checks

Detect bloat and redundancy in the per-agent tool list.

## Checks

### `tools.duplicate` (warn)

**Detect:** two or more tools across different agents with the same
description or near-identical signatures.

**Why:** duplication forces the router LLM to disambiguate every turn
and inflates aggregate prompt size.

**Next:** "Consolidate `<tool-A>` and `<tool-B>` into a single shared
tool exposed by both agents."

### `tools.dead` (info)

**Detect:** tools registered but never invoked in any code path or
call trace. Static check: tool name does not appear in any agent's
system prompt or instructions.

**Why:** every registered tool costs prompt tokens whether it gets
called or not.

**Next:** "Remove `<tool-name>` from `<agent>`'s tool list."

### `tools.description-too-long` (warn)

**Detect:** a tool's `[Description]` attribute or `description:` field
is longer than 200 characters. Measure after string concatenation —
multi-line `+` concatenations count as one description.

**Why:** long descriptions multiply across agents that import the
tool. Most tools can be described in one sentence.

**Next:** "Trim `<tool>`'s description from `<N>` chars to ≤ 200; move
the detailed contract into XML docs on the parameters."

## What used to live here

`tools.too-many-per-agent` (was `TI1`) was a taste-based threshold
(>8 tools per agent) that fired on legitimate designs. The real
question — "is this tool earning its prompt-token weight" — is better
served by `tools.dead` and `tools.duplicate`.
