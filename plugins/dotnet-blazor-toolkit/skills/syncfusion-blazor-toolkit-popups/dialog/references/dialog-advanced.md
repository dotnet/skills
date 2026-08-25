# Dialog Advanced Features

## Table of Contents
- [Positioning](#positioning)
- [Drag and Resize Functionality](#drag-and-resize-functionality)
- [Custom Templates and Content](#custom-templates-and-content)
- [Complex Event Handling](#complex-event-handling)
- [State Management Patterns](#state-management-patterns)
- [Advanced Scenarios](#advanced-scenarios)

---

## Positioning

### Dialog Positioning - Keyword Alignment

Positions the dialog using predefined alignment keywords such as `top`, `center`, or `right`.

```razor
<div id="target-Keyword">
    <SfButton Content="Open Keyword Dialog" @onclick="@OpenKeywordDialog"></SfButton>

    <SfDialog Target="#target-Keyword"
              Width="400px"
              Header="Keyword Position"
              ShowCloseIcon="true"
              @bind-Visible="@KeywordVisibility">
        <DialogTemplates>
            <Content>
                <p>Dialog aligned with keywords</p>
            </Content>
        </DialogTemplates>
        <DialogPositionData X="center" Y="top"></DialogPositionData>
    </SfDialog>
</div>

<style>
    #target-Keyword {
        height: 200px;
        width: 700px;
    }
</style>

@code {
    private bool KeywordVisibility { get; set; } = false;

    private void OpenKeywordDialog()
    {
        KeywordVisibility = true;
    }
}
```

### Preset Positions

```razor
// Center on screen
private DialogPositionData centerPosition = new()
{
    X = "center",
    Y = "center"
};

// Top-left corner
private DialogPositionData topLeftPosition = new()
{
    X = "0",
    Y = "0"
};

// Top-center
private DialogPositionData topCenterPosition = new()
{
    X = "center",
    Y = "0"
};

// Bottom-right
private DialogPositionData bottomRightPosition = new()
{
    X = "right",
    Y = "bottom"
};
```

## Drag and Resize Functionality

### Resizing Dialog and ResizeHandles property

The `EnableResize` property allows users to dynamically adjust the size of a dialog, making it easier to view content in expanded mode. When this feature is enabled, resize handles appear along the dialog’s edges or corners, providing interactive grip areas that can be dragged to change its dimensions. The `ResizeHandles` property further refines this behavior by specifying which sides or corners of the dialog can be resized, giving developers precise control over the resizing experience.

```razor
<div id="target-resize">
    <div>
        <SfButton Content="Open Resize Dialog" @onclick="@OpenResizeDialog"></SfButton>
    </div>
        <SfDialog Target="#target-resize"
              Width="300px"
              ShowCloseIcon="true"
              EnableResize="true"
              ResizeHandles="@dialogResizeDirections"
              @bind-Visible="@IsResizeVisible"
              Header="Resize Dialog"
              Content="This is a resizing Dialog"
              OnResizeStart="@OnResizeStart"
              Resizing="@OnResizing"
              OnResizeStop="@OnResizeStop">
    </SfDialog>
    </div>

<style>
    #target-resize {
        height: 400px;
        width: 700px;
    }
</style>

@code {
    private bool IsResizeVisible { get; set; } = false;
    private ResizeDirection[] dialogResizeDirections { get; set; } = new ResizeDirection[] { ResizeDirection.All };
    private void OpenResizeDialog()
    {
        this.IsResizeVisible = true;
    }
    private void OnResizeStart(MouseEventArgs args)
    {
        Console.WriteLine("Resize started");
    }

    private void OnResizing(MouseEventArgs args)
    {
        // Resizing in progress - real-time updates
        Console.WriteLine($"Resizing at X: {args.ClientX}, Y: {args.ClientY}");
    }

    private void OnResizeStop(MouseEventArgs args)
    {
        Console.WriteLine("Resize stopped");
    }
}
```

### Draggable Dialog

To enable dragging capabilities, set the `AllowDragging` property to true on the Dialog component. When enabled, users can drag the Dialog by clicking and holding the Dialog header area.

```razor
@using Syncfusion.Blazor.Toolkit.Popups

<div id="target-draggable">
    <div>
        <SfButton Content="Open Draggable Dialog" @onclick="@OpenDraggableDialog"></SfButton>
    </div>

    <SfDialog Target="#target-draggable"
              Width="400px"
              Height="250px"
              ShowCloseIcon="true"
              AllowDragging="true"
              @bind-Visible="@IsDraggableVisible"
              Header="Draggable Dialog"
              Content="You can drag this dialog"
              OnDragStart="@OnDragStart"
              OnDrag="@OnDrag"
              OnDragStop="@OnDragStop">
    </SfDialog>
</div>

<style>
    #target-draggable {
        height: 400px;
        width: 700px;
    }
</style>

@code {
    private bool IsDraggableVisible { get; set; } = false;

    private void OpenDraggableDialog()
    {
        IsDraggableVisible = true;
    }

    private void OnDragStart(DragStartEventArgs args)
    {
        Console.WriteLine($"Drag started at X: {args.Event.ClientX}, Y: {args.Event.ClientY}");
    }

    private void OnDrag(DragEventArgs args)
    {
        Console.WriteLine($"Dragging at X: {args.Event.ClientX}, Y: {args.Event.ClientY}");
    }

    private void OnDragStop(DragStopEventArgs args)
    {
        Console.WriteLine($"Drag stopped at X: {args.Event.ClientX}, Y: {args.Event.ClientY}");
    }
}
```

---

## Custom Templates and Content

### Header and Footer Templates

You can set the Header and FooterTemplate of the SfDialog in two different ways. For simple cases, you can assign them directly as string or HTML properties, which is quick and useful when you only need plain text or a small snippet of markup. 

```razor
<SfDialog Width="500px" @bind-Visible="Visibility" Header="@HeaderText" FooterTemplate="@FooterHtml">
    <DialogTemplates>
        <Content>
            <p>Dialog content</p>
        </Content>
    </DialogTemplates>
</SfDialog>

@code {
    private bool Visibility { get; set; } = true;
    private string HeaderText { get; set; } = "Simple Header";
    private string FooterHtml { get; set; } = "<p>Simple Footer</p>";
}
```

When you need more complex layouts — such as multiple elements, styled sections, or action buttons — you should use render fragments inside `DialogTemplates`.

```razor
<SfDialog @bind-Visible="isDialogOpen" Width="600px" Height="auto">
    <DialogTemplates>
        <Header>
            <div style="display: flex; justify-content: space-between; align-items: center;">
                <span>Custom Header</span>
                <span style="font-size: 12px; color: #666;">ID: 12345</span>
            </div>
        </Header>
        
        <Content>
            <div style="padding: 20px;">
                <h4>Dialog Content</h4>
                <p>This is custom content with full HTML support</p>
                
                <div style="background-color: #f0f0f0; padding: 10px; border-radius: 4px;">
                    <strong>Info Box:</strong> Dialog supports complex content
                </div>
            </div>
        </Content>
        
        <FooterTemplate>
            <div style="display: flex; gap: 10px; justify-content: flex-end;">
                <button class="e-btn">Save</button>
                <button class="e-btn">Cancel</button>
            </div>
        </FooterTemplate>
    </DialogTemplates>
</SfDialog>

@code {
    private bool isDialogOpen = true;
}
```

### Form Content Template

```razor
<div id="dialog-container" style="height:500px; width:800px; position:relative;">
    <SfButton Content="Open Dialog" @onclick="@(() => showFormDialog = true)" />

    <SfDialog @bind-Visible="showFormDialog"
              Target="#dialog-container"
              Width="500px"
              Height="auto">
        <DialogTemplates>
            <Header>
                <span>User Registration Form</span>
            </Header>
            
            <Content>
                <div style="padding: 20px;">
                    <div style="margin-bottom: 15px;">
                        <SfTextBox @bind-Value="formData.FullName" Placeholder="Full Name" FloatLabelType="FloatLabelType.Auto" />
                    </div>
                    
                    <div style="margin-bottom: 15px;">
                        <SfTextBox @bind-Value="formData.Email" Placeholder="Email" Type="InputType.Email" FloatLabelType="FloatLabelType.Auto" />
                    </div>
                    
                    <div style="margin-bottom: 15px;">
                        <SfTextBox @bind-Value="formData.Message" Placeholder="Message" Multiline="true" FloatLabelType="FloatLabelType.Auto" />
                    </div>
                    
                    @if (!string.IsNullOrEmpty(validationError))
                    {
                        <div style="color: #f44336; padding: 10px; background-color: #ffebee; border-radius: 4px;">
                            @validationError
                        </div>
                    }
                </div>
            </Content>
        </DialogTemplates>
        
        <DialogButtons>
            <DialogButton Content="Submit" IsPrimary="true" OnClick="@OnSubmit" />
            <DialogButton Content="Cancel" OnClick="@OnCancel" />
        </DialogButtons>
    </SfDialog>
</div>

@code {
    private bool showFormDialog = false;
    private string validationError = "";

    private class FormData
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Message { get; set; } = "";
    }

    private FormData formData = new();

    private async Task OnSubmit()
    {
        validationError = "";

        if (string.IsNullOrWhiteSpace(formData.FullName))
        {
            validationError = "Full Name is required";
            return;
        }

        if (string.IsNullOrWhiteSpace(formData.Email))
        {
            validationError = "Email is required";
            return;
        }

        Console.WriteLine($"Form submitted: {formData.FullName}, {formData.Email}");
        showFormDialog = false;
        formData = new();
    }

    private void OnCancel()
    {
        validationError = "";
        formData = new();
        showFormDialog = false;
    }
}
```

## Complex Event Handling

### Preventing Dialog Close

```razor
<SfDialog @bind-Visible="isOpen"
          OnClose="OnCloseHandler"
          ShowCloseIcon="true"
          IsModal="true"
          Width="350px">
    <DialogTemplates>
        <Content>
            <p>You have unsaved changes!</p>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Discard" OnClick="(() => isOpen = false)" />
    </DialogButtons>
</SfDialog>

@code {
    private bool isOpen = true;

    private void OnCloseHandler(BeforeCloseEventArgs args)
    {
        // Prevent dialog from closing 
        args.Cancel = true;
    }
}
```

### Handling Overlay Click

When the modal overlay (backdrop) is clicked, `OnOverlayModalClick` is triggered.  It provides an opportunity to respond to overlay clicks, which commonly involves closing the dialog or showing confirmation messages. This event is only applicable when the dialog is configured as a modal dialog using the `IsModal` property.

```razor
<SfDialog @bind-Visible="isOpen"
          IsModal="true"
          OnOverlayModalClick="OnOverlayClick"
          Width="400px">
    <DialogTemplates>
        <Header><span>Click Outside</span></Header>
        <Content>
            <p>Click the backdrop.</p>
        </Content>
    </DialogTemplates>
</SfDialog>

@code {
    private bool isOpen = true;

    private void OnOverlayClick(OverlayModalClickEventArgs args)
    {
        Console.WriteLine("Overlay was clicked");
    }
}
```

### Multi-Step Dialog

```razor
<SfDialog @bind-Visible="isDialogOpen" Width="600px" Height="auto">
    <DialogTemplates>
        <Header>
            <span>Setup Wizard - Step @currentStep of 3</span>
        </Header>
        
        <Content>
            @if (currentStep == 1)
            {
                <div style="padding: 20px;">
                    <h4>Step 1: Enter Name</h4>
                    <SfTextBox @bind-Value="wizardData.Name" Placeholder="Enter your name" FloatLabelType="FloatLabelType.Auto" />
                </div>
            }
            else if (currentStep == 2)
            {
                <div style="padding: 20px;">
                    <h4>Step 2: Enter Email</h4>
                    <SfTextBox @bind-Value="wizardData.Email" Placeholder="Enter your email" Type="InputType.Email" FloatLabelType="FloatLabelType.Auto" />
                </div>
            }
            else if (currentStep == 3)
            {
                <div style="padding: 20px;">
                    <h4>Step 3: Confirm Details</h4>
                    <p><strong>Name:</strong> @wizardData.Name</p>
                    <p><strong>Email:</strong> @wizardData.Email</p>
                </div>
            }
        </Content>
    </DialogTemplates>
    
    <DialogButtons>
        @if (currentStep > 1)
        {
            <DialogButton Content="Previous" OnClick="PreviousStep" />
        }
        
        @if (currentStep < 3)
        {
            <DialogButton Content="Next" IsPrimary="true" OnClick="NextStep" />
        }
        else
        {
            <DialogButton Content="Finish" IsPrimary="true" OnClick="FinishWizard" />
        }
        
        <DialogButton Content="Cancel" OnClick="CancelWizard" />
    </DialogButtons>
</SfDialog>

@code {
    private bool isDialogOpen = true;
    private int currentStep = 1;
    
    private class WizardData
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }
    
    private WizardData wizardData = new();

    private void NextStep()
    {
        if (currentStep < 3) currentStep++;
    }

    private void PreviousStep()
    {
        if (currentStep > 1) currentStep--;
    }

    private async Task FinishWizard()
    {
        Console.WriteLine($"Wizard completed: {wizardData.Name}, {wizardData.Email}");
        isDialogOpen = false;
        currentStep = 1;
    }

    private void CancelWizard()
    {
        isDialogOpen = false;
        currentStep = 1;
        wizardData = new();
    }
}
```

---

## State Management Patterns

### Dialog State in Parent Component

```razor
@page "/state-management"

<EditForm Model="formData" OnValidSubmit="@SaveForm">
    <DataAnnotationsValidator />

    <div>
        <label for="nameInput">Name:</label>
        <SfTextBox @bind-Value="formData.Name" Placeholder="Enter your name" ID="nameInput" />
    </div>

    <div>
        <label for="emailInput">Email:</label>
        <SfTextBox @bind-Value="formData.Email" Placeholder="Enter your email" ID="emailInput" />
    </div>

    <SfButton Content="Preview" OnClick="@ShowPreview" />
    <SfButton Content="Save" IsPrimary="true" Type="ButtonType.Submit" />
</EditForm>

<SfDialog @bind-Visible="showPreview" Width="400px" Height="auto">
    <DialogTemplates>
        <Header>
            <span>Preview</span>
        </Header>
        <Content>
            <p><strong>Name:</strong> @formData.Name</p>
            <p><strong>Email:</strong> @formData.Email</p>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Close" OnClick="@(() => showPreview = false)" />
    </DialogButtons>
</SfDialog>

@code {
    private bool showPreview = false;

    private class FormModel
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    private FormModel formData = new();

    private void ShowPreview()
    {
        showPreview = true;
    }

    private async Task SaveForm()
    {
        Console.WriteLine($"Saved: {formData.Name}, {formData.Email}");
    }
}
```

---

## Advanced Scenarios

### Nested Dialogs

```razor
<SfButton Content="Open Dialog 1" OnClick="() => showDialog1 = true" />

<SfDialog @bind-Visible="showDialog1" Width="500px">
    <DialogTemplates>
        <Header><span>Dialog 1</span></Header>
        <Content>
            <p>This is the first dialog</p>
            <SfButton class="e-btn" @onclick="() => showDialog2 = true">
                Open Dialog 2
            </SfButton>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Close" OnClick="@(() => showDialog1 = false)" />
    </DialogButtons>
</SfDialog>

<SfDialog @bind-Visible="showDialog2" Width="400px">
    <DialogTemplates>
        <Header><span>Dialog 2</span></Header>
        <Content>
            <p>This is a nested dialog</p>
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Close" OnClick="@(() => showDialog2 = false)" />
    </DialogButtons>
</SfDialog>

@code {
    private bool showDialog1 = false;
    private bool showDialog2 = false;
}
```

### Dialog with Loading State

```razor
<SfButton Content="Start Loading" OnClick="SimulateLoading" />

<SfDialog @bind-Visible="showDialog" Width="350px">
    <DialogTemplates>
        <Header><span>Loading</span></Header>
        <Content>
            @if (isLoading)
            {
                <p>Processing your request...</p>
            }
            else
            {
                <p>@resultMessage</p>
            }
        </Content>
    </DialogTemplates>
    <DialogButtons>
        <DialogButton Content="Close" OnClick="@(() => showDialog = false)" />
    </DialogButtons>
</SfDialog>

@code {
    private bool showDialog = false;
    private bool isLoading = false;
    private string resultMessage = "";

    private async Task SimulateLoading()
    {
        showDialog = true;
        isLoading = true;
        resultMessage = "";

        await Task.Delay(2000); // simulate work

        isLoading = false;
        resultMessage = "Operation completed!";
    }
}
```
---

## Key Takeaways

✅ **DO:**
- Use positioning for better UX
- Implement drag and resize for flexibility
- Handle OnClose for data validation
- Use templates for complex content
- Implement multi-step workflows for complex processes

❌ **DON'T:**
- Create deeply nested dialogs (hard to navigate)
- Ignore resize constraints (can break UI)
- Use service for every dialog (prefer direct binding)
- Forget to cleanup dialog state
- Create inaccessible forms in dialogs
