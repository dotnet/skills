# Button Fundamentals

## Table of Contents
1. [Button States](#button-states)
2. [Disabled State](#disabled-state)
3. [Primary Button Styling](#primary-button-styling)
4. [CSS Class Combinations](#css-class-combinations)
5. [HTML Attributes](#html-attributes)
6. [Icon Support](#icon-support)
7. [Toggle Button](#toggle-button)
8. [Form Button Types](#form-button-types)
9. [Standard vs Primary](#standard-vs-primary)
10. [Practical Examples](#practical-examples)

## Button States

A button can exist in different states depending on user interaction and application logic:

- **Enabled:** Normal state, user can interact
- **Disabled:** User cannot click or interact
- **Hover:** Visual feedback when mouse hovers
- **Pressed/Active:** Feedback during click

The Syncfusion button component automatically handles hover and pressed states through CSS.

> **Note:** For button components with icons or enum values like `IconPosition` and `ButtonType`, add the buttons namespace to your page or component:
> ```razor
> @using Syncfusion.Blazor.Toolkit.Buttons
> ```

## Disabled State

### Basic Disabled Button
```razor
<SfButton Content="Disabled Button" Disabled="true" />
```

When a button is disabled (`Disabled="true"`):
- Cannot be clicked
- Cannot receive focus via Tab key
- Appears grayed out with reduced opacity
- Screen readers announce it as disabled

### Conditional Disabled State
```razor
<SfButton Content="@(isProcessing ? "Processing..." : "Submit")" 
          Disabled="@isProcessing" 
          OnClick="OnSubmit" />

@code {
    private bool isProcessing = false;
    
    private async Task OnSubmit(MouseEventArgs args)
    {
        isProcessing = true;
        try
        {
            await PerformOperation();
        }
        finally
        {
            isProcessing = false;
        }
    }
    
    private async Task PerformOperation()
    {
        await Task.Delay(2000); // Simulate work
    }
}
```

### Multi-button Coordination
```razor
<div>
    <input type="text" value="@formData" @oninput="@HandleInput" placeholder="Enter data" />
    
    <div style="margin-top: 12px;">
        <SfButton Content="Save" Disabled="@(!hasChanges)" OnClick="Save" />
        <SfButton Content="Reset" Disabled="@(!hasChanges)" OnClick="Reset" />
        <SfButton Content="Delete" Disabled="@isProcessing" OnClick="Delete" />
    </div>
</div>

@code {
    private string formData = "";
    private bool hasChanges = false;
    private bool isProcessing = false;

    private void HandleInput(ChangeEventArgs e)
    {
        formData = e.Value?.ToString() ?? "";
        hasChanges = true;
    }
    
    private async Task Save(MouseEventArgs args)
    {
        isProcessing = true;
        await Task.Delay(1000);
        isProcessing = false;
        hasChanges = false;
        formData = "";
    }
    
    private void Reset(MouseEventArgs args)
    {
        hasChanges = false;
        formData = "";
    }
    
    private async Task Delete(MouseEventArgs args)
    {
        isProcessing = true;
        await Task.Delay(1000);
        isProcessing = false;
    }
}
```

## Primary Button Styling

### Standard Button
```razor
<SfButton Content="Standard Button" />
```

### Primary Button
```razor
<SfButton Content="Primary Action" IsPrimary="true" />
```

The `IsPrimary` property applies prominent styling to indicate the primary action:
- Darker background color
- Higher contrast
- Often used for "Save", "Submit", "Continue" actions
- Should typically be only one per action area

### Best Practice: Primary Actions
```razor
<div style="display: flex; gap: 8px;">
    <!-- Primary action on the right -->
    <SfButton Content="Cancel" />
    <SfButton Content="Save Changes" IsPrimary="true" />
</div>
```

## CSS Class Combinations

### Using CssClass Property
```razor
<SfButton Content="Custom Style" CssClass="e-primary e-large" />
```

The `CssClass` property accepts space-separated CSS class names that apply to the button element.

### Syncfusion Built-in Classes

| Class | Purpose |
|-------|---------|
| `e-primary` | Primary button styling |
| `e-success` | Success/positive action styling |
| `e-warning` | Warning action styling |
| `e-danger` | Destructive action styling |
| `e-info` | Informational button styling |

### Combining Multiple Classes
```razor
<!-- Large primary danger button -->
<SfButton Content="Delete Permanently" CssClass="e-danger e-primary" />

<!-- Small success button -->
<SfButton Content="Approve" CssClass="e-success" />

<!-- Warning info button -->
<SfButton Content="Caution" CssClass="e-warning e-info" />
```

### Custom CSS with Built-in Classes
```razor
<SfButton Content="Custom Button" CssClass="e-primary my-custom-class" />

<style>
    .my-custom-class {
        border-radius: 20px;
        font-weight: bold;
    }
    
    @@media (max-width: 768px) {
        .my-custom-class {
            border-radius: 12px;
            font-size: 0.9em;
            padding: 8px 12px;
        }
    }
</style>
```

## HTML Attributes

### Capturing HTML Attributes
The `HtmlAttributes` parameter captures unmatched HTML attributes:

```razor
<SfButton Content="Button with Attributes" 
          title="Click to submit form"
          data-value="123"
          aria-label="Submit Button" />
```

These attributes are applied to the underlying `<button>` HTML element.

### Accessibility Attributes
```razor
<SfButton Content="Accessible Button"
          title="Save your changes"
          aria-label="Save Changes"
          aria-describedby="save-help" />
<small id="save-help">Saves current form data</small>
```

### Data Attributes for JavaScript
```razor
<SfButton Content="Track Me" 
          data-analytics="button-click"
          data-form-id="contact-form"
          OnClick="OnClick" />
```

### SfButton Properties

| Property | Type | Purpose | Default |
|----------|------|---------|---------|
| `Content` | string | Button text | "" |
| `CssClass` | string | Custom CSS classes | "" |
| `Disabled` | bool | Enable/disable state | false |
| `HtmlAttributes` | Dictionary<string, object> | Capture HTML attributes (title, data-*, aria-*) | {} |
| `IconCss` | string | Icon CSS classes | "" |
| `IconPosition` | IconPosition | Icon placement (Left, Right, Top, Bottom) | Left |
| `IsPrimary` | bool | Primary styling | false |
| `IsToggle` | bool | Enable toggle behavior | false |
| `Type` | ButtonType | Form button type (Button, Submit, Reset) | Button |
| `OnClick` | EventCallback<MouseEventArgs> | Click event handler | - |
| `Created` | EventCallback<object> | Lifecycle event after first render | - |

> **Note:** The `ChildContent` parameter is available but marked as an internal API (`[EditorBrowsable(EditorBrowsableState.Never)]`). For simple text content, prefer using the `Content` property instead.

### Icon Support

The `IconCss` property allows you to add icons to buttons for better visual representation of actions.

### Icon with Text
```razor
<SfButton Content="Add" IconCss="e-icons e-plus" />
<SfButton Content="Save" IconCss="e-icons e-save" />
<SfButton Content="Refresh" IconCss="e-icons e-refresh" />
```

### Icon Position

Use `IconPosition` to control icon placement.

```razor
<SfButton Content="Send" IconCss="e-icons e-send" IconPosition="IconPosition.Right" />
<SfButton Content="Export" IconCss="e-icons e-export" IconPosition="IconPosition.Left" />
<SfButton Content="Upload" IconCss="e-icons e-upload" IconPosition="IconPosition.Top" />
```

### Icon Only Button
```razor
<SfButton IconCss="e-icons e-settings" aria-label="Settings" />
<SfButton IconCss="e-icons e-close" CssClass="e-danger" aria-label="Close" />
```

### Best Practice:
- Always include `aria-label` for icon-only buttons

## Toggle Button

The `IsToggle` property enables a button to act as a toggle (two-state button). Toggle buttons automatically manage their `aria-pressed` attribute for accessibility.

### Basic Toggle
```razor
<SfButton IsToggle="true" Content="Toggle Me" OnClick="ToggleState" />

@code {
    private bool isOn;

    private void ToggleState(MouseEventArgs args)
    {
        isOn = !isOn;
    }
}
```

### Toggle with Visual State
```razor
<SfButton IsToggle="true"
          Content="@(isBold ? "Bold On" : "Bold Off")"
          OnClick="ToggleBold"
          CssClass="@(isBold ? "e-primary" : "e-outline")"
          IconCss="e-icons e-bold" />

@code {
    private bool isBold;

    private void ToggleBold(MouseEventArgs args)
    {
        isBold = !isBold;
    }
}
```

### Icon-only Toggle
```razor
<SfButton IsToggle="true"
          IconCss="e-icons e-settings"
          CssClass="@(isSettingsOn ? "e-primary" : "e-outline")"
          OnClick="ToggleSettings"
          aria-label="Settings" />

@code {
    private bool isSettingsOn;

    private void ToggleSettings(MouseEventArgs args)
    {
        isSettingsOn = !isSettingsOn;
    }
}
```

### Toggle Button Accessibility
Toggle buttons automatically apply `aria-pressed="true"` or `aria-pressed="false"` based on their internal state. The component handles this automatically, but you should use `aria-label` for icon-only toggle buttons to provide a meaningful name.

## Form Button Types

The `Type` property controls how the button behaves in HTML forms. It accepts values from the `ButtonType` enumeration.

### Button Types

| Type | Description |
|------|-------------|
| `ButtonType.Button` | Standard button, no form interaction (default) |
| `ButtonType.Submit` | Submits the form when clicked |
| `ButtonType.Reset` | Resets form fields to their initial values |

### Submit Button
```razor
<EditForm Model="@formModel" OnValidSubmit="HandleSubmit">
    <SfTextBox @bind-Value="formModel.Name" />
    <SfButton Type="ButtonType.Submit" Content="Submit" IsPrimary="true" />
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

### Reset Button
```razor
<EditForm Model="@formModel" OnValidSubmit="HandleSubmit" @onreset="HandleReset">
    <SfTextBox @bind-Value="formModel.Name" />
    <SfButton Type="ButtonType.Submit" Content="Submit" IsPrimary="true" />
    <SfButton Type="ButtonType.Reset" Content="Reset" />
</EditForm>

@code {
    private FormModel formModel = new();

    private void HandleSubmit(EditContext editContext)
    {
        Console.WriteLine($"Form submitted: {formModel.Name}");
    }

    private void HandleReset(EditContext editContext)
    {
        formModel = new FormModel();
        Console.WriteLine("Form reset");
    }

    public class FormModel
    {
        public string Name { get; set; } = "";
    }
}
```

### Default Button Type
```razor
<SfButton Content="Click Me" />
<!-- Equivalent to: -->
<SfButton Type="ButtonType.Button" Content="Click Me" />
```

**Best Practice:** Always specify `ButtonType.Submit` for form submission buttons to ensure proper form handling.

## Standard vs Primary

### Visual Comparison
```razor
<div style="display: flex; gap: 12px;">
    <!-- Standard button -->
    <SfButton Content="Standard" />
    
    <!-- Primary button -->
    <SfButton Content="Primary" IsPrimary="true" />
    
    <!-- Primary with custom class -->
    <SfButton Content="Custom Primary" IsPrimary="true" CssClass="e-large" />
</div>
```

### When to Use Each

**Standard Button:**
- Secondary actions
- Cancel operations
- Navigation without save
- Optional actions

**Primary Button:**
- Main action in a dialog
- Submit forms
- Confirm important operations
- Single action to emphasize

## Practical Examples

### Form Submit Pattern
```razor
<div>
    <input type="text" @bind="formData" @bind:event="oninput" placeholder="Enter name" />
    
    <div style="margin-top: 12px;">
        <SfButton Content="Cancel" OnClick="OnCancel" />
        <SfButton Content="Submit" 
                  IsPrimary="true" 
                  Disabled="@string.IsNullOrEmpty(formData)"
                  OnClick="OnSubmit" />
    </div>
</div>

@code {
    private string formData = "";
    
    private void OnCancel(MouseEventArgs args)
    {
        formData = "";
    }
    
    private void OnSubmit(MouseEventArgs args)
    {
        Console.WriteLine($"Submitted: {formData}");
        formData = "";
    }
}
```

### Status-Based Button Styling
```razor
<SfButton Content="@GetButtonText()"
          IsPrimary="@(status == "active")"
          CssClass="@GetStatusClass()"
          Disabled="@(status == "locked")" />

@code {
    private string status = "active"; // active, inactive, locked
    
    private string GetButtonText()
    {
        return status switch
        {
            "active" => "Active - Click Me",
            "inactive" => "Inactive",
            "locked" => "Locked",
            _ => "Unknown"
        };
    }
    
    private string GetStatusClass()
    {
        return status switch
        {
            "active" => "e-success",
            "inactive" => "",
            "locked" => "e-danger",
            _ => ""
        };
    }
}
```

### Action Button Group
```razor
<div style="display: flex; gap: 8px; flex-wrap: wrap;">
    <SfButton Content="Edit" CssClass="e-info" OnClick="@(args => OnAction("edit", args))" />
    <SfButton Content="Delete" CssClass="e-danger" OnClick="@(args => OnAction("delete", args))" />
    <SfButton Content="Duplicate" CssClass="e-success" OnClick="@(args => OnAction("duplicate", args))" />
    <SfButton Content="Archive" OnClick="@(args => OnAction("archive", args))" />
</div>

@code {
    private void OnAction(string action, MouseEventArgs args)
    {
        Console.WriteLine($"Action: {action}");
    }
}
```

## Edge Cases and Gotchas

### Gotcha 1: Disable While Processing
```razor
<!-- Wrong: Button re-enables before async completes -->
<SfButton Content="Process (Wrong Way)" OnClick="async () => await ProcessAsync()" />

<!-- Correct: Manage disabled state during async -->
<SfButton Content="Process (Correct)" Disabled="@isProcessing" OnClick="ProcessWithState" />

@code {
    private bool isProcessing = false;
    
    private async Task ProcessWithState(MouseEventArgs args)
    {
        isProcessing = true;
        await ProcessAsync();
        isProcessing = false;
    }
    
    private async Task ProcessAsync()
    {
        // Simulate async operation (e.g., API call, database query)
        await Task.Delay(2000);
        Console.WriteLine("Processing complete!");
    }
}
```

### Gotcha 2: Multiple Primary Buttons
Avoid this pattern - confuses user about what's the main action:
```razor
<!-- Avoid -->
<SfButton Content="Save" IsPrimary="true" />
<SfButton Content="Delete" IsPrimary="true" />
<SfButton Content="Archive" IsPrimary="true" />

<!-- Better: One primary, others standard -->
<SfButton Content="Save" IsPrimary="true" />
<SfButton Content="Delete" />
<SfButton Content="Archive" />
```

### Gotcha 3: CSS Class Specificity
Custom CSS might not override Syncfusion styles due to specificity:
```razor
<!-- May not work as expected -->
<SfButton Content="Styled" CssClass="my-color" />

<style>
    .my-color { color: red; } /* Low specificity */
</style>

<!-- Better: Use more specific selectors -->
<style>
    /* Use !important as a last resort */
    .e-btn.my-color { color: red !important; }
    
    /* Or target the specific component */
    .my-custom-button { 
        background-color: #ff6b6b !important;
        border-radius: 20px;
    }
</style>
```

**Note:** The exact approach may vary depending on your CSS scoping strategy. For Blazor CSS isolation, you may need to use the `::deep` selector or target the component's scoped CSS file directly.
