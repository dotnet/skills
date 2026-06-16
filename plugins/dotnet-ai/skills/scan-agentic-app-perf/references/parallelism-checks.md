# Parallelism checks

Detect sequential awaits that could run concurrently.

## Checks

### P1. Sequential awaits over independent inputs (warn)

**Detect:** a `foreach` / `for` loop that awaits an LLM call or tool call
each iteration where the iteration values are independent of each other.

**Pattern:**

```csharp
foreach (var item in items)
{
    results.Add(await agent.RunAsync(item));   // sequential
}
```

**Why:** N items × per-call latency. With N=5 and 2s/call, that's 10s
that could be 2s under `Task.WhenAll`.

**Next:** "Replace the loop with `await Task.WhenAll(items.Select(i =>
agent.RunAsync(i)))`. Watch for shared mutable state inside the agent."

### P2. Sequential agent handoffs that don't share context (warn)

**Detect:** two consecutive `await downstreamA.RunAsync(...)` and
`await downstreamB.RunAsync(...)` calls in the same method where B's
input does not depend on A's output.

**Why:** the second call could start as soon as the inputs are known.

**Next:** "Run <A> and <B> with `Task.WhenAll`. Rejoin in the parent
agent for the consolidation step."

### P3. Tool fan-out behind a single tool wrapper (info)

**Detect:** a tool method that internally loops and calls 3+ external
APIs sequentially.

**Why:** tools hide their own latency from the agent. A single slow tool
that is internally serial is the hardest kind of latency to find from
the outside.

**Next:** "Parallelize the inner calls in <tool-name>; document the
expected bound in the tool description so the agent can plan around it."
