# Breaking Down Components

## Sibling Decomposition

When a component has two independent blocks (no shared state/handlers), extract each as a sibling.

```razor
<!-- CardTitle.razor -->
<div class="card-header">
    <h3>@Title</h3>
    <button @onclick="OnPin">Pin</button>
</div>
@code {
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public EventCallback OnPin { get; set; }
}
```

```razor
<!-- CardBody.razor -->
<div class="card-body">
    <p>@Description</p>
    <button @onclick="OnExpand">Read more</button>
</div>
@code {
    [Parameter, EditorRequired] public string Description { get; set; } = "";
    [Parameter] public EventCallback OnExpand { get; set; }
}
```

```razor
<!-- Card.razor — composes siblings -->
<div class="card">
    <CardTitle Title="@Title" OnPin="OnPin" />
    <CardBody Description="@Description" OnExpand="OnExpand" />
</div>
```

## List-Item Extraction

Extract complex item templates into their own component. Use `@key` for efficient diffing.

```razor
<!-- TaskItem.razor -->
<li class="task-item @(Task.IsComplete ? "done" : "")">
    <input type="checkbox" checked="@Task.IsComplete"
           @onchange="() => OnToggle.InvokeAsync(Task)" />
    <span>@Task.Title</span>
    <button @onclick="() => OnDelete.InvokeAsync(Task)">Delete</button>
</li>
@code {
    [Parameter, EditorRequired] public TaskModel Task { get; set; } = default!;
    [Parameter] public EventCallback<TaskModel> OnToggle { get; set; }
    [Parameter] public EventCallback<TaskModel> OnDelete { get; set; }
}
```

```razor
<!-- TaskList.razor -->
<ul class="task-list">
    @foreach (var task in Tasks)
    {
        <TaskItem @key="task.Id" Task="task"
                  OnToggle="HandleToggle" OnDelete="HandleDelete" />
    }
</ul>
```

## Cascading Context

Avoid parameter drilling through intermediate components. Cascade a context object or cascade the parent itself.

```razor
<!-- TabSet.razor — cascades itself -->
<CascadingValue Value="this" IsFixed="true">
    <ul class="nav nav-tabs">@ChildContent</ul>
</CascadingValue>
<div class="tab-body">@ActiveTab?.ChildContent</div>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
    public ITab? ActiveTab { get; private set; }

    public void AddTab(ITab tab) { if (ActiveTab is null) SetActiveTab(tab); }
    public void SetActiveTab(ITab tab)
    {
        if (ActiveTab != tab) { ActiveTab = tab; StateHasChanged(); }
    }
}
```

```razor
<!-- Tab.razor — receives parent via cascading parameter -->
@implements ITab
<li>
    <a @onclick="() => ContainerTabSet?.SetActiveTab(this)"
       class="nav-link @(ContainerTabSet?.ActiveTab == this ? "active" : "")">@Title</a>
</li>
@code {
    [CascadingParameter] private TabSet? ContainerTabSet { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    protected override void OnInitialized() => ContainerTabSet?.AddTab(this);
}
```

- Mark `IsFixed="true"` when the cascaded reference never changes — avoids unnecessary re-renders.
- For app-wide values (theme, auth), register via DI: `builder.Services.AddCascadingValue(sp => new ThemeInfo { ... });`
