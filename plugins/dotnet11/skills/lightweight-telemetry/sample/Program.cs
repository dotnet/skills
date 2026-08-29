using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

var meter = new Meter("MyTool", "1.0.0");

// Counter: a total that only grows — the consumer derives a rate from it.
var runs = meter.CreateCounter<int>("tool.runs", "{run}", "Number of executions");
// Histogram: per-operation duration, so percentiles stay available.
var duration = meter.CreateHistogram<double>("tool.step.duration", "ms", "Duration per step");
// Gauge: the value right now, not a cumulative sum.
var queueDepth = meter.CreateGauge<int>("tool.queue.depth", "{item}", "Items currently queued");

// The listener must start BEFORE the first measurement — anything recorded
// earlier is never observed.
using var listener = new MetricListener(meter);

var clock = TimeProvider.System;

foreach (var step in new[] { "restore", "compile" })
{
    var start = clock.GetTimestamp();
    await Task.Delay(60); // stand-in for real work

    // Cheap path: only assemble tags when something is actually collecting.
    if (duration.Enabled)
    {
        var elapsedMs = clock.GetElapsedTime(start).TotalMilliseconds;
        duration.Record(elapsedMs, new TagList { { "step", step } });
    }
}

runs.Add(1);
queueDepth.Record(3);

// Pull observable instruments once so nothing is lost at exit.
listener.Flush();

/// <summary>
/// Minimal console telemetry sink. Attaches to a Meter and prints JSON lines.
/// No external dependencies — uses the built-in metrics listener API.
/// </summary>
sealed class MetricListener : IDisposable
{
    private readonly MeterListener _inner = new();
    private readonly Meter _meter;

    public MetricListener(Meter meter)
    {
        _meter = meter;
        _inner.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == _meter.Name)
                l.EnableMeasurementEvents(instrument);
        };
        _inner.SetMeasurementEventCallback<double>(OnMeasurement);
        _inner.SetMeasurementEventCallback<int>(OnMeasurement);
        _inner.SetMeasurementEventCallback<long>(OnMeasurement);
        _inner.Start();
    }

    /// <summary>Records observable instruments once, e.g. just before exit.</summary>
    public void Flush() => _inner.RecordObservableInstruments();

    private static void OnMeasurement<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
        where T : struct
    {
        var dimensions = new Dictionary<string, object?>(tags.Length);
        foreach (var tag in tags)
            dimensions[tag.Key] = tag.Value;

        var line = new
        {
            meter = instrument.Meter.Name,
            instrument = instrument.Name,
            unit = instrument.Unit,
            description = instrument.Description,
            value = measurement,
            tags = dimensions,
            timestamp = DateTimeOffset.UtcNow.ToString("O")
        };
        Console.WriteLine(JsonSerializer.Serialize(line));
    }

    public void Dispose()
    {
        _inner.Dispose();
        _meter.Dispose();
    }
}
