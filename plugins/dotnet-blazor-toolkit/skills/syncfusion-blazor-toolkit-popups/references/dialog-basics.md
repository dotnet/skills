# Dialog Basics

## Table of Contents
- [What is SfDialog?](#what-is-sfdialog)
- [Installation and Setup](#installation-and-setup)
- [Creating Your First Dialog](#creating-your-first-dialog)
- [Dialog Configuration](#dialog-configuration)
- [Modal vs Modeless](#modal-vs-modeless)
- [Animation and Effects](#animation-and-effects)
- [Button Configuration](#button-configuration)
- [Basic Event Handling](#basic-event-handling)
- [Content AllowPrerender and CloseOnEscape APIs](#content-allowprerender-and-closeonescape-apis)
- [Programmatic Methods in Dialog](#programmatic-methods-in-dialog)
- [Common Scenarios](#common-scenarios)

---

## What is SfDialog?

SfDialog is a versatile modal or modeless dialog component from Syncfusion Blazor Toolkit. It displays content in a separate window that can be dragged, resized, and positioned. Dialogs are ideal for:

- Confirming user actions
- Collecting form input
- Displaying alerts or notifications
- Creating settings panels
- Showing detailed information

**Key Characteristics:**
- Fully customizable header, content, and footer
- Built-in buttons with event handling
- Drag and resize capabilities
- Animation support
- Modal and modeless variants
- Responsive sizing

---

## Installation and Setup

### 1. Import the Component
In your Razor component or `_Imports.razor`:

```csharp
@using Syncfusion.Blazor.Toolkit.Popups
```

### 2. Register the Component
In `Program.cs`:

```csharp
using Syncfusion.Blazor.Toolkit;

builder.Services.AddSyncfusionBlazorToolkit();
```

### 3. Include CSS
In `_Host.cshtml` or `App.razor`:

```html
<link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet">
```

---

## Creating Your First Dialog

### Minimal Dialog Example

```razor
@page "/dialog-demo"
@using Syncfusion.Blazor.Toolkit.Popups
@using Syncfusion.Blazor.Toolkit.Buttons

<SfDialog @bind-Visible="isDialogOpen" Width="500px" Height="auto">
    <DialogTemplates>
        <Header>
            <span>Welcome</span>
        </Header>
        <Content>
            <p>This is a simple dialog.</p>
        </Content>
    </DialogTemplates>
</SfDialog>

<SfButton class="e-btn" @onclick="OpenDialog">Open Dialog</SfButton>

@code {
    private bool isDialogOpen = false;

    private void OpenDialog()
    {
        isDialogOpen = true;
    }
}
```

**What's happening:**
- `@bind-Visible="isDialogOpen"` - Binds dialog visibility to a boolean variable
- `Width` and `Height` - Set dialog dimensions
- `DialogTemplates` - Contains Header, Content, and Footer sections
- Button click opens the dialog by setting `isDialogOpen = true`

---

## Dialog Configuration

### Essential Properties

```razor
<SfDialog Visible="true"
          Header="Dialog Title"
          Width="600px"
          Height="400px"
          AllowDragging="true"
          EnableResize="true"
          IsModal="true"
          ZIndex="2000"
          ID="dialog_default">
    <DialogTemplates>
        <Content>
            <p>Dialog content goes here</p>
        </Content>
    </DialogTemplates>
</SfDialog>

```

### Dialog with HtmlAttribute

```razor
<SfDialog @bind-Visible="Visibility"
          Header="Simple Dialog"
          HtmlAttributes="DialogAttributes">
    <DialogTemplates>
        <Content>
            <p>This dialog has HtmlAttributes applied.</p>
        </Content>
    </DialogTemplates>
</SfDialog>

<SfButton Content="Open Dialog" OnClick="@(() => Visibility = true)" />

@code {
    private bool Visibility { get; set; } = false;

    // Simple HtmlAttributes dictionary
    private Dictionary<string, object> DialogAttributes = new()
    {
        { "title", "My Simple Dialog" }
    };
}
```

**Property Details:**

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `Visible` | bool | true | Show/hide the dialog |
| `Header` | string | null | Dialog header text |
| `Width` | string | "100%" | Dialog width (px, %, em, etc.) |
| `Height` | string | "auto" | Dialog height |
| `AllowDragging` | bool | false | Enable dragging the dialog |
| `EnableResize` | bool | false | Enable resizing the dialog |
| `AllowPrerender` | bool | false | Keep DOM when closed (performance optimization) |
| `CloseOnEscape` | bool | true | Allow closing with Escape key |
| `IsModal` | bool | false | Modal overlay behind dialog |
| `ZIndex` | int | 1000 | Stacking order |
| `CssClass` | string | String.Empty | Custom CSS class |
| `MinHeight` | string | String.Empty | Minimum height constraint |
| `Target` | string | null | Container element for dialog display |
| `ShowCloseIcon` | bool | false | Display close icon in header |
| `VisibleChanged` | EventCallback<bool> | - | Two-way binding callback, invoked when Visible changes |
| `Created` | EventCallback<object> | - | Component created event |
| `Destroyed` | EventCallback<object> | - | Component destroyed event |
| `EnablePersistence` | bool | false | Persist dialog position and size to localStorage using the component's ID |
| `ID` | string | null | Unique identifier for the component (required when EnablePersistence is true) |
| `HtmlAttributes` | Dictionary<string, object> | null | allows to add custom attributes like id, title, or aria- attributes to the dialog element |

### Sizing Options

**Fixed Size:**
```razor
<SfDialog Width="500px" Height="300px"></SfDialog>
```

**Responsive Size:**
```razor
<SfDialog Width="90%" Height="80%"></SfDialog>
```

**Auto Height:**
```razor
<SfDialog Width="500px" Height="auto"></SfDialog>
```

**Min Dimensions:**
```razor
<SfDialog @ref="dialog" Width="500px" MinHeight="150px"></SfDialog>

@code {
    private SfDialog dialog;
}
```

### State Persistence

The `EnablePersistence` property allows the dialog's position and size to be persisted in browser localStorage across page reloads.

```razor
<SfDialog ID="SettingsDialog" EnablePersistence="true" Width="500px" Height="400px">
    <DialogTemplates>
        <Content><p>Dialog content</p></Content>
    </DialogTemplates>
</SfDialog>
```

**Important:** When using `EnablePersistence`, you must also set an `ID` property on the component. The persistence mechanism uses the component's `ID` as the storage key in localStorage. Without a unique `ID`, the persistence behavior may not work correctly across multiple component instances.

---

## Modal vs Modeless

### Modal Dialog (Default)
User cannot interact with background content while dialog is open:

```razor
<SfDialog @bind-Visible="isDialogOpen" IsModal="true">
    <DialogTemplates>
        <Content>
            <p>Modal dialog - background is disabled</p>
        </Content>
    </DialogTemplates>
</SfDialog>
<SfButton class="e-btn" @onclick="OpenDialog">Open Dialog</SfButton>

@code {
    private bool isDialogOpen = false;

    private void OpenDialog()
    {
        isDialogOpen = true;
    }
}
```

**Use Cases:**
- Critical confirmations
- Important forms
- Alerts that need user attention

### Modeless Dialog
User can interact with background content:

```razor
<SfDialog @bind-Visible="isDialogOpen" IsModal="false">
    <DialogTemplates>
        <Content>
            <p>Modeless dialog - background is interactive</p>
        </Content>
    </DialogTemplates>
</SfDialog>
<SfButton class="e-btn" @onclick="OpenDialog">Open Dialog</SfButton>

@code {
    private bool isDialogOpen = false;

    private void OpenDialog()
    {
        isDialogOpen = true;
    }
}
```

**Use Cases:**
- Help panels
- Notes or scratch pads
- Docked panels
- Tool palettes

---

## Animation and Effects

### Basic Animation

```razor
<SfDialog Width="500px" @bind-Visible="isDialogOpen">
    <DialogTemplates>
        <Content>
            <p>Dialog content</p>
        </Content>
    </DialogTemplates>
    <DialogAnimationSettings Delay="400" Effect="DialogEffect.SlideBottom" />
</SfDialog>

<SfButton class="e-btn" @onclick="OpenDialog">Open Dialog</SfButton>

@code {
    private bool isDialogOpen = false;

    private void OpenDialog()
    {
        isDialogOpen = true;
    }
}
```

### Available Effects

| Effect | Behavior |
|--------|----------|
| `DialogEffect.Fade` | Gradual opacity transition |
| `DialogEffect.FadeZoom` | Combined fade and zoom effect |
| `DialogEffect.FlipLeftDown` | Flip animation from left to down |
| `DialogEffect.FlipLeftUp` | Flip animation from left to up |
| `DialogEffect.FlipRightDown` | Flip animation from right to down |
| `DialogEffect.FlipRightUp` | Flip animation from right to up |
| `DialogEffect.FlipXDown` | Horizontal flip with downward motion |
| `DialogEffect.FlipXUp` | Horizontal flip with upward motion |
| `DialogEffect.FlipYLeft` | Vertical flip with leftward motion |
| `DialogEffect.FlipYRight` | Vertical flip with rightward motion |
| `DialogEffect.SlideBottom` | Slide in/out from bottom |
| `DialogEffect.SlideLeft` | Slide in/out from left |
| `DialogEffect.SlideRight` | Slide in/out from right |
| `DialogEffect.SlideTop` | Slide in/out from top |
| `DialogEffect.Zoom` | Scale-based zoom effect |
| `DialogEffect.None` | No animation effect |

### Custom Animation

```razor
<SfDialog @bind-Visible="isDialogOpen">
    <DialogTemplates>
        <Content>
            <p>Custom animation</p>
        </Content>
    </DialogTemplates>
    <DialogAnimationSettings Effect="DialogEffect.Zoom" Duration="800" Delay="100" />
</SfDialog>
```

---

## Button Configuration

### Dialog Buttons

```razor
<SfDialog @bind-Visible="isDialogOpen">
    <DialogTemplates>
        <Content>
            <p>Do you want to continue?</p>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Yes" IsPrimary="true" CssClass="e-success" OnClick="@OnYesClick" />
        <DialogButton Content="No" CssClass="e-danger" OnClick="@OnNoClick" />
        <DialogButton Content="Cancel" OnClick="@OnCancelClick" />
    </DialogButtons>
</SfDialog>

@code {
    private bool isDialogOpen = true;

    private async Task OnYesClick()
    {
        isDialogOpen = false;
    }

    private async Task OnNoClick()
    {
        isDialogOpen = false;
    }

    private async Task OnCancelClick()
    {
        isDialogOpen = false;
    }
}
```

### Button Properties

```razor
private DialogButton submitButton = new()
{
    Content = "Submit",
    IsPrimary = true,
    CssClass = "custom-button",
    Disabled = false,
    IconCss = "e-icons e-save"
};
```

---

## Basic Event Handling

```razor
<SfDialog @bind-Visible="isDialogOpen"
          OnOpen="@OnDialogOpen"
          OnClose="@OnDialogClose"
          ShowCloseIcon="true">
    <DialogTemplates>
        <Content>
            <p>Event handling demo</p>
        </Content>
    </DialogTemplates>
</SfDialog>

<SfButton class="e-btn" @onclick="OpenDialog">Open Dialog</SfButton>

@code {
    private bool isDialogOpen = false;

    private void OpenDialog()
    {
        isDialogOpen = true;
    }

    private async Task OnDialogOpen(BeforeOpenEventArgs args)
    {
        Console.WriteLine("Dialog is about to open");
        // Cancel opening if needed: args.Cancel = true;
    }

    private async Task OnDialogClose(BeforeCloseEventArgs args)
    {
        Console.WriteLine("Dialog is about to close");
        // Prevent closing if needed: args.Cancel = true;
    }
}
```

### Created and Destroyed Events

The `Created` event fires when the dialog component is fully rendered, and `Destroyed` fires when it's being disposed:

```razor
<SfDialog Width="500px" @bind-Visible="isDialogOpen" 
          Created="OnCreated" 
          Destroyed="OnDestroyed">
    <DialogTemplates>
        <Content><p>Lifecycle events demo</p></Content>
    </DialogTemplates>
</SfDialog>

@code {
    private bool isDialogOpen = true;
    
    private void OnCreated(object args)
    {
        Console.WriteLine("Dialog has been created and rendered");
    }
    
    private void OnDestroyed(object args)
    {
        Console.WriteLine("Dialog is being destroyed and removed from DOM");
    }
}
```

### Event Arguments

**BeforeOpenEventArgs:**
```csharp
public class BeforeOpenEventArgs
{
    public bool Cancel { get; set; }        // Cancel opening
    public string MaxHeight { get; set; }   // Override max height before open
}
```

**OpenEventArgs:**
```csharp
public class OpenEventArgs
{
    public bool Cancel { get; set; }        // Not typically used
    public string Name { get; set; }        // Event name ("Opened")
    public bool PreventFocus { get; set; }  // Prevent auto-focus on first element
}
```

**BeforeCloseEventArgs:**
```csharp
public class BeforeCloseEventArgs
{
    public bool Cancel { get; set; }        // Cancel closing
    public string ClosedBy { get; set; }   // "CloseIcon", "Escape", etc.
    public EventArgs Event { get; set; }    // MouseEventArgs or KeyboardEventArgs
    public bool IsInteracted { get; set; }   // User-initiated close
    public bool PreventFocus { get; set; }   // Prevent focus restoration
}
```

**CloseEventArgs:**
```csharp
public class CloseEventArgs
{
    public bool Cancel { get; set; }         // Not typically used
    public string ClosedBy { get; set; }    // Source of close action
    public EventArgs Event { get; set; }     // Original event
    public bool IsInteracted { get; set; }  // User-initiated close
    public string Name { get; set; }        // Event name ("Closed")
}
```

### Drag Event Args

**DragStartEventArgs:**
```csharp
public class DragStartEventArgs
{
    public MouseEventArgs Event { get; set; }  // Mouse event data
    public string Name { get; set; }           // "OnDragStart"
}
```

**DragEventArgs:**
```csharp
public class DragEventArgs
{
    public MouseEventArgs Event { get; set; }  // Current mouse position
    public string Name { get; set; }           // "OnDrag"
}
```

**DragStopEventArgs:**
```csharp
public class DragStopEventArgs
{
    public MouseEventArgs Event { get; set; }  // Final mouse position
    public string Name { get; set; }           // "OnDragStop"
}
```

### Overlay Event Args

**OverlayModalClickEventArgs:**
```csharp
public class OverlayModalClickEventArgs
{
    public MouseEventArgs Event { get; set; }   // Click event data
    public bool PreventFocus { get; set; }       // Prevent auto-focus
}
```
---

## Content AllowPrerender and CloseOnEscape APIs

### Content property

For simple text content, you can use the `Content` property instead of `DialogTemplates`:

```razor
<SfDialog Width="500px" @bind-Visible="isDialogOpen" 
          Header="Notice" 
          Content="This is a simple text content dialog.">
</SfDialog>

@code {
    private bool isDialogOpen = false;
}
```

### AllowPrerender for Performance

When `AllowPrerender` is set to `true`, the dialog's DOM elements remain in the page when closed (hidden via CSS) instead of being removed. This improves performance for frequently opened/closed dialogs:

```razor
<SfDialog @bind-Visible="isDialogOpen" AllowPrerender="true">
    <DialogTemplates>
        <Header><span>Frequently Opened Dialog</span></Header>
        <Content><p>This dialog's DOM stays in the page when closed.</p></Content>
    </DialogTemplates>
</SfDialog>

@code {
    private bool isDialogOpen = false;
}
```

### CloseOnEscape Configuration

By default, pressing the Escape key closes the dialog. Set `CloseOnEscape="false"` to disable this behavior:

```razor
<SfDialog @bind-Visible="isDialogOpen" CloseOnEscape="false">
    <DialogTemplates>
        <Content><p>Press Escape key - this dialog won't close!</p></Content>
    </DialogTemplates>
</SfDialog>

@code {
    private bool isDialogOpen = true;
}
```

---

## Programmatic Methods in Dialog


SfDialog provides several async methods for programmatic control:

### Showing and Hiding Dialogs

```razor
<SfDialog @ref="dialog" Width="500px">
    <DialogTemplates>
        <Content><p>Programmatic control demo</p></Content>
    </DialogTemplates>
</SfDialog>

<SfButton Content="Show Dialog" OnClick="ShowDialog" />
<SfButton Content="Hide Dialog" OnClick="HideDialog" />
<SfButton Content="Toggle Fullscreen" OnClick="ToggleFullscreen" />

@code {
    private SfDialog dialog;
    
    private async Task ShowDialog()
    {
        await dialog.ShowAsync();
    }
    
    private async Task HideDialog()
    {
        await dialog.HideAsync();
    }
    
    private async Task ToggleFullscreen()
    {
        await dialog.ShowAsync(true); // true = fullscreen mode
    }
}
```

### Getting Dialog Dimensions using GetDimensionAsync method

```razor
<SfDialog @ref="dialog"
          Width="400px"
          Header="Sample Dialog"
          @bind-Visible="showDialog">
    <DialogTemplates>
        <Content>
            <p>This is a sample dialog.</p>
        </Content>
    </DialogTemplates>
</SfDialog>

<SfButton Content="Open Dialog" OnClick="@(() => showDialog = true)" />
<SfButton Content="Get Dimensions" OnClick="GetDimensions" />

@code {
    private SfDialog? dialog;
    private bool showDialog = false;
    private async Task GetDimensions()
    {
        if (dialog is not null)
        {
            var dimensions = await dialog.GetDimensionAsync();
            Console.WriteLine($"Width: {dimensions.Width}, Height: {dimensions.Height}");
        }
        else
        {
            Console.WriteLine("Dialog reference is null — make sure it's rendered.");
        }
    }
}
```
<!-- ### Refreshing Position

After dynamically changing the dialog's size, refresh its position to keep it properly centered:

```razor
<SfDialog @ref="dialog" @bind-Visible="isOpen" Width="500px">
    <DialogTemplates>
        <Content><p>Refresh position demo</p></Content>
    </DialogTemplates>
</SfDialog>

<SfButton Content="Expand and Refresh" OnClick="ExpandDialog" />

@code {
    private SfDialog dialog;
    private bool isOpen = true;
    
    private async Task ExpandDialog()
    {
        dialog.Width = "800px";
        dialog.Height = "600px";
        await dialog.RefreshPositionAsync();
    }
}
``` -->

### GetButton
Returns the specific DialogButton instance at the specified index.

```csharp
SfDialog dialog;
DialogButton firstButton = dialog.GetButton(0);
DialogButton secondButton = dialog.GetButton(1);
```

### GetButtonItems
Returns the complete collection of DialogButton instances.

```csharp
SfDialog dialog;
List<DialogButton> allButtons = dialog.GetButtonItems();
if (allButtons is not null)
{
    foreach (var button in allButtons)
    {
        Console.WriteLine($"Button: {button.Content}");
    }
}
```

---

## Common Scenarios

### Simple Alert Dialog

```razor
<SfDialog @bind-Visible="showAlert" Width="400px" Height="auto">
    <DialogTemplates>
        <Header>
            <span>⚠️ Alert</span>
        </Header>
        <Content>
            <p>@alertMessage</p>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="OK" IsPrimary="true" OnClick="@(() => showAlert = false)" />
    </DialogButtons>
</SfDialog>

<SfButton class="e-btn" @onclick="ShowAlert">Show Alert</SfButton>

@code {
    private bool showAlert = false;
    private string alertMessage = "This is an alert message";

    private void ShowAlert()
    {
        alertMessage = "Something important happened!";
        showAlert = true;
    }
}
```

### Confirmation Dialog

```razor
<SfDialog @bind-Visible="showConfirm" Width="450px" Height="auto" IsModal="true">
    <DialogTemplates>
        <Header>
            <span>❓ Confirm</span>
        </Header>
        <Content>
            <p>Are you sure you want to delete this item?</p>
            <p style="color: #f44336; font-weight: bold;">This action cannot be undone.</p>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Delete" IsPrimary="true" CssClass="e-danger" OnClick="@OnConfirmDelete" />
        <DialogButton Content="Cancel" OnClick="@(() => showConfirm = false)" />
    </DialogButtons>
</SfDialog>
<SfButton class="e-btn" @onclick="ShowPopup">Show Alert</SfButton>

@code {
    private bool showConfirm = false;

    private async Task OnConfirmDelete()
    {
        // Perform delete action
        showConfirm = false;
    }
    private void ShowPopup()
    {
        showConfirm = true; // open the dialog
    }
}
```

### Information Dialog with Multiple Sections

```razor
<SfDialog @bind-Visible="showInfo" Width="600px" Height="auto">
    <DialogTemplates>
        <Header>
            <span>ℹ️ Information</span>
        </Header>
        <Content>
            <div style="padding: 16px;">
                <h4>Product Details</h4>
                <p><strong>Name:</strong> Syncfusion Blazor Toolkit</p>
                <p><strong>Version:</strong> 1.0.0</p>
                <p><strong>License:</strong> Commercial</p>
                
                <h4 style="margin-top: 20px;">Features</h4>
                <ul>
                    <li>Rich component library</li>
                    <li>Full responsive support</li>
                    <li>Accessibility compliant</li>
                </ul>
            </div>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Close" IsPrimary="true" OnClick="@(() => showInfo = false)" />
    </DialogButtons>
</SfDialog>

<SfButton class="e-btn" @onclick="() => showInfo = true">Show Info</SfButton>

@code {
    private bool showInfo = false;
}
```

---

## Key Takeaways

✅ **DO:**
- Use Modal="true" for critical user confirmations
- Provide clear header and footer for context
- Include descriptive button labels
- Handle BeforeClose event for unsaved changes validation
- Use animations sparingly for better UX

❌ **DON'T:**
- Create dialogs without clear close mechanisms
- Use dialogs for simple messages (use tooltips instead)
- Trap users in dialogs without escape option
- Overuse animations (performance impact)
- Hide important information in dialog content only

---

## Next: Advanced Dialog Features

For more complex scenarios like drag, resize, dynamic creation, and advanced templating, see [dialog-advanced.md](dialog-advanced.md).
