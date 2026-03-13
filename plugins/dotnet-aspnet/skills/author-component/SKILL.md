---
name: author-component
description: Use this skill when you need to create or review a Blazor component. Covers component design principles (UI decomposition, data flow, size guidelines), parameter definitions (Parameter, EditorRequired), child content with RenderFragment, EventCallback for parent-child communication, cascading parameters, route directives, layouts, code-behind patterns, async programming rules, and rendering behavior. Also use as a reference for correct patterns when reviewing component structure.
---

# Author Blazor Component

## Component Design Principles

### Decompose the UI into a natural hierarchy

Break the UI into a tree of components that mirrors the visual structure. Each distinct region becomes its own component.

For example, a data grid component might decompose as:

```
DataGrid<T>
├── GridToolbar            (search box, filter toggles, "Add" button)
├── GridHeader             (column headings with sort indicators)
├── GridBody
│   └── GridRow<T>         (one per item: cells, inline actions)
│       └── GridCell       (single value, optional edit mode)
├── GridPager              (page size selector, prev/next, page numbers)
└── GridEmptyState         (illustration + message when Items is empty)
```

### Identify all component states up front

Before writing markup, enumerate every visual state the component can be in. Then make sure the template handles each one explicitly — missing states cause blank screens or stale data.

| State | Example |
|-------|---------|
| **Loading** | Data is being fetched; show a spinner or skeleton |
| **Empty** | Fetch completed but returned zero items; show an empty-state message |
| **Loaded** | Data is available; render the normal content |
| **Error** | The fetch or operation failed; show an error message with a retry option |
| **Unauthorized** | User lacks permission; show an access-denied message |

```razor
@if (error is not null)
{
    <div class="alert alert-danger">
        @error
        <button @onclick="LoadData">Retry</button>
    </div>
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
    <GridPager TotalCount="totalCount" Page="page" OnPageChanged="HandlePageChanged" />
}
```

### Data flows top-down

Pass data from parent to children via `[Parameter]` properties. Parents own the state; children receive it as read-only inputs.

```razor
<ProductHeader Name="@product.Name" Price="@product.Price" Rating="@product.Rating" />
<ReviewList Reviews="@product.Reviews" />
```

### Events flow bottom-up

When a child needs to notify a parent, expose an `EventCallback` parameter. The parent passes a handler; the child invokes it.

```razor
<!-- Child component -->
<button @onclick="() => OnAddToCart.InvokeAsync(Quantity)">Add to Cart</button>

@code {
    [Parameter] public int Quantity { get; set; }
    [Parameter] public EventCallback<int> OnAddToCart { get; set; }
}
```

```razor
<!-- Parent component -->
<AddToCartPanel Quantity="@selectedQty" OnAddToCart="HandleAddToCart" />

@code {
    private async Task HandleAddToCart(int qty) { /* update cart state */ }
}
```

### Parameters are inputs — never mutate them

Parameters are set by the framework after construction. Do **not** write to `[Parameter]` properties inside the component. If you need mutable local state derived from a parameter, copy it to a private field:

```csharp
[Parameter] public string InitialText { get; set; } = "";

private string currentText = "";

protected override void OnParametersSet()
{
    currentText = InitialText;
}
```

### Keep components small and focused

| Guideline | Target |
|-----------|--------|
| Total lines of code (markup + `@code`) | 100–200 (investigate at ~300, refactor above ~500) |
| Cyclomatic complexity of the render logic | ≤ 10 |
| Cyclomatic complexity per lifecycle/event handler method | ≤ 10 |
| Parameters | 0–10 |
| Event handlers | 0–10 |

A component's purpose and structure should be self-evident. If you need to scroll to understand a component, it is too large. See `references/breaking-down-components.md` for extraction techniques.

### Keep lifecycle and event handler methods simple

Component code should be focused on **UI concerns**: reading parameters, managing local UI state, and invoking rendering. Business logic, validation rules, data transformation, and orchestration belong in injected services.

```csharp
// Wrong — component does too much
protected override async Task OnInitializedAsync()
{
    var raw = await Http.GetFromJsonAsync<List<OrderDto>>("api/orders");
    orders = raw!
        .Where(o => o.Status != "Cancelled")
        .OrderByDescending(o => o.CreatedAt)
        .Select(o => new OrderViewModel(o.Id, o.Total, o.CreatedAt))
        .ToList();
}

// Correct — component delegates to a service
protected override async Task OnInitializedAsync()
{
    orders = await OrderService.GetActiveOrdersAsync();
}
```

## Component File Patterns

### Single-file (.razor)

```razor
@page "/my-route"
@using System.ComponentModel.DataAnnotations
@implements IDisposable
@inject NavigationManager Navigation

<h3>@Title</h3>
@ChildContent

@code {
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

### Code-behind (.razor + .razor.cs)

Split markup and logic into two files with matching names:

**MyComponent.razor:**
```razor
<h3>@Title</h3>
@ChildContent
```

**MyComponent.razor.cs:**
```csharp
public partial class MyComponent : ComponentBase
{
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

Use code-behind when the `@code` block exceeds ~50 lines.

## Parameters

### Rules

- Parameters **must** be public auto-properties with `{ get; set; }` (analyzer BL0007).
- Do **not** use `required` or `init` — the component framework sets parameters after construction.
- Do **not** write to parameter properties from within the component — copy to a local field instead.
- Use `[EditorRequired]` (not `[Required]`) to mark parameters the consumer must provide.

### Basic parameters

```csharp
[Parameter] public string Title { get; set; } = "";
[Parameter] public int Count { get; set; }
[Parameter] public bool IsVisible { get; set; } = true;
[Parameter, EditorRequired] public string Label { get; set; } = "";
```

### Capture unmatched values

Pass arbitrary HTML attributes to an underlying element:

```csharp
[Parameter(CaptureUnmatchedValues = true)]
public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
```

## Child Content

### Single child content

```csharp
[Parameter] public RenderFragment? ChildContent { get; set; }
```

### Templated (generic) content

```csharp
[Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }
[Parameter] public IReadOnlyList<TItem> Items { get; set; } = [];
```

```razor
@foreach (var item in Items)
{
    @ItemTemplate?.Invoke(item)
}
```

### Multiple render fragments

Use named render fragments when a component needs multiple content slots:

```csharp
[Parameter] public RenderFragment? Header { get; set; }
[Parameter] public RenderFragment? Body { get; set; }
[Parameter] public RenderFragment? Footer { get; set; }
```

## EventCallback

Use `EventCallback` / `EventCallback<T>` for parent-child communication. Never use raw `Action` or `Func` delegates as parameters — `EventCallback` automatically triggers re-rendering of the parent.

```csharp
[Parameter] public EventCallback OnClick { get; set; }
[Parameter] public EventCallback<string> OnSearch { get; set; }
```

```razor
<button @onclick="OnClick">Click me</button>
<input @oninput="e => OnSearch.InvokeAsync(e.Value?.ToString())" />
```

### Always assign component methods to event handlers

When the component needs to re-render in response to an event, pass a component method (or `EventCallback`) — not a delegate from an external object — to the event handler attribute.

**Correct — component method, triggers re-render:**
```razor
<button @onclick="HandleClick">Click</button>

@code {
    [Inject] private CartService CartService { get; set; } = default!;

    private async Task HandleClick()
    {
        await CartService.AddItemAsync(itemId);
    }
}
```

**Wrong — external object delegate, parent will NOT re-render:**
```razor
<button @onclick="CartService.AddItemAsync">Click</button>
```

## Directives

| Directive | Purpose | Example |
|-----------|---------|---------|
| `@page` | Routable component with route template | `@page "/items/{Id:int}"` |
| `@layout` | Specify layout for a page | `@layout MainLayout` |
| `@implements` | Implement an interface | `@implements IDisposable` |
| `@inherits` | Inherit a base class | `@inherits CustomBase` |
| `@inject` | DI injection | `@inject HttpClient Http` |
| `@rendermode` | Set render mode | `@rendermode InteractiveServer` |
| `@attribute` | Apply an attribute | `@attribute [Authorize]` |
| `@typeparam` | Generic type parameter | `@typeparam TItem` |

### Route parameters

```razor
@page "/items/{Id:int}"
@page "/items/{Id:int}/{Slug}"

@code {
    [Parameter] public int Id { get; set; }
    [Parameter] public string? Slug { get; set; }
}
```

## Lifecycle Methods

Override in order of execution:

1. `SetParametersAsync(ParameterView parameters)` — raw parameter assignment (advanced)
2. `OnInitialized` / `OnInitializedAsync` — runs once on first render
3. `OnParametersSet` / `OnParametersSetAsync` — runs after parameters are set (every render)
4. `OnAfterRender(bool firstRender)` / `OnAfterRenderAsync(bool firstRender)` — runs after DOM update

- Perform async data loading in `OnInitializedAsync`.
- Access JS interop only in `OnAfterRenderAsync` (DOM must exist).
- Implement `IAsyncDisposable` to clean up resources when the component is removed. See `references/component-disposal.md` for patterns.

## Async Programming Rules

Blazor runs inside a synchronization context that guarantees single-threaded access. See `references/async-programming-rules.md` for full rules.

Key rules:
- Always `await` every async operation — never leave a `Task` unobserved.
- **Forbidden in components:** `Task.Run`, `.Result`, `.Wait()`, `ContinueWith`, `Thread.Start`, concurrent collections.
- Call `StateHasChanged` only for intermediate updates in multi-await handlers or external event callbacks (timer, WebSocket).
- Use `InvokeAsync` to marshal back onto the sync context from external events.
- Use `DispatchExceptionAsync` for fire-and-forget tasks to route errors to error boundaries.

## Cascading Parameters

Cascading values flow data down the component hierarchy without explicit parameter passing at each level. They are useful for cross-cutting concerns like theming, authorization state, or parent-child coordination.

For detailed patterns and `[CascadingParameter]` usage, see the `implement-data-binding` skill.

## Common Mistakes to Avoid

- Using `required` or `init` on `[Parameter]` properties — causes runtime failures.
- Logic in parameter getters/setters — triggers BL0007 analyzer warning.
- Setting `[Parameter]` properties from within the component — use a local backing field instead.
- Putting `@ref` and `@rendermode` on the same element — not supported.
- Calling JS interop in `OnInitializedAsync` — JS is only available in `OnAfterRenderAsync`.
- Using `Action`/`Func` instead of `EventCallback` for event parameters — parent won't re-render.
- Using `Task.Run`, `.Result`, `.Wait()`, `ContinueWith`, or `Thread.Start` — deadlocks or escapes the sync context.
- Calling `StateHasChanged` in every event handler — unnecessary and adds rendering overhead.
- Passing an external object's method directly to an `@on*` attribute — the component won't re-render.
- Discarding a `Task` (`_ = SomethingAsync()`) without wrapping it in try/catch + `DispatchExceptionAsync` — exceptions are silently lost.
- Forgetting to cancel async work on disposal — causes memory leaks and operations on disposed components.
