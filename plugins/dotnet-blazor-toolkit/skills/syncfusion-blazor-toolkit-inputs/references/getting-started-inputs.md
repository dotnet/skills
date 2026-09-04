# Getting Started with Syncfusion Blazor Toolkit Inputs

## Table of Contents
- [Installation & Setup](#installation--setup)
- [CSS Imports and Theming](#css-imports-and-theming)
- [Basic Component Initialization](#basic-component-initialization)
- [Common Event Patterns](#common-event-patterns)
- [State Management Basics](#state-management-basics)
- [Namespace & Using Statements](#namespace--using-statements)

## Installation & Setup

### Step 1: Add NuGet Package

The Syncfusion Blazor Toolkit is available via NuGet. Add it to your Blazor project:

```bash
dotnet add package Syncfusion.Blazor.Toolkit
```

**Version Compatibility:**
- Syncfusion Blazor Toolkit: Latest stable version
- .NET: 8.0 or higher
- Blazor: WebAssembly or Server

### Step 2: Register Services

In your `Program.cs` (Blazor Server or WebAssembly), register Syncfusion services:

```csharp
// Program.cs
using Syncfusion.Blazor;

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

## CSS Imports and Theming

### Required CSS Files

Add Syncfusion CSS files to your HTML head. In `_Layout.cshtml` (Blazor Server) or `index.html` (Blazor WebAssembly):

```html
<!-- Syncfusion Blazor Fluent Theme CSS -->
<link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />

<!-- Alternative Themes Available -->
<!-- Bootstrap Theme -->
<!-- <link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/bootstrap5.css" rel="stylesheet"> -->

<!-- Material Theme -->
<!-- <link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/material.css" rel="stylesheet"> -->

<!-- Tailwind Theme -->
<!-- <link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/tailwind.css" rel="stylesheet"> -->
```

### Available Themes

| Theme | Use Case |
|-------|----------|
| **fluent.css** | Modern, minimal design (default) |
| **bootstrap5.css** | Bootstrap-compatible styling |
| **material.css** | Material Design principles |
| **tailwind.css** | Tailwind CSS integration |
| **highcontrast.css** | Accessibility-focused |

### Custom Theming

Override Syncfusion CSS variables in your custom CSS file:

```css
/* app.css */
:root {
    --sf-primary: #0078d4;
    --sf-surface: #ffffff;
    --sf-surface-variant: #f3f3f3;
    --sf-outline: #d0d0d0;
    --sf-error: #d32f2f;
}

body {
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}
```

Reference this file after the Syncfusion CSS:

```html
<link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
<link href="app.css" rel="stylesheet">
```

## Basic Component Initialization

### Minimal Component Setup

Every input component requires at minimum:

```razor
@page "/basic-input"
@using Syncfusion.Blazor.Toolkit.Inputs

<div class="input-container">
    <label>Text Input:</label>
    <SfTextBox Placeholder="Enter text"></SfTextBox>
</div>
```

### Component with Value Binding

```razor
<SfTextBox @bind-Value="@userInput" Placeholder="Enter your name"></SfTextBox>

@code {
    private string userInput = "";
}
```

### Component with Initial Value

```razor
<SfNumericTextBox TValue="int" Value="42"></SfNumericTextBox>
```

### Component with Reference

Store a component reference for programmatic access:

```razor
<SfTextBox @ref="myTextBox" Placeholder="Type here"></SfTextBox>

<button @onclick="FocusInput">Focus Input</button>

@code {
    private SfTextBox myTextBox;
    
    private async Task FocusInput()
    {
        await myTextBox.FocusAsync();
    }
}
```

## Common Event Patterns

### Change Event Handler

Triggered when the component value changes:

```razor
<SfTextBox ValueChange="OnTextChanged"></SfTextBox>

@code {
    private void OnTextChanged(ChangedEventArgs args)
    {
        Console.WriteLine($"New value: {args.Value}");
        Console.WriteLine($"Previous value: {args.PreviousValue}");
    }
}
```

### Focus and Blur Events

```razor
<SfTextBox 
    OnFocus="@OnFocus" 
    OnBlur="@OnBlur">
</SfTextBox>

@code {
    private void OnFocus(FocusInEventArgs args)
    {
        Console.WriteLine("Input focused");
    }

    private void OnBlur(FocusOutEventArgs args)
    {
        Console.WriteLine("Input blurred");
    }
}
```

### Input Event (Real-time Typing)

```razor
<SfTextBox OnInput="@OnInput"></SfTextBox>

@code {
    private void OnInput(InputEventArgs args)
    {
        // Fires on every keystroke
        Console.WriteLine($"Current input: {args.Value}");
    }
}
```

### Combining Multiple Events

```razor
<SfTextBox 
    @bind-Value="@text"
    ValueChange="OnValueChanged"
    OnFocus="@OnFocused"
    OnBlur="@OnBlurred">
</SfTextBox>

@code {
    private string text = "";

    private void OnValueChanged(ChangedEventArgs args)
    {
        Console.WriteLine($"Value changed: {args.Value}");
    }

    private void OnFocused(FocusInEventArgs args)
    {
        Console.WriteLine("Focused");
    }

    private void OnBlurred(FocusOutEventArgs args)
    {
        Console.WriteLine("Blurred");
    }
}
```

## State Management Basics

### Simple Component State

```razor
@page "/counter"
@using Syncfusion.Blazor.Toolkit.Inputs

<div>
    <p>Count: @count</p>
    <SfNumericTextBox TValue="int" @bind-Value="@count" Min="0" Max="100"></SfNumericTextBox>
</div>

@code {
    private int count = 0;
}
```

### Form State Management

```razor
@page "/form"
@using Syncfusion.Blazor.Toolkit.Inputs

<div class="form-container">
    <div class="form-group">
        <label>Name:</label>
        <SfTextBox @bind-Value="@formData.Name" Placeholder="Enter name"></SfTextBox>
    </div>

    <div class="form-group">
        <label>Email:</label>
        <SfTextBox Type="InputType.Email" @bind-Value="@formData.Email"></SfTextBox>
    </div>

    <div class="form-group">
        <label>Age:</label>
        <SfNumericTextBox TValue="int?" @bind-Value="@formData.Age" Min="0" Max="150"></SfNumericTextBox>
    </div>

    <button @onclick="SubmitForm">Submit</button>
</div>

@code {
    private FormModel formData = new();

    private void SubmitForm()
    {
        Console.WriteLine($"Name: {formData.Name}, Email: {formData.Email}, Age: {formData.Age}");
    }

    public class FormModel
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public int? Age { get; set; }
    }
}
```

### Conditional State Management

```razor
@using Syncfusion.Blazor.Toolkit.Inputs

<div>
    <div style="display: flex; align-items: center; gap: 10px;">
        <label style="margin: 0;">Show Advanced Options:</label>
        <SfSwitch TChecked="bool" @bind-Checked="@isEnabled" OnLabel="ON" OffLabel="OFF" />
    </div>

    @if (isEnabled)
    {
        <div style="margin-top: 10px;">
            <SfTextBox @bind-Value="@advancedOption" Placeholder="Advanced setting"></SfTextBox>
        </div>
    }
</div>

@code {
    private bool isEnabled = false;
    private string advancedOption = "";
}
```

### Reactive Updates with StateHasChanged

```razor
@using Syncfusion.Blazor.Toolkit.Inputs

<div>
    <SfTextBox ValueChange="OnInputChange"></SfTextBox>
    <p>@message</p>
</div>

@code {
    private string message = "";

    private async Task OnInputChange(ChangedEventArgs args)
    {
        message = $"You entered: {args.Value}";
        await Task.Delay(2000);
        message = "";
        StateHasChanged();
    }
}
```

## Namespace & Using Statements

### Required Namespaces

Add these using statements to your Razor components:

```csharp
// Input components
using Syncfusion.Blazor.Toolkit.Inputs;
```

### Global Imports (_Imports.razor)

Add to your `_Imports.razor` to avoid repeating using statements:

```razor
@using Syncfusion.Blazor.Toolkit
```

### Complete Setup Example

```razor
@* _Imports.razor *@
@using System.Globalization
@using Microsoft.AspNetCore.Components
@using Syncfusion.Blazor.Toolkit

@* Counter.razor *@
@page "/counter"
@using Syncfusion.Blazor.Toolkit.Inputs

<div class="counter-demo">
    <h2>Counter Component</h2>
    <p>Current value: @count</p>
    
    <SfNumericTextBox 
        TValue="int" 
        @bind-Value="@count" 
        Min="0" 
        Max="100"
        Placeholder="Enter a number">
    </SfNumericTextBox>
    
    <button @onclick="Increment">+1</button>
    <button @onclick="Decrement">-1</button>
</div>

@code {
    private int count = 0;

    private void Increment() => count++;
    private void Decrement() => count--;
}
```

## Next Steps

- **Basic Inputs:** See [textbox-textarea.md](textbox-textarea.md) for TextBox and TextArea
- **Numeric Input:** See [numeric-currency.md](numeric-currency.md) for NumericTextBox with formatting
- **Selection Controls:** See [checkbox-radio-switch.md](checkbox-radio-switch.md) for CheckBox, RadioButton, and Switch
- **Accessibility:** See [input-accessibility.md](input-accessibility.md) for WCAG compliance
