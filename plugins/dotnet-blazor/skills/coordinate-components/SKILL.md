---
license: MIT
name: coordinate-components
description: >
  Share state between components that don't have a direct parent-child parameter relationship,
  using cascading values, scoped services with change events, or CascadingValueSource via DI.
  USE WHEN the user needs shared state for interactive components hosted from a static layout,
  a shopping cart or notification count accessible from multiple pages, a theme or user
  preference cascaded app-wide to interactive subscribers, or when components in different
  parts of the tree must react when shared data changes. Also USE WHEN a CascadingValue from
  static SSR isn't reaching an interactive child, or when the user needs to understand scoped
  vs singleton service lifetime for state on Blazor Server.
  DO NOT USE for direct parent-child parameter passing or EventCallback (see author-component),
  for persisting state across prerender-to-interactive transitions (see support-prerendering),
  or for service abstractions for data fetching in Auto/WebAssembly (see fetch-and-send-data).
---

# Coordinate Components

## Step 1 — Read AGENTS.md

Read `AGENTS.md` at the workspace root to learn the project's conventions before making changes.

## Step 2 — Decide the scope

| Need | Mechanism | When to use |
|------|-----------|-------------|
| Subtree (same render mode) | `CascadingValue` component | Theme, layout config within a layout |
| App-wide interactive state | Root-level `CascadingValueSource<T>` via DI | Current user, feature flags, theme shared by interactive subscribers |
| Mutable shared state within a circuit | Scoped service + `Action` event | Shopping cart, notification count, selected filters |

For parent→child one level: use `[Parameter]` / `EventCallback` (see `author-component` skill).
For persisting state across prerender→interactive: see `support-prerendering` skill.

## Workflow (quick reference)

1. Choose the mechanism from the table in Step 2
2. If a static layout hosts interactive islands, make every live subscriber an interactive component; static SSR markup cannot re-render
3. Register a root-level cascade in each interactive host with `AddCascadingValue(...)` and `isFixed: false`
4. Consume via `[CascadingParameter]` in child components
5. Update via `NotifyChangedAsync(newValue)` — never page reload
6. For additional mutable state within a circuit → add scoped service (Step 5)
7. Wrap any `StateHasChanged` from background threads in `InvokeAsync`
8. Implement `IDisposable` — dispose timers, cancel tokens, unsubscribe events

## Step 3 — CascadingValue for subtree state

Wrap a subtree with `<CascadingValue>` to flow data to all descendants without passing it through every intermediate component.

```razor
@* In a layout or parent component *@
<CascadingValue Value="theme">
    @Body
</CascadingValue>

@code {
    private ThemeInfo theme = new() { ButtonClass = "btn-primary" };
}
```

Consume in any descendant:

```csharp
[CascadingParameter]
private ThemeInfo? Theme { get; set; }
```

**Rules:**
- Matched by **type**, not name. To cascade multiple values of the same type, add `Name`:
  ```razor
  <CascadingValue Value="primary" Name="PrimaryTheme">...</CascadingValue>
  ```
  ```csharp
  [CascadingParameter(Name = "PrimaryTheme")]
  private ThemeInfo? Primary { get; set; }
  ```
- Set `IsFixed="true"` when the value never changes — avoids subscription overhead.
- **Does NOT cross render mode boundaries.** A `<CascadingValue>` in a static SSR parent is invisible to interactive children. See Step 6.

## Step 4 — CascadingValueSource&lt;T&gt; for app-wide interactive state

Register a `CascadingValueSource<T>` when the value must be available to interactive
components throughout an app. Static SSR components receive only their rendered
snapshot and cannot subscribe to later changes.

```csharp
// Program.cs
builder.Services.AddCascadingValue(sp =>
{
    var theme = new ThemeInfo { ButtonClass = "btn-primary" };
    return new CascadingValueSource<ThemeInfo>(theme, isFixed: false);
});
```

Consume identically to Step 3:

```csharp
[CascadingParameter]
private ThemeInfo? Theme { get; set; }
```

**To update and notify subscribers**, either mutate the existing object or replace it:

```razor
@* Component that changes the theme *@
@inject ThemeState Theme

<button @onclick="ToggleDarkMode">Toggle theme</button>

@code {
    private bool isDark;

    private async Task ToggleDarkMode()
    {
        isDark = !isDark;
        await Theme.SetAsync(
            new ThemeInfo { ButtonClass = isDark ? "btn-dark" : "btn-primary" });
    }
}
```

Use a small state wrapper so consumers don't need to inject framework plumbing directly:

```csharp
public sealed class ThemeState
{
    private readonly CascadingValueSource<ThemeInfo> source;

    public ThemeState()
    {
        source = new CascadingValueSource<ThemeInfo>(
            new ThemeInfo { ButtonClass = "btn-primary" },
            isFixed: false);
    }

    public CascadingValueSource<ThemeInfo> Source => source;

    public Task SetAsync(ThemeInfo value) => source.NotifyChangedAsync(value);
}
```

Register the wrapper and expose its source as the root cascade:

```csharp
builder.Services.AddScoped<ThemeState>();
builder.Services.AddCascadingValue(
    sp => sp.GetRequiredService<ThemeState>().Source);
```

**Update protocol:** Whenever shared state changes, the state wrapper MUST call
`NotifyChangedAsync()` on its `CascadingValueSource<T>`. This is the mechanism that
triggers re-rendering in all interactive `[CascadingParameter]` subscribers.
Without this call, no subscribers update. Do not use `NavigationManager.Refresh()`
or page reloads as a substitute.

**Rules:**
- `isFixed: false` enables change notifications. `isFixed: true` is better for truly static values (feature flags).
- Root-level cascades are recreated for each interactive renderer. They don't transfer
  a value from static SSR into an interactive session, and notifications don't update
  static SSR markup.
- Keep cascaded types **granular**. Every `NotifyChangedAsync` re-renders ALL subscribers regardless of which property changed. Don't put all app state into one cascaded type.
- For Auto/WebAssembly apps, register in **both** server and `.Client` `Program.cs`. The type must be in a shared assembly.

## Step 5 — Scoped state service with change events

For mutable shared state that multiple components read **and write** (shopping cart, notification count, filters), use a scoped service with an event for change notification.

**Define the service:**

```csharp
public class CartState
{
    private readonly List<CartItem> _items = [];

    public IReadOnlyList<CartItem> Items => _items;
    public int Count => _items.Count;

    public event Action? OnChange;

    public void Add(CartItem item)
    {
        _items.Add(item);
        OnChange?.Invoke();
    }

    public void Remove(CartItem item)
    {
        _items.Remove(item);
        OnChange?.Invoke();
    }
}
```

**Register as scoped:**

```csharp
builder.Services.AddScoped<CartState>();
```

**Subscribe in components:**

```razor
@inject CartState Cart
@implements IDisposable

<span class="badge">@Cart.Count</span>

@code {
    protected override void OnInitialized()
    {
        Cart.OnChange += StateHasChanged;
    }

    public void Dispose()
    {
        Cart.OnChange -= StateHasChanged;
    }
}
```

The simple `Action OnChange` pattern works when the event fires from the Blazor sync context (button click → `Cart.Add(…)`). If the event fires from **outside** the sync context (timer, background task, SignalR hub), wrap in `InvokeAsync`:

```csharp
private Action? _handler;

protected override void OnInitialized()
{
    _handler = () => InvokeAsync(StateHasChanged);
    Cart.OnChange += _handler;
}

public void Dispose() => Cart.OnChange -= _handler;
```

Store the delegate in a field so you can unsubscribe the exact same instance.

## Step 6 — Render mode and service lifetime rules

### Cascading values don't cross render mode boundaries

A `<CascadingValue>` placed in a static SSR layout (`MainLayout.razor` when the layout renders statically) will **not** reach interactive children. The interactive component sees `null` for the cascading parameter.

**Fix:** Keep the live state inside the interactive renderer. Make each UI element
that must update an interactive component and resolve a root-level cascade or scoped
service there. A static header or layout cannot update after the response is sent;
turn only the live indicator into an interactive island, or make the layout interactive.

### Service lifetime on Server vs WebAssembly

| Lifetime | Server | WebAssembly |
|----------|--------|-------------|
| **Scoped** | Per circuit (per user connection) | Per browser tab |
| **Singleton** | Shared across ALL users | Per browser tab (safe) |
| **Transient** | New instance per injection | New instance per injection |

On Server, **never store user-specific state in a singleton** — every user's circuit shares the same singleton. One user's cart leaks into another's. Use `AddScoped<T>()`.

On WebAssembly, singletons are per-tab and safe. But code meant for **both** Server and WebAssembly (Auto mode) must use scoped.

### Auto/WebAssembly with prerendering

State services must be defined in the `.Client` project or a shared assembly — they cannot reference server-only types. Register the service in both `Program.cs` files. State created during prerender does not survive the switch to the interactive runtime. Use the `support-prerendering` skill's `[PersistentState]` pattern to carry state across.

## Don'ts

- **Don't use a singleton for per-user state on Server** — all circuits share it, leaking state between users.
- **Don't put all app state into one cascaded object** — `NotifyChangedAsync` re-renders ALL subscribers on every change. Separate concerns into distinct types (`ThemeState`, `CartState`, `UserPreferences`).
- **Don't forget to unsubscribe** — omitting `Dispose` on event subscriptions causes memory leaks that grow per-circuit.
- **Don't use `<CascadingValue>` in a static layout expecting it to reach interactive children** — it won't cross render mode boundaries. Put the live subscriber in an interactive island and resolve its state inside that renderer.
- **Don't promise live updates to static SSR markup** — it cannot re-render after the response. Make the changing region interactive.
- **Don't use `NavigationManager.Refresh(forceReload: true)` to propagate cascading value changes** — this destroys the circuit and forces a full page reload. Instead, update the scoped state wrapper so it calls `NotifyChangedAsync(newValue)` for interactive `[CascadingParameter]` subscribers.
- **Don't call `StateHasChanged` from a non-Blazor thread** — wrap in `InvokeAsync`. The framework throws `InvalidOperationException: The current thread is not associated with the Dispatcher`.
