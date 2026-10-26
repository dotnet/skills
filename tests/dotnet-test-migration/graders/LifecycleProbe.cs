using System;
using System.IO;
using System.Text.Json;
using System.Threading;

internal sealed class LifecycleProbe : IDisposable
{
    private readonly string instance = Guid.NewGuid().ToString("N");
    private readonly object gate = new();
    private string? active;

    public LifecycleProbe() => Record("created", "");

    public void Enter(string name)
    {
        lock (gate)
        {
            if (active is not null)
                throw new InvalidOperationException("Shared fixture used concurrently.");
            active = name;
            Record("entered", name);
        }
        Thread.Yield();
    }

    public void Exit(string name)
    {
        lock (gate)
        {
            if (active != name)
                throw new InvalidOperationException("Fixture ownership changed.");
            Record("exited", name);
            active = null;
        }
    }

    public void Dispose()
    {
        if (active is not null)
            throw new InvalidOperationException("Fixture disposed while in use.");
        Record("disposed", "");
    }

    private void Record(string kind, string name)
    {
        var path = Environment.GetEnvironmentVariable("MIGRATION_PROBE");
        if (!string.IsNullOrEmpty(path))
            File.AppendAllText(path, JsonSerializer.Serialize(new { @event = kind, @case = name, instance }) + Environment.NewLine);
    }
}
