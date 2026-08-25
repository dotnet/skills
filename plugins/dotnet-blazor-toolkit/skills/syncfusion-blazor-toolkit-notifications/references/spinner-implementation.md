# Spinner: Implementation Guide

## Table of Contents

- [Installation & Setup](#installation--setup)
- [Visibility Control](#visibility-control)
- [Label Configuration](#label-configuration)
- [CSS Class Customization](#css-class-customization)
- [Child Content Support](#child-content-support)
- [Real-World Implementation Patterns](#real-world-implementation-patterns)
- [Size and Positioning](#size-and-positioning)
- [Theme Integration](#theme-integration)

---

## Installation & Setup

### Step 1: Add NuGet Package

The Syncfusion Blazor Toolkit Spinner component is part of the main toolkit package:

```bash
dotnet add package Syncfusion.Blazor.Toolkit
```

**Version Compatibility:**
- .NET: 8.0 or higher
- Blazor: WebAssembly or Server

### Step 2: Register Services

Register Syncfusion services in your `Program.cs`:

```csharp
// Program.cs
using Syncfusion.Blazor.Toolkit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

// Register Syncfusion Blazor service
builder.Services.AddSyncfusionBlazorToolkit();

var app = builder.Build();
app.Run();
```

**For Blazor Server:**
```csharp
services.AddSyncfusionBlazorToolkit();
```

**For Blazor WebAssembly:**
```csharp
builder.Services.AddSyncfusionBlazorToolkit();
```

### Step 3: Add Theme CSS

Add the Syncfusion theme CSS to your HTML head. In `_Host.cshtml` (Blazor Server) or `index.html` (Blazor WebAssembly):

```html
<!-- Syncfusion Blazor Fluent Theme CSS -->
<link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />

<!-- Alternative Themes Available -->
<!-- Bootstrap Theme -->
<!-- <link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/bootstrap5.min.css" rel="stylesheet"> -->

<!-- Material Theme -->
<!-- <link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/material.min.css" rel="stylesheet"> -->
```

### Step 4: Add Global Imports

Add the Syncfusion namespace to your `_Imports.razor`:

```razor
@using Syncfusion.Blazor.Toolkit
```

For Spinner-specific types in your Razor pages, add individual namespaces:

```razor
@using Syncfusion.Blazor.Toolkit.Spinner
```

### Step 5: Basic Spinner Component

The simplest spinner shows the loading indicator with default styling:

```csharp
<SfSpinner Visible="true"></SfSpinner>
```

**What this renders:**
- A rotating circular loading animation
- Centered on the page
- Default size and colors from theme

---

## Visibility Control

### Show/Hide Spinner
Control spinner visibility with the `Visible` property:

```csharp
<SfSpinner Visible="true"></SfSpinner>

<!-- Hidden spinner -->
<SfSpinner Visible="false"></SfSpinner>
```

### Data Binding with Two-Way Binding
Use `@bind-Visible` for two-way binding to sync with component state:

```csharp
<SfSpinner @bind-Visible="@isLoading"></SfSpinner>

<button @onclick="@StartLoading">Start Loading</button>

@code {
	private bool isLoading = false;

	private void StartLoading()
	{
		isLoading = true;
		// Perform operation...
	}
}
```

### Conditional Rendering
Show spinner only when needed:

```csharp
@if (isLoading)
{
	<SfSpinner Visible="true" Label="Loading data..."></SfSpinner>
}
else
{
	<div>Data loaded successfully</div>
}

@code {
	private bool isLoading = true;

	protected override async Task OnInitializedAsync()
	{
		await LoadData();
		isLoading = false;
	}

	private async Task LoadData()
	{
		await Task.Delay(2000);
	}
}
```

---

## Label Configuration

The `Label` property displays text below the spinner animation.

### Custom Label
```csharp
<!-- Default label -->
<SfSpinner Visible="true"></SfSpinner>

<!-- Custom label -->
<SfSpinner Visible="true" Label="Loading data..."></SfSpinner>

<!-- Percentage loading with dynamic value -->
<SfSpinner Visible="true" Label="@($"{progress}%")"></SfSpinner>

@code {
    private int progress = 75;
}
```

### Dynamic Label Updates
```csharp
<SfSpinner @bind-Visible="@showSpinner" Label="@currentStatus"></SfSpinner>

<button @onclick="@UpdateStatus" disabled="@showSpinner">Update Status</button>

@code {
	private bool showSpinner = false;
	private string currentStatus = "Initializing...";

	private async Task UpdateStatus()
	{
		showSpinner = true;
        await Task.Delay(1000);
        
		currentStatus = "Processing...";
		StateHasChanged();
		
		// After some work
		await Task.Delay(1000);
		
		currentStatus = "Finalizing...";
		StateHasChanged();
		await Task.Delay(500);
		
		showSpinner = false;
	}
}
```

### No Label
```csharp
<!-- Empty string for no label -->
<SfSpinner Visible="true" Label=""></SfSpinner>
```

---

## CSS Class Customization

Apply custom CSS classes for styling. The spinner root element always has CSS class `e-spinner-pane`; custom classes from `CssClass` are appended to this.

### Single Class
```csharp
<SfSpinner Visible="true" CssClass="custom-spinner"></SfSpinner>

<style>
	/* Root class is always e-spinner-pane */
	.e-spinner-pane.custom-spinner .e-spinner-inner {
		/* Custom styling */
	}
</style>
```

### Multiple Classes
```csharp
<SfSpinner Visible="true" CssClass="large-spinner primary-color"></SfSpinner>
```

### Practical Styling Examples
```csharp
<!-- Large spinner -->
<div class="spinner-item">
	<SfSpinner CssClass="spinner-lg" Visible="true" Label="Large loader"></SfSpinner>
</div>

<!-- Small spinner -->
<div class="spinner-item">
	<SfSpinner CssClass="spinner-sm" Visible="true" Label="Small loader"></SfSpinner>
</div>

<!-- Custom colored spinner -->
<div class="spinner-item">
	<SfSpinner CssClass="spinner-custom-color" Visible="true" Label="Processing..."></SfSpinner>
</div>

<style>
	.spinner-item {
		position: relative;
		display: inline-flex;
		padding: 40px;
	}

	.e-spinner-pane.spinner-lg .e-spinner-inner {
		transform: scale(1.5);
	}

	.e-spinner-pane.spinner-sm .e-spinner-inner {
		transform: scale(0.7);
	}

	.e-spinner-pane.spinner-custom-color .e-spinner-inner .e-path-arc {
		stroke: #ff6b6b;
	}
</style>
```

---

## Child Content Support

The `ChildContent` parameter allows you to render nested content within the spinner container:

### Basic Child Content
```csharp
<SfSpinner Visible="true">
	<div style="color: black; text-align: center; padding: 20px;">
		<p>Your custom content here</p>
	</div>
</SfSpinner>
```

### Complex Child Content
```csharp
<SfSpinner Visible="@isProcessing">
	<div style="display: flex; flex-direction: column; align-items: center; gap: 16px;">
		<!-- Custom elements -->
		<div>
			<h4>Processing Your Request</h4>
			<p>Please wait, this may take a moment...</p>
		</div>
		
		<!-- Progress indicator -->
		<div style="width: 200px; height: 4px; background: #ccc; border-radius: 2px;">
			<div style="width: @($"{progress}%"); height: 100%; background: #007bff; border-radius: 2px; transition: width 0.3s;"></div>
		</div>
		
		<span>@($"{progress}% Complete")</span>
	</div>
</SfSpinner>

@code {
	private bool isProcessing = true;
	private int progress = 0;

	protected override async Task OnInitializedAsync()
	{
		for (int i = 0; i <= 100; i += 10)
		{
			progress = i;
			await Task.Delay(500);
			StateHasChanged();
		}
		isProcessing = false;
	}
}
```

---

## Real-World Implementation Patterns

### Pattern 1: Simple Loading State
```csharp
@page "/simple-loading"

<div class="container">
	@if (!isLoaded)
	{
		<SfSpinner Visible="true" Label="Loading content..."></SfSpinner>
	}
	else
	{
		<div>
			<h2>Content Loaded!</h2>
			<p>@contentData</p>
		</div>
	}
</div>

@code {
	private bool isLoaded = false;
	private string contentData = "";

	protected override async Task OnInitializedAsync()
	{
		await Task.Delay(2000);
		contentData = "This is the loaded content.";
		isLoaded = true;
	}
}
```

### Pattern 2: API Call with Spinner
```csharp
@page "/api-loading"

<div class="container">
	@if (isLoading)
	{
		<SfSpinner Visible="true" Label="Fetching data from server..."></SfSpinner>
	}
	else
	{
		<div class="content">
			<h2>Data Retrieved</h2>
			@foreach (var item in items)
			{
				<div class="item">@item.Name</div>
			}
		</div>
	}
</div>

@code {
	private bool isLoading = true;
	private List<DataItem> items = new();

	protected override async Task OnInitializedAsync()
	{
		items = await FetchDataFromApi();
		isLoading = false;
	}

	private async Task<List<DataItem>> FetchDataFromApi()
	{
		// Simulate API call
		await Task.Delay(2000);
		return new List<DataItem>
		{
			new() { Name = "Item 1" },
			new() { Name = "Item 2" }
		};
	}

	private class DataItem
	{
		public string Name { get; set; }
	}
}
```

### Pattern 3: Form Submission Spinner
```csharp
@page "/form-submission"

<EditForm Model="@formModel" OnSubmit="@HandleSubmit">
	<div class="form-group">
		<label>Name</label>
		<InputText @bind-Value="formModel.Name" class="form-control"></InputText>
	</div>
	
	<div class="form-group">
		<label>Email</label>
		<InputText @bind-Value="formModel.Email" class="form-control"></InputText>
	</div>
	
	<button type="submit" disabled="@isSubmitting" class="btn btn-primary">
		@(isSubmitting ? "Submitting..." : "Submit")
	</button>
</EditForm>

@if (isSubmitting)
{
	<SfSpinner Visible="true" Label="Submitting form..."></SfSpinner>
}

@if (submissionStatus != null)
{
	<div class="alert alert-@(submissionStatus.IsSuccess ? "success" : "danger")">
		@submissionStatus.Message
	</div>
}

@code {
	private FormModel formModel = new();
	private bool isSubmitting = false;
	private SubmissionStatus submissionStatus = null;

	private async Task HandleSubmit()
	{
		isSubmitting = true;
		submissionStatus = null;

		try
		{
			await Task.Delay(1500); // Simulate API call
			submissionStatus = new SubmissionStatus
			{
				IsSuccess = true,
				Message = "Form submitted successfully!"
			};
		}
		catch (Exception ex)
		{
			submissionStatus = new SubmissionStatus
			{
				IsSuccess = false,
				Message = $"Error: {ex.Message}"
			};
		}
		finally
		{
			isSubmitting = false;
		}
	}

	private class FormModel
	{
		public string Name { get; set; }
		public string Email { get; set; }
	}

	private class SubmissionStatus
	{
		public bool IsSuccess { get; set; }
		public string Message { get; set; }
	}
}
```

### Pattern 4: Multiple Task Spinners
```csharp
<div class="tasks-container">
	@foreach (var task in tasks)
	{
		<div class="task-item">
			<h4>@task.Name</h4>
			
			@if (task.IsRunning)
			{
				<SfSpinner Visible="true" Label="@task.Status"></SfSpinner>
			}
			else
			{
				<div class="alert alert-@(task.IsSuccess ? "success" : "danger")">
					@(task.IsSuccess ? "✓ Completed" : "✗ Failed")
				</div>
			}
		</div>
	}
</div>

@code {
	private List<TaskItem> tasks = new();

	protected override async Task OnInitializedAsync()
	{
		tasks = new List<TaskItem>
		{
			new() { Name = "Task 1", Status = "Processing..." },
			new() { Name = "Task 2", Status = "Waiting..." },
			new() { Name = "Task 3", Status = "Queued..." }
		};

		for (int i = 0; i < tasks.Count; i++)
		{
			tasks[i].IsRunning = true;
			StateHasChanged();
			
			await Task.Delay(2000);
			
			tasks[i].IsRunning = false;
			tasks[i].IsSuccess = new Random().Next(2) == 0;
			StateHasChanged();
		}
	}

	private class TaskItem
	{
		public string Name { get; set; }
		public string Status { get; set; }
		public bool IsRunning { get; set; } = false;
		public bool IsSuccess { get; set; } = false;
	}
}
```

---

## Size and Positioning

### Size Property
The `Size` property sets the spinner's dimensions directly. Values can be numeric (interpreted as pixels) or include units:

```csharp
<!-- Small spinner (numeric values are interpreted as pixels) -->
<SfSpinner Size="24" Visible="true" Label="Small loader"></SfSpinner>

<!-- Medium spinner -->
<SfSpinner Size="36" Visible="true" Label="Medium loader"></SfSpinner>

<!-- Large spinner -->
<SfSpinner Size="48" Visible="true" Label="Large loader"></SfSpinner>

<!-- Explicit pixel unit -->
<SfSpinner Size="50px" Visible="true" Label="Explicit size"></SfSpinner>
```

### Thickness Property
The `Thickness` property controls the stroke width of the spinner's arc:

```csharp
<!-- Thin stroke -->
<SfSpinner Thickness="2" Visible="true" Label="Thin spinner"></SfSpinner>

<!-- Default stroke -->
<SfSpinner Thickness="4" Visible="true" Label="Default spinner"></SfSpinner>

<!-- Thick stroke -->
<SfSpinner Thickness="6" Visible="true" Label="Thick spinner"></SfSpinner>
```

### CSS-Based Sizing (Alternative)
```csharp
<!-- Large spinner via CSS -->
<div class="spinner-item">
	<SfSpinner CssClass="spinner-large" Visible="true" Label="Large loading"></SfSpinner>
</div>

<!-- Small spinner via CSS -->
<div class="spinner-item">
	<SfSpinner CssClass="spinner-small" Visible="true" Label="Small loading"></SfSpinner>
</div>

<style>
	.spinner-item {
		position: relative;
		display: inline-flex;
		padding: 40px;
	}

	.e-spinner-pane.spinner-large .e-spinner-inner {
		transform: scale(1.5);
	}

	.e-spinner-pane.spinner-small .e-spinner-inner {
		transform: scale(0.7);
	}
</style>
```

### Positioned Spinner (Overlay)
```csharp
<div style="position: relative; width: 100%; height: 300px; background: #f0f0f0;">
	<!-- Content area -->
	<div style="padding: 20px;">
		<h3>Content Goes Here</h3>
	</div>
	
	<!-- Overlay spinner -->
	@if (isLoading)
	{
		<div style="position: absolute; top: 0; left: 0; right: 0; bottom: 0; 
		            display: flex; align-items: center; justify-content: center; 
		            background: rgba(255,255,255,0.8);">
			<SfSpinner Visible="true" Label="Loading..."></SfSpinner>
		</div>
	}
</div>

@code {
	private bool isLoading = true;
}
```

### ZIndex for Overlay Scenarios
The `ZIndex` property controls the stacking order for overlay scenarios:

```csharp
<!-- Default z-index (auto) -->
<SfSpinner Visible="true" Label="Default z-index"></SfSpinner>

<!-- High z-index for modal overlays -->
<SfSpinner ZIndex="1000" Visible="true" Label="Modal spinner"></SfSpinner>
```

The `CssClass="e-spin-overlay"` can be combined with `ZIndex` for full-container overlay behavior:

```csharp
<SfSpinner @bind-Visible="@showSpinner" CssClass="e-spin-overlay" ZIndex="1000"></SfSpinner>
```

**Note:** The `e-spin-overlay` class is a **user-defined** CSS class that you need to define in your own styles. The `e-` prefix is a Syncfusion naming convention indicating it should be added to the element for specific behavior. You must provide the overlay styles yourself:

```css
/* User-defined overlay styles - e-spin-overlay is a naming convention */
/* You must define these styles in your CSS */
.e-spin-overlay {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(255,255,255,0.8);
}
```

### Programmatic Control with ShowAsync/HideAsync
For direct programmatic control outside of property binding:

```csharp
<SfSpinner @ref="Spinner" Visible="false" Label="Processing..."></SfSpinner>
<button @onclick="@StartProcess">Start Process</button>

@code {
	private SfSpinner? Spinner;

	private async Task StartProcess()
	{
		// Show the spinner programmatically
		await Spinner.ShowAsync();
		
		// Simulate work
		await Task.Delay(2000);
		
		// Hide the spinner programmatically
		await Spinner.HideAsync();
	}
}
```

---

## Theme Integration

### Default Theme
The Spinner inherits colors from the current Syncfusion theme:

```csharp
<SfSpinner Visible="true" Label="Using default theme..."></SfSpinner>
```

### Theme Variations
Different themes available through CSS:

```html
<!-- Bootstrap theme -->
<link href="_content/Syncfusion.Blazor.Toolkit/styles/bootstrap.min.css" rel="stylesheet" />

<!-- Fluent theme -->
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />

<!-- Material theme -->
<link href="_content/Syncfusion.Blazor.Toolkit/styles/material.min.css" rel="stylesheet" />
```

---

## Common Gotchas

### Issue 1: Spinner Not Showing
```csharp
<!-- ❌ Might not render if parent has layout issues -->
<div style="display: none;">
	<SfSpinner Visible="true"></SfSpinner>
</div>

<!-- ✅ Make sure parent is visible -->
<div>
	<SfSpinner Visible="true"></SfSpinner>
</div>
```

### Issue 2: Label Not Displaying
```csharp
<!-- ❌ Label requires explicit text -->
<SfSpinner Visible="true"></SfSpinner>

<!-- ✅ Add Label parameter -->
<SfSpinner Visible="true" Label="Loading..."></SfSpinner>
```

### Issue 3: Event Handling
```csharp
<!-- ✅ Valid event handler with explicit lambda -->
<SfSpinner Visible="true" OnOpen="@HandleOpen"></SfSpinner>

@code {
	private async Task HandleOpen(SpinnerEventArgs args)
	{
		// Event handling
	}
}
```

---

## Next Steps

- Learn about events: [Spinner Events & Customization](spinner-events-customization.md)
- For accessibility: [Accessibility & Best Practices](accessibility-best-practices.md)
