# Async Programming Rules

Blazor runs inside a synchronization context that guarantees only one thread executes component code at a time. Follow these rules to work correctly within that model.

## Always use async/await

Every asynchronous operation must be `await`ed. Never leave a `Task` unobserved — unobserved tasks swallow exceptions silently and can cause subtle bugs.

**Correct:**
```csharp
private async Task LoadData()
{
    items = await Http.GetFromJsonAsync<List<Item>>("api/items");
}
```

**Wrong — fire-and-forget hides exceptions:**
```csharp
private void LoadData()
{
    // BUG: unobserved task — exceptions are silently lost
    _ = Http.GetFromJsonAsync<List<Item>>("api/items");
}
```

## Forbidden threading primitives

Do **not** use any of the following in Blazor components. They are unnecessary because the synchronization context already guarantees single-threaded access, and using them introduces deadlocks or race conditions:

- `Thread.Start` / `new Thread(...)`
- `Task.Run` (offloads work to a thread-pool thread outside the sync context)
- `Channel<T>` / `BlockingCollection<T>` / `ConcurrentDictionary<K,V>` and other concurrent collections
- `.Result` / `.Wait()` (synchronous blocking deadlocks the sync context)
- `Task.ContinueWith` (runs continuations outside the sync context)

**Wrong — Task.Run escapes the synchronization context:**
```csharp
private void ProcessOrder()
{
    // BUG: runs outside the sync context — StateHasChanged will throw
    _ = Task.Run(async () =>
    {
        var result = await OrderService.SubmitAsync(order);
        message = result.Message;
        StateHasChanged(); // InvalidOperationException!
    });
}
```

**Correct — stay on the sync context:**
```csharp
private async Task ProcessOrder()
{
    var result = await OrderService.SubmitAsync(order);
    message = result.Message;
    // No StateHasChanged needed — the framework re-renders after the handler completes
}
```

## When to call StateHasChanged

The framework automatically re-renders a component:
1. After the first synchronous block of a lifecycle method or event handler completes.
2. After the `Task` returned by an async lifecycle method or event handler completes.

You do **not** need to call `StateHasChanged` in routine event handlers or lifecycle methods.

**Call `StateHasChanged` only when:**

1. **An async handler has multiple await points and you want intermediate UI updates:**

```csharp
private async Task ProcessSteps()
{
    status = "Step 1...";
    await Step1Async();
    status = "Step 2...";
    StateHasChanged();
    await Step2Async();
    status = "Step 3...";
    StateHasChanged();
    await Step3Async();
    status = "Done!";
}
```

2. **An external event (timer, C# event, WebSocket message) modifies state outside the Blazor event pipeline:**

```csharp
private void OnTimerElapsed()
{
    _ = InvokeAsync(() =>
    {
        count++;
        StateHasChanged();
    });
}
```

Use `InvokeAsync` to marshal back onto the renderer's synchronization context when responding to events from external sources. `StateHasChanged` can only be called from the sync context — calling it from a raw thread-pool thread throws `InvalidOperationException`.

## Handling fire-and-forget tasks

Sometimes you intentionally start work that outlives an event handler (e.g., sending an analytics event, dispatching a notification). Even then, you **must** observe the `Task` so exceptions are not silently lost.

Use `DispatchExceptionAsync` to route any failure back into Blazor's normal error-handling pipeline (error boundaries, circuit termination, lifecycle exception logging):

```razor
<button @onclick="SendReport">Send report</button>

@code {
    private void SendReport()
    {
        _ = SendReportAsync();
    }

    private async Task SendReportAsync()
    {
        try
        {
            await ReportSender.SendAsync();
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    }
}
```

`DispatchExceptionAsync` is a method on `ComponentBase`. It lets the component treat the failure as though it occurred during a lifecycle method, which means:
- An `<ErrorBoundary>` wrapping the component will activate.
- If no error boundary exists, the circuit is terminated (server) or the error UI is shown (WebAssembly).
- The exception is logged the same way as lifecycle exceptions.

## Alternatives to forbidden threading primitives

If you reach for `Thread.Start`, `Task.Run`, `Channel<T>`, or concurrent collections, there is almost always a simpler Blazor-friendly alternative.

### Instead of `Thread.Start` / `Task.Run` — use `await` directly or `Task.Yield`

If the work is not CPU-heavy and you just want to "kick it to the background" so the UI updates first, yield and continue:

```csharp
private async Task StartLongOperation()
{
    status = "Starting...";
    await Task.Yield();
    await LongOperationService.RunAsync();
    status = "Done!";
}
```

`Task.Yield` returns control to the renderer so it can paint, then the continuation resumes on the same synchronization context. No thread-pool thread is needed.

For compute-heavy synchronous work (e.g., processing a large list), break the work into chunks separated by `Task.Yield` so the UI thread can process events and repaint between chunks:

```csharp
private async Task ProcessLargeList()
{
    for (var i = 0; i < items.Count; i++)
    {
        ProcessItem(items[i]);

        if (i % 100 == 0)
        {
            status = $"Processed {i}/{items.Count}...";
            StateHasChanged();
            await Task.Yield();
        }
    }

    status = "Done!";
}
```

Without the periodic `Task.Yield`, the entire loop runs synchronously on the sync context, blocking the UI from responding to clicks, keyboard input, or painting progress updates.

For indivisible long-running async operations (e.g., a single HTTP call or database query that cannot be chunked), use `Task.WhenAny` with `Task.Delay` to provide periodic progress feedback while waiting:

```csharp
private async Task RunLongQuery()
{
    status = "Running query...";
    var elapsed = TimeSpan.Zero;
    var interval = TimeSpan.FromSeconds(1);

    var queryTask = DatabaseService.RunExpensiveQueryAsync();

    while (queryTask != await Task.WhenAny(queryTask, Task.Delay(interval)))
    {
        elapsed += interval;
        status = $"Still working... ({elapsed.TotalSeconds:0}s)";
        StateHasChanged();
    }

    result = await queryTask;
    status = "Done!";
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
