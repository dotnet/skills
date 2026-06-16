# OTel coverage checks

Detect missing instrumentation that makes perf invisible at dev time.

## Checks

### O1. No `AddOpenTelemetry` call (critical)

**Detect:** the AppHost or service projects do not call
`builder.Services.AddOpenTelemetry()` and do not import
`OpenTelemetry.Exporter.OpenTelemetryProtocol` or
`Aspire.Hosting.Dashboard`.

**Why:** without OTel, you cannot see per-call latency, token counts, or
spans. Every other check in this audit becomes a guess.

**Next:** "Add `builder.AddServiceDefaults()` (Aspire) or wire OTel
manually with HTTP + Activity sources for `Microsoft.Extensions.AI`."

### O2. No Aspire dashboard reference (warn)

**Detect:** the AppHost does not declare the dashboard, or the
`appsettings.json` lacks a `Dashboard:OtlpEndpointUrl`.

**Why:** the dashboard is the cheapest way to see per-agent token use
during local dev.

**Next:** "Run with `dotnet run --project <AppHost>` and ensure the
dashboard URL is logged. If not, install `Aspire.Hosting.Dashboard`."

### O3. Token / cost surfacing missing (warn)

**Detect:** no log, no meter, no tag for `gen_ai.usage.input_tokens` /
`gen_ai.usage.output_tokens` anywhere in the codebase.

**Why:** without token telemetry the team has no early-warning signal
for prompt bloat. Cost spikes are discovered in the bill, not the
dashboard.

**Next:** "Microsoft.Extensions.AI emits `gen_ai.*` activity tags
automatically. Confirm the OTel exporter forwards them, or run
`setup-maf-evals` to capture them in eval reports."

**Ref:** `skill:setup-maf-evals`

### O4. Per-agent activity source missing (info)

**Detect:** all agents share a single activity source name; no way to
filter the dashboard by agent.

**Why:** with 3+ agents, traces become unreadable without filtering.

**Next:** "Give each agent its own `ActivitySource` named after the
agent role."
