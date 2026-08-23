using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

var meter = new Meter("MyTool", "1.0.0");
var runs = meter.CreateCounter<int>("tool.runs", "runs", "Number of executions");
var duration = meter.CreateHistogram<double>("tool.duration.ms", "ms", "Execution duration");

// Console listener: emits one structured line per measurement.
var listener = new MetricListener(meter);

var clock = TimeProvider.System;
var start = clock.GetTimestamp();

// Simulate work so the metric has a non-zero value.
await Task.Delay(120);

var elapsedMs = clock.GetElapsedTime(start).TotalMilliseconds;
runs.Add(1);
duration.Record(elapsedMs);

listener.Dispose();
meter.Dispose();

/// <summary>
/// Minimal console telemetry sink. Attaches to a Meter and prints JSON lines.
/// No external dependencies — uses the built-in diagnostic source listener API.
/// </summary>
sealed class MetricListener : IDisposable
{
    private readonly MeterListener _inner = new();
    private readonly Meter _meter;

    public MetricListener(Meter meter)
    {
        _meter = meter;
        _inner.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == _meter.Name)
                _inner.EnableMeasurementEvents(instrument);
        };
        _inner.SetMeasurementEventCallback<double>(OnMeasurement);
        _inner.SetMeasurementEventCallback<int>(OnMeasurement);
        _inner.Start();
    }

    private static void OnMeasurement<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        var line = new
        {
            meter = instrument.Meter.Name,
            instrument = instrument.Name,
            unit = instrument.Unit,
            value = measurement?.ToString(),
            timestamp = DateTimeOffset.UtcNow.ToString("O")
        };
        Console.WriteLine(JsonSerializer.Serialize(line));
    }

    public void Dispose() => _inner.Dispose();
}
