---
name: author-component
description: Create or review Blazor components. Covers UI decomposition, data flow, parameters, EventCallback, RenderFragment, lifecycle, async rules, disposal. Use as a reference when authoring new components or reviewing existing ones.
---

# Author Blazor Component

## Design Rules

- Decompose UI into a component tree mirroring visual structure. Parent orchestrates; children render.
- Data flows **down** via `[Parameter]`. Events flow **up** via `EventCallback`.
- Enumerate all states before writing markup: loading, empty, loaded, error, unauthorized. Handle each with `@if`/`@else`.
- Never mutate `[Parameter]` properties. Copy to a private field in `OnParametersSet`.
- Delegate business logic to injected services. Components are thin UI shells.

### Size Limits

| Metric | Target |
|--------|--------|
| Lines (markup + `@code`) | 100–200; refactor above 500 |
| Cyclomatic complexity | ≤ 10 per method/render block |
| Parameters / event handlers | ≤ 10 each |

See `references/breaking-down-components.md` for extraction patterns.

### State Handling Pattern

```razor
@if (error is not null)
{
    <div class="alert alert-danger">@error <button @onclick="LoadData">Retry</button></div>
}
else if (items is null)
{
    <p>Loading...</p>
}
else if (items.Count == 0)
{
    <GridEmptyState Message="No records found." />
}
else
{
    <GridBody Items="items" />
}
```

## Parameters

**Do:**
- `[Parameter] public string Title { get; set; } = "";` — public auto-property with `{ get; set; }`.
- `[Parameter, EditorRequired] public string Label { get; set; } = "";` — mark required params.
- `[Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }` — splatting HTML attributes.

**Don't:**
- `required` or `init` on parameters — runtime failures (BL0007).
- Logic in parameter getters/setters.
- Write to parameter properties inside the component.

### Deriving Local State

```csharp
[Parameter] public string InitialText { get; set; } = "";
private string currentText = "";

protected override void OnParametersSet()
{
    currentText = InitialText;
}
```

## EventCallback

Use `EventCallback` / `EventCallback<T>` for parent-child events. Never use `Action` or `Func` — they don't trigger parent re-render.

```csharp
[Parameter] public EventCallback<int> OnAddToCart { get; set; }
```

```razor
<button @onclick="() => OnAddToCart.InvokeAsync(Quantity)">Add</button>
```

**Don't** bind external object methods directly to `@on*` attributes — you lose the ability to update local state, debounce/cancel, or route errors via `DispatchExceptionAsync`:
```razor
<!-- WRONG --> <button @onclick="CartService.AddItemAsync">Click</button>
<!-- RIGHT --> <button @onclick="HandleClick">Click</button>
```

## Child Content / RenderFragment

```csharp
// Single slot
[Parameter] public RenderFragment? ChildContent { get; set; }

// Typed template (generic component)
[Parameter] public RenderFragment<TItem>? RowTemplate { get; set; }

// Multiple named slots
[Parameter] public RenderFragment? Header { get; set; }
[Parameter] public RenderFragment? Footer { get; set; }
```

Use `@typeparam TItem` for generic components. Use `@key` on repeated elements in loops.

## File Patterns

**Single-file:** All in `.razor` file. Use when `@code` block < ~50 lines.

**Code-behind:** `.razor` for markup, `.razor.cs` for `partial class`. Use when `@code` > ~50 lines.

```csharp
// MyComponent.razor.cs
public partial class MyComponent : ComponentBase
{
    [Parameter] public string Title { get; set; } = "";
}
```

## Directives

| Directive | Example |
|-----------|---------|
| `@page` | `@page "/items/{Id:int}"` |
| `@layout` | `@layout MainLayout` |
| `@implements` | `@implements IAsyncDisposable` |
| `@inject` | `@inject HttpClient Http` |
| `@rendermode` | `@rendermode InteractiveServer` |
| `@typeparam` | `@typeparam TItem` |
| `@attribute` | `@attribute [Authorize]` |

## Lifecycle

Execution order:
1. `SetParametersAsync` — raw parameter assignment (advanced).
2. `OnInitialized[Async]` — once on first render. Load data here.
3. `OnParametersSet[Async]` — after every parameter update. Copy params to local fields here.
4. `OnAfterRender[Async](bool firstRender)` — after DOM update. JS interop only here.

## Disposal

Implement `IAsyncDisposable` (not `IDisposable`) when the component owns event subscriptions, timers, `CancellationTokenSource`, or JS interop references.

```razor
@implements IAsyncDisposable
```

In `DisposeAsync`: unsubscribe events (`-=`), dispose timers, cancel tokens. Don't call `StateHasChanged`. Null-check fields — `DisposeAsync` may run before `OnInitializedAsync` completes. Catch `JSDisconnectedException` when disposing JS references.

See `references/component-disposal.md` for full patterns.

## Async Rules

**Do:** `await` every async operation. Use `InvokeAsync` + `StateHasChanged` for external events (timers, C# events). Use `DispatchExceptionAsync` for fire-and-forget error routing.

**Don't:** `.Result`, `.Wait()`, `Task.Run`, `ContinueWith`, `Thread.Start`, `ConcurrentDictionary`, `Channel<T>`. These deadlock or escape the sync context.

`StateHasChanged` is only needed for: (1) intermediate updates between multiple awaits, (2) external event callbacks marshaled via `InvokeAsync`.

### Debounce with Task.Delay

Prefer `Task.Delay` + `CancellationTokenSource` over `Timer` callbacks. Stays on the sync context — no `InvokeAsync`, no fire-and-forget trampolines.

```csharp
private CancellationTokenSource? _debounceCts;

private async Task OnInput(ChangeEventArgs e)
{
    _debounceCts?.Cancel();
    _debounceCts?.Dispose();
    _debounceCts = new CancellationTokenSource();
    var token = _debounceCts.Token;

    try
    {
        await Task.Delay(300, token);
        await DoWorkAsync(token);
    }
    catch (OperationCanceledException) { }
}
```

**Don't** use `System.Threading.Timer` or `System.Timers.Timer` for debounce — they fire on thread-pool threads, require `InvokeAsync` marshaling, and create unobserved `Task` risks.

### Polling with Task.Delay

A loop started from `OnInitializedAsync` stays on the Blazor sync context. No `InvokeAsync` needed — just call `StateHasChanged` directly.

```csharp
protected override async Task OnInitializedAsync()
{
    _cts = new CancellationTokenSource();
    // Safe to discard — PollAsync catches all exceptions internally
    _ = PollAsync(_cts.Token);
}

private async Task PollAsync(CancellationToken token)
{
    try
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(30_000, token);
            count = await Service.GetCountAsync();
            StateHasChanged();
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { await DispatchExceptionAsync(ex); }
}
```

Cancel the CTS in `DisposeAsync` — `Task.Delay` throws `OperationCanceledException` and the loop exits cleanly. Don't catch `ObjectDisposedException` as a disposal guard — use CTS cancellation instead.

### External C# Event Handlers

C# events (e.g. `Action<T>`) fire on arbitrary threads. The handler must marshal back to the Blazor sync context and route errors through `DispatchExceptionAsync`.

```csharp
private async void HandleNotification(Notification n)
{
    try
    {
        await InvokeAsync(() =>
        {
            count++;
            StateHasChanged();
        });
    }
    catch (Exception ex)
    {
        await DispatchExceptionAsync(ex);
    }
}
```

**Don't** use `_ = InvokeAsync(...)` — the discarded `Task` swallows exceptions silently.

See `references/async-programming-rules.md` for alternatives to forbidden primitives.

## Styling

**Don't** use inline `style` attributes. Use CSS classes or data attributes with CSS selectors.

```razor
<!-- WRONG -->
<tr style="@(OnRowClick.HasDelegate ? "cursor:pointer" : null)">
<!-- RIGHT -->
<tr data-clickable="@OnRowClick.HasDelegate">
```
```css
::deep tr[data-clickable="True"] { cursor: pointer; }
```

Use CSS isolation (`.razor.css`) for component-scoped styles.

## Don'ts Checklist

- `required`/`init` on `[Parameter]` — runtime failure.
- Logic in parameter setters — BL0007.
- Mutate `[Parameter]` from inside — copy to private field.
- `@ref` + `@rendermode` on same element — not supported.
- JS interop in `OnInitializedAsync` — use `OnAfterRenderAsync`.
- `Action`/`Func` for event params — use `EventCallback`.
- `Task.Run`/`.Result`/`.Wait()`/`ContinueWith`/`Thread.Start` — deadlock.
- `Timer`/`System.Timers.Timer` for debounce — use `Task.Delay` + CTS.
- `StateHasChanged` in every handler — unnecessary overhead.
- Inline `style` attributes — use CSS classes or `data-*` attributes.
- `catch (SomeException) { throw; }` — noise. Use `when` guard or let exceptions propagate.
- `catch (ObjectDisposedException)` as disposal guard — use CTS cancellation instead.
- External delegate on `@on*` — component won't re-render.
- Unobserved `Task` without `DispatchExceptionAsync` — silent exception loss.
- `_ = InvokeAsync(...)` in event handlers — use `async void` + `DispatchExceptionAsync`.
- `IEnumerable<T>` for collection parameters — use `IReadOnlyList<T>` and copy in `OnParametersSet`.
- Gold-plating: ARIA roles, extra wrapper `<div>`s, accessibility attributes, or features the prompt didn't ask for.
- Missing disposal of subscriptions/timers/tokens — memory leak.
