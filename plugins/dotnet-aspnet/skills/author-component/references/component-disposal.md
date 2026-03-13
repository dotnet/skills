# Component Disposal

When a component is removed from the UI, the framework calls `DisposeAsync` (or `Dispose`) to release resources.

Always use `IAsyncDisposable` — it works for both synchronous and asynchronous cleanup without additional allocations because it returns `ValueTask`. There is no need to choose between `IDisposable` and `IAsyncDisposable`; always prefer `IAsyncDisposable`.

## When to implement disposal

Implement `IAsyncDisposable` when the component owns any of:
- **Event subscriptions** on long-lived objects (e.g., `EditContext.OnFieldChanged`, `NavigationManager.LocationChanged`).
- **Timers** (`System.Timers.Timer`, `PeriodicTimer`).
- **CancellationTokenSource** instances.
- **JS interop object references** (`IJSObjectReference`, `DotNetObjectReference<T>`).

If none of these apply, the component does not need disposal.

## Implementing IAsyncDisposable

Use `@implements IAsyncDisposable` and a `DisposeAsync` method. For purely synchronous cleanup, return `ValueTask.CompletedTask`:

```razor
@implements IAsyncDisposable
@inject NavigationManager Navigation

@code {
    protected override void OnInitialized()
    {
        Navigation.LocationChanged += HandleLocationChanged;
    }

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // ...
    }

    public ValueTask DisposeAsync()
    {
        Navigation.LocationChanged -= HandleLocationChanged;
        return ValueTask.CompletedTask;
    }
}
```

Always unsubscribe event handlers in `DisposeAsync`. If the event source (e.g., an injected service) outlives the component, keeping the subscription alive leaks the component.

When cleanup requires async work — typically disposing JS interop references — `await` inside `DisposeAsync`:

```razor
@implements IAsyncDisposable
@inject IJSRuntime JS

@code {
    private IJSObjectReference? module;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./js/myModule.js");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try
            {
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Circuit is already gone — safe to ignore
            }
        }
    }
}
```

Catch `JSDisconnectedException` in `DisposeAsync` because the SignalR circuit may already be disconnected when disposal runs (e.g., the browser tab was closed). The JS-side object is already gone in that case, so the exception is harmless.

## Disposal rules

- **Do not call `StateHasChanged`** in `DisposeAsync` — the renderer is tearing down and will not process UI updates.
- **Null-check objects created in lifecycle methods** — `DisposeAsync` may run before `OnInitializedAsync` completes, so fields may still be `null`.
- **Do not assume disposal timing** — `DisposeAsync` can be triggered before or after an incomplete `Task` from `OnInitializedAsync` or `OnParametersSetAsync` completes.

## Timer disposal example

A common pattern combining a timer, `InvokeAsync`, and disposal:

```razor
@using System.Timers
@implements IAsyncDisposable

<p>Current count: @currentCount</p>

@code {
    private int currentCount = 0;
    private Timer? timer;

    protected override void OnInitialized()
    {
        timer = new Timer(1000);
        timer.Elapsed += (sender, e) => OnTimerElapsed();
        timer.Start();
    }

    private void OnTimerElapsed()
    {
        _ = InvokeAsync(() =>
        {
            currentCount++;
            StateHasChanged();
        });
    }

    public ValueTask DisposeAsync()
    {
        timer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

The `Timer.Elapsed` callback runs on a thread-pool thread, not on the Blazor synchronization context. Wrap the update in `InvokeAsync` to marshal back onto the sync context and call `StateHasChanged` safely.
