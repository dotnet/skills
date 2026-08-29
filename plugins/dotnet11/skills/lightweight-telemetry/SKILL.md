---
name: lightweight-telemetry
description: Emit structured metrics from a .NET 11 console app, CLI, or build tool using the built-in System.Diagnostics.Metrics API with no OpenTelemetry, APM, or collector dependency. Use when asked to "report how long it took", "count how many times it ran", expose queue depth or a running total, pick between a counter, gauge, and histogram, split one metric by a tag/dimension, keep the measurement path cheap when nothing is listening, or print readings as JSON lines from a short-lived process. Do not use for distributed tracing across services (use configuring-opentelemetry-dotnet), cloud ingestion into Application Insights or Azure Monitor, or shipping log lines to Seq/Elasticsearch.
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

## Pick the instrument first

The instrument type is the decision that is most often wrong, and it is not
recoverable downstream — a consumer cannot turn a gauge back into a rate.

| The value is | Use | Never use | Because |
|---|---|---|---|
| A total that only grows (bytes processed, runs) | `CreateCounter<T>` | a gauge | the consumer derives the rate from the increasing total; a gauge that resets destroys it |
| The value right now (queue depth, open handles) | `CreateGauge<T>` | a counter | a cumulative sum misrepresents a level that goes down again |
| A per-operation duration or size you want percentiles for | `CreateHistogram<T>` | a counter | summing durations loses the distribution |
| A level you can only sample when asked | `CreateObservableGauge<T>` | recording in a hot loop | the callback runs at collection time |

Always pass the unit and description — put the unit in the **metadata**, not only
in a `.ms` name suffix, or a consumer cannot tell seconds from milliseconds:

```csharp
meter.CreateHistogram<double>("tool.step.duration", "ms", "Duration per build step");
```

## Split a metric by a dimension, not by name

One instrument plus a tag, never one instrument per value:

```csharp
stepDuration.Record(elapsedMs, new TagList { { "step", "restore" } });
```

Tag **values** must come from a bounded set (step names, status codes). Never tag
with a user id, path, or timestamp — each distinct value is a separate time
series downstream.

## Keep the hot path cheap

`Record`/`Add` are cheap, but building the tags and formatting values is not.
Guard the expensive part when nothing is collecting:

```csharp
if (stepDuration.Enabled)          // false when no listener is attached
    stepDuration.Record(elapsedMs, new TagList { { "step", step } });
```

Use `TagList` (a struct) rather than allocating a `KeyValuePair[]` per iteration.

## Lifetime: set up the listener before the first measurement

A `MeterListener` only sees measurements recorded **after** `Start()`. In a
short-lived CLI this is the difference between output and silence:

```csharp
var listener = BuildListener(meter);   // Start() called inside
// ... all recording happens after this point ...
listener.RecordObservableInstruments(); // pull observable gauges once before exit
listener.Dispose();
meter.Dispose();
```

Verified on .NET 10 (`System.Diagnostics.Metrics` is unchanged for these APIs on
net11.0): a measurement recorded before `listener.Start()` produces **no** output
line, one recorded after it produces exactly one. Observable instruments emit
nothing at all unless `RecordObservableInstruments()` is called, so a process that
exits without it reports nothing for them.

## The pattern

Use `System.Diagnostics.Metrics.Meter` to define a counter and a histogram, drive
time measurement with `TimeProvider.System`, and flush a snapshot on exit.

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

var meter = new Meter("MyTool", "1.0.0");                       // stable name = metric identity
var runs = meter.CreateCounter<int>("tool.runs", "{run}", "Number of executions");
var duration = meter.CreateHistogram<double>("tool.step.duration", "ms", "Duration per step");

using var listener = new MetricListener(meter);                 // BEFORE the first measurement

var clock = TimeProvider.System;                                // injectable, testable clock
var start = clock.GetTimestamp();

// ... work ...

if (duration.Enabled)                                           // skip tag building when idle
    duration.Record(clock.GetElapsedTime(start).TotalMilliseconds,
                    new TagList { { "step", "compile" } });
runs.Add(1);

listener.Flush();                                               // pull observables before exit
```

Substitute a test `TimeProvider` (e.g. `Microsoft.Extensions.Time.Testing.FakeTimeProvider`)
to assert on recorded durations without sleeping.

## Sample (runnable)

See `sample/Program.cs` and `sample/telemetry.csproj`. Build and run:

```bash
dotnet run --project sample
```

It prints one JSON line per metric reading, e.g.:

```json
{"meter":"MyTool","instrument":"tool.step.duration","unit":"ms","description":"Duration per step","value":58.6,"tags":{"step":"compile"},"timestamp":"2026-08-29T18:32:07+00:00"}
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
- Keep the meter name stable — it becomes the metric namespace downstream. The
  meter *version* string is safe to bump; the name is not.
- One instrument + a tag beats one instrument per value, but keep tag values
  bounded — unbounded values (ids, paths) create a time series each.
