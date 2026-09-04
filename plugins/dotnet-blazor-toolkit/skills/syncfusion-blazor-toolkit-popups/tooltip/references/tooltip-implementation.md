# Tooltip Implementation and Styling

## Table of Contents
- [What is SfTooltip?](#what-is-sftooltip)
- [Basic Tooltip Setup](#basic-tooltip-setup)
- [Content Configuration](#content-configuration)
- [Position and Arrow Indicators](#position-and-arrow-indicators)
- [Animation Settings](#animation-settings)
- [Open Modes](#open-modes)
- [Sticky Mode and Mouse Trail](#sticky-mode-and-mouse-trail)
- [Delays Window Collision and TargetContainer APIs](#delays-window-collision-and-targetcontainer-apis)
- [Event Handling](#event-handling)
- [Tooltip Programmatic Methods](#tooltip-programmatic-methods)
- [Keyboard Accessibility](#keyboard-accessibility)
- [Styling and Theming](#styling-and-theming)
- [Real-World Examples](#real-world-examples)

---

## What is SfTooltip?

SfTooltip is a lightweight component that displays contextual help text or information on hover, focus, or click. It's ideal for:

- Icon descriptions
- Keyboard shortcut hints
- Status or validation messages
- Brief explanations
- Accessibility enhancements

**Key Characteristics:**
- Multiple positioning options
- Arrow indicators pointing to target
- Customizable open modes
- Animation support
- RTL support
- Accessibility compliant (WCAG)

---

## Basic Tooltip Setup

### Minimal Tooltip Example

```razor
@using Syncfusion.Blazor.Toolkit.Popups

<SfTooltip Content="This is a tooltip">
    <span>Hover over me</span>
</SfTooltip>
```

### With Target Element

```razor
<SfTooltip Target="#helpIcon" Content="Click the help button for more information">
    <SfButton id="helpIcon" class="e-btn" title="Help">
        ❓
    </SfButton>
</SfTooltip>
```

### With Container

```razor
<div class="parent">
<SfTooltip Content="Let's go green to save the planet!!" Container=".parent">
 <SfButton Content="Show Tooltip"></SfButton>
</SfTooltip>
</div>
```

### CSS Import

Ensure Syncfusion CSS is included in `_Host.cshtml` or `App.razor`:

```html
<link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet">
```

---

## Content Configuration

### Text Content

```razor
<SfTooltip Target="#helpBtn" Content="Click for help">
    <SfButton id="helpBtn"> Help </SfButton>
</SfTooltip>
```

### HTML Template

```razor
<div id="custom" style="width:100%;max-width:400px;">
    <SfButton CssClass="e-outline" IsPrimary="true" Content="HTML Tooltip" id="btn" aria-label="Open HTML Tooltip"></SfButton>
</div>

<SfTooltip Position=Position.BottomCenter Target="#btn" WindowCollision="true" TargetContainer="#custom">
    <ContentTemplate>
        <div style="padding: 10px;">
            <strong>Tooltip Title</strong>
            <p>This tooltip contains <em>HTML</em> content</p>
        </div>
    </ContentTemplate>
</SfTooltip>
```

### Dynamic Content

```razor
<div style="margin-top:200px;text-align:center;">
    <SfTooltip @ref="tooltipInstance" Target=".custom" OpensOn="Click" Position="Position.BottomCenter">
        <ContentTemplate>
            <div style="padding:10px;">
                <strong>Selected:</strong> @DynamicContent
            </div>
        </ContentTemplate>
        <ChildContent>
            <SfButton class="custom e-btn" Content="Wireless" @onclick="@(() => DynamicContent = "Wireless")"></SfButton>
            <SfButton class="custom e-btn" Content="Device" @onclick="@(() => DynamicContent = "Device")"></SfButton>
            <SfButton class="custom e-btn" Content="Personal" @onclick="@(() => DynamicContent = "Personal")"></SfButton>
        </ChildContent>
    </SfTooltip>
</div>

@code {
    private SfTooltip? tooltipInstance;
    private string DynamicContent = "Wireless";
}
```

---

## Position and Arrow Indicators

### Position Options

```razor
@using Syncfusion.Blazor.Toolkit

<div style="display: flex; gap: 20px; flex-wrap: wrap;">
    @foreach(var position in positions)
    {
        <SfTooltip Content="@($"Position: {position}")" Position="@position">
            <button class="e-btn">
                @position
            </button>
        </SfTooltip>
    }
</div>

@code {
    private Position[] positions = 
    {
        Position.TopCenter,
        Position.TopLeft,
        Position.TopRight,
        Position.BottomCenter,
        Position.BottomLeft,
        Position.BottomRight,
        Position.LeftCenter,
        Position.LeftTop,
        Position.LeftBottom,
        Position.RightCenter,
        Position.RightTop,
        Position.RightBottom
    };
}
```

### Arrow Positioning

```razor
@using Syncfusion.Blazor.Toolkit

<SfTooltip Content="Tooltip with arrow"
           Position=Position.TopCenter
           TipPointerPosition=TipPointerPosition.End>
    <SfButton>
        Arrow points to end
    </SfButton>
</SfTooltip>

<SfTooltip Content="Tooltip with arrow"
           Position=Position.TopCenter
           TipPointerPosition=TipPointerPosition.Middle>
    <SfButton>
        Arrow points to middle
    </SfButton>
</SfTooltip>

@code {
}
```

### Show/Hide Tip Pointer

```razor
<div style="text-align:center;padding:40px;">
    <SfTooltip Content="Welcome to Syncfusion Blazor Tooltip!" ShowTipPointer="false">
        <SfButton Content="Hover me"></SfButton>
    </SfTooltip>
</div>
```

<!-- ### Custom Positioning

```razor
<SfTooltip Content="Let's go green to save the planet!!" OffsetX="20">
 <SfButton Content="Show Tooltip"></SfButton>
</SfTooltip>
``` -->

---

## Animation Settings

### Basic Animation

```razor
@using Syncfusion.Blazor.Toolkit
@using Syncfusion.Blazor.Toolkit.Popups

<SfTooltip Content="Let's go green to save the planet!!" Animation="@Animation">
    <SfButton Content="Show Tooltip"></SfButton>
</SfTooltip>

@code 
{
    public AnimationModel Animation { get; set; } = new AnimationModel
    {
        Open = new TooltipAnimationSettings { Delay = 0, Duration = 500, Effect = Effect.ZoomIn },
        Close = new TooltipAnimationSettings { Delay = 0, Duration = 500, Effect = Effect.ZoomOut }
    };
}
```

### Available Effects

The available effects include fade, slide, zoom, and other predefined animation types.

| Effect | Behavior |
|--------|----------|
| `Effect.FadeIn` | Fade in effect for opening |
| `Effect.FadeOut` | Fade out effect for closing |
| `Effect.ZoomIn` | Zoom in effect for opening |
| `Effect.ZoomOut` | Zoom out effect for closing |
| `Effect.SlideLeftIn` | Slide from right to left for opening |
| `Effect.SlideLeftOut` | Slide from left to right for closing |
| `Effect.SlideRightIn` | Slide from left to right for opening |
| `Effect.SlideRightOut` | Slide from right to left for closing |
| `Effect.SlideTopIn` | Slide from bottom to top for opening |
| `Effect.SlideTopOut` | Slide from top to bottom for closing |
| `Effect.SlideBottomIn` | Slide from top to bottom for opening |
| `Effect.SlideBottomOut` | Slide from bottom to top for closing |
| `Effect.None` | No animation effect |

---

## Open Modes

The `OpensOn` property determines the event that triggers the Tooltip to appear. The supported values are: `Auto` , `Hover`, `Click`, `Focus` and `Custom`.

### Hover

```razor
<SfTooltip Content="Appears on hover"
           OpensOn="Hover">
    <span style="text-decoration: underline; cursor: help;">
        Hover over me
    </span>
</SfTooltip>
```

### Click

```razor
<SfTooltip Content="Appears on click"
           OpensOn="Click">
    <span style="text-decoration: underline; cursor: pointer;">
        Click me
    </span>
</SfTooltip>
```

### Focus (for keyboard navigation)

```razor
<SfTooltip Content="Appears on focus"
           OpensOn="Focus">
    <input type="text" placeholder="Tab here for tooltip" />
</SfTooltip>
```

### Auto Mode (Default)

```razor
<SfTooltip Content="Auto-detects trigger mode"
           OpensOn="Auto">
    <span style="text-decoration: underline; cursor: help;">
        Hover or click me
    </span>
</SfTooltip>
```

### Custom Trigger (Programmatic)

```razor
<SfTooltip @ref="tooltip" Content="Custom triggered tooltip" OpensOn="Custom">
    <button class="e-btn" @onclick="ShowTooltip">
        Show Tooltip
    </button>
</SfTooltip>

<button class="e-btn" @onclick="HideTooltip">
    Hide Tooltip
</button>

@code {
    private SfTooltip tooltip;
    
    private async Task ShowTooltip()
    {
        await tooltip.OpenAsync();
    }
    
    private async Task HideTooltip()
    {
        await tooltip.CloseAsync();
    }
}
```

---

## Sticky Mode and Mouse Trail

### Sticky Tooltip

```razor
<SfTooltip Content="This tooltip stays open until closed manually"
           IsSticky="true"
           OpensOn="Click">
    <button class="e-btn">Click to open sticky tooltip</button>
</SfTooltip>
```

### Mouse Trail

```razor
<SfTooltip Content="Following mouse movement"
           MouseTrail="true">
    <div style="padding: 20px; background-color: #f0f0f0;">
        Move mouse here to see trail effect
    </div>
</SfTooltip>
```

---

## Delays Window Collision and TargetContainer APIs

### Open and Close Delays

```razor
<SfTooltip Content="Delayed opening (1 second)"
           OpenDelay="1000">
    <span style="text-decoration: underline; cursor: help;">
        Hover - wait 1 second
    </span>
</SfTooltip>

<SfTooltip Content="Stays open for 3 seconds after mouse leave"
           CloseDelay="3000">
    <span style="text-decoration: underline; cursor: help;">
        Hover and then leave
    </span>
</SfTooltip>
```

### Window Collision

```razor
<SfTooltip Content="Collision detected against window viewport"
           WindowCollision="true">
    <span style="text-decoration: underline; cursor: help;">
        Hover near edges
    </span>
</SfTooltip>
```

### Target Container

```razor
<SfTooltip Content="Auto-applied to all buttons in container"
           TargetContainer=".button-container"
           OpensOn="Hover">
    <div class="button-container">
        <button class="e-btn">Button 1</button>
        <button class="e-btn">Button 2</button>
        <button class="e-btn">Button 3</button>
    </div>
</SfTooltip>
```

### Size Properties (Height and Width)

```razor
<SfTooltip Content="Fixed size tooltip" Height="100px" Width="200px">
    <SfButton Content="Hover me"></SfButton>
</SfTooltip>
```

### Offset Properties

```razor
<SfTooltip Content="Offset from target" OffsetX="20" OffsetY="10">
    <SfButton Content="Hover me"></SfButton>
</SfTooltip>
```

### HtmlAttributes

```razor
<SfTooltip Content="With custom attributes" HtmlAttributes="@TooltipAttributes">
    <SfButton Content="Hover me"></SfButton>
</SfTooltip>

@code {
    private Dictionary<string, object> TooltipAttributes = new()
    {
        { "aria-label", "Custom tooltip" }
    };
}
```

---

## Event Handling

### Lifecycle Events

```razor
@using Syncfusion.Blazor.Toolkit.Popups

<SfTooltip Content="Event handling demo"
           Opened="@OnTooltipOpened"
           Closed="@OnTooltipClosed"
           OnOpen="@OnBeforeOpen"
           OnClose="@OnBeforeClose">
    <span style="text-decoration: underline; cursor: help;">
        Hover to see events
    </span>
</SfTooltip>

<div style="margin-top: 20px; padding: 10px; background-color: #f0f0f0; border-radius: 4px;">
    <p><strong>Event Log:</strong></p>
    @foreach(var evt in eventLog.TakeLast(5))
    {
        <p style="margin: 5px 0; font-family: monospace;">@evt</p>
    }
</div>

@code {
    private List<string> eventLog = new();
    
    private async Task OnBeforeOpen(TooltipEventArgs args)
    {
        eventLog.Add($"[{DateTime.Now:HH:mm:ss}] BeforeOpen - Can cancel with args.Cancel");
    }
    
    private async Task OnTooltipOpened(TooltipEventArgs args)
    {
        eventLog.Add($"[{DateTime.Now:HH:mm:ss}] Opened");
    }
    
    private async Task OnBeforeClose(TooltipEventArgs args)
    {
        eventLog.Add($"[{DateTime.Now:HH:mm:ss}] BeforeClose - Can cancel with args.Cancel");
    }
    
    private async Task OnTooltipClosed(TooltipEventArgs args)
    {
        eventLog.Add($"[{DateTime.Now:HH:mm:ss}] Closed");
    }
}
```

### Collision Detection

```razor
<SfTooltip Content="collision detection demo"
           Colliding="@OnCollision">
    <span style="text-decoration: underline; cursor: help;">
        Hover to see collision handling
    </span>
</SfTooltip>

@code {
    private void OnCollision(TooltipEventArgs args)
    {
        // Contains collision information for custom positioning logic
        Console.WriteLine($"Collision at: {args.Left}, {args.Top}");
    }
}
```

### OnRender Event

```razor
<SfTooltip Content="Before render occurs"
           OnRender="@OnTooltipRender">
    <span style="text-decoration: underline; cursor: help;">
        Hover to see render event
    </span>
</SfTooltip>

@code {
    private bool someCondition = false;

    private void OnTooltipRender(TooltipEventArgs args)
    {
        // Called before tooltip is added to DOM
        if (someCondition)
        {
            // Cancel showing the tooltip
            args.Cancel = true;
        }
        else
        {
            // Dynamically change the content before render
            Console.WriteLine("Tooltip rendered");
        }
    }
}
```

### Collision Detection

```razor
@using Syncfusion.Blazor.Toolkit.Popups
<SfTooltip Content="Collision detection demo"
           Colliding="@OnCollision">
    <span style="text-decoration: underline; cursor: help;">
        Hover to see collision handling
    </span>
</SfTooltip>

@code {
    private void OnCollision(TooltipEventArgs args)
    {
        // Contains collision information for custom positioning logic
        Console.WriteLine("Collision detected");
    }
}
```

### Creation and Destruction Events

```razor
<SfTooltip Content="Lifecycle demo"
           Created="@OnCreated"
           Destroyed="@OnDestroyed">
    <span style="text-decoration: underline; cursor: help;">
        Hover to see lifecycle
    </span>
</SfTooltip>

@code {
    private void OnCreated(object args)
    {
        Console.WriteLine("Tooltip created");
    }
    
    private void OnDestroyed(object args)
    {
        Console.WriteLine("Tooltip destroyed");
    }
}
```

---

## Tooltip Programmatic Methods

### OpenAsync and CloseAsync

These methods allow programmatic control over showing and hiding the tooltip with optional animation settings and target element specification.

```razor
@using Syncfusion.Blazor.Toolkit.Popups

<SfTooltip @ref="tooltip" Content="Animated tooltip" OpensOn="Custom">
    <SfButton class="e-btn" @onclick="ShowTooltip">
        Show Tooltip
    </SfButton>
</SfTooltip>

<SfButton class="e-btn" @onclick="HideTooltip">
    Hide Tooltip
</SfButton>

@code {
    private SfTooltip? tooltip;

    private async Task ShowTooltip()
    {
        var fadeIn = new TooltipAnimationSettings
        {
            Effect = Effect.FadeIn,
            Duration = 300
        };

        if (tooltip is not null)
            await tooltip.OpenAsync(null, fadeIn);
    }

    private async Task HideTooltip()
    {
        var fadeOut = new TooltipAnimationSettings
        {
            Effect = Effect.FadeOut,
            Duration = 500
        };

        if (tooltip is not null)
            await tooltip.CloseAsync(fadeOut);
    }
}
```

### PreventRender
Controls the re-rendering behavior of the Tooltip component.

```csharp
// Prevent tooltip from re-rendering (default behavior)
tooltip.PreventRender(true);

// Allow tooltip to re-render again
tooltip.PreventRender(false);
```

### RefreshAsync
Asynchronously refreshes the entire Tooltip component to synchronize with dynamic DOM changes and target element updates.

```csharp
// After adding new elements dynamically
await AddNewElementsToPage();
await tooltipComponent.RefreshAsync();

// After modifying existing target elements
await UpdateTargetElementAttributes();
await tooltipComponent.RefreshAsync();
```

### RefreshPositionAsync
Recalculates and updates the tooltip’s position when the layout changes, keeping it aligned with its target element. Useful after resizing, scrolling, or moving elements dynamically.

```csharp
// Refresh position with default target
await tooltip.RefreshPositionAsync();

// Refresh position for a specific element
await tooltip.RefreshPositionAsync(targetElement);
```

---

## Keyboard Accessibility

### Keyboard Support

```razor
<table style="width: 100%; border-collapse: collapse;">
    <tr>
        <th style="border: 1px solid #ddd; padding: 10px;">Keyboard Shortcut</th>
        <th style="border: 1px solid #ddd; padding: 10px;">Description</th>
    </tr>
    
    <tr>
        <td style="border: 1px solid #ddd; padding: 10px;">
            <SfTooltip Content="Move focus to previous element"
                       Position=Position.BottomCenter>
                <span style="font-family: monospace; background-color: #f0f0f0; padding: 5px; border-radius: 3px;">
                    Shift + Tab
                </span>
            </SfTooltip>
        </td>
        <td style="border: 1px solid #ddd; padding: 10px;">Navigate backwards</td>
    </tr>
    
    <tr>
        <td style="border: 1px solid #ddd; padding: 10px;">
            <SfTooltip Content="Move focus to next element"
                       Position=Position.BottomCenter>
                <span style="font-family: monospace; background-color: #f0f0f0; padding: 5px; border-radius: 3px;">
                    Tab
                </span>
            </SfTooltip>
        </td>
        <td style="border: 1px solid #ddd; padding: 10px;">Navigate forwards</td>
    </tr>
    
    <tr>
        <td style="border: 1px solid #ddd; padding: 10px;">
            <SfTooltip Content="Activate or press button"
                       Position=Position.BottomCenter>
                <span style="font-family: monospace; background-color: #f0f0f0; padding: 5px; border-radius: 3px;">
                    Enter / Space
                </span>
            </SfTooltip>
        </td>
        <td style="border: 1px solid #ddd; padding: 10px;">Activate element</td>
    </tr>
</table>
```

### Accessible Tooltips

```razor
<SfTooltip Content="Save your work (Ctrl+S)"
           Position=Position.TopCenter
           OpensOn="Hover">
    <button class="e-btn" 
            aria-label="Save button"
            title="Save">
        💾 Save
    </button>
</SfTooltip>

<SfTooltip Content="Delete this item (cannot be undone)"
           Position=Position.TopCenter
           OpensOn="Focus">
    <button class="e-btn" 
            aria-label="Delete button"
            title="Delete">
        🗑️ Delete
    </button>
</SfTooltip>

@code {
}
```

---

## Styling and Theming

### CSS Classes

The `CssClass` property is used to apply custom CSS class names that define specific user-defined styles and themes to be applied to the Tooltip element. Multiple class names can be specified by separating them with a space.

```razor
<SfTooltip Content="Let's go green to save the planet!!" CssClass="customtip">
    <SfButton Content="Show Tooltip"></SfButton>
</SfTooltip>

<style>
    /* Custom tooltip style applied via CssClass */
    .customtip {
        
        font-weight: bold;           /* Bold text */
        box-shadow: 0 4px 8px rgba(0,0,0,0.2); /* Subtle shadow */
    }
</style>
```

---

## Real-World Examples

### Icon Button Tooltip

```razor
<div style="display: flex; gap: 20px;">
    <SfTooltip Content="Save your changes (Ctrl+S)"
               Position=Position.BottomCenter>
        <button class="icon-button" title="Save">
            💾
        </button>
    </SfTooltip>
    
    <SfTooltip Content="Undo last action (Ctrl+Z)"
               Position=Position.BottomCenter>
        <button class="icon-button" title="Undo">
            ↶
        </button>
    </SfTooltip>
    
    <SfTooltip Content="Redo action (Ctrl+Y)"
               Position=Position.BottomCenter>
        <button class="icon-button" title="Redo">
            ↷
        </button>
    </SfTooltip>
    
    <SfTooltip Content="Delete item (Ctrl+D)"
               Position=Position.BottomCenter>
        <button class="icon-button" title="Delete">
            🗑️
        </button>
    </SfTooltip>
</div>

<style>
    .icon-button {
        background: none;
        border: none;
        font-size: 24px;
        cursor: pointer;
        padding: 8px;
        border-radius: 4px;
        transition: background-color 0.2s;
    }
    
    .icon-button:hover {
        background-color: #f0f0f0;
    }
</style>
```

### Data Cell Tooltip

```razor
<table style="width: 100%; border-collapse: collapse; margin-top: 20px;">
    <thead>
        <tr style="background-color: #f0f0f0;">
            <th style="border: 1px solid #ddd; padding: 10px;">Name</th>
            <th style="border: 1px solid #ddd; padding: 10px;">Email</th>
            <th style="border: 1px solid #ddd; padding: 10px;">Status</th>
        </tr>
    </thead>
    <tbody>
        @foreach(var user in users)
        {
            <tr>
                <td style="border: 1px solid #ddd; padding: 10px;">@user.Name</td>
                <td style="border: 1px solid #ddd; padding: 10px;">
                    <SfTooltip Content="@user.Email"
                               Position=Position.BottomCenter>
                        <span style="cursor: help; text-decoration: underline;">
                            @(user.Email.Length > 15 ? user.Email.Substring(0, 12) + "..." : user.Email)
                        </span>
                    </SfTooltip>
                </td>
                <td style="border: 1px solid #ddd; padding: 10px;">
                    <SfTooltip Content="@GetStatusDescription(user.Status)"
                               Position=Position.BottomCenter>
                        <span style="padding: 4px 8px; border-radius: 4px; 
                                   background-color: @GetStatusColor(user.Status); 
                                   color: white; cursor: help;">
                            @user.Status
                        </span>
                    </SfTooltip>
                </td>
            </tr>
        }
    </tbody>
</table>

@code {
    private class User
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Status { get; set; }
    }
    
    private List<User> users = new()
    {
        new() { Name = "John Doe", Email = "john@example.com", Status = "Active" },
        new() { Name = "Jane Smith", Email = "jane@example.com", Status = "Inactive" },
        new() { Name = "Bob Wilson", Email = "bob@example.com", Status = "Pending" }
    };
    
    private string GetStatusDescription(string status) => status switch
    {
        "Active" => "User is currently active",
        "Inactive" => "User account is inactive",
        "Pending" => "User account approval pending",
        _ => ""
    };
    
    private string GetStatusColor(string status) => status switch
    {
        "Active" => "#4caf50",
        "Inactive" => "#999",
        "Pending" => "#ff9800",
        _ => "#999"
    };
}
```

## Key Takeaways

✅ **DO:**
- Use tooltips for help and clarification, not critical information
- Keep tooltip text concise and clear
- Include keyboard shortcut hints in tooltips
- Test tooltip positioning at different screen sizes
- Use appropriate Open modes for different contexts
- Use `IsSticky` for tooltips that should stay open until manually closed
- Use `MouseTrail` for following mouse movement during demos
- Use `OpenDelay` and `CloseDelay` to prevent tooltip flashing during quick mouse movements
- Use `WindowCollision` when tooltip should avoid viewport edges

❌ **DON'T:**
- Hide critical information in tooltips only
- Use tooltips as a replacement for proper labeling
- Create tooltips with excessive text (use dialogs instead)
- Overuse animations (performance impact)
- Forget to make tooltips accessible (keyboard support)
- Use fixed positioning without considering collision handling
