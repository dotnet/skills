---
name: lightweight-telemetry
description: Emit structured metrics from a .NET 11 console app, CLI, or build tool using the built-in System.Diagnostics.Metrics API with no OpenTelemetry, APM, or collector dependency. Use when asked to "report how long it took", "count how many times it ran", expose queue depth or a running total, pick between a counter, gauge, and histogram, split one metric by a tag/dimension, keep the measurement path cheap when nothing is listening, or print readings as JSON lines from a short-lived process. Do not use for apps targeting before net11.0, distributed tracing across services (use configuring-opentelemetry-dotnet), cloud ingestion into Application Insights or Azure Monitor, or shipping log lines to Seq/Elasticsearch.
license: MIT
---

# Lightweight telemetry in .NET 11

A minimal, dependency-free way to expose operational metrics from a CLI or tool.
No OpenTelemetry SDK or external collector is required. These APIs predate .NET 11;
this skill applies them to tools targeting `net11.0`. A listener can write JSON
lines to stdout for a local consumer.

## When to use

- A build tool, CLI, or local agent needs to report timing/counts.
- You want structured telemetry without an APM vendor SDK.
- The host may be resource-constrained (no background collector).

## When not to use

- The project targets an earlier .NET version; these metrics APIs also work
  there, but this skill is for the .NET 11 plugin.
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

Set the unit and description when defining the instrument. Do not rely only on
a `.ms` name suffix to tell consumers which unit the value uses:

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
using var meter = new Meter("MyTool");
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, l) =>
{
    if (ReferenceEquals(instrument.Meter, meter))
        l.EnableMeasurementEvents(instrument);
};
listener.Start(); // before any measurements
// Record counters, histograms, and synchronous gauges after Start().
listener.RecordObservableInstruments(); // only if observable instruments are used
```

Also register `SetMeasurementEventCallback<T>` before calling `Start()` to consume
recorded values. Synchronous counter, histogram, and gauge callbacks run when
the measurement is recorded; they cannot be flushed later. Observable instruments
emit only when a listener calls `RecordObservableInstruments()`. Dispose the
listener before its meter; do not make a listener dispose a caller-owned meter.

## The pattern

For a `net11.0` console project, this complete `Program.cs` prints one JSON line
per measurement. The framework `MeterListener` needs no external package:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

using var meter = new Meter("MyTool", "1.0.0");
var runs = meter.CreateCounter<int>("tool.runs", "{run}", "Number of executions");
var duration = meter.CreateHistogram<double>("tool.step.duration", "ms", "Duration per step");
int queueDepth = 3;
meter.CreateObservableGauge<int>("tool.queue.depth", () => queueDepth, "{item}", "Items queued");

TimeProvider clock = TimeProvider.System;
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, l) =>
{
    if (ReferenceEquals(instrument.Meter, meter))
        l.EnableMeasurementEvents(instrument, clock);
};
listener.SetMeasurementEventCallback<int>(WriteReading);
listener.SetMeasurementEventCallback<double>(WriteReading);
listener.Start(); // start before the first measurement

await RecordStepAsync(clock, duration);
runs.Add(1);
listener.RecordObservableInstruments(); // poll the observable gauge once before exit

static async Task RecordStepAsync(TimeProvider clock, Histogram<double> duration)
{
    long start = clock.GetTimestamp();
    await Task.Delay(TimeSpan.FromMilliseconds(10), clock);
    if (duration.Enabled)
        duration.Record(clock.GetElapsedTime(start).TotalMilliseconds,
                        new TagList { { "step", "compile" } });
}

static void WriteReading<T>(
    Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags,
    object? state) where T : struct
{
    var dimensions = new Dictionary<string, object?>(tags.Length);
    foreach (var tag in tags)
        dimensions[tag.Key] = tag.Value;

    var reading = new
    {
        meter = instrument.Meter.Name,
        instrument = instrument.Name,
        unit = instrument.Unit,
        description = instrument.Description,
        value,
        tags = dimensions,
        timestamp = ((TimeProvider)state!).GetUtcNow().ToString("O")
    };
    Console.WriteLine(JsonSerializer.Serialize(reading));
}
```

`RecordStepAsync` takes a `TimeProvider`, so a test can pass a controlled clock
(for example `FakeTimeProvider`) without changing this method. A bare local
assignment to `TimeProvider.System` is not itself an injection seam.

## Output contract

Each reading is exactly one JSON object on its own line — no banner, no summary
line, nothing else on stdout. A consumer can `tail -f` and parse every line.

```json
{"meter":"MyTool","instrument":"tool.step.duration","unit":"ms","description":"Duration per step","value":58.6,"tags":{"step":"compile"},"timestamp":"2026-08-29T18:32:07+00:00"}
```

Contract, in order:

1. `meter` — the fixed `Meter` name, never per-run or per-environment
2. `instrument` — the stable instrument name
3. `unit` — from the instrument metadata, never only a name suffix
4. `description` — so a scraped reading is self-describing
5. `value` — the measurement
6. `tags` — object of the bounded dimensions
7. `timestamp` — ISO 8601, UTC

Note that a non-standard unit is written in UCUM annotation form: a counter
measures `{run}` or `{item}`, not `runs` or `items`. That is the correct
convention, and it is what a consumer expects to see — do not "fix" it to a
plain noun.

The listener must be constructed and `Start()`ed **before** the first `Record`/
`Add`. When the program uses an observable instrument, call
`RecordObservableInstruments()` before exit. That call polls observables; it
does not flush counters, histograms, or synchronous gauges. Measurements taken
before `Start()` produce no line.

## Verify it works

1. Put the complete program above in a console project that targets `net11.0`.
2. Run `dotnet run -c Release` from that project.
3. Confirm the output has three JSON lines (duration, run count, queue depth),
   with numeric `value` fields, and no extra lines from the application.

If the application prints nothing, check that the listener is started, has
registered callbacks, and has enabled the instruments before recording.

## Notes

- `Meter`/`Counter`/`Histogram` are built into `System.Diagnostics.DiagnosticSource`
  (no extra NuGet package for the API itself).
- For production scraping or OTLP export, use an exporter. This skill only
  handles local consumption without an SDK or collector.
- Keep the meter name stable — it becomes the metric namespace downstream. The
  meter *version* string is safe to bump; the name is not.
- One instrument + a tag beats one instrument per value, but keep tag values
  bounded — unbounded values (ids, paths) create a time series each.
