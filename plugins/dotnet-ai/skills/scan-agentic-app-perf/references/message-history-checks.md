# Message-history checks

Detect strategies that pass too much history to too many agents.

## Checks

### `history.full-share` (critical)

**Detect:** code that passes the entire `IList<ChatMessage>` (or
`History`) from the entry agent to a downstream agent without
filtering, slicing, or summarizing.

**Why:** every additional agent that sees the full history pays the
input token cost. With 4 agents and a 6K-token history, you spend 24K
input tokens per turn doing nothing.

**Next:** "Pass only the last user message and a one-paragraph summary
to `<downstream>`. Use `IChatHistoryReducer` or a manual slice."

### `history.unbounded` (warn)

**Detect:** no usage of `MaxMessages`, `IChatHistoryReducer`,
summarization tool, or sliding-window code anywhere in agent setup.

**Why:** unbounded history = monotonically growing per-turn cost.

**Next:** "Wire a `ChatHistoryReducer` with `MaxMessages = 20` or
summarize-and-replace at the agent level."

### `history.through-deterministic` (warn)

**Detect:** an agent whose role is purely deterministic (formatter,
validator, tool router) is given full chat history.

**Why:** deterministic steps do not need conversational context. Their
prompt cost should be near-constant.

**Next:** "Pass only the immediate input artifact to `<agent>`; drop
the chat history."
