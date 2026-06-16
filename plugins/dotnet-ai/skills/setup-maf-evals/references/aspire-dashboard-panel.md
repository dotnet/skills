# Aspire dashboard panel (optional, v1 = static file)

A minimal way to surface per-agent token/latency live during
`dotnet run` without extending the Aspire dashboard itself.

## V1 — static file served from AppHost

1. Telemetry mode (when running long-lived) writes
   `wwwroot/eval-panel/telemetry.json` every N calls.
2. AppHost serves a static HTML page at `/eval-panel/` that fetches
   the JSON every 2 seconds and renders a table.

### `wwwroot/eval-panel/index.html`

```html
<!doctype html>
<title>Agentic perf panel</title>
<style>
  body { font: 14px ui-sans-serif, system-ui; padding: 1rem; }
  table { border-collapse: collapse; }
  th, td { border: 1px solid #ccc; padding: 4px 8px; text-align: right; }
  th:first-child, td:first-child { text-align: left; }
</style>
<h1>Agentic perf panel</h1>
<p>Last update: <span id="ts">—</span></p>
<table id="tbl">
  <thead>
    <tr><th>Agent</th><th>Model</th><th>Calls</th><th>Avg ms</th><th>p95 ms</th><th>In tok</th><th>Out tok</th><th>$/1K</th></tr>
  </thead>
  <tbody></tbody>
</table>
<script>
async function tick() {
  try {
    const r = await fetch('telemetry.json?t=' + Date.now());
    const d = await r.json();
    document.getElementById('ts').textContent = d.timestamp;
    const tb = document.querySelector('#tbl tbody');
    tb.innerHTML = '';
    for (const a of d.aggregate_by_agent ?? []) {
      const row = tb.insertRow();
      for (const c of [a.agent, a.model, a.calls, a.avg_ms, a.p95_ms, a.avg_in, a.avg_out, '$' + a.cost_per_1k.toFixed(4)]) {
        row.insertCell().textContent = c;
      }
    }
  } catch (e) { /* ignore */ }
}
setInterval(tick, 2000); tick();
</script>
```

## V1 caveats

- This is **not** an embedded Aspire dashboard panel; the dashboard
  panel API is out of scope here.
- The panel reads only the latest `telemetry.json`; it does not
  retain history across runs.
- If the AppHost project does not already enable static files, the
  skill adds `app.UseStaticFiles()` (in apply mode only, with the
  same diff-preview-and-confirm flow as `select-agent-models` apply
  mode).

## Future v2

A proper Aspire dashboard contribution is tracked separately. The
v1 static panel buys most of the benefit (live per-agent visibility)
without depending on the dashboard extension story.
