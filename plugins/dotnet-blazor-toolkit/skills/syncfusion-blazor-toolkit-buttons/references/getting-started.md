# Getting Started with Syncfusion Blazor Buttons

## Table of Contents
1. [Installation Requirements](#installation-requirements)
2. [Adding to Your Project](#adding-to-your-project)
3. [First Button Component](#first-button-component)
4. [Basic Click Handling](#basic-click-handling)
5. [Form Integration](#form-integration)
6. [Using the Content Property](#using-the-content-property)
7. [Testing in Samples](#testing-in-samples)
8. [Common Setup Issues](#common-setup-issues)

## Installation Requirements

The Syncfusion Blazor Toolkit button components are part of the main toolkit package. Ensure you have:

- **.NET SDK:** .NET 8 or later
- **Blazor Project:** WebAssembly or Server-side
- **NuGet Package:** `Syncfusion.Blazor.Toolkit` latest version
- **CSS Files:** Theme CSS imported in your layout

## Adding to Your Project

### 1. Install via NuGet
```bash
dotnet add package Syncfusion.Blazor.Toolkit
```

### 2. Register in Program.cs (Blazor Server)
```csharp
builder.Services.AddSyncfusionBlazorToolkit();
```

Or for Blazor WebAssembly:
```csharp
builder.Services.AddSyncfusionBlazorToolkit();
```

### 3. Add Theme CSS in Layout
In `_Host.cshtml` or `index.html`, add the theme stylesheet:
```html
<link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```

### 4. Add Global Imports
In `_Imports.razor`, add:
```razor
@using Syncfusion.Blazor.Toolkit
```

For button components with icons or enum values like `IconPosition`, add the buttons namespace to your page or component:
```razor
@using Syncfusion.Blazor.Toolkit.Buttons
```

## First Button Component

### Minimal Button
```razor
<SfButton Content="Click Me" />
```

This renders a standard button with default styling. The button is clickable but doesn't perform any action without an event handler.

### Button with Text Only
```razor
<SfButton Content="Submit" />
<SfButton Content="Cancel" />
<SfButton Content="Save Changes" />
```

The `Content` property accepts any string value and displays it as button text.

## Basic Click Handling

### Simple Click Handler
```razor
<SfButton Content="Say Hello" OnClick="OnClick" />

@code {
    private void OnClick(MouseEventArgs args)
    {
        Console.WriteLine("Button was clicked!");
    }
}
```

### Async Click Handler
```razor
<SfButton Content="Load Data" OnClick="LoadDataAsync" />

@code {
    private async Task LoadDataAsync(MouseEventArgs args)
    {
        await Task.Delay(1000); // Simulate API call
        Console.WriteLine("Data loaded!");
    }
}
```

### Click with Parameter
```razor
<SfButton Content="Delete Item 1" OnClick="@((e) => DeleteItem(1))" />
<SfButton Content="Delete Item 2" OnClick="@((e) => DeleteItem(2))" />

@code {
    private void DeleteItem(int id)
    {
        Console.WriteLine($"Deleting item {id}");
    }
}
```

## Form Integration

### Button Types for Forms
```razor
<EditForm Model="@formModel" OnValidSubmit="HandleSubmit">
    <DataAnnotationsValidator />
    
    <div>
        <label>Name:</label>
        <SfTextBox @bind-Value="formModel.Name" />
    </div>
    
    <div>
        <SfButton Type="ButtonType.Submit" Content="Submit" IsPrimary="true" />
        <SfButton Type="ButtonType.Reset" Content="Reset" />
    </div>
</EditForm>

@code {
    private FormModel formModel = new();
    
    private void HandleSubmit(EditContext editContext)
    {
        Console.WriteLine($"Form submitted: {formModel.Name}");
    }
    
    public class FormModel
    {
        public string Name { get; set; } = "";
    }
}
```

**Note:** The `Type` property accepts `ButtonType.Button` (default), `ButtonType.Submit`, or `ButtonType.Reset`.

## Using the Content Property

### String Content
```razor
<SfButton Content="@currentLabel" />

@code {
    private string currentLabel = "Click Here";
}
```

### Dynamic Content
```razor
<SfButton Content="@(isLoading ? "Loading..." : "Load")" />

@code {
    private bool isLoading = false;
}
```

### Content from Variables
```razor
<SfButton Content="@GetButtonText()" />

@code {
    private string GetButtonText()
    {
        return DateTime.Now.Hour < 12 ? "Good Morning" : "Good Afternoon";
    }
}
```

## Testing in Samples

### Running the Sample App
```bash
cd samples/Blazor.Toolkit.Samples
dotnet watch run
```

The application starts at `https://localhost:7145` (or similar port).

### Adding a Test Page
1. Create `Pages/ButtonTest.razor`
2. Add button examples
3. Navigate to `/button-test` to test

### Example Test Page
```razor
@page "/button-test"

<h3>Button Component Tests</h3>

<div>
    <h4>Basic Button</h4>
    <SfButton Content="Click Me" />
</div>

<div>
    <h4>Button with Click Handler</h4>
    <SfButton Content="Submit" OnClick="OnSubmit" />
    <p>@clickMessage</p>
</div>

@code {
    private string clickMessage = "";
    
    private void OnSubmit(MouseEventArgs args)
    {
        clickMessage = "Submitted at " + DateTime.Now.ToString("HH:mm:ss");
    }
}
```

## Common Setup Issues

### Issue 1: Button Not Rendering
**Symptom:** No button appears on page
**Solution:** 
- Verify `@using` statement includes `Syncfusion.Blazor.Toolkit.Buttons`
- Check that services are registered in `Program.cs`
- Ensure CSS file is imported in layout

### Issue 2: Styling Looks Wrong
**Symptom:** Button appears unstyled or broken
**Solution:**
- Verify theme CSS is loaded: Check browser DevTools Network tab
- Confirm correct theme CSS path: `<link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />` Replace fluent.min.css with the relevant stylesheet (bootstrap.min.css, material.min.css, tailwind.min.css, etc.) depending on the theme you’re using.
- Ensure theme matches your layout expectations

### Issue 3: Click Handler Not Firing
**Symptom:** Button clicks don't trigger handler
**Solution:**
- Verify `OnClick` directive is present
- Check handler method has correct signature
- For async handlers, use `OnClick="OnClickAsync"` with `async Task` return type
- Ensure button is not disabled

### Issue 4: Content Not Updating
**Symptom:** Button text doesn't change when variable changes
**Solution:**
- Wrap content in `@(...)` expression for binding
- Use `StateHasChanged()` if manual refresh needed
- Verify variable is not hardcoded

## Next Steps

- Learn styling in **button-fundamentals.md**
- Add icons in **icons-and-content.md**
- Handle events in **events-and-callbacks.md**
- Explore button groups in **button-group.md**
