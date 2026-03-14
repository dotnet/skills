# Component Disposal

Always use `IAsyncDisposable` (not `IDisposable`). Returns `ValueTask` — works for sync and async cleanup.

## When to Implement

Implement when component owns: event subscriptions, timers, `CancellationTokenSource`, or JS interop references (`IJSObjectReference`, `DotNetObjectReference<T>`). Otherwise skip disposal.

## Pattern — Sync Cleanup

```razor
@implements IAsyncDisposable
@inject NavigationManager Navigation

@code {
    protected override void OnInitialized()
        => Navigation.LocationChanged += HandleLocationChanged;

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs e) { }

    public ValueTask DisposeAsync()
    {
        Navigation.LocationChanged -= HandleLocationChanged;
        return ValueTask.CompletedTask;
    }
}
```

## Pattern — JS Interop Cleanup

```razor
@implements IAsyncDisposable
@inject IJSRuntime JS

@code {
    private IJSObjectReference? module;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/myModule.js");
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try { await module.DisposeAsync(); }
            catch (JSDisconnectedException) { } // Circuit already gone
        }
    }
}
```

## Anti-pattern — Timer (Don't)

Prefer `Task.Delay` polling loops (see SKILL.md). If you must use a timer, use `async void` to avoid discarding the `InvokeAsync` task:

```razor
@using System.Timers
@implements IAsyncDisposable

@code {
    private Timer? timer;

    protected override void OnInitialized()
    {
        timer = new Timer(1000);
        timer.Elapsed += OnTimerElapsed;
        timer.Start();
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
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

    public ValueTask DisposeAsync()
    {
        timer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

`Timer.Elapsed` fires on thread-pool thread. `async void` is the only correct handler signature — it awaits `InvokeAsync` and routes errors via `DispatchExceptionAsync`.

## Rules

- **Don't** call `StateHasChanged` in `DisposeAsync` — renderer is tearing down.
- **Do** null-check fields created in lifecycle methods — `DisposeAsync` may run before `OnInitializedAsync` completes.
- **Do** catch `JSDisconnectedException` when disposing JS refs — circuit may be gone.
- **Do** unsubscribe all event handlers (`-=`) — subscriptions on long-lived objects leak the component.
