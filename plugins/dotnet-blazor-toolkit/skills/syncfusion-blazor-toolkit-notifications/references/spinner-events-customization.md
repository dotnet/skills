# Spinner: Events & Customization

## Table of Contents

- [Event Callbacks Overview](#event-callbacks-overview)
- [Event 1: Created](#event-1-created)
- [Event 2: OnOpen](#event-2-onopen)
- [Event 3: OnClose](#event-3-onclose)
- [Event 4: Destroyed](#event-4-destroyed)
- [Complete Event Lifecycle Example](#complete-event-lifecycle-example)
- [Template-Based Customization](#template-based-customization)
- [Performance Considerations](#performance-considerations)
- [Common Event Patterns](#common-event-patterns)

---

## Event Callbacks Overview

The Spinner component provides four event callbacks for lifecycle management:

| Event | Callback Type | When Triggered | Use Case |
|-------|--------------|---------------|---------|
| `Created` | `EventCallback<object>` | After first render | Initialize spinner |
| `OnOpen` | `EventCallback<SpinnerEventArgs>` | Before spinner shows | Pre-display logic, can cancel via `args.Cancel` |
| `OnClose` | `EventCallback<SpinnerEventArgs>` | Before spinner hides | Cleanup operations, can cancel via `args.Cancel` |
| `Destroyed` | `EventCallback<object>` | After removal from DOM | Resource cleanup |

### SpinnerEventArgs Reference

The `SpinnerEventArgs` class passed to `OnOpen` and `OnClose` events:

```csharp
public class SpinnerEventArgs
{
    /// <summary>
    /// Gets or sets a value indicating whether to cancel the event.
    /// Set to true to prevent the associated action (opening or closing).
    /// </summary>
    public bool Cancel { get; set; }
}
```

### Usage Pattern
```csharp
private async Task HandleOpenAsync(SpinnerEventArgs args)
{
    // Check some condition
    if (someCondition)
    {
        // Prevent the action
        args.Cancel = true;
    }
}
```

---

## Event 1: Created

Fired after the spinner is created and rendered for the first time.

### Basic Usage
```csharp
<SfSpinner Visible="true" Created="@OnSpinnerCreated"></SfSpinner>

@code {
	private async Task OnSpinnerCreated(object args)
	{
		// Execute initialization logic
		Console.WriteLine("Spinner created");
	}
}
```

### Real-World Example: Initialize with Data
```csharp
<SfSpinner Created="@InitializeSpinner" @bind-Visible="@isLoading"></SfSpinner>

@code {
	private bool isLoading = false;
	private string message = "Starting process...";

	private async Task InitializeSpinner(object args)
	{
		// Set up initial state
		isLoading = true;
		message = "Processing started";
		
		// Perform initialization work
		await PerformInitialization();
	}

	private async Task PerformInitialization()
	{
		// Async initialization logic
		await Task.Delay(1000);
	}
}
```

---

## Event 2: OnOpen

Fired before the spinner becomes visible. Can be used to prevent showing or prepare state.

### Basic Usage
```csharp
<SfSpinner OnOpen="@OnSpinnerOpen" @bind-Visible="@showSpinner" Label="Loading..."></SfSpinner>

<button @onclick="@ToggleSpinner" style="position: relative; z-index: 10;">Toggle Spinner</button>

@code {
	private bool showSpinner = false;

	private void ToggleSpinner()
	{
		showSpinner = !showSpinner;
	}

	private async Task OnSpinnerOpen(SpinnerEventArgs args)
	{
		// args.Cancel = true to prevent opening
		Console.WriteLine("Spinner about to open");
	}
}
```

### Cancel Opening
```csharp
<SfSpinner OnOpen="@HandleOpenAsync" @bind-Visible="@showSpinner"></SfSpinner>

<button @onclick="@ToggleSpinner" style="position: relative; z-index: 10;">Show Spinner</button>

@code {
	private bool showSpinner = false;

	private async Task HandleOpenAsync(SpinnerEventArgs args)
	{
		// Check condition before allowing open
		if (!IsOperationAllowed())
		{
			args.Cancel = true; // Prevent spinner from showing
			Console.WriteLine("Operation not allowed - spinner opening cancelled");
		}
	}

	private bool IsOperationAllowed()
	{
		// Your validation logic
		return DateTime.Now.Hour > 9; // Only allow after 9 AM
	}

	private void ToggleSpinner()
	{
		showSpinner = !showSpinner;
	}
}
```

### Pre-Display Preparation
```csharp
<SfSpinner OnOpen="@PrepareForLoading" 
           Label="@loadingMessage" 
           @bind-Visible="@isLoading">
</SfSpinner>

<button @onclick="@StartProcess" style="position: relative; z-index: 10;">Start Process</button>

@code {
	private bool isLoading = false;
	private string loadingMessage = "";

	private void StartProcess()
	{
		isLoading = true;
	}

	private async Task PrepareForLoading(SpinnerEventArgs args)
	{
		// Prepare UI state - use console for quick transitions
		Console.WriteLine("Initializing...");
		
		// Disable other UI elements
		await Task.Delay(500);
		
		// Start actual work - update label for visible operation
		loadingMessage = "Processing...";
		StateHasChanged();
	}
}
```

---

## Event 3: OnClose

Fired before the spinner is hidden. Can be used to validate or delay closing.

### Basic Usage
```csharp
<SfSpinner OnClose="@OnSpinnerClose" @bind-Visible="@showSpinner" Label="Loading..."></SfSpinner>

<button @onclick="@ToggleSpinner" style="position: relative; z-index: 10;">Toggle Spinner</button>

@code {
	private bool showSpinner = true;

	private void ToggleSpinner()
	{
		showSpinner = !showSpinner;
	}

	private async Task OnSpinnerClose(SpinnerEventArgs args)
	{
		Console.WriteLine("Spinner about to close");
	}
}
```

### Validate Before Closing
```csharp
<SfSpinner OnClose="@BeforeClose" @bind-Visible="@isProcessing" Label="Processing..."></SfSpinner>

<button @onclick="@ToggleSpinner" style="position: relative; z-index: 10;">Toggle Spinner</button>

@code {
	private bool isProcessing = true;
	private int itemsProcessed = 0;
	private int totalItems = 100;

	private void ToggleSpinner()
	{
		isProcessing = !isProcessing;
	}

	private async Task BeforeClose(SpinnerEventArgs args)
	{
		// Check if processing is actually complete
		if (itemsProcessed < totalItems)
		{
			args.Cancel = true;
			Console.WriteLine($"Processing not complete: {itemsProcessed}/{totalItems}");
		}
		else
		{
			Console.WriteLine("All items processed - allowing close");
		}
	}
}
```

### Async Cleanup
```csharp
<SfSpinner OnClose="@PerformCleanup" @bind-Visible="@isWorking"></SfSpinner>

<button @onclick="@StopWork" style="position: relative; z-index: 10;">Stop</button>

@code {
	private bool isWorking = true;
	private bool cleanupComplete = false;

	private async Task PerformCleanup(SpinnerEventArgs args)
	{
		// Temporarily prevent close while cleaning up
		args.Cancel = true;
		
		try
		{
			// Perform async cleanup
			await SaveState();
			await ReleaseResources();
			cleanupComplete = true;
			
			// Now allow closing
			isWorking = false;
			args.Cancel = false;
			StateHasChanged();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Cleanup failed: {ex.Message}");
		}
	}

	private async Task SaveState()
	{
		await Task.Delay(500);
		Console.WriteLine("State saved");
	}

	private async Task ReleaseResources()
	{
		await Task.Delay(300);
		Console.WriteLine("Resources released");
	}

	private void StopWork()
	{
		isWorking = false;
	}
}
```

---

## Event 4: Destroyed

Fired after the spinner is completely removed from the DOM.

### Basic Usage
```csharp
<SfSpinner Visible="true" Destroyed="@OnSpinnerDestroyed"></SfSpinner>

@code {
	private async Task OnSpinnerDestroyed(object args)
	{
		// Execute cleanup logic
		Console.WriteLine("Spinner destroyed and removed from DOM");
	}
}
```

### Resource Cleanup
```csharp
<SfSpinner Destroyed="@CleanupResources" @bind-Visible="@showSpinner" Label="Loading..."></SfSpinner>

<button @onclick="@ToggleSpinner" style="position: relative; z-index: 10;">Toggle Spinner</button>

@code {
	private bool showSpinner = true;
	private List<Resource> allocatedResources = new();

	private async Task CleanupResources(object args)
	{
		// Release any allocated resources
		foreach (var resource in allocatedResources)
		{
			await resource.Dispose();
		}
		allocatedResources.Clear();
		
		Console.WriteLine("All resources cleaned up");
	}

	private void ToggleSpinner()
	{
		showSpinner = !showSpinner;
	}

	private class Resource
	{
		public async Task Dispose()
		{
			await Task.Delay(100);
		}
	}
}
```

---

## Complete Event Lifecycle Example

```csharp
<div class="event-demo">
	<h3>Spinner Event Lifecycle Demo</h3>
	
	<div style="margin-bottom: 10px;">
		<button @onclick="@AddAndShowSpinner" style="position: relative; z-index: 10;">Add & Show</button>
		<button @onclick="@HideSpinner" style="position: relative; z-index: 10;">Hide</button>
		<button @onclick="@RemoveSpinner" style="position: relative; z-index: 10;">Remove from DOM</button>
	</div>
	
	<div style="margin-top: 20px;">
		<h4>Event Log:</h4>
		<div class="event-log" style="height: 200px; overflow-y: auto; border: 1px solid #ccc; padding: 10px; position: relative; z-index: 11;">
			@foreach (var log in eventLogs)
			{
				<div>
					<small>[<strong>@log.Time</strong>] @log.Message</small>
				</div>
			}
		</div>
	</div>
	
	@if (spinnerInDOM)
	{
		<SfSpinner @bind-Visible="@spinnerVisible"
		           Created="@LogCreated"
		           OnOpen="@LogOnOpen"
		           OnClose="@LogOnClose"
		           Destroyed="@LogDestroyed"
		           Label="Processing...">
		</SfSpinner>
	}
</div>

@code {
	private bool spinnerVisible = false;
	private bool spinnerInDOM = false;
	private List<EventLog> eventLogs = new();

	private void AddAndShowSpinner()
	{
		AddLog("User clicked Add & Show");
		spinnerInDOM = true;
		StateHasChanged();
		spinnerVisible = true;
	}

	private void HideSpinner()
	{
		AddLog("User clicked Hide");
		spinnerVisible = false;
	}

	private void RemoveSpinner()
	{
		AddLog("User clicked Remove from DOM");
		spinnerInDOM = false;
	}

	private async Task LogCreated(object args)
	{
		AddLog("✓ Spinner Created event fired");
	}

	private async Task LogOnOpen(SpinnerEventArgs args)
	{
		AddLog("→ Spinner OnOpen event fired");
	}

	private async Task LogOnClose(SpinnerEventArgs args)
	{
		AddLog("← Spinner OnClose event fired");
	}

	private async Task LogDestroyed(object args)
	{
		AddLog("✗ Spinner Destroyed event fired");
	}

	private void AddLog(string message)
	{
		eventLogs.Add(new EventLog
		{
			Time = DateTime.Now.ToString("HH:mm:ss.fff"),
			Message = message
		});
		StateHasChanged();
	}

	private class EventLog
	{
		public string Time { get; set; }
		public string Message { get; set; }
	}
}
```

---

## Template-Based Customization

### SpinnerTemplates Component

The `SpinnerTemplates` component is a child component used to define custom visual content for the spinner, replacing the default animation. It must be placed as a child of `SfSpinner`.

#### SpinnerTemplates Properties

| Property | Type | Description |
|----------|------|-------------|
| `Template` | `RenderFragment?` | The custom content to display instead of the default spinner animation |

#### Basic Usage
```csharp
<SfSpinner @bind-Visible="@showSpinner">
    <SpinnerTemplates>
        <Template>
            <div class="custom-spinner">
                <div class="spinner-dots">
                    <span></span><span></span><span></span>
                </div>
                <span>Loading...</span>
            </div>
            <style>
                .custom-spinner {
                    display: flex;
                    flex-direction: column;
                    align-items: center;
                    gap: 12px;
                }

                .spinner-dots {
                    display: flex;
                    gap: 6px;
                }

                .spinner-dots span {
                    width: 10px;
                    height: 10px;
                    border-radius: 50%;
                    background: #0F6CBD;
                    animation: dotPulse 1.2s infinite ease-in-out;
                }

                .spinner-dots span:nth-child(1) { animation-delay: 0.00s; }
                .spinner-dots span:nth-child(2) { animation-delay: 0.15s; }
                .spinner-dots span:nth-child(3) { animation-delay: 0.30s; }

                @@keyframes dotPulse {
                    0%, 80%, 100% { transform: scale(0.6); opacity: 0.5; }
                    40% { transform: scale(1.0); opacity: 1; }
                }
            </style>
        </Template>
    </SpinnerTemplates>
</SfSpinner>

@code {
    private bool showSpinner = true;
}
```

**Best Practice Note**: When using custom templates, CSS styles should typically be placed in separate `.css` files or in `<style>` blocks outside of template content for better maintainability. The inline `<style>` example above is shown for self-contained demonstration purposes.

### Custom Template Examples

#### Bar Animation Template
```csharp
<SfSpinner Visible="true">
	<SpinnerTemplates>
		<Template>
			<div class="sf-spin-custom">
				<div class="bars">
					<span></span><span></span><span></span><span></span><span></span>
				</div>
				<div class="label-bar">Loading</div>
			</div>
			<style>
				.sf-spin-custom {
					display: flex;
					flex-direction: column;
					align-items: center;
					gap: 10px;
				}

				.bars {
					display: flex;
					gap: 4px;
					height: 26px;
					align-items: flex-end;
				}

				.bars span {
					width: 4px;
					background: #0F6CBD;
					border-radius: 2px;
					height: 8px;
					animation: barGrow 1s infinite ease-in-out;
				}

				.bars span:nth-child(1) { animation-delay: 0.00s; }
				.bars span:nth-child(2) { animation-delay: 0.10s; }
				.bars span:nth-child(3) { animation-delay: 0.20s; }
				.bars span:nth-child(4) { animation-delay: 0.30s; }
				.bars span:nth-child(5) { animation-delay: 0.40s; }

				@@keyframes barGrow {
					0%, 100% { height: 8px; opacity: .6; }
					50% { height: 26px; opacity: 1; }
				}
			</style>
		</Template>
	</SpinnerTemplates>
</SfSpinner>
```

#### Dot Pulse Template
```csharp
<SfSpinner Visible="true">
	<SpinnerTemplates>
		<Template>
			<div class="sf-spin-custom">
				<div class="dots">
					<span></span><span></span><span></span>
				</div>
				<div class="label-dot">Loading</div>
			</div>
			<style>
				.sf-spin-custom {
					display: flex;
					flex-direction: column;
					align-items: center;
					gap: 10px;
				}

				.dots {
					display: flex;
					gap: 8px;
					align-items: center;
					margin-top: 20px;
				}

				.dots span {
					width: 8px;
					height: 8px;
					border-radius: 50%;
					background: #0F6CBD;
					opacity: 0.35;
					animation: dotPulse 1.2s infinite ease-in-out;
				}

				.dots span:nth-child(2) { animation-delay: 0.15s; }
				.dots span:nth-child(3) { animation-delay: 0.30s; }

				@@keyframes dotPulse {
					0%, 80%, 100% { transform: scale(0.75); opacity: 0.35; }
					40% { transform: scale(1.15); opacity: 1; }
				}
			</style>
		</Template>
	</SpinnerTemplates>
</SfSpinner>
```

### Template Guidelines
- Template content replaces the default spinner animation entirely
- Custom templates should include `aria-busy` considerations for accessibility
- Ensure animation durations are reasonable (1-2s typical) to avoid user discomfort
- Include a descriptive label when using custom templates

---

## Performance Considerations

### Event Handler Best Practices

```csharp
<!-- ✅ Efficient: Minimal work in event handlers -->
<SfSpinner OnOpen="@QuickLogEvent" @bind-Visible="@showSpinner" Label="Loading..."></SfSpinner>

<button @onclick="@ToggleSpinner" style="position: relative; z-index: 10;">Toggle Spinner</button>

@code {
	private bool showSpinner = false;

	private void ToggleSpinner()
	{
		showSpinner = !showSpinner;
	}

	private async Task QuickLogEvent(SpinnerEventArgs args)
	{
		// Simple logging only
		Console.WriteLine("Event triggered");
		StateHasChanged();
	}
}
```

```csharp
<!-- ❌ Avoid: Heavy processing in event handlers -->
<SfSpinner OnOpen="@HeavyProcessing" @bind-Visible="@showSpinner" Label="Loading..."></SfSpinner>

<button @onclick="@ToggleSpinner" style="position: relative; z-index: 10;">Toggle Spinner</button>

@code {
	private bool showSpinner = false;

	private void ToggleSpinner()
	{
		showSpinner = !showSpinner;
	}

	private async Task HeavyProcessing(SpinnerEventArgs args)
	{
		// Don't do expensive operations here
		var result = await ExpensiveApiCall();
		// This blocks event handling
	}

	private Task<string> ExpensiveApiCall()
	{
		return Task.FromResult("result");
	}
}
```

---

## Common Event Patterns

### Pattern 1: Auto-Hide After Duration
```csharp
<SfSpinner Created="@SetAutoClose" @bind-Visible="@visible"></SfSpinner>

@code {
	private bool visible = true;

	private async Task SetAutoClose(object args)
	{
		await Task.Delay(3000);
		visible = false;
	}
}
```

### Pattern 2: Retry on Close
```csharp
<SfSpinner OnClose="@CheckForRetry" @bind-Visible="@retrying" Label="Loading..."></SfSpinner>

<button @onclick="@ToggleSpinner" style="position: relative; z-index: 10;">Toggle Spinner</button>

@code {
	private bool retrying = true;
	private int retryCount = 0;

	private void ToggleSpinner()
	{
		retryCount = 0;
		retrying = !retrying;
	}

	private async Task CheckForRetry(SpinnerEventArgs args)
	{
		if (HasError() && retryCount < 3)
		{
			args.Cancel = true;
			retryCount++;
			await Task.Delay(1000);
			retrying = true; // Re-show
		}
	}

	private bool HasError()
	{
		return new Random().Next(2) == 0;
	}
}
```

---

## Next Steps

- For accessibility: [Accessibility & Best Practices](accessibility-best-practices.md)
- Back to implementation: [Spinner Implementation](spinner-implementation.md)
