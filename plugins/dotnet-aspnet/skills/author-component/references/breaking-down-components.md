# Strategies for Breaking Down Components

## Sibling decomposition

When a component contains two independent blocks (they don't share state or handlers), extract each into its own component and place them as siblings.

**Before — single `CardContent` with unrelated sections:**
```razor
<div class="card">
    <div class="card-header">
        <h3>@Title</h3>
        <button @onclick="OnPin">Pin</button>
    </div>
    <div class="card-body">
        <p>@Description</p>
        <button @onclick="OnExpand">Read more</button>
    </div>
</div>

@code {
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public string Description { get; set; } = "";
    [Parameter] public EventCallback OnPin { get; set; }
    [Parameter] public EventCallback OnExpand { get; set; }
}
```

**After — `CardTitle` + `CardBody`, each owning only the parameters and handlers they use:**

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
<!-- Card.razor — composes the siblings -->
<div class="card">
    <CardTitle Title="@Title" OnPin="OnPin" />
    <CardBody Description="@Description" OnExpand="OnExpand" />
</div>
```

Each sibling is independently testable and has a minimal parameter surface.

## List-item extraction

When rendering a list where each item has complex markup or behavior, extract the item template into its own component.

**Before — inline item rendering:**
```razor
<ul class="task-list">
    @foreach (var task in tasks)
    {
        <li class="task-item @(task.IsComplete ? "done" : "")">
            <input type="checkbox" checked="@task.IsComplete"
                   @onchange="() => ToggleTask(task)" />
            <span>@task.Title</span>
            <span class="due">@task.DueDate.ToShortDateString()</span>
            <button @onclick="() => DeleteTask(task)">Delete</button>
        </li>
    }
</ul>
```

**After — extracted `TaskItem` component:**

```razor
<!-- TaskItem.razor -->
<li class="task-item @(Task.IsComplete ? "done" : "")">
    <input type="checkbox" checked="@Task.IsComplete"
           @onchange="() => OnToggle.InvokeAsync(Task)" />
    <span>@Task.Title</span>
    <span class="due">@Task.DueDate.ToShortDateString()</span>
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
                  OnToggle="HandleToggle"
                  OnDelete="HandleDelete" />
    }
</ul>

@code {
    [Parameter] public List<TaskModel> Tasks { get; set; } = [];
    [Parameter] public EventCallback<TaskModel> OnToggle { get; set; }
    [Parameter] public EventCallback<TaskModel> OnDelete { get; set; }

    private Task HandleToggle(TaskModel t) => OnToggle.InvokeAsync(t);
    private Task HandleDelete(TaskModel t) => OnDelete.InvokeAsync(t);
}
```

Use `@key` on the extracted component in loops so Blazor can efficiently track and diff items.

## Cascading a context object

When too many related callbacks and values need to flow through several levels of nesting, create a context object and cascade it. This avoids "parameter drilling" through intermediate components.

A component can cascade **itself** to its children, exposing methods they can call:

```razor
<!-- TabSet.razor — cascades itself as a context -->
<CascadingValue Value="this" IsFixed="true">
    <ul class="nav nav-tabs">
        @ChildContent
    </ul>
</CascadingValue>

<div class="tab-body">
    @ActiveTab?.ChildContent
</div>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }

    public ITab? ActiveTab { get; private set; }

    public void AddTab(ITab tab)
    {
        if (ActiveTab is null)
            SetActiveTab(tab);
    }

    public void SetActiveTab(ITab tab)
    {
        if (ActiveTab != tab)
        {
            ActiveTab = tab;
            StateHasChanged();
        }
    }
}
```

```razor
<!-- Tab.razor — receives the parent as a cascading parameter -->
@implements ITab

<li>
    <a @onclick="ActivateTab"
       class="nav-link @(ContainerTabSet?.ActiveTab == this ? "active" : "")"
       role="button">
        @Title
    </a>
</li>

@code {
    [CascadingParameter] private TabSet? ContainerTabSet { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized() => ContainerTabSet?.AddTab(this);
    private void ActivateTab() => ContainerTabSet?.SetActiveTab(this);
}
```

Mark cascading values as `IsFixed="true"` when the reference does not change to avoid unnecessary re-renders of all consumers.

For cascading values that serve the entire application (theme info, auth state), prefer registering them via DI in `Program.cs`:

```csharp
builder.Services.AddCascadingValue(sp => new ThemeInfo { ButtonClass = "btn-primary" });
```
