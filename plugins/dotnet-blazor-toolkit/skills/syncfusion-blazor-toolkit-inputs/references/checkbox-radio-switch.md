# Checkbox, RadioButton, and Switch Components - Selection Controls

## Table of Contents
- [SfCheckBox Overview](#sfcheckbox-overview)
- [CheckBox Properties](#checkbox-properties)
- [CheckBox Events](#checkbox-events)
- [SfRadioButton Overview](#sfradiobutton-overview)
- [RadioButton Properties](#radiobutton-properties)
- [RadioButton Events](#radiobutton-events)
- [SfSwitch Overview](#sfswitch-overview)
- [Switch Properties](#switch-properties)
- [Switch Events](#switch-events)
- [Practical Examples](#practical-examples)

## SfCheckBox Overview

**SfCheckBox<T>** allows multiple independent selections. It's generic to support any value type and provides checked, unchecked, and indeterminate states.

**When to use SfCheckBox:**
- Multiple independent options (select many)
- Agreement/terms acceptance
- Feature toggles with multiple choices
- Preference selections
- Bulk actions (select all)

### Basic Implementation

```razor
<SfCheckBox TChecked="bool" Checked="true" />
```

### With Label

```razor
<SfCheckBox TChecked="bool" @bind-Checked="@agreeTerms" Label="I agree to the terms and conditions"></SfCheckBox>

@code {
    private bool agreeTerms = false;
}
```

## CheckBox Properties

### Essential Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Checked` | `TChecked` | `default(TChecked)` | Checked state |
| `Indeterminate` | `bool` | `false` | Indeterminate state |
| `Label` | `string` | `""` | Label text |
| `Disabled` | `bool` | `false` | Disable checkbox |
| `LabelPosition` | `LabelPosition` | `Before` | Label placement |
| `EnableTriState` | `bool` | `false` | Enable three-state-mode |
| `CssClass` | `string` | `""` | Custom CSS classes |

### Checkbox States

Common CheckBox states include `Checked`, `Unchecked`, `Disabled`, and `Indeterminate`. Use Disabled to prevent interaction, and enable tri‑state using `EnableTriState` to support the Indeterminate state.

**Note:** For proper tri-state behavior, use `bool?` (nullable bool) as the generic type `TChecked`. The `bool` type only supports binary checked/unchecked states.

```razor
<SfCheckBox TChecked="bool" Checked="true" Label="Checked" />
<SfCheckBox TChecked="bool" Label="Unchecked" />
<SfCheckBox TChecked="bool" Checked="true" Label="Disabled" Disabled="true" />
<SfCheckBox TChecked="bool?" @bind-Checked="tIndeterminate" EnableTriState="true" Label="Indeterminate" />
@code {
    private bool? tIndeterminate = null; // null = indeterminate state
}
```

### Label Positioning

```razor
<div class="label-positioning">
    <h3>Label Positions</h3>

    <div>
        <SfCheckBox 
            TChecked="bool"
            Label="Label on the Right" 
            LabelPosition="LabelPosition.After">
        </SfCheckBox>
    </div>

    <div>
        <SfCheckBox 
            TChecked="bool"
            Label="Label on the Left" 
            LabelPosition="LabelPosition.Before">
        </SfCheckBox>
    </div>
</div>
```

### CheckBox Customization

Customize the appearance of the CheckBox using built‑in theme color classes or fully custom CSS.

```razor
<!-- Color -->
<SfCheckBox TChecked="bool" Checked="true" Label="Primary" CssClass="e-primary"></SfCheckBox>
@code {
    private bool isPrimary = true;
}
<style>
.e-checkbox-wrapper.e-primary .e-frame {
    border-color: #007bff;   /* blue border */
}

.e-checkbox-wrapper.e-primary .e-frame.e-check {
    background-color: #007bff; /* blue fill when checked */
    border-color: #007bff;
}

.e-checkbox-wrapper.e-primary .e-label {
    color: #007bff; /* label text color */
}

</style>

```

### Grouping

Bind the checked state using Blazor’s `bind-Checked` for two‑way binding. When you need custom logic during updates, use the explicit pattern with `Checked` and `CheckedChanged`.

```razor
<SfCheckBox TChecked="bool?"
            Checked="@notifParentState"
            CheckedChanged="OnNotifParentClicked"
            EnableTriState="true"
            Label="All notifications" />

<ul class="list-unstyled ms-3">
@for (int i = 0; i < notifItems.Count; i++)
{
    var idx = i;
    <li class="mb-1" @key="notifItems[idx]">
        <SfCheckBox TChecked="bool"
                    Checked="@notifItems[idx].Selected"
                    CheckedChanged="@((bool v) => { notifItems[idx].Selected = v; OnNotifChildChanged(); })"
                    Label="@notifItems[idx].Name" />
    </li>
}
</ul>

<p class="mt-2">
    <small>
        Selected: <b>@notifSelectedCount</b> of <b>@notifItems.Count</b>
    </small>
</p>

@code {
    private bool? notifParentState = false;
    private bool indeterminateWhenAllSelected = false;

    private class Pick { public string Name { get; set; } = ""; public bool Selected { get; set; } }

    private List<Pick> notifItems = new()
    {
        new Pick { Name = "Product updates", Selected = true },
        new Pick { Name = "Security alerts", Selected = true },
        new Pick { Name = "Promotions",      Selected = false },
        new Pick { Name = "Surveys",         Selected = false },
    };

    private int notifSelectedCount => notifItems.Count(i => i.Selected);

    protected override void OnInitialized()
    {
        UpdateNotifParentState();
    }

    private void UpdateNotifParentState()
    {
        int total = notifItems.Count;
        int selected = notifItems.Count(i => i.Selected);

        if (selected == 0)
            notifParentState = false;
        else if (selected == total)
            notifParentState = indeterminateWhenAllSelected ? null : true;
        else
            notifParentState = null;
    }

    private void OnNotifChildChanged()
    {
        UpdateNotifParentState();
    }

    private void OnNotifParentClicked(bool? valueFromUI)
    {
        bool target = notifItems.Any(i => !i.Selected); // if any unchecked -> check all; else uncheck all

        if (valueFromUI == true) target = true;
        else if (valueFromUI == false) target = false;

        foreach (var it in notifItems)
            it.Selected = target;

        UpdateNotifParentState();
    }
}
```

## CheckBox Events

### ValueChange Event

```razor
<SfCheckBox 
    TChecked="bool"
    @bind-Checked="@newsletter"
    Label="Subscribe to Newsletter"
    ValueChange="@OnNewsletterChanged">
</SfCheckBox>

<p>@newsletterStatus</p>

@code {
    private bool newsletter = false;
    private string newsletterStatus = "Not subscribed";

    private void OnNewsletterChanged(CheckedChangeEventArgs<bool> args)
    {
        newsletter = args.Checked;
        newsletterStatus = args.Checked ? "Subscribed to newsletter" : "Unsubscribed from newsletter";
    }
}
```

### Created Event

```razor
<SfCheckBox TChecked="bool" Created="@OnCreated" Label="Created checkbox"></SfCheckBox>

@code {
    private void OnCreated(object args)
    {
        Console.WriteLine("CheckBox created");
    }
}
```

## SfRadioButton Overview

**SfRadioButton<T>** allows single selection from a group. All radio buttons in a group must share the same `Name` attribute.

**When to use SfRadioButton:**
- Single selection from multiple options
- Mutually exclusive choices
- Gender/marital status
- Shipping method selection
- Survey questions

### Basic Implementation

```razor
<SfRadioButton TChecked="string" @bind-Checked="@gender" Value="male" Label="Male" Name="gender"></SfRadioButton>
<SfRadioButton TChecked="string" @bind-Checked="@gender" Value="female" Label="Female" Name="gender"></SfRadioButton>

@code {
    private string gender = "";
}
```

## RadioButton Properties

### Essential Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Label` | `string` | `null` | Label text |
| `Disabled` | `bool` | `false` | Disable radio button |
| `LabelPosition` | `LabelPosition` | `Before` | Label placement |
| `Name` | `string` | `""` | Groups radio buttons together |
| `Value` | `string?` | `null` | Value associated with this radio option |
| `CssClass` | `string` | `""` | Custom CSS classes |

### Radio Button Labels and States

The `Label` property provides contextual text next to the Radio Button. Use the `LabelPosition` property to place the label either `Before` or `After` the radio. Radio Buttons support common selection states including `Checked`, `Unchecked`, and `Disabled`.

```razor

<SfRadioButton TChecked="string" Name="labelpos" Label="Label First" Value="Before" LabelPosition="LabelPosition.Before" @bind-Checked="labelPosPick" />
<SfRadioButton TChecked="string" Name="labelpos" Label="Label Last"  Value="After"  LabelPosition="LabelPosition.After"  @bind-Checked="labelPosPick" />
@code {
    private string? labelPosPick = "After";
}

```

### Radio Button Color Customization

Customize the appearance of Radio Buttons using built‑in theme color classes such as `e-success`, `e-info`, `e-warning`, and `e-danger`. These semantic color utilities help visually differentiate important selections or highlight specific actions. Additional styling can be applied using custom CSS targeting the Radio Button wrapper and its state indicators.

```razor
<SfRadioButton TChecked="string" Name="colors" CssClass="e-success" Label="Success" Value="Success" @bind-Checked="colorPick" />
<SfRadioButton TChecked="string" Name="colors" Label="Info" Value="Info" @bind-Checked="colorPick" />

@code {
    private string? colorPick = "Success";
}
<style>
  .e-radio-wrapper.e-success .e-radio:checked + label::after {
    background-color: #689f38;
}
.e-radio-wrapper.e-success .e-radio:checked + label::before {
    border-color: #689f38;
}
</style>

```

### Radio Button Groups

```razor
<div class="radio-group">
    <h3>Select Shipping Method:</h3>

    <SfRadioButton TChecked="string" 
        @bind-Checked="@selectedShipping"
        Value="standard" 
        Label="Standard (5-7 days)" 
        Name="shipping">
    </SfRadioButton>

    <SfRadioButton TChecked="string" 
        @bind-Checked="@selectedShipping"
        Value="express" 
        Label="Express (2-3 days)" 
        Name="shipping">
    </SfRadioButton>

    <SfRadioButton TChecked="string" 
        @bind-Checked="@selectedShipping"
        Value="overnight" 
        Label="Overnight (1 day)" 
        Name="shipping">
    </SfRadioButton>

    <p>Selected: @selectedShipping</p>
</div>

@code {
    private string selectedShipping = "standard";
}
```

### Disabled Radio Buttons

Disabled radio buttons prevent user selection, typically used for unavailable options:

```razor
<div class="radio-disabled-demo">
    <h3>Select Payment Method:</h3>

    <SfRadioButton TChecked="string" 
        @bind-Checked="@selectedPayment"
        Value="credit-card" 
        Label="Credit Card" 
        Name="payment">
    </SfRadioButton>

    <SfRadioButton TChecked="string" 
        @bind-Checked="@selectedPayment"
        Value="debit-card" 
        Label="Debit Card" 
        Name="payment">
    </SfRadioButton>

    <SfRadioButton TChecked="string" 
        @bind-Checked="@selectedPayment"
        Value="cash" 
        Label="Cash on Delivery" 
        Name="payment"
        Disabled="true">
    </SfRadioButton>

    <p>Selected: @selectedPayment</p>
</div>

@code {
    private string selectedPayment = "credit-card";
}
```

### Radio Buttons with Data

```razor
<div class="radio-data-demo">
    <h3>Select Plan:</h3>

    @foreach (var plan in plans)
    {
        <div class="plan-option">
            <SfRadioButton TChecked="string" 
                @bind-Checked="@selectedPlanId"
                Value="@plan.Id" 
                Label="@plan.Name" 
                Name="plan">
            </SfRadioButton>
            <p>@plan.Price/month</p>
        </div>
    }
</div>

@code {
    private string selectedPlanId = "basic";

    private List<Plan> plans = new()
    {
        new Plan { Id = "basic", Name = "Basic Plan", Price = "$9.99" },
        new Plan { Id = "pro", Name = "Professional", Price = "$19.99" },
        new Plan { Id = "enterprise", Name = "Enterprise", Price = "Custom" }
    };

    private class Plan
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Price { get; set; }
    }
}
```

## RadioButton Events

### ValueChange Event

```razor
<SfRadioButton TChecked="string" 
    @bind-Checked="@paymentMethod"
    Value="credit-card" 
    Label="Credit Card"
    Name="payment"
    ValueChange="@((ChangeArgs<string> args) => OnPaymentChanged(args))">
</SfRadioButton>

<SfRadioButton TChecked="string" 
    @bind-Checked="@paymentMethod"
    Value="paypal" 
    Label="PayPal"
    Name="payment"
    ValueChange="@((ChangeArgs<string> args) => OnPaymentChanged(args))">
</SfRadioButton>

<p>Selected method: @paymentMethod</p>

@code {
    private string paymentMethod = "credit-card";

    private void OnPaymentChanged(ChangeArgs<string> args)
    {
        paymentMethod = args.Value;
        Console.WriteLine($"Payment method changed to: {paymentMethod}");
    }
}

```

## SfSwitch Overview

**SfSwitch<T>** is a toggle control for binary on/off states. It provides visual feedback and supports checked/unchecked states.

**When to use SfSwitch:**
- Feature toggles (on/off)
- Notifications (enable/disable)
- Settings (active/inactive)
- Dark mode
- Two-state preferences

### Basic Implementation

```razor
<SfSwitch TChecked="bool" @bind-Checked="@darkMode"></SfSwitch>

@code {
    private bool darkMode = false;
}
```

### With Label

```razor
<div style="display: flex; align-items: center; gap: 10px;">
    <label style="margin: 0;">Email Notifications:</label>
    <SfSwitch TChecked="bool" @bind-Checked="@notifications" OnLabel="ON" OffLabel="OFF" />
</div>

@code {
    private bool notifications = true;
}
```

## Switch Properties

### Essential Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Checked` | `bool` | `false` | Checked state |
| `OnLabel` | `string` | `null` | Text when switch is ON |
| `OffLabel` | `string` | `null` | Text when switch is OFF |
| `Disabled` | `bool` | `false` | Disable switch |
| `CssClass` | `string` | `""` | Custom CSS classes |

### Switch with Labels

```razor
<div style="display: flex; flex-direction: column; gap: 15px;">
    <h3 style="margin: 0;">Feature Toggles</h3>

    <div style="display: flex; align-items: center; gap: 10px;">
        <label style="margin: 0;">Email Notifications:</label>
        <SfSwitch TChecked="bool" @bind-Checked="@emailNotifications" OnLabel="ON" OffLabel="OFF" />
    </div>

    <div style="display: flex; align-items: center; gap: 10px;">
        <label style="margin: 0;">Push Notifications:</label>
        <SfSwitch TChecked="bool" @bind-Checked="@pushNotifications" OnLabel="ON" OffLabel="OFF" />
    </div>

    <div style="display: flex; align-items: center; gap: 10px;">
        <label style="margin: 0;">Two-Factor Authentication:</label>
        <SfSwitch TChecked="bool" @bind-Checked="@twoFactorAuth" OnLabel="ON" OffLabel="OFF" />
    </div>
</div>

@code {
    private bool emailNotifications = true;
    private bool pushNotifications = false;
    private bool twoFactorAuth = true;
}
```

### Disabled Switch

Disabled switches prevent user interaction, typically used when a feature is unavailable based on subscription tier or other conditions:

```razor
<div style="display: flex; flex-direction: column; gap: 15px;">
    <h3 style="margin: 0;">Subscription Features</h3>

    <div style="display: flex; align-items: center; gap: 10px;">
        <label style="margin: 0;">Cloud Storage (50GB):</label>
        <SfSwitch TChecked="bool" @bind-Checked="@cloudStorage" OnLabel="ON" OffLabel="OFF" />
    </div>

    <div style="display: flex; align-items: center; gap: 10px;">
        <label style="margin: 0;">Advanced Analytics:</label>
        <SfSwitch TChecked="bool" @bind-Checked="@advancedAnalytics" OnLabel="ON" OffLabel="OFF" Disabled="true" />
        <small>(Premium feature)</small>
    </div>

    <div style="display: flex; align-items: center; gap: 10px;">
        <label style="margin: 0;">Priority Support:</label>
        <SfSwitch TChecked="bool" @bind-Checked="@prioritySupport" OnLabel="ON" OffLabel="OFF" Disabled="true" />
        <small>(Enterprise only)</small>
    </div>
</div>

@code {
    private bool cloudStorage = true;
    private bool advancedAnalytics = false;
    private bool prioritySupport = false;
}
```

## Switch Events

### CheckedChanged Event

Use `CheckedChanged` for explicit two-way binding when you need custom logic during updates:

```razor
<div style="display: flex; align-items: center; gap: 10px;">
    <label style="margin: 0;">Dark Mode:</label>
    <SfSwitch TChecked="bool" 
        Checked="@darkMode"
        CheckedChanged="@(EventCallback.Factory.Create<bool>(this, OnDarkModeChanged))"
        OnLabel="ON" 
        OffLabel="OFF" />
</div>

<p>@themeStatus</p>

@code {
    private bool darkMode = false;
    private string themeStatus = "Light mode active";

    private async Task OnDarkModeChanged(bool value)
    {
        darkMode = value;
        themeStatus = value ? "Dark mode enabled" : "Light mode enabled";
    }
}
```

### ValueChange Event

```razor
<div style="display: flex; align-items: center; gap: 10px;">
    <label style="margin: 0;">Dark Mode:</label>
    <SfSwitch TChecked="bool" @bind-Checked="@darkMode" OnLabel="ON" OffLabel="OFF" ValueChange="@((CheckedChangeEventArgs<bool> e) => OnDarkModeToggled(e))" />
</div>

<p>@themeStatus</p>

@code {
    private bool darkMode = false;
    private string themeStatus = "Light mode active";

    private void OnDarkModeToggled(CheckedChangeEventArgs<bool> args)
    {
        darkMode = args.Checked;
        themeStatus = args.Checked ? "Dark mode enabled" : "Light mode enabled";
    }
}
```

### Reactive Updates with Switch

```razor
<div style="display: flex; flex-direction: column; gap: 15px;">
    <h3 style="margin: 0;">Settings Panel</h3>

    <div style="display: flex; align-items: center; gap: 10px;">
        <label style="margin: 0;">Show Advanced Options:</label>
        <SfSwitch TChecked="bool" @bind-Checked="@advancedMode" OnLabel="ON" OffLabel="OFF" ValueChange="@((CheckedChangeEventArgs<bool> e) => OnAdvancedModeChanged(e))" />
    </div>

    @if (advancedMode)
    {
        <div style="display: flex; flex-direction: column; gap: 10px; margin-top: 10px;">
            <h4 style="margin: 0;">Advanced Options:</h4>
            <SfCheckBox TChecked="bool" Label="Enable caching"></SfCheckBox>
            <SfCheckBox TChecked="bool" Label="Enable compression"></SfCheckBox>
            <SfCheckBox TChecked="bool" Label="Enable logging"></SfCheckBox>
        </div>
    }
</div>

@code {
    private bool advancedMode = false;

    private void OnAdvancedModeChanged(CheckedChangeEventArgs<bool> args)
    {
        advancedMode = args.Checked;
        Console.WriteLine($"Advanced mode: {advancedMode}");
    }
}
```

## Practical Examples

### Terms and Conditions Form

```razor
@using System.ComponentModel.DataAnnotations

<div class="terms-form">
    <h2>Create Account</h2>

    <EditForm Model="@signUpModel" OnValidSubmit="@HandleSignUp">
        <DataAnnotationsValidator />

        <div class="form-group">
            <label>Email:</label>
            <SfTextBox @bind-Value="@signUpModel.Email" Placeholder="Enter your email"></SfTextBox>
            <ValidationMessage For="@(() => signUpModel.Email)" />
        </div>

        <div class="form-group">
            <SfCheckBox 
                TChecked="bool"
                @bind-Checked="@signUpModel.AgreeTerms" 
                Label="I agree to the Terms of Service">
            </SfCheckBox>
            @if (signUpModel.AgreeTerms == false)
            {
                <span class="error">You must agree to terms</span>
            }
        </div>

        <div class="form-group">
            <SfCheckBox 
                TChecked="bool"
                @bind-Checked="@signUpModel.NewsletterOptIn" 
                Label="I want to receive marketing emails">
            </SfCheckBox>
        </div>

        <button type="submit" disabled="@(!signUpModel.AgreeTerms)">Create Account</button>
    </EditForm>
</div>

@code {
    private SignUpModel signUpModel = new();

    private void HandleSignUp()
    {
        Console.WriteLine($"Sign up: {signUpModel.Email}, Newsletter: {signUpModel.NewsletterOptIn}");
    }

    private class SignUpModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; } = "";
        public bool AgreeTerms { get; set; }
        public bool NewsletterOptIn { get; set; }
    }
}
```

### Survey with Multiple Question Types

```razor
<div class="survey-form">
    <h2>Customer Feedback Survey</h2>

    <div class="question-1">
        <h3>1. How satisfied are you with our service?</h3>
        <SfRadioButton 
            @bind-Checked="@satisfaction"
            Value="very-satisfied" 
            Label="Very Satisfied" 
            Name="satisfaction">
        </SfRadioButton>
        <SfRadioButton 
            @bind-Checked="@satisfaction"
            Value="satisfied" 
            Label="Satisfied" 
            Name="satisfaction">
        </SfRadioButton>
        <SfRadioButton 
            @bind-Checked="@satisfaction"
            Value="neutral" 
            Label="Neutral" 
            Name="satisfaction">
        </SfRadioButton>
        <SfRadioButton 
            @bind-Checked="@satisfaction"
            Value="dissatisfied" 
            Label="Dissatisfied" 
            Name="satisfaction">
        </SfRadioButton>
    </div>

    <div class="question-2">
        <h3>2. Which features did you use? (Select all that apply)</h3>
        <SfCheckBox TChecked="bool" @bind-Checked="@features[0]" Label="Feature A" Name="features"></SfCheckBox>
        <SfCheckBox TChecked="bool" @bind-Checked="@features[1]" Label="Feature B" Name="features"></SfCheckBox>
        <SfCheckBox TChecked="bool" @bind-Checked="@features[2]" Label="Feature C" Name="features"></SfCheckBox>
    </div>

    <div class="question-3">
        <h3>3. Would you recommend us to others?</h3>
        <div style="display: flex; align-items: center; gap: 10px;">
            <label style="margin: 0;">Would recommend:</label>
            <SfSwitch TChecked="bool" @bind-Checked="@wouldRecommend" OnLabel="Yes" OffLabel="No" />
        </div>
    </div>

    <button @onclick="SubmitSurvey">Submit Survey</button>
</div>

@code {
    private string satisfaction = "";
    private bool[] features = new bool[3];
    private bool wouldRecommend = false;

    private void SubmitSurvey()
    {
        var usedFeatures = string.Join(", ", 
            new[] { "A", "B", "C" }
            .Where((f, i) => features[i])
            .ToArray());
        Console.WriteLine($"Survey: Satisfaction={satisfaction}, Features={usedFeatures}, Recommend={wouldRecommend}");
    }
}
```

### Settings Dashboard

```razor
<div style="display: flex; flex-direction: column; gap: 20px;">
    <h2 style="margin: 0;">Account Settings</h2>

    <div style="display: flex; flex-direction: column; gap: 10px;">
        <h3 style="margin: 0;">Notifications</h3>

        <div style="display: flex; align-items: center; gap: 10px;">
            <label style="margin: 0;">Email Notifications:</label>
            <SfSwitch TChecked="bool" @bind-Checked="@emailNotifications" OnLabel="ON" OffLabel="OFF" ValueChange="@((CheckedChangeEventArgs<bool> e) => OnSettingChanged(e))" />
        </div>

        <div style="display: flex; align-items: center; gap: 10px;">
            <label style="margin: 0;">Push Notifications:</label>
            <SfSwitch TChecked="bool" @bind-Checked="@pushNotifications" OnLabel="ON" OffLabel="OFF" ValueChange="@((CheckedChangeEventArgs<bool> e) => OnSettingChanged(e))" />
        </div>

        <div style="display: flex; align-items: center; gap: 10px;">
            <label style="margin: 0;">SMS Alerts:</label>
            <SfSwitch TChecked="bool" @bind-Checked="@smsAlerts" OnLabel="ON" OffLabel="OFF" ValueChange="@((CheckedChangeEventArgs<bool> e) => OnSettingChanged(e))" />
        </div>
    </div>

    <div style="display: flex; flex-direction: column; gap: 10px;">
        <h3 style="margin: 0;">Privacy</h3>

        <div>
            <SfCheckBox 
                @bind-Checked="@profilePublic" 
                Label="Make profile public">
            </SfCheckBox>
        </div>

        <div>
            <SfCheckBox 
                @bind-Checked="@allowMessages" 
                Label="Allow direct messages">
            </SfCheckBox>
        </div>
    </div>

    <div style="display: flex; flex-direction: column; gap: 10px;">
        <h3 style="margin: 0;">Theme</h3>

        <div>
            <SfRadioButton 
                @bind-Checked="@theme"
                Value="light" 
                Label="Light Theme" 
                Name="theme"
                ValueChange="@((ChangeArgs<string> args) => OnThemeChanged(args))">
            </SfRadioButton>
        </div>

        <div>
            <SfRadioButton 
                @bind-Checked="@theme"
                Value="dark" 
                Label="Dark Theme" 
                Name="theme"
                ValueChange="@((ChangeArgs<string> args) => OnThemeChanged(args))">
            </SfRadioButton>
        </div>
    </div>

    <button @onclick="SaveSettings">Save Settings</button>
    <p>@settingsMessage</p>
</div>

@code {
    private bool emailNotifications = true;
    private bool pushNotifications = false;
    private bool smsAlerts = true;
    private bool profilePublic = true;
    private bool allowMessages = true;
    private string theme = "light";
    private string settingsMessage = "";

    private void OnSettingChanged(CheckedChangeEventArgs<bool> args)
    {
        settingsMessage = "Settings updated (unsaved)";
    }

    private void OnThemeChanged(ChangeArgs<string> args)
    {
        theme = args.Value;
        settingsMessage = $"Theme changed to {theme}";
    }

    private void SaveSettings()
    {
        settingsMessage = "All settings saved successfully!";
    }
}
```
