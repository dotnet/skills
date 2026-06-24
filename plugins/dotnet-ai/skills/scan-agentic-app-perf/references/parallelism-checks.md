# Parallelism checks

Detect sequential agent invocations that could run concurrently.

## Checks

### `parallel.independent-handoffs` (warn)

**Detect:** two consecutive `await downstreamA.RunAsync(...)` and
`await downstreamB.RunAsync(...)` calls in the same method where B's
input does not depend on A's output.

**Why:** the second call could start as soon as the inputs are known.
Each sequential LLM hop adds full per-call latency.

**Next:** "Run `<A>` and `<B>` with `Task.WhenAll`. Rejoin in the
parent agent for the consolidation step."

### `parallel.hidden-tool-fanout` (info)

**Detect:** a tool method that internally loops and calls 3+ external
APIs sequentially.

**Why:** tools hide their own latency from the agent. A single slow
tool that is internally serial is the hardest kind of latency to find
from the outside.

**Next:** "Parallelize the inner calls in `<tool-name>`; document the
expected bound in the tool description so the agent can plan around
it."

## What used to live here

`parallel.sequential-awaits` (was `P1`, a generic `foreach (var x in
xs) await ...` pattern) was removed — that's a general .NET concurrency
anti-pattern, not specific to agents. For that class of finding run
`optimizing-dotnet-performance` instead.
