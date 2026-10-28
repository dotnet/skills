---
license: MIT
name: coordinate-components
description: >
  Share state between components that don't have a direct parent-child parameter relationship,
  using cascading values, scoped services with change events, or CascadingValueSource via DI.
  USE WHEN the user needs a CascadingParameter or CascadingValue that works across render mode
  boundaries, a shopping cart or notification count accessible from multiple pages, a theme or
  user preference cascaded app-wide, or when components in different parts of the tree must
  react when shared data changes. Also USE WHEN cascading values aren't reaching interactive
  children in per-page interactivity mode, or when the user needs to understand scoped vs
  singleton service lifetime for state on Blazor Server.
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
| App-wide (all render modes) | `CascadingValueSource<T>` via DI | Current user, feature flags, theme shared globally |
| Mutable shared state within a circuit | Scoped service + `Action` event | Shopping cart, notification count, selected filters |

For parent→child one level: use `[Parameter]` / `EventCallback` (see `author-component` skill).
For persisting state across prerender→interactive: see `support-prerendering` skill.

## Workflow (quick reference)

1. Choose the mechanism from the table in Step 2
2. If crossing render mode boundaries → use `CascadingValueSource<T>` (Step 4)
3. Register in `Program.cs` with `AddCascadingValue(...)` and `isFixed: false`
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

## Step 4 — CascadingValueSource&lt;T&gt; for app-wide state

Register a `CascadingValueSource<T>` in DI when the value must be available to **all components regardless of render mode**.

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
@inject CascadingValueSource<ThemeInfo> ThemeSource

<button @onclick="ToggleDarkMode">Toggle theme</button>

@code {
    private bool isDark;

    private async Task ToggleDarkMode()
    {
        isDark = !isDark;
        // Replace the value entirely:
        var newTheme = new ThemeInfo { ButtonClass = isDark ? "btn-dark" : "btn-primary" };
        await ThemeSource.NotifyChangedAsync(newTheme);
    }
}
```

`NotifyChangedAsync()` (no argument) also works — mutate the object and then call it. `NotifyChangedAsync(newValue)` replaces the value and notifies in one step.

**Update protocol:** Whenever shared state changes, the component that changes it MUST inject `CascadingValueSource<T>` and call `NotifyChangedAsync()`. This is the only mechanism that triggers re-rendering in all `[CascadingParameter]` subscribers. Without this call, no subscribers update. Do not use `NavigationManager.Refresh()` or page reloads as a substitute.

**Rules:**
- `isFixed: false` enables change notifications. `isFixed: true` is better for truly static values (feature flags).
- **Crosses render mode boundaries** — works for per-page interactivity, global interactivity, and WebAssembly. Key advantage over `<CascadingValue>`.
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

**Fix:** Use `CascadingValueSource<T>` registered in DI (Step 4) or a scoped service (Step 5). Both cross boundaries because DI services are resolved per-circuit, not from the component tree.

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
- **Don't use `<CascadingValue>` in a static layout expecting it to reach interactive children** — it won't cross render mode boundaries. Use DI-registered `CascadingValueSource<T>` or scoped services.
- **Don't use `NavigationManager.Refresh(forceReload: true)` to propagate cascading value changes** — this destroys the circuit and forces a full page reload. Instead, inject `CascadingValueSource<T>` and call `NotifyChangedAsync(newValue)` to push updates to all `[CascadingParameter]` subscribers without a page reload.
- **Don't call `StateHasChanged` from a non-Blazor thread** — wrap in `InvokeAsync`. The framework throws `InvalidOperationException: The current thread is not associated with the Dispatcher`.
