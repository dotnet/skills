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
var listener = new MeterListener();    // see "The pattern" for the full wiring
listener.Start();                      // BEFORE any Record/Add
// ... all recording happens after this point ...
listener.RecordObservableInstruments(); // pull observable gauges once before exit
listener.Dispose();
meter.Dispose();
```

Verified against the pinned preview SDK (`11.0.100-preview.3.26207.106`,
`net11.0`): a measurement recorded before `listener.Start()` produces **no**
output line, one recorded after it produces exactly one. Observable instruments
emit nothing at all unless `RecordObservableInstruments()` is called, so a
process that exits without it reports nothing for them.

## The pattern

Use `System.Diagnostics.Metrics.Meter` to define a counter and a histogram, drive
time measurement with `TimeProvider.System`, and pull the observables on exit.

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

var meter = new Meter("MyTool", "1.0.0");                       // stable name = metric identity
var runs = meter.CreateCounter<int>("tool.runs", "{run}", "Number of executions");
var duration = meter.CreateHistogram<double>("tool.step.duration", "ms", "Duration per step");

// One JSON object per reading, on its own line (the output contract below).
static void WriteReading(Instrument inst, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
{
    var tagsObj = tags.ToArray().ToDictionary(t => t.Key, t => t.Value?.ToString() ?? "");
    Console.WriteLine(JsonSerializer.Serialize(new {
        meter = inst.Meter.Name, instrument = inst.Name,
        unit = inst.Unit, description = inst.Description,
        value, tags = tagsObj, timestamp = DateTimeOffset.UtcNow,
    }));
}

// Wire a listener BEFORE the first measurement, or the readings are lost.
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, l) => {
    if (instrument.Meter.Name == meter.Name) l.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<int>((inst, value, tags, state) =>
    WriteReading(inst, value, tags));
listener.SetMeasurementEventCallback<double>((inst, value, tags, state) =>
    WriteReading(inst, value, tags));
listener.Start();                                               // measurements before this are dropped

var clock = TimeProvider.System;                                // injectable, testable clock
var start = clock.GetTimestamp();

// ... work ...

if (duration.Enabled)                                           // skip tag building when idle
    duration.Record(clock.GetElapsedTime(start).TotalMilliseconds,
                    new TagList { { "step", "compile" } });
runs.Add(1);

listener.RecordObservableInstruments();                          // pull observables before exit
```

`InstrumentPublished` + `EnableMeasurementEvents` is what opts each instrument in;
without it the callback is never invoked and the process prints nothing (measured,
not assumed). `MeterListener` has no `Flush` — the pull method is
`RecordObservableInstruments()`, and it only matters for observable instruments.

Substitute a test `TimeProvider` (e.g. `Microsoft.Extensions.Time.Testing.FakeTimeProvider`)
to assert on recorded durations without sleeping.

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
`Add`, and `RecordObservableInstruments()` must be called before exit. A
measurement taken before `Start()` produces no line at all — in a short-lived
CLI that is the whole difference between telemetry and silence.

## Verify it works

```bash
dotnet build -c Release          # must succeed with 0 errors
dotnet run -c Release --no-build # one JSON line per measurement
```

If `run` prints nothing, the listener ordering above is the first thing to
check — not the instrument definitions.

## Notes

- `Meter`/`Counter`/`Histogram` are built into `System.Diagnostics.DiagnosticSource`
  (no extra NuGet package for the API itself).
- For production scraping, attach a listener (`MeterListener` from
  `System.Diagnostics.Metrics`, or `IMetricsListener` from
  `Microsoft.Extensions.Diagnostics.Abstractions` when you already use metrics
  configuration/DI) or export to OTLP; this skill intentionally stays at the
  smallest useful surface.
- Keep the meter name stable — it becomes the metric namespace downstream. The
  meter *version* string is safe to bump; the name is not.
- One instrument + a tag beats one instrument per value, but keep tag values
  bounded — unbounded values (ids, paths) create a time series each.
