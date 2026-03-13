# Async Programming Rules

Blazor's sync context guarantees single-threaded component execution. All rules below follow from this.

## Await every Task

`await` every `Task` by default — discarded tasks silently lose exceptions. The only exception: fire-and-forget where the called method wraps its body in `try/catch` and routes errors via `DispatchExceptionAsync` (see Fire-and-Forget section below).

```csharp
// DO
private async Task LoadData()
{
    items = await Http.GetFromJsonAsync<List<Item>>("api/items");
}

// DON'T — fire-and-forget hides exceptions
private void LoadData()
{
    _ = Http.GetFromJsonAsync<List<Item>>("api/items");
}
```

## Forbidden Primitives

These deadlock or escape the sync context. Never use in components:

| Forbidden | Why |
|-----------|-----|
| `Thread.Start` / `new Thread` | Escapes sync context |
| `Task.Run` | Offloads to thread-pool; `StateHasChanged` throws |
| `.Result` / `.Wait()` | Deadlocks sync context |
| `Task.ContinueWith` | Continuation runs outside sync context |
| `Channel<T>`, `BlockingCollection<T>`, concurrent collections | Unnecessary — single-threaded access guaranteed |

```csharp
// DON'T — Task.Run escapes sync context
_ = Task.Run(async () => {
    var result = await OrderService.SubmitAsync(order);
    StateHasChanged(); // InvalidOperationException!
});

// DO — stay on sync context
private async Task ProcessOrder()
{
    var result = await OrderService.SubmitAsync(order);
    message = result.Message;
}
```

## StateHasChanged

Framework auto-renders after lifecycle methods and event handlers complete. Don't call `StateHasChanged` routinely.

**Call only for:**

1. **Intermediate updates** between multiple awaits:
```csharp
private async Task ProcessSteps()
{
    status = "Step 1...";
    await Step1Async();
    status = "Step 2...";
    StateHasChanged(); // intermediate update
    await Step2Async();
}
```

2. **External events** (timer, C# event, WebSocket) via `InvokeAsync`:
```csharp
private async void OnExternalEvent(object? sender, EventArgs e)
{
    try
    {
        await InvokeAsync(() => { count++; StateHasChanged(); });
    }
    catch (Exception ex)
    {
        await DispatchExceptionAsync(ex);
    }
}
```

`InvokeAsync` marshals onto the sync context. `StateHasChanged` from a raw thread throws `InvalidOperationException`. Use `async void` for external event handlers — it's the only place `async void` is appropriate in Blazor. Always `await InvokeAsync` and route errors via `DispatchExceptionAsync`.

## Fire-and-Forget

Route errors via `DispatchExceptionAsync` (activates error boundaries, logs like lifecycle exceptions):

```csharp
private void SendReport() => _ = SendReportCore();

private async Task SendReportCore()
{
    try { await ReportSender.SendAsync(); }
    catch (Exception ex) { await DispatchExceptionAsync(ex); }
}
```

## Alternatives to Forbidden Primitives

**Instead of `Task.Run`** — use `await` directly or `Task.Yield`:

```csharp
// Yield to let renderer paint, then continue on sync context
private async Task StartLongOperation()
{
    status = "Starting...";
    await Task.Yield();
    await LongOperationService.RunAsync();
    status = "Done!";
}
```

**Chunked CPU work** — break with `Task.Yield` so UI stays responsive:

```csharp
private async Task ProcessLargeList()
{
    for (var i = 0; i < items.Count; i++)
    {
        ProcessItem(items[i]);
        if (i % 100 == 0)
        {
            StateHasChanged();
            await Task.Yield();
        }
    }
}
```

**Indivisible long ops** — `Task.WhenAny` + `Task.Delay` for progress:

```csharp
private async Task RunLongQuery()
{
    var queryTask = DatabaseService.RunExpensiveQueryAsync();
    while (queryTask != await Task.WhenAny(queryTask, Task.Delay(1000)))
    {
        status = "Still working...";
        StateHasChanged();
    }
    result = await queryTask;
}
```

### Instead of `.Result` / `.Wait()` — use `await`

```csharp
// Wrong — blocks the sync context, deadlocks the circuit
private void Load()
{
    var data = Http.GetFromJsonAsync<List<Item>>("api/items").Result;
}

// Correct — use async all the way through
private async Task Load()
{
    var data = await Http.GetFromJsonAsync<List<Item>>("api/items");
}
```

When the calling context is synchronous and cannot be changed to `async` (e.g., an interface method that returns `void`), use fire-and-forget with error handling:

```csharp
private void Load()
{
    _ = LoadAsync();
}

private async Task LoadAsync()
{
    try
    {
        data = await Http.GetFromJsonAsync<List<Item>>("api/items");
        StateHasChanged();
    }
    catch (Exception ex)
    {
        await DispatchExceptionAsync(ex);
    }
}
```

`StateHasChanged` is required here because the framework does not know about the fire-and-forget task, so it will not trigger a re-render when it completes.

### Instead of `ConcurrentDictionary` / `Channel<T>` — use plain collections

Because the synchronization context guarantees single-threaded access within a circuit, regular `Dictionary<K,V>`, `List<T>`, and `Queue<T>` are safe. Concurrent collections add overhead with no benefit:

```csharp
// Wrong — unnecessary overhead, hides the threading model
private readonly ConcurrentDictionary<string, int> cache = new();

// Correct — the sync context already prevents concurrent access
private readonly Dictionary<string, int> cache = [];
```

### Instead of `Task.ContinueWith` — use `await` with code after it

```csharp
// Wrong — continuation may run on a thread-pool thread
private void Start()
{
    _ = Http.GetFromJsonAsync<List<Item>>("api/items")
        .ContinueWith(t =>
        {
            items = t.Result;
            StateHasChanged(); // InvalidOperationException!
        });
}

// Correct — straightforward async/await
private async Task Start()
{
    items = await Http.GetFromJsonAsync<List<Item>>("api/items");
}
```

## Cancelling async work with CancellationToken

Components that start long-running async operations (HTTP calls, database queries, streaming) should cancel that work when the component is disposed — typically when the user navigates away.

Use a `CancellationTokenSource` that is cancelled in `DisposeAsync`:

```razor
@implements IAsyncDisposable
@inject HttpClient Http

<p>@status</p>

@code {
    private string status = "Loading...";
    private CancellationTokenSource cts = new();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var data = await Http.GetFromJsonAsync<List<Item>>(
                "api/items", cts.Token);
            status = $"Loaded {data?.Count} items.";
        }
        catch (OperationCanceledException)
        {
            // Component was disposed while loading — expected, nothing to do.
        }
    }

    public ValueTask DisposeAsync()
    {
        cts.Cancel();
        cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
```
