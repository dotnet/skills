# Tool inventory checks

Detect bloat and redundancy in the per-agent tool list.

## Checks

### TI1. Tools per agent > 8 (warn) / > 15 (critical)

**Detect:** count tools registered on each agent (`AIFunctionFactory.Create`,
`[Description]`-attributed methods passed to `tools:`, MCP tool imports).

**Why:** tool descriptions are sent in the system prompt every turn. 15
tools at ~80 tokens each is 1.2K tokens of overhead before the user message.

**Next:** "Split <agent> into two agents by domain, or move rarely-used
tools behind a single 'lookup' tool that takes a category argument."

### TI2. Duplicate tool functionality across agents (warn)

**Detect:** two or more tools across different agents with the same
description or near-identical signatures.

**Why:** duplication forces the router LLM to disambiguate every turn and
inflates aggregate prompt size.

**Next:** "Consolidate <tool-A> and <tool-B> into a single shared tool
exposed by both agents."

### TI3. Dead tools (info)

**Detect:** tools registered but never invoked in any code path or call
trace. Static check: tool name does not appear in any agent's system
prompt or instructions.

**Why:** every registered tool costs prompt tokens whether it gets called
or not.

**Next:** "Remove <tool-name> from <agent>'s tool list."

### TI4. Tool description > 200 chars (warn)

**Detect:** a tool's `[Description]` attribute or `description:` field is
longer than 200 characters.

**Why:** long descriptions multiply across agents that import the tool.
Most tools can be described in one sentence.

**Next:** "Trim <tool>'s description from <N> chars to ≤ 200; move the
detailed contract into XML docs on the parameters."
