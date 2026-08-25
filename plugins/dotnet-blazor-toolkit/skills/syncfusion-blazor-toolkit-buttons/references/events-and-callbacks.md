# Events and Callbacks

## Table of Contents
1. [Click Event Handling](#click-event-handling)
2. [EventCallback Parameter](#eventcallback-parameter)
3. [Created Event Lifecycle](#created-event-lifecycle)
4. [Async Event Handling](#async-event-handling)
5. [Event Arguments](#event-arguments)
6. [State Management with Events](#state-management-with-events)
7. [Debugging Events](#debugging-events)

## Click Event Handling

### Basic Click Handler
```razor
<SfButton Content="Click Me" OnClick="OnClick" />

@code {
    private void OnClick(MouseEventArgs args)
    {
        Console.WriteLine("Button clicked!");
    }
}
```

When the button is clicked, the `OnClick` method executes.

### Click with Event Arguments
```razor
<SfButton Content="Click" OnClick="OnClickWithArgs" />

@code {
    private void OnClickWithArgs(MouseEventArgs e)
    {
        Console.WriteLine($"Button clicked at X:{e.ClientX}, Y:{e.ClientY}");
        Console.WriteLine($"Button: {e.Button}");
        Console.WriteLine($"CtrlKey: {e.CtrlKey}");
    }
}
```

### Multiple Buttons with Different Handlers
```razor
<SfButton Content="Save" OnClick="OnSave" />
<SfButton Content="Cancel" OnClick="OnCancel" />
<SfButton Content="Delete" OnClick="OnDelete" />

@code {
    private void OnSave(MouseEventArgs args)
    {
        Console.WriteLine("Saving...");
    }
    
    private void OnCancel(MouseEventArgs args)
    {
        Console.WriteLine("Cancelled");
    }
    
    private void OnDelete(MouseEventArgs args)
    {
        Console.WriteLine("Deleting...");
    }
}
```

### Click with Lambda Expression
```razor
<SfButton Content="Increment" OnClick="@((args) => count++)" />
<p>Count: @count</p>

@code {
    private int count = 0;
}
```

### Click with Parameters
```razor
<SfButton Content="Select Item 1" OnClick="@((e) => SelectItem(1))" />
<SfButton Content="Select Item 2" OnClick="@((e) => SelectItem(2))" />
<p>Selected: @selectedId</p>

@code {
    private int selectedId = 0;
    
    private void SelectItem(int id)
    {
        selectedId = id;
    }
}
```

## EventCallback Parameter

### Understanding EventCallback
`EventCallback` is Blazor's mechanism for component communication. In buttons, it enables:
- Parent-to-child event propagation
- Proper change detection
- Support for async handlers

### Creating a Button Component Wrapper
```razor
<!-- Custom component: MyButton.razor -->
@inherits ComponentBase

<SfButton Content="@Content" 
          IsPrimary="@IsPrimary" 
          OnClick="HandleClick" />

@code {
    [Parameter]
    public string Content { get; set; } = "Button";
    
    [Parameter]
    public bool IsPrimary { get; set; } = false;
    
    [Parameter]
    public EventCallback<MouseEventArgs> OnClick { get; set; }
    
    private async Task HandleClick(MouseEventArgs e)
    {
        await OnClick.InvokeAsync(e);
    }
}
```

Usage:
```razor
<MyButton Content="Click Me" OnClick="@HandleMyClick" />

@code {
    private async Task HandleMyClick(MouseEventArgs e)
    {
        Console.WriteLine("Button clicked via callback");
    }
}
```

## Created Event Lifecycle

### Created Event
```razor
<SfButton Content="Component Created" Created="@OnCreated" />

@code {
    private void OnCreated(object args)
    {
        Console.WriteLine("SfButton component rendering is complete");
    }
}
```

The `Created` event fires after the component has finished rendering in the DOM.

### Using Created for Initialization
```razor
<SfButton Content="@buttonLabel" Created="@OnButtonCreated" />

@code {
    private string buttonLabel = "Loading...";

    private async void OnButtonCreated(object args)
    {
        // Perform initialization after render
        await Task.Delay(500);
        buttonLabel = "Ready!";

        await InvokeAsync(StateHasChanged);
    }
}
```

### Created Event on SfButtonGroup
The `Created` event also works on `SfButtonGroup`:
```razor
<SfButtonGroup Created="@OnGroupCreated">
    <Button>Option 1</Button>
    <Button>Option 2</Button>
</SfButtonGroup>

@code {
    private void OnGroupCreated(object args)
    {
        Console.WriteLine("ButtonGroup has been created");
    }
}
```

### Lifecycle Order
```razor
<!-- Component initialization order -->
<SfButton Content="Tracking Lifecycle" 
          Created="@OnCreated" 
          OnClick="OnClick" />

@code {
    protected override void OnInitialized()
    {
        Console.WriteLine("1. OnInitialized");
    }
    
    protected override async Task OnInitializedAsync()
    {
        Console.WriteLine("2. OnInitializedAsync");
    }
    
    protected override void OnParametersSet()
    {
        Console.WriteLine("3. OnParametersSet");
    }
    
    protected override async Task OnParametersSetAsync()
    {
        Console.WriteLine("4. OnParametersSetAsync");
    }
    
    protected override void OnAfterRender(bool firstRender)
    {
        Console.WriteLine($"5. OnAfterRender (firstRender: {firstRender})");
    }
    
    private void OnCreated(object args)
    {
        Console.WriteLine("6. Created event");
    }
    
    private void OnClick(MouseEventArgs args)
    {
        Console.WriteLine("7. Click event");
    }
}
```

## Async Event Handling

### Async Click Handler
```razor
<SfButton Content="@(isLoading ? "Loading..." : "Load Data")" 
          Disabled="@isLoading"
          OnClick="LoadDataAsync" />

@code {
    private bool isLoading = false;
    
    private async Task LoadDataAsync(MouseEventArgs args)
    {
        isLoading = true;
        try
        {
            // Simulate API call
            await Task.Delay(2000);
            Console.WriteLine("Data loaded!");
        }
        finally
        {
            isLoading = false;
        }
    }
}
```

### Async with Error Handling
```razor
<SfButton Content="Submit" Disabled="@isProcessing" OnClick="SubmitAsync" />
<p style="color: @(errorMessage != null ? "red" : "green")">
    @(errorMessage ?? successMessage)
</p>

@code {
    private bool isProcessing = false;
    private string? errorMessage;
    private string? successMessage;
    
    private async Task SubmitAsync(MouseEventArgs args)
    {
        isProcessing = true;
        errorMessage = null;
        successMessage = null;
        
        try
        {
            await Task.Delay(1000);
            
            // Simulate potential error (10% chance)
            if (new Random().Next(10) == 0)
                throw new Exception("Submission failed");
            
            successMessage = "Submitted successfully!";
        }
        catch (Exception ex)
        {
            errorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            isProcessing = false;
        }
    }
}
```

### Async with Timeout
```razor
<SfButton Content="Fetch (timeout: 5s)" OnClick="FetchWithTimeout" />
<p>@message</p>

@code {
    private string message = "";
    
    private async Task FetchWithTimeout(MouseEventArgs args)
    {
        message = "Fetching...";
        
        try
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await SimulateApiCallAsync(cts.Token);
                message = "Success!";
            }
        }
        catch (OperationCanceledException)
        {
            message = "Request timed out after 5 seconds";
        }
        catch (Exception ex)
        {
            message = $"Error: {ex.Message}";
        }
    }
    
    private async Task SimulateApiCallAsync(CancellationToken ct)
    {
        await Task.Delay(3000, ct); // Simulates work
    }
}
```

## Event Arguments

### MouseEventArgs Properties
The `MouseEventArgs` object contains:

| Property | Type | Description |
|----------|------|-------------|
| `ClientX` | double | Mouse X position relative to viewport |
| `ClientY` | double | Mouse Y position relative to viewport |
| `Button` | long | Mouse button (0=left, 1=middle, 2=right) |
| `Buttons` | long | Pressed buttons (bitmask) |
| `CtrlKey` | bool | Ctrl key pressed |
| `ShiftKey` | bool | Shift key pressed |
| `AltKey` | bool | Alt key pressed |
| `MetaKey` | bool | Meta/Command key pressed |

### Using MouseEventArgs
```razor
<SfButton Content="Click Info" OnClick="ShowClickInfo" />
<p>@clickInfo</p>

@code {
    private string clickInfo = "";
    
    private void ShowClickInfo(MouseEventArgs e)
    {
        clickInfo = $"Button: {e.Button}, Ctrl: {e.CtrlKey}, Shift: {e.ShiftKey}";
    }
}
```

## State Management with Events

### Simple State Update
```razor
<SfButton Content="Toggle" OnClick="ToggleState" />
<p>State: @(isVisible ? "Visible" : "Hidden")</p>

@code {
    private bool isVisible = false;
    
    private void ToggleState(MouseEventArgs args)
    {
        isVisible = !isVisible;
    }
}
```

### List State Management
```razor
<SfButton Content="Add Item" OnClick="AddItem" />
<SfButton Content="Clear All" OnClick="ClearItems" />

<ul>
    @foreach (var item in items)
    {
        <li>@item</li>
    }
</ul>

@code {
    private List<string> items = new();
    private int itemCount = 0;
    
    private void AddItem(MouseEventArgs args)
    {
        items.Add($"Item {++itemCount}");
    }
    
    private void ClearItems(MouseEventArgs args)
    {
        items.Clear();
        itemCount = 0;
    }
}
```

### Parent-Child State Sharing
```razor
<!-- Parent component -->
<div>
    <ChildCounter InitialCount="count" OnCountChanged="@UpdateCount" />
    <p>Parent sees: @count</p>
</div>

@code {
    private int count = 0;
    
    private void UpdateCount(int newCount)
    {
        count = newCount;
    }
}

<!-- ChildCounter.razor -->
@inherits ComponentBase

<p>Child: @LocalCount</p>
<SfButton Content="Increment" OnClick="IncrementAsync" />

@code {
    [Parameter]
    public int InitialCount { get; set; }
    
    [Parameter]
    public EventCallback<int> OnCountChanged { get; set; }
    
    private int LocalCount { get; set; }
    
    protected override void OnInitialized()
    {
        LocalCount = InitialCount;
    }
    
    private async Task IncrementAsync(MouseEventArgs args)
    {
        LocalCount++;
        await OnCountChanged.InvokeAsync(LocalCount);
    }
}
```

## Debugging Events

### Console Logging
```razor
<SfButton Content="Debug Click" OnClick="DebugClick" />

@code {
    private void DebugClick(MouseEventArgs args)
    {
        Console.WriteLine("Click event fired");
        Console.WriteLine($"Timestamp: {DateTime.Now:HH:mm:ss.fff}");
    }
}
```

### Event Tracking
```razor
<SfButton Content="Track" OnClick="TrackEvent" />
<div>
    <h4>Event Log:</h4>
    <ul>
        @foreach (var log in eventLog)
        {
            <li>@log</li>
        }
    </ul>
</div>

@code {
    private List<string> eventLog = new();
    
    private void TrackEvent(MouseEventArgs args)
    {
        eventLog.Add($"[{DateTime.Now:HH:mm:ss}] Click event");
        
        // Keep only last 10 events
        if (eventLog.Count > 10)
            eventLog.RemoveAt(0);
    }
}
```

### Conditional Event Logging
```razor
<SfButton Content="Conditional Log" OnClick="ConditionalLog" />

@code {
    private int clickCount = 0;
    private bool debugMode = false;
    
    private void ConditionalLog(MouseEventArgs args)
    {
        clickCount++;
        
        if (debugMode)
        {
            Console.WriteLine($"Click #{clickCount}");
            Console.WriteLine($"Stack: {Environment.StackTrace}");
        }
    }
}
```

## Edge Cases and Gotchas

### Gotcha 1: Async Void Handlers
```razor
<!-- Avoid: Async void loses error handling -->
<SfButton OnClick="BadAsyncVoidHandler" />

<!-- Correct: Use async Task -->
<SfButton OnClick="GoodAsyncTaskHandler" />

@code {
    private async void BadAsyncVoidHandler(MouseEventArgs args)  // ❌ Bad
    {
        await Task.Delay(1000);
    }
    
    private async Task GoodAsyncTaskHandler(MouseEventArgs args)  // ✅ Good
    {
        await Task.Delay(1000);
    }
}
```

### Gotcha 2: StaleClosures
```razor
<!-- Problem: Loop variable captured incorrectly -->
@for (int i = 0; i < 3; i++)
{
    <SfButton Content="@($"Button {i}")" OnClick="@(() => OnClick(i))" />
}

<!-- Solution: Capture in local variable -->
@for (int i = 0; i < 3; i++)
{
    int index = i;
    <SfButton Content="@($"Button {index}")" OnClick="@(() => OnClick(index))" />
}

@code {
    private void OnClick(int buttonIndex)
    {
        Console.WriteLine($"Clicked button {buttonIndex}");
    }
}
```

### Gotcha 3: Not Awaiting EventCallback
```razor
<!-- Problem: Not awaiting might skip code -->
private async Task HandleClick()
{
    await OnClick.InvokeAsync(null);  // ✅ Correct: await
    // Next code runs after callback completes
}
```
