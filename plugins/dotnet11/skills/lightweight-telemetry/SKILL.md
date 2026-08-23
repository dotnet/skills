---
name: lightweight-telemetry
description: Emit small, structured telemetry from a .NET 11 console app using System.Diagnostics.Metrics and the new .NET 11 TimeProvider/keyed services ergonomics. Use when a tool or CLI needs to report duration, counts, or build metadata without pulling in a full APM SDK.
license: MIT
---

# Lightweight telemetry in .NET 11

A minimal, dependency-free way to expose operational metrics from a CLI or tool.
No OpenTelemetry SDK, no external collector required — metrics are written to the
console as structured lines and can be scraped or redirected.

## When to use

- A build tool, CLI, or local agent needs to report timing/counts.
- You want structured telemetry without an APM vendor SDK.
- The host may be resource-constrained (no background collector).

## When not to use

- You need distributed tracing across services → use the
  `configuring-opentelemetry-dotnet` skill instead.
- You need cloud ingestion (Application Insights) → use the vendor SDK.

## The pattern

Use `System.Diagnostics.Metrics.Meter` to define a counter and a histogram, drive
time measurement with `TimeProvider.System`, and flush a snapshot on exit.

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

var meter = new Meter("MyTool", "1.0.0");
var runs = meter.CreateCounter<int>("tool.runs", "runs", "Number of executions");
var duration = meter.CreateHistogram<double>("tool.duration.ms", "ms", "Execution duration");

var clock = TimeProvider.System;
var start = clock.GetTimestamp();

// ... work ...

var elapsedMs = clock.GetElapsedTime(start).TotalMilliseconds;
runs.Add(1);
duration.Record(elapsedMs);

// Snapshot is emitted via a console listener (see sample).
```

## Sample (runnable)

See `sample/Program.cs` and `sample/telemetry.csproj`. Build and run:

```bash
dotnet run --project sample
```

It prints one JSON line per metric reading, e.g.:

```json
{"meter":"MyTool","instrument":"tool.duration.ms","value":12.4,"unit":"ms","timestamp":"2026-08-23T..."}
```

## Running the sample inside `ubuntu-termux` (PRoot)

This skill is verified to run inside the glibc Ubuntu 24.04 guest of
[qapdex-maker/ubuntu-termux](https://github.com/qapdex-maker/ubuntu-termux) on an
Android/Termux host — the practical way to execute `net11.0` code on a phone.
PRoot blocks .NET's default ~256 GiB virtual-address reservation, so set:

```bash
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # no libicu in minimal rootfs
export DOTNET_GCHeapHardLimit=134217728          # 128 MiB hard GC limit
ulimit -v 8388608                                # cap virtual memory at 8 GiB
```

Then `dotnet build -c Release` (succeeded with 0 warnings/0 errors) and
`dotnet run -c Release --no-build` produce the designed structured output. See
`docs/LOCAL-DEVELOPMENT.md` for the full walkthrough.

## Notes

- `Meter`/`Counter`/`Histogram` are built into `System.Diagnostics.DiagnosticSource`
  (no extra NuGet package for the API itself).
- For production scraping, attach an `IMetricsListener` or export to OTLP; this
  skill intentionally stays at the smallest useful surface.
- Keep the meter name stable — it becomes the metric namespace downstream.
