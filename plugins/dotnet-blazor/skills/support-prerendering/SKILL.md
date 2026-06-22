---
license: MIT
name: support-prerendering
description: Make interactive Blazor components work correctly with prerendering. USE FOR fixing duplicate data loads, UI flicker during prerender-to-interactive handoff, null references during prerender, persisting state across prerender, disabling prerendering, excluding pages from interactive routing, or detecting whether a component is currently prerendering. DO NOT USE for choosing which render mode to use (see create-blazor-project) or general component authoring (see author-component).
---

# Support Prerendering

## How Prerendering Works

Prerendering is **on by default** for all interactive render modes. The server renders the component as static HTML and ships it to the browser immediately. Then the interactive runtime (Server/WebAssembly) loads and re-renders the component with full interactivity.

This means:
- `OnInitializedAsync` runs **twice** — once during prerender (static), once when the interactive runtime attaches.
- `OnAfterRenderAsync` is **NOT** called during prerender — only after the interactive render.
- Internal navigation between interactive pages (interactive routing) **skips prerendering** — prerendering only happens on full page loads.

## Step 1 — Read the Project's AGENTS.md

Check the project's `AGENTS.md` for the **Interactivity Mode** and **Interactivity Scope**:

| Mode | Prerendering applies? |
|------|----------------------|
| None (Static SSR) | No — there's no interactive handoff |
| Server | Yes |
| WebAssembly | Yes |
| Auto | Yes |

If the mode is `None`, this skill doesn't apply.

## Persist State Across Prerender → Interactive

The most common prerendering problem: data loaded in `OnInitializedAsync` during prerender is thrown away and re-fetched when the interactive runtime attaches. This causes flicker and duplicate API/DB calls.

### Recommended: `[PersistentState]` attribute

Annotate properties to automatically serialize during prerender and restore on interactive activation:

```razor
@page "/forecasts"
@rendermode InteractiveServer

<h1>Weather</h1>

@if (Forecasts is null)
{
    <p>Loading...</p>
}
else
{
    @foreach (var f in Forecasts)
    {
        <p>@f.Date: @f.TemperatureC°C</p>
    }
}

@code {
    [PersistentState]
    public WeatherForecast[]? Forecasts { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Forecasts ??= await ForecastService.GetForecastsAsync();
    }
}
```

The `??=` pattern is critical — it means "only fetch if the property wasn't already restored from prerender state."

### Multiple instances of the same component

When the same component type appears multiple times, use `@key` to disambiguate state:

```razor
@foreach (var item in items)
{
    <ItemCard @key="item.Id" />
}
```

### Advanced: `PersistentComponentState` service

For complex scenarios (dynamic keys, custom serialization), use the imperative API:

```csharp
@inject PersistentComponentState ApplicationState

@code {
    private List<Order>? orders;

    protected override async Task OnInitializedAsync()
    {
        ApplicationState.RegisterOnPersisting(PersistOrders);

        if (!ApplicationState.TryTakeFromJson<List<Order>>("orders", out var restored))
        {
            orders = await OrderService.GetOrdersAsync();
        }
        else
        {
            orders = restored;
        }
    }

    private Task PersistOrders()
    {
        ApplicationState.PersistAsJson("orders", orders);
        return Task.CompletedTask;
    }
}
```

## Disable Prerendering

Disable prerendering when a component depends on browser APIs immediately or when the prerender+interactive double render causes problems you can't solve with `[PersistentState]`.

### On a component definition

```razor
@rendermode @(new InteractiveServerRenderMode(prerender: false))
```

Replace `InteractiveServerRenderMode` with `InteractiveWebAssemblyRenderMode` or `InteractiveAutoRenderMode` as needed.

### On a component instance

```razor
<MyChart @rendermode="new InteractiveServerRenderMode(prerender: false)" />
```

### On the entire app

In `App.razor`:

```razor
<HeadOutlet @rendermode="new InteractiveServerRenderMode(prerender: false)" />
<Routes @rendermode="new InteractiveServerRenderMode(prerender: false)" />
```

Note: A parent's prerendering setting overrides children. If `<Routes>` disables prerendering, individual pages cannot re-enable it.

## Exclude Pages from Interactive Routing

In a globally interactive app, some pages may need `HttpContext` (cookies, request headers, response status codes). These pages must render via static SSR, not inside the interactive runtime.

Use `[ExcludeFromInteractiveRouting]`:

```razor
@page "/privacy"
@attribute [ExcludeFromInteractiveRouting]

<h1>Privacy Policy</h1>
```

This forces a **full page reload** when navigating to this page, exiting interactive routing. The page renders as static SSR with full `HttpContext` access.

In `App.razor`, conditionally apply the render mode:

```razor
<!DOCTYPE html>
<html>
<head>
    <HeadOutlet @rendermode="RenderModeForPage" />
</head>
<body>
    <Routes @rendermode="RenderModeForPage" />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>

@code {
    [CascadingParameter]
    public HttpContext HttpContext { get; set; } = default!;

    private IComponentRenderMode? RenderModeForPage =>
        HttpContext.AcceptsInteractiveRouting() ? InteractiveServer : null;
}
```

Replace `InteractiveServer` with the app's configured render mode.

## Detect Prerender vs Interactive at Runtime

Use `RendererInfo` to guard code that should only run interactively:

```csharp
protected override async Task OnInitializedAsync()
{
    if (RendererInfo.IsInteractive)
    {
        // Only runs during the interactive render, not during prerender
        await StartSignalRConnection();
    }
}
```

`RendererInfo` properties:
- `IsInteractive` — `false` during prerender, `true` after interactive runtime attaches
- `Name` — `"Static"` during prerender, `"Server"` or `"WebAssembly"` when interactive

## Client Services Fail During Prerender

Components in the `.Client` project prerender on the server. Services registered only in the client `Program.cs` (e.g., `IWebAssemblyHostEnvironment`) won't be available during prerender.

Fix by one of:
1. **Register a matching service on the server** — both `Program.cs` files provide the service
2. **Make the service optional** — use constructor injection with a nullable default: `public MyComponent(IMyService? svc = null)`
3. **Create a service abstraction** — interface in `.Client`, implementations in both projects
4. **Disable prerendering** for that component

## Don'ts

- Don't call JS interop in `OnInitializedAsync` — JS isn't available during prerender. Use `OnAfterRenderAsync(firstRender)`.
- Don't assume `OnInitializedAsync` runs once — it runs twice with prerendering. Always use `[PersistentState]` or `??=` guards.
- Don't use `HttpContext` in interactive components — it's only available during the static prerender, not during the interactive lifetime. Use `[ExcludeFromInteractiveRouting]` for pages that need it.
- Don't disable prerendering as a first resort — it hurts perceived load time and SEO. Use `[PersistentState]` to preserve state instead.
