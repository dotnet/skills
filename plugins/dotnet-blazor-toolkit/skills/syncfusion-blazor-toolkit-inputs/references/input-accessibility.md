# Input Component Accessibility - WCAG Compliance Guide

## Table of Contents
- [Accessibility Overview](#accessibility-overview)
- [Keyboard Navigation](#keyboard-navigation)
- [ARIA Attributes](#aria-attributes)
- [Focus Management](#focus-management)
- [Labels and Instructions](#labels-and-instructions)
- [Error Messaging](#error-messaging)
- [Component-Specific Guidelines](#component-specific-guidelines)
- [Testing Accessibility](#testing-accessibility)

## Accessibility Overview

Accessibility ensures that all users, including those with disabilities, can interact with input components effectively. This includes users with:
- **Visual impairments** (blind, low vision) - screen reader users
- **Motor impairments** - keyboard-only navigation, speech input
- **Cognitive impairments** - clear instructions, consistent patterns
- **Hearing impairments** - visual feedback not sound-dependent

### WCAG 2.1 Level AA Compliance

Syncfusion input components support WCAG 2.1 Level AA standards:
- **Principle 1: Perceivable** - Information visible to all users
- **Principle 2: Operable** - All functionality keyboard accessible
- **Principle 3: Understandable** - Clear, predictable interface
- **Principle 4: Robust** - Compatible with assistive technologies

## Keyboard Navigation

### Tab Order and Focus

```razor
<div class="keyboard-nav-demo">
    <h2>Tab Order Example</h2>

    <label for="name">Name:</label>
    <SfTextBox 
        ID="name"
        @ref="nameInput"
        Placeholder="Enter your name"
        TabIndex="1">
    </SfTextBox>

    <label for="email">Email:</label>
    <SfTextBox 
        ID="email"
        Placeholder="Enter your email"
        TabIndex="2">
    </SfTextBox>

    <!-- Tab order: name → email → button -->
    <button TabIndex="3">Submit</button>
</div>

@code {
    private SfTextBox nameInput;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && nameInput != null)
        {
            // Focus first input on page load
            await nameInput.FocusAsync();
        }
    }
}
```

### Keyboard Access for All Components

| Component | Keyboard Shortcuts |
|-----------|-------------------|
| **TextBox/TextArea** | Tab: navigate, Shift+Tab: back, Enter: submit (form) |
| **NumericTextBox** | Arrow Up/Down: increment/decrement by step |
| **CheckBox** | Space: toggle, Tab: navigate |
| **RadioButton** | Arrow keys: navigate group, Space: select, Tab: next group |

### Keyboard Event Handling

```razor
<SfTextBox 
    @bind-Value="@searchText"
    Placeholder="Search (use arrow keys for history)"
    @onkeydown="@OnSearchKeyDown">
</SfTextBox>

@code {
    private string searchText = "";
    private List<string> searchHistory = new();

    private void OnSearchKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "ArrowUp" && searchHistory.Count > 0)
        {
            // Navigate search history upward
            searchText = searchHistory.Last();
        }
        else if (args.Key == "ArrowDown")
        {
            // Navigate search history downward
            searchText = "";
        }
        else if (args.Key == "Enter")
        {
            // Execute search
            searchHistory.Add(searchText);
        }
    }
}
```

## ARIA Attributes

### ARIA Labels and Descriptions

```razor
<div class="aria-labels-demo">
    <h2>Form with Proper ARIA</h2>

    <label for="username">Username:</label>
    <SfTextBox 
        ID="username"
        aria-label="Username - required field"
        aria-required="true"
        aria-describedby="username-hint"
        Placeholder="Enter username">
    </SfTextBox>
    <small id="username-hint">Must be at least 3 characters</small>

    <label for="password">Password:</label>
    <SfTextBox 
        ID="password"
        Type="InputType.Password"
        aria-label="Password - required field"
        aria-required="true"
        Placeholder="Enter password">
    </SfTextBox>
</div>
```

### ARIA Live Regions

```razor
<div class="aria-live-demo">
    <h2>Real-time Validation Feedback</h2>

    <SfTextBox 
        @bind-Value="@email"
        Placeholder="Enter email"
        OnInput="@OnEmailInput">
    </SfTextBox>

    <div 
        id="email-status"
        role="status"
        aria-live="polite"
        aria-atomic="true">
        @emailValidationMessage
    </div>
</div>

@code {
    private string email = "";
    private string emailValidationMessage = "";

    private void OnEmailInput(InputEventArgs args)
    {
        // Validation happens, aria-live region updates automatically
        if (string.IsNullOrEmpty(email))
        {
            emailValidationMessage = "Email is required";
        }
        else if (!email.Contains("@"))
        {
            emailValidationMessage = "Please enter a valid email";
        }
        else
        {
            emailValidationMessage = "Email format is valid";
        }
    }
}
```

### ARIA for Form Groups

```razor
<fieldset>
    <legend>Select Your Preferences</legend>

    <div role="group" aria-labelledby="notification-legend">
        <h3 id="notification-legend">Notification Options:</h3>
        
        <SfCheckBox 
            TChecked="bool"
            @bind-Checked="@emailNotif"
            Label="Email Notifications"
            aria-describedby="email-desc">
        </SfCheckBox>
        <span id="email-desc">Receive updates via email</span>

        <SfCheckBox 
            TChecked="bool"
            @bind-Checked="@pushNotif"
            Label="Push Notifications"
            aria-describedby="push-desc">
        </SfCheckBox>
        <span id="push-desc">Receive alerts on your device</span>

        <SfCheckBox 
            TChecked="bool"
            @bind-Checked="@smsNotif"
            Label="SMS Notifications"
            aria-describedby="sms-desc">
        </SfCheckBox>
        <span id="sms-desc">Receive text messages</span>
    </div>
</fieldset>

@code {
    private bool emailNotif = false;
    private bool pushNotif = false;
    private bool smsNotif = false;
}
```

## Focus Management

### Visible Focus Indicators

```razor
<style>
    /* Ensure visible focus indicators */
    :focus-visible {
        outline: 3px solid #4A90E2;
        outline-offset: 2px;
    }

    /* High contrast focus for dark mode */
    @@media (prefers-contrast: more) {
        :focus-visible {
            outline: 3px solid #FFF;
            outline-offset: 2px;
        }
    }
</style>

<SfTextBox 
    Placeholder="Click or tab to focus"
    class="accessible-textbox">
</SfTextBox>
```

### Programmatic Focus

```razor
@using System.ComponentModel.DataAnnotations

<div class="focus-management-demo">
    <h2>Form Validation with Focus</h2>

    <EditForm Model="@formModel" OnValidSubmit="@HandleSubmit">
        <DataAnnotationsValidator />

        <div class="form-group">
            <label for="firstname">First Name (required):</label>
            <SfTextBox 
                ID="firstname"
                @ref="firstNameInput"
                @bind-Value="@formModel.FirstName"
                Placeholder="Enter first name">
            </SfTextBox>
            <ValidationMessage For="@(() => formModel.FirstName)" />
        </div>

        <div class="form-group">
            <label for="age">Age:</label>
            <SfNumericTextBox 
                TValue="int"
                ID="age"
                @bind-Value="@formModel.Age"
                Min="18"
                Max="120">
            </SfNumericTextBox>
        </div>

        <button type="submit">Submit</button>
    </EditForm>
</div>

@code {
    private SfTextBox firstNameInput;
    private FormModel formModel = new();

    private async Task HandleSubmit()
    {
        // Focus returns to first field if validation fails
        if (formModel.FirstName?.Length < 2)
        {
            await firstNameInput.FocusAsync();
        }
    }

    private class FormModel
    {
        public string FirstName { get; set; }
        public int Age { get; set; }
    }
}
```

### Focus Trap for Modals

```razor
<div class="modal-dialog" role="dialog" aria-modal="true" aria-labelledby="modal-title">
    <h2 id="modal-title">Confirm Action</h2>

    <p>Are you sure you want to proceed?</p>

    <div class="modal-buttons">
        <button 
            @ref="confirmButton"
            @onclick="OnConfirm"
            @onkeydown="@OnModalKeyDown">
            Confirm
        </button>

        <button 
            @ref="cancelButton"
            @onclick="OnCancel">
            Cancel
        </button>
    </div>
</div>

@code {
    private ElementReference confirmButton;
    private ElementReference cancelButton;

    private async Task OnModalKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Tab")
        {
            // Trap focus within modal
            args.PreventDefault();
            // Focus next button or wrap to first
        }
    }

    private void OnConfirm() => Console.WriteLine("Confirmed");
    private void OnCancel() => Console.WriteLine("Cancelled");
}
```

## Labels and Instructions

### Explicit Label Association

```razor
<div class="label-association">
    <h2>Properly Associated Labels</h2>

    <!-- Good: label with for attribute -->
    <label for="email-input">Email Address:</label>
    <SfTextBox 
        ID="email-input"
        Type="InputType.Email"
        Placeholder="user@example.com">
    </SfTextBox>

    <!-- Good: aria-label fallback -->
    <SfNumericTextBox 
        TValue="int"
        aria-label="Enter quantity"
        Min="1"
        Max="100">
    </SfNumericTextBox>
</div>
```

### Help Text and Instructions

```razor
<div class="instructions-demo">
    <h2>Form with Clear Instructions</h2>

    <div class="form-group">
        <label for="password">Create Password:</label>
        <SfTextBox 
            ID="password"
            Type="InputType.Password"
            aria-describedby="password-requirements"
            Placeholder="Enter password">
        </SfTextBox>
        <ul id="password-requirements">
            <li>At least 8 characters</li>
            <li>Include uppercase and lowercase</li>
            <li>Include numbers and symbols</li>
        </ul>
    </div>
</div>
```

## Error Messaging

### Accessible Error Messages

```razor
@using System.ComponentModel.DataAnnotations

<div class="error-messaging-demo">
    <h2>Form with Error Messages</h2>

    <EditForm Model="@errorModel" OnValidSubmit="@SubmitForm">
        <DataAnnotationsValidator />

        <div class="form-group">
            <label for="email">Email:</label>
            <SfTextBox 
                ID="email"
                Type="InputType.Email"
                @bind-Value="@errorModel.Email"
                aria-describedby="email-error"
                Placeholder="Enter email">
            </SfTextBox>
            
            <!-- Error message with aria-live for screen readers -->
            <div 
                id="email-error" 
                role="alert"
                aria-live="assertive"
                class="error-message">
                <ValidationMessage For="@(() => errorModel.Email)" />
            </div>
        </div>

        <button type="submit">Submit</button>
    </EditForm>
</div>

@code {
    private ErrorModel errorModel = new();

    private void SubmitForm()
    {
        Console.WriteLine("Form submitted");
    }

    private class ErrorModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Please enter a valid email")]
        public string Email { get; set; }
    }
}
```

### Real-time Validation Feedback

```razor
<SfTextBox 
    @bind-Value="@username"
    OnInput="@OnUsernameInput"
    OnBlur="@OnUsernameBlur">
</SfTextBox>

<div role="status" aria-live="polite">
    @if (!string.IsNullOrEmpty(usernameError))
    {
        <span class="error" aria-label="@usernameError">
            ❌ @usernameError
        </span>
    }
    else if (usernameValid)
    {
        <span class="success" aria-label="Username is valid">
            ✓ Username is available
        </span>
    }
</div>

@code {
    private string username = "";
    private string usernameError = "";
    private bool usernameValid = false;

    private void OnUsernameInput(InputEventArgs args)
    {
        username = args.Value.ToString();
        
        if (username.Length < 3)
        {
            usernameError = "Username must be at least 3 characters";
            usernameValid = false;
        }
        else
        {
            usernameError = "";
            // Check availability (mock)
            usernameValid = !new[] { "admin", "user", "root" }.Contains(username);
        }
    }

    private void OnUsernameBlur(FocusOutEventArgs args)
    {
        // Ensure error message is announced when focus leaves
    }
}
```

## Component-Specific Guidelines

### TextBox and TextArea Accessibility

```razor
<div class="textbox-accessibility">
    <div>
        <label for="full-name">Full Name:</label>
        <SfTextBox 
            ID="full-name"
            @bind-Value="@fullName"
            Placeholder="First Last"
            aria-required="true"
            aria-describedby="name-format">
        </SfTextBox>
        <span id="name-format">Enter your first and last name</span>
    </div>

    <div>
        <label for="bio">Biography:</label>
        <SfTextArea 
            ID="bio"
            @bind-Value="@bio"
            RowCount="5"
            MaxLength="500"
            aria-describedby="char-count">
        </SfTextArea>
        <span id="char-count">@bio.Length / 500 characters</span>
    </div>
</div>

@code {
    private string fullName = "";
    private string bio = "";
}
```

### Checkbox and RadioButton Accessibility

```razor
<fieldset>
    <legend>Contact Preferences</legend>

    <div role="group" aria-labelledby="frequency-legend">
        <h3 id="frequency-legend">How often would you like to hear from us?</h3>

        <SfRadioButton 
            TChecked="string"
            @bind-Checked="@frequency"
            Value="weekly" 
            Label="Weekly"
            Name="frequency">
        </SfRadioButton>

        <SfRadioButton 
            TChecked="string"
            @bind-Checked="@frequency"
            Value="monthly" 
            Label="Monthly"
            Name="frequency">
        </SfRadioButton>

        <SfRadioButton 
            TChecked="string"
            @bind-Checked="@frequency"
            Value="never" 
            Label="Never"
            Name="frequency">
        </SfRadioButton>
    </div>

    <fieldset>
        <legend>Notification Types (Select all that apply):</legend>

        <SfCheckBox 
            TChecked="bool"
            @bind-Checked="@notifEmail"
            Label="Email"
            aria-describedby="email-desc">
        </SfCheckBox>
        <span id="email-desc">Get updates via email</span>

        <SfCheckBox 
            TChecked="bool"
            @bind-Checked="@notifPush"
            Label="Push"
            aria-describedby="push-desc">
        </SfCheckBox>
        <span id="push-desc">Get alerts on your device</span>
    </fieldset>
</fieldset>

@code {
    private string frequency = "weekly";
    private bool notifEmail = true;
    private bool notifPush = false;
}
```

### NumericTextBox Accessibility

```razor
<div class="numeric-accessibility">
    <label for="quantity">Quantity:</label>
    <SfNumericTextBox 
        TValue="int"
        ID="quantity"
        @bind-Value="@quantity"
        Min="1"
        Max="100"
        ShowSpinButton="true"
        aria-label="Quantity - use arrow keys or spinner buttons"
        aria-describedby="quantity-hint">
    </SfNumericTextBox>
    <span id="quantity-hint">Minimum 1, Maximum 100. Use arrow keys to adjust.</span>
</div>

@code {
    private int quantity = 1;
}
```

## Testing Accessibility

### Manual Testing Checklist

- [ ] **Keyboard Navigation**: Tab through all inputs in logical order
- [ ] **Focus Visibility**: All interactive elements show clear focus indicator
- [ ] **Screen Reader**: Test with NVDA/JAWS/VoiceOver
- [ ] **Labels**: All inputs have associated labels
- [ ] **Error Messages**: Errors announced with aria-live
- [ ] **Color Contrast**: Text contrast ≥ 4.5:1 for normal text, ≥ 3:1 for large text
- [ ] **Touch Targets**: Clickable areas ≥ 44×44 pixels
- [ ] **Zoom**: Content remains usable at 200% zoom

### Automated Testing Example

```csharp
// Using AxeCore accessibility testing in Blazor
[TestClass]
public class AccessibilityTests
{
    [TestMethod]
    public async Task TextBoxFormShouldBeAccessible()
    {
        // Setup component with accessibility attributes
        var formComponent = new FormComponent();
        
        // Run AxeCore scan
        var results = await formComponent.RunAccessibilityAudit();
        
        // Assert no accessibility violations
        Assert.AreEqual(0, results.Violations.Count, 
            "Form should have no accessibility violations");
    }
}
```

### Screen Reader Testing Commands

**NVDA (Windows):**
```
- Insert + F7: View element list
- Insert + F: Find mode
- h: Next heading
- l: Next list
- g: Next graphic
```

**JAWS (Windows):**
```
- Ins + F6: Settings
- Ins + ; (semicolon): List of links
- Ins + ' (apostrophe): List of headings
- Ins + ? : Help
```

**VoiceOver (Mac/iOS):**
```
- Control + Option + U: Web rotor
- Control + Option + Right Arrow: Navigate
- Control + Option + Space: Activate
```

### Accessibility Best Practices Summary

1. **Always use semantic HTML** (label, fieldset, legend)
2. **Provide clear, descriptive labels** for all inputs
3. **Make keyboard navigation obvious** and logical
4. **Test with actual assistive technologies**
5. **Use ARIA only when HTML semantics insufficient**
6. **Provide sufficient color contrast** (≥ 4.5:1)
7. **Don't rely on color alone** to convey information
8. **Make error messages clear and associated** with inputs
9. **Use aria-live for dynamic updates**
10. **Test with real users** who use assistive technologies
