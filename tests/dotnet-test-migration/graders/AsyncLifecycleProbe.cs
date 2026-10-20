using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

internal sealed class AsyncLifecycleProbe
{
    private readonly string instance = Guid.NewGuid().ToString("N");
    private readonly object gate = new();
    private bool initializing;
    private bool initialized;
    private bool disposing;
    private string? active;

    public AsyncLifecycleProbe() => Record("created", "");

    public async Task InitializeAsync()
    {
        lock (gate)
        {
            if (initializing || initialized || disposing)
                throw new InvalidOperationException("Async fixture initialized more than once.");
            initializing = true;
            Record("initializing", "");
        }
        await Task.Delay(75);
        lock (gate)
        {
            initialized = true;
            Record("initialized", "");
        }
    }

    public void Enter(string name)
    {
        lock (gate)
        {
            if (!initialized || disposing)
                throw new InvalidOperationException("Async fixture was not ready or was disposed early.");
            if (active is not null)
                throw new InvalidOperationException("Async shared fixture used concurrently.");
            active = name;
            Record("entered", name);
        }
    }

    public void Exit(string name)
    {
        lock (gate)
        {
            if (active != name || disposing)
                throw new InvalidOperationException("Async fixture ownership changed.");
            Record("exited", name);
            active = null;
        }
    }

    public async Task DisposeAsync()
    {
        lock (gate)
        {
            if (!initialized || disposing || active is not null)
                throw new InvalidOperationException("Async fixture cleanup was premature or repeated.");
            disposing = true;
            Record("disposing", "");
        }
        await Task.Delay(75);
        lock (gate)
            Record("disposed", "");
    }

    private void Record(string kind, string name)
    {
        var path = Environment.GetEnvironmentVariable("MIGRATION_PROBE");
        if (!string.IsNullOrEmpty(path))
            File.AppendAllText(path, JsonSerializer.Serialize(
                new { @event = kind, @case = name, instance }) + Environment.NewLine);
    }
}
