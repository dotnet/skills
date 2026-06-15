# Telemetry capture

How telemetry mode wraps the existing `IChatClient` and writes per-call
records to disk.

## Wrapper

```csharp
public sealed class TelemetryChatClient(IChatClient inner, TelemetrySink sink) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IList<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var resp = await inner.GetResponseAsync(messages, options, ct);
        sw.Stop();

        sink.Record(new TelemetryRecord(
            AgentName: options?.AdditionalProperties?["agent"] as string ?? "unknown",
            Model: options?.ModelId ?? "unknown",
            InputTokens: resp.Usage?.InputTokenCount ?? 0,
            OutputTokens: resp.Usage?.OutputTokenCount ?? 0,
            LatencyMs: sw.ElapsedMilliseconds,
            CostUsd: PriceTable.Cost(options?.ModelId, resp.Usage)));

        return resp;
    }
}
```

Register in DI as a decorator over the real client.

## Report — `telemetry.md`

```markdown
# Telemetry — {{ utc_timestamp }}

Inputs: {{ count }}    Stub mode: {{ true | false }}

| Agent     | Model        | Calls | Avg ms | p95 ms | Avg in tok | Avg out tok | Cost (USD) |
|-----------|--------------|-------|--------|--------|------------|-------------|------------|
| router    | gpt-4o-mini  |    12 |    340 |    520 |        180 |          22 |  $0.00031  |
| planner   | gpt-4o       |     6 |   1240 |   1880 |       1100 |         260 |  $0.00385  |
| worker    | gpt-4o-mini  |    18 |    410 |    640 |        320 |         180 |  $0.00118  |

Total cost: $0.00534
```

## Machine-readable — `telemetry.json`

```json
{
  "timestamp": "2026-06-15T17:00:00Z",
  "stub": false,
  "records": [
    { "agent": "router", "model": "gpt-4o-mini", "input_tokens": 178, "output_tokens": 21, "latency_ms": 332, "cost_usd": 0.0000256 }
  ],
  "aggregate": { "calls": 36, "total_cost_usd": 0.00534 }
}
```

## JUnit-XML — `telemetry.junit.xml`

Standard JUnit suite where each test case is one input id, marked
passed if the run succeeded (no thrown exception). Latency/token
metrics are emitted as `<system-out>` per test case so CI can pick
them up.

## Stub mode

Two independent toggles control whether real models are called:

- `EVAL_USE_REAL_AGENT` (default `0`) — when `0`, the wrapper
  short-circuits the agent-under-test client and returns a deterministic
  canned response. Telemetry numbers reflect the stub, marked `(stub)`.
- `EVAL_USE_REAL_JUDGE` (default `0`) — when `0`, quality mode skips
  the real judge call and emits per-input scores of `null` with a
  rationale of `"(stub) judge disabled"`. The pass-rate row reports
  `(stub)`.

Compare mode honors both toggles independently. Setting only
`EVAL_USE_REAL_AGENT=1` is a valid local-dev configuration: real
agent calls, no judge cost.
