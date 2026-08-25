# TimePicker Component
The TimePicker component provides time-only selection with support for various time formats and step intervals.

## Table of Contents
- [Basic Usage](#basic-usage)
- [Properties](#properties)
- [Two-Way Binding](#two-way-binding)
- [Time Format](#time-format)
- [Value Change Events](#value-change-events)
- [Time Range Restrictions](#time-range-restrictions)
- [Step Interval](#step-interval)
- [Popup Behavior](#popup-behavior)
- [States](#states)
- [AllowEdit](#allowedit)
- [EnableMask](#enablemask)
- [StrictMode](#strictmode)
- [Events](#events)
- [Methods](#methods)
- [Keyboard Navigation](#keyboard-navigation)
- [Form Integration](#form-integration)
- [Accessibility](#accessibility)
- [Performance Tips](#performance-tips)

## Basic Usage

```razor
<SfTimePicker TValue="DateTime?" Placeholder="Select time"></SfTimePicker>
```

## Properties

### SfTimePicker-Specific Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Format` | `string` | `string.Empty` | Time format string (e.g., "HH:mm", "hh:mm tt") |
| `Min` | `DateTime` | 1900-01-01 00:00:00 | Minimum selectable time |
| `Max` | `DateTime` | 2099-12-31 23:59:59 | Maximum selectable time |
| `Placeholder` | `string` | `null` | Placeholder text |
| `Step` | `int` | `30` | Time step interval in minutes |
| `StrictMode` | `bool` | `false` | Enable strict mode validation |
| `Readonly` | `bool` | `false` | Read-only mode |
| `OpenOnFocus` | `bool` | `false` | Open time picker on focus |
| `ShowClearButton` | `bool` | `false` | Show clear button |
| `AllowEdit` | `bool` | `true` | Allow direct typing in input |
| `FullScreen` | `bool` | `false` | Full screen popup on mobile |
| `EnableMask` | `bool` | `false` | Enable input masking |
| `ZIndex` | `int` | `1000` | Popup z-index |
| `Width` | `string` | `null` | Component width |
| `ScrollTo` | `DateTime?` | `null` | Initial scroll position in popup |
| `InputFormats` | `string[]` | `null` | Array of acceptable input formats |
| `KeyConfigs` | `Dictionary<string, object>` | `null` | Custom keyboard shortcuts |
| `FloatLabelType` | `FloatLabelType` | `Never` | Floating label behavior |
| `TabIndex` | `int` | `0` | Tab navigation order |
| `HtmlAttributes` | `Dictionary<string, object>?` | `null` | HTML attributes for root element |
| `InputAttributes` | `Dictionary<string, object>?` | `null` | HTML attributes for input element |
| `Disabled` | `bool` | `false` | Disable the component |

### Properties Inherited from SfInputBase

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ID` | `string` | `null` | Unique identifier for the component |
| `ValidateOnInput` | `bool` | `true` | Validate input on keystroke events |
| `Disabled` | `bool` | `false` | Disable the component interactions |
| `CssClass` | `string` | `null` | Custom CSS class for styling |
| `EnablePersistence` | `bool` | `false` | Persist component state in browser localStorage |

> **Important:** When using `EnablePersistence`, you must also set an `ID` property on the component. The persistence mechanism uses the component's `ID` as the storage key in localStorage. Without a unique `ID`, the persistence behavior may not work correctly across multiple component instances.

## Two-Way Binding

```razor
<SfTimePicker TValue="DateTime?" @bind-Value="selectedTime"></SfTimePicker>

Selected time: @selectedTime?.ToString("HH:mm")

@code {
    private DateTime? selectedTime;
}
```

## Time Format

```razor
<!-- 24-hour format (HH:mm) -->
<SfTimePicker TValue="DateTime?" Format="HH:mm"></SfTimePicker>

<!-- 24-hour format with seconds (HH:mm:ss) -->
<SfTimePicker TValue="DateTime?" Format="HH:mm:ss"></SfTimePicker>

<!-- 12-hour format (hh:mm tt) -->
<SfTimePicker TValue="DateTime?" Format="hh:mm tt"></SfTimePicker>

<!-- 12-hour format with seconds (hh:mm:ss tt) -->
<SfTimePicker TValue="DateTime?" Format="hh:mm:ss tt"></SfTimePicker>
```

## Value Change Events

```razor
<SfTimePicker TValue="DateTime?"
    ValueChange="@OnTimeChanged">
</SfTimePicker>

@code {
    private void OnTimeChanged(ChangeEventArgs<DateTime?> args)
    {
        if (args.Value.HasValue)
        {
            Console.WriteLine($"Selected time: {args.Value:HH:mm}");
        }
    }
}
```

## Time Range Restrictions

Set minimum and maximum selectable times:

```razor
<SfTimePicker TValue="DateTime?"
    Min="@minTime"
    Max="@maxTime">
</SfTimePicker>

@code {
    private DateTime minTime = new DateTime(1900, 1, 1, 9, 0, 0);    // 9:00 AM
    private DateTime maxTime = new DateTime(1900, 1, 1, 17, 0, 0);   // 5:00 PM
}
```

## Step Interval

Control the time increment when scrolling or using arrow keys:

```razor
<!-- 15-minute steps -->
<SfTimePicker TValue="DateTime?" Step="15"></SfTimePicker>

<!-- 30-minute steps -->
<SfTimePicker TValue="DateTime?" Step="30"></SfTimePicker>

<!-- 1-hour steps -->
<SfTimePicker TValue="DateTime?" Step="60"></SfTimePicker>

<!-- 1-minute steps -->
<SfTimePicker TValue="DateTime?" Step="1"></SfTimePicker>
```

## Popup Behavior

```razor
<SfTimePicker TValue="DateTime?"
    OpenOnFocus="true"
    ShowClearButton="true"
    Placeholder="Choose a time">
</SfTimePicker>
```

## States

### Disabled
```razor
<SfTimePicker TValue="DateTime?" Disabled="true"></SfTimePicker>
```

### Readonly
```razor
<SfTimePicker TValue="DateTime?" Readonly="true"></SfTimePicker>
```

## AllowEdit

Control whether users can type directly into the input field:

```razor
<!-- Allow direct typing (default) -->
<SfTimePicker TValue="DateTime?" AllowEdit="true"></SfTimePicker>

<!-- Disable direct typing - popup only -->
<SfTimePicker TValue="DateTime?" AllowEdit="false"></SfTimePicker>
```

## EnableMask

Enable input masking for formatted time entry:

```razor
<SfTimePicker TValue="DateTime?" EnableMask="true" Format="HH:mm" Placeholder="Enter time"></SfTimePicker>
```

## StrictMode

Enable strict mode to validate and reset invalid entries:

```razor
<SfTimePicker TValue="DateTime?" Width="300px" Min="@StrictModeMinTime" Max="@StrictModeMaxTime" StrictMode="true" Value="@StrictModeTimeValue" Placeholder="Select a time" ShowClearButton="true"></SfTimePicker>


@code{
    public DateTime StrictModeMinTime { get; set; } = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 15, 08, 00, 00);  // 8:00 AM
    public DateTime StrictModeMaxTime { get; set; } = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 15, 17, 00, 00);  // 5:00 PM
    public DateTime? StrictModeTimeValue { get; set; } = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 15, 14, 00, 00); // 2:00 PM (within range)
}
```

## Events

### SfTimePicker-Specific Events

| Event | Args | Description |
|-------|------|-------------|
| `ValueChange` | `ChangeEventArgs<TValue>` | Time value changed (via selection or programmatic) |
| `OnInput` | `ChangeEventArgs` | User input event (on each keystroke) |
| `Selected` | `SelectedEventArgs<TValue>` | Time selected from popup |
| `Cleared` | `ClearedEventArgs` | Clear button clicked |
| `OnFocus` | `FocusEventArgs` | Component received focus |
| `OnBlur` | `BlurEventArgs` | Component lost focus |
| `OnItemRender` | `ItemEventArgs<TValue>` | Popup item rendering |
| `OnOpen` | `PopupEventArgs` | Popup opening (set `Cancel` to prevent) |
| `OnClose` | `PopupEventArgs` | Popup closing (set `Cancel` to prevent) |
| `Created` | `EventCallback<object>` | Component initialized |
| `Destroyed` | `EventCallback<object>` | Component destroyed |

```razor
<div class="control-wrapper">
     <SfTimePicker TValue="DateTime?" Width="300px" Placeholder="Select a time" ShowClearButton="true" OnBlur="BlurHandler" ValueChange="ValueChangeHandler" OnFocus="FocusHandler"></SfTimePicker>
 </div>
 <div class="mt-3">
     <span class="example-label"><b>Triggered Event:</b> @_triggeredEvent</span>
 </div>

@code {
    private string _triggeredEvent = string.Empty;
	public void BlurHandler(Syncfusion.Blazor.Toolkit.Calendars.BlurEventArgs args)
	{
	    _triggeredEvent = "Blur event is triggered";
	}

	public void ValueChangeHandler(ChangeEventArgs<DateTime?> args)
	{
	
	    _triggeredEvent = "ValueChange event is triggered";
	}

	public void FocusHandler(Syncfusion.Blazor.Toolkit.Calendars.FocusEventArgs args)
	{
	    _triggeredEvent = "Focus event is triggered";
	}
}
<style>
    .control-wrapper {
        max-width: 275px;
        margin: 0 auto;
        padding: 50px 0px 0px;
    }
    .example-label {
        font-size: 14px;
        margin-bottom: 6px;
    }
</style>
```

## Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `FocusAsync()` | `Task` | Sets focus to the TimePicker component |
| `FocusOutAsync()` | `Task` | Removes focus from the TimePicker |
| `ShowPopupAsync(EventArgs? args)` | `Task` | Opens the time selection popup |
| `HidePopupAsync(EventArgs? args)` | `Task` | Closes the time selection popup |

```razor
<SfButton @onclick="OpenPopup">Open Time Picker</SfButton>
<SfTimePicker @ref="timePickerRef" TValue="DateTime?"></SfTimePicker>

@code {
    private SfTimePicker<DateTime?> timePickerRef;
    
    private async Task OpenPopup()
    {
        await timePickerRef.ShowPopupAsync();
    }
}
```

## Keyboard Navigation

| **Windows Key** | **Mac Key** | **Description** |
| --- | --- | --- |
| ↑ | ↑ | Navigates and selects the previous item |
| ↓ | ↓ | Navigates and selects the next item |
| ← | ← | Moves the cursor towards arrow key pressed direction |
| → | → | Moves the cursor towards arrow key pressed direction |
| Home | Home | Navigates and selects the first item |
| End | End | Navigates and selects the last item |
| Enter | Enter | Selects the currently focused item and closes the popup |
| Alt + ↑ | ⌥ + ↑ | Closes the popup |
| Alt + ↓ | ⌥ + ↓ | Opens the popup |
| Esc | Esc | Closes the popup |

## Form Integration

```razor
@using Syncfusion.Blazor.Toolkit.Calendars
@using System.ComponentModel.DataAnnotations

<EditForm Model="@model" OnValidSubmit="@HandleSubmit">
    <div class="form-group">
        <label for="workStart">Start Time</label>
        <SfTimePicker TValue="DateTime?"
            @bind-Value="model.StartTime"
            Format="HH:mm"
            Min="@minTime"
            Max="@maxTime"
            ID="workStart">
        </SfTimePicker>
        <ValidationMessage For="@(() => model.StartTime)" />
    </div>
    
    <div class="form-group">
        <label for="workEnd">End Time</label>
        <SfTimePicker TValue="DateTime?"
            @bind-Value="model.EndTime"
            Format="HH:mm"
            Min="@minTime"
            Max="@maxTime"
            ID="workEnd">
        </SfTimePicker>
        <ValidationMessage For="@(() => model.EndTime)" />
    </div>
    
    <button type="submit" class="btn btn-primary">Save</button>
</EditForm>

@code {
    private TimeModel model = new();
    private DateTime minTime = new DateTime(1900, 1, 1, 0, 0, 0);
    private DateTime maxTime = new DateTime(1900, 1, 1, 23, 59, 59);
    
    private void HandleSubmit()
    {
        Console.WriteLine($"Start: {model.StartTime}, End: {model.EndTime}");
    }
    
    class TimeModel
    {
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
    }
}
```

## Common Patterns

### Business Hours
```razor
@{
    var businessStart = new DateTime(1900, 1, 1, 9, 0, 0);   // 9 AM
    var businessEnd = new DateTime(1900, 1, 1, 17, 0, 0);    // 5 PM
}

<SfTimePicker TValue="DateTime?"
    Min="@businessStart"
    Max="@businessEnd"
    Format="hh:mm tt">
</SfTimePicker>
```

### 15-Minute Slots
```razor
<SfTimePicker TValue="DateTime?"
    Step="15"
    Format="HH:mm">
</SfTimePicker>
```

### Current Time Default
```razor
<SfTimePicker TValue="DateTime?" Value="@DateTime.Now"></SfTimePicker>
```

### Time Range Validation
```razor
<SfTimePicker TValue="DateTime?"
    ValueChange="@ValidateTimeRange">
</SfTimePicker>

@code {
    private void ValidateTimeRange(ChangeEventArgs<DateTime?> args)
    {
        if (args.Value.HasValue)
        {
            var hour = args.Value.Value.Hour;
            if (hour < 9 || hour >= 17)
            {
                Console.WriteLine("Time must be between 9 AM and 5 PM");
            }
        }
    }
}
```

## Accessibility

- ARIA labels and descriptions
- Keyboard navigation support
- Screen reader friendly
- High contrast mode support

```razor
<label for="timepicker">Select appointment time</label>
<SfTimePicker TValue="DateTime?" ID="timepicker"></SfTimePicker>
```

## Performance Tips

- Use appropriate `Step` value to avoid excessive list items
- Use `Min` and `Max` to limit selectable range
- Consider `Readonly` for display-only scenarios
- Batch time value updates when possible

## See Also

- [Calendar](calendar.md) - Calendar component
- [DatePicker](datepicker.md) - Date selection
- [DateTimePicker](datetimepicker.md) - Combined date and time
- [Features](features.md) - Core features overview
