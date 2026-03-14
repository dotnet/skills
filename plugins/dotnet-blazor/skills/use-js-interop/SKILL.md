---
name: use-js-interop
description: Use when adding, reviewing, or fixing JavaScript interop in Blazor components. Covers collocated JS modules, IJSObjectReference lifecycle, DotNetObjectReference, timing rules, disposal patterns, and server-side safety. Do not use for general Blazor component authoring without JS interop needs.
---

# JS Interop in Blazor

## 1. Collocated JS Modules

Always use collocated `.razor.js` files with `export` — never global `window.*` functions or `<script>` tags.

```javascript
// ChartPanel.razor.js — placed next to ChartPanel.razor
export function initialize(canvas, dotNetRef) { /* ... */ }
export function updateData(points) { /* ... */ }
export function dispose() { /* ... */ }
```

Import paths: same project = `"./Components/ChartPanel.razor.js"`, RCL = `"./_content/{AssemblyName}/..."`.

## 2. Lifecycle Timing

**All JS interop must happen in `OnAfterRenderAsync` or event handlers** — never in `OnInitialized`, `OnParametersSet`, or constructors. JS is not available during server prerendering.

Use a typed interop wrapper (see Section 4) — never call `InvokeAsync`/`InvokeVoidAsync` with raw string literals:

```csharp
private ChartInterop? _chart;

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        _chart = new ChartInterop(JS);
        await _chart.InitializeAsync(_canvasRef);
    }
}
```

**Parameter changes**: set a flag in `OnParametersSet`, apply in `OnAfterRenderAsync`:

```csharp
private bool _dataChanged;

protected override void OnParametersSet() => _dataChanged = true;

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender) { /* init */ }
    else if (_dataChanged && _chart is not null)
    {
        _dataChanged = false;
        await _chart.UpdateDataAsync(DataPoints);
    }
}
```

## 3. Batch Related Operations

Each JS interop call crosses the .NET-to-JS boundary (and in Blazor Server, the SignalR circuit). Batching applies in **both directions** — .NET→JS and JS→.NET.

### .NET → JS: merge consecutive calls

If the C# side makes two or more JS calls in a row, combine them into one JS function:

```csharp
// ❌ Two round-trips — theme and locale are always applied together
await _module.InvokeVoidAsync("applyTheme", theme);
await _module.InvokeVoidAsync("applyLocale", locale);

// ❌ Result of one call feeds into another — both can stay in JS
var token = await _module.InvokeAsync<string>("createAccessToken");
await _module.InvokeVoidAsync("storeToken", token);
```

```javascript
// ✅ One call applies both — no data dependency, no reason for two trips
export function applyPreferences(theme, locale) {
    document.documentElement.dataset.theme = theme;
    document.documentElement.lang = locale;
}

// ✅ Chain stays in JS — the token never needs to cross the boundary
export function createAndStoreToken() {
    const token = crypto.randomUUID();
    sessionStorage.setItem('access-token', token);
    return token;
}
```

### JS → .NET: batch callbacks

When JS needs to send multiple pieces of data back to .NET, send them in a single `invokeMethodAsync` call rather than making separate callbacks:

```javascript
// ❌ Two .NET round-trips from JS
await dotNetRef.invokeMethodAsync(ON_VOLUME_CHANGED, volume);
await dotNetRef.invokeMethodAsync(ON_PLAYBACK_CHANGED, isPlaying);

// ✅ One callback with all data
await dotNetRef.invokeMethodAsync(ON_PLAYER_STATE_CHANGED, { volume, isPlaying });
```

**Rule**: if two interop calls always happen together from either side, merge them into one function.

## 4. Typed Interop Wrapper

Encapsulate interop for a feature in a plain class that owns the module lifecycle:

```csharp
public sealed class ChartInterop : IAsyncDisposable
{
    internal const string ModulePath = "./Components/ChartPanel.razor.js";
    internal const string InitMethod = "initialize";
    internal const string UpdateMethod = "updateData";
    internal const string DisposeMethod = "dispose";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public ChartInterop(IJSRuntime js) => _js = js;

    private async ValueTask<IJSObjectReference> GetModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask InitializeAsync(ElementReference canvas)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync(InitMethod, canvas);
    }

    public async ValueTask UpdateDataAsync(IReadOnlyList<DataPoint> points)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync(UpdateMethod, points);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync(DisposeMethod);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
    }
}
```

The component creates and uses the wrapper with no magic strings:

```razor
@inject IJSRuntime JS
@implements IAsyncDisposable

<canvas @ref="_canvasRef" width="600" height="400"></canvas>

@code {
    private ElementReference _canvasRef;
    private ChartInterop? _chart;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _chart = new ChartInterop(JS);
            await _chart.InitializeAsync(_canvasRef);
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_chart is not null)
            await _chart.DisposeAsync();
    }
}
```

Prefer a concrete class over interface + implementation for interop wrappers. For unit testing, substitute `IJSRuntime` directly (it is already an interface).

## 5. DotNetObjectReference for JS-to-.NET Callbacks

```csharp
_dotNetRef = DotNetObjectReference.Create(this);
await _module.InvokeVoidAsync("initialize", _dotNetRef);
```

On the JS side, wrap the `dotNetRef` in a class. Use `async`/`await` with `try/catch` (not `.catch()`) to guard against circuit loss. Define .NET method name constants at the top:

```javascript
const ON_CLIPBOARD_CHANGED = 'OnClipboardChanged';

class ClipboardMonitor {
    #dotNetRef;
    #abortController;

    constructor(dotNetRef) {
        this.#dotNetRef = dotNetRef;
        this.#abortController = new AbortController();
    }

    start() {
        document.addEventListener('copy', async () => {
            try {
                const text = await navigator.clipboard.readText();
                await this.#dotNetRef.invokeMethodAsync(ON_CLIPBOARD_CHANGED, text);
            } catch { /* circuit disconnected or clipboard denied */ }
        }, { signal: this.#abortController.signal });
    }

    dispose() {
        this.#abortController.abort();
    }
}

let monitor;
export function initialize(dotNetRef) {
    monitor = new ClipboardMonitor(dotNetRef);
    monitor.start();
}

export function dispose() {
    monitor?.dispose();
}
```

Rules:
- `[JSInvokable]` methods **must be `public`** — private/internal silently fails at runtime
- Wrap `StateHasChanged` in `InvokeAsync` inside `[JSInvokable]` callbacks:
  ```csharp
  [JSInvokable]
  public async Task OnClipboardChanged(string text)
  {
      await InvokeAsync(() => { _lastClipboard = text; StateHasChanged(); });
  }
  ```
- Always `try/catch` around `invokeMethodAsync` in JS — circuit loss throws
- Use `const` for .NET method name strings in JS — prevents typo bugs that silently fail
- Dispose `DotNetObjectReference` in `DisposeAsync`

## 6. Disposal and Server Safety

Always implement `IAsyncDisposable`. Call JS cleanup first, then dispose references. Catch `JSDisconnectedException` for Blazor Server circuit loss:

```csharp
public async ValueTask DisposeAsync()
{
    try
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }
    }
    catch (JSDisconnectedException) { }

    _dotNetRef?.Dispose();
}
```

Never use sync `IDisposable` for JS interop cleanup — `InvokeVoidAsync` returns `ValueTask` and must be awaited.

## 7. ElementReference

Pass DOM elements via `@ref`, not string IDs:

```razor
<canvas @ref="_canvasRef" width="600" height="400"></canvas>
```

```csharp
await _chart.InitializeAsync(_canvasRef);
```

## Checklist

- [ ] JS is in collocated `.razor.js` with `export` — no `window.*` globals
- [ ] All interop in `OnAfterRenderAsync` or event handlers — never during prerender
- [ ] `IAsyncDisposable` catches `JSDisconnectedException`
- [ ] `DotNetObjectReference` disposed in `DisposeAsync`; JS side has `try/catch` around `invokeMethodAsync`
- [ ] `[JSInvokable]` methods are `public` and use `await InvokeAsync(StateHasChanged)`
- [ ] `InvokeVoidAsync` used when no return value is needed
- [ ] `ElementReference` instead of string IDs
- [ ] Related operations batched into single interop calls (both .NET→JS and JS→.NET)

## Common Mistakes Checklist

| Mistake | Fix |
|---------|-----|
| Using JS for something achievable with CSS | Use CSS custom properties, `data-` attributes, pseudo-classes |
| Many fine-grained interop calls | Batch into coarse functions — both .NET→JS and JS→.NET |
| Component imports JS module directly | Encapsulate in a strongly typed interop class |
| Magic strings for method names / module paths | Define `internal const` fields in the interop class |
| Interface + implementation for interop wrapper | Use a plain class; mock `IJSRuntime` for tests instead |
| JS calls in `OnInitializedAsync` | Move to `OnAfterRenderAsync(firstRender)` |
| `InvokeAsync<object>` for void calls | Use `InvokeVoidAsync` |
| `IDisposable` with fire-and-forget JS | Use `IAsyncDisposable` with `await` |
| Global `window.*` JS functions | Use collocated `.razor.js` with `export` |
| String element IDs passed to JS | Use `ElementReference` with `@ref` |
| `[JSInvokable]` on private method | Must be `public` — silently fails otherwise |
| `DotNetObjectReference` not disposed | Dispose in `DisposeAsync` — causes memory leak |
| `StateHasChanged()` without `InvokeAsync` | Wrap in `await InvokeAsync(() => { StateHasChanged(); })` |
| JS `invokeMethodAsync` without error handling | Wrap in `try/catch` — circuit loss throws |
| Bare `dotNetRef` in JS event handlers | Wrap in a class with `#dotNetRef` private field |
| Magic strings in JS `invokeMethodAsync` calls | Use `const` at module top — typos silently fail at runtime |
| JS calls in `OnParametersSetAsync` | Track changes, apply in `OnAfterRenderAsync` with guard |
| No null check before calling module | Check `module is not null` before use |
