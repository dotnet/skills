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

## Pattern — Timer

```razor
@using System.Timers
@implements IAsyncDisposable

@code {
    private Timer? timer;

    protected override void OnInitialized()
    {
        timer = new Timer(1000);
        timer.Elapsed += (_, _) => InvokeAsync(() => { count++; StateHasChanged(); });
        timer.Start();
    }

    public ValueTask DisposeAsync()
    {
        timer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

`Timer.Elapsed` fires on thread-pool thread. Wrap in `InvokeAsync` to marshal onto sync context.

## Rules

- **Don't** call `StateHasChanged` in `DisposeAsync` — renderer is tearing down.
- **Do** null-check fields created in lifecycle methods — `DisposeAsync` may run before `OnInitializedAsync` completes.
- **Do** catch `JSDisconnectedException` when disposing JS refs — circuit may be gone.
- **Do** unsubscribe all event handlers (`-=`) — subscriptions on long-lived objects leak the component.
