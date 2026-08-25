# DateTimePicker Component

The DateTimePicker component provides combined date and time selection in a single control.

## Table of Contents
- [Basic Usage](#basic-usage)
- [Properties](#properties)
- [Two-Way Binding](#two-way-binding)
- [DateTime Format](#datetime-format)
- [Separate Time Format](#separate-time-format)
- [Min/Max Time Restrictions](#minmax-time-restrictions)
- [ScrollTo Property](#scrollto-property)
- [Value Change Events](#value-change-events)
- [DateTime Range Restrictions](#datetime-range-restrictions)
- [Time Step Interval](#time-step-interval)
- [Popup Behavior](#popup-behavior)
- [States](#states)
- [Events](#events)
- [Methods](#methods)
- [Keyboard Navigation](#keyboard-navigation)
- [Form Integration](#form-integration)
- [Accessibility](#accessibility)
- [Performance Tips](#performance-tips)

## Basic Usage

```razor
<SfDateTimePicker TValue="DateTime?" Placeholder="Select date and time"></SfDateTimePicker>
```

## Properties

### SfDateTimePicker-Specific Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TimeFormat` | `string` | `null` | Format of the time values in the popup list. |
| `Min` | `DateTime` | 1900-01-01 00:00:00 | Minimum selectable date and time. |
| `Max` | `DateTime` | 2099-12-31 23:59:59 | Maximum selectable date and time. |
| `MinTime` | `DateTime` | 1900-01-01 00:00:00 | Minimum selectable time (applies to all dates except Min date). |
| `MaxTime` | `DateTime` | 2099-12-31 23:59:59 | Maximum selectable time (applies to all dates except Max date). |
| `ScrollTo` | `DateTime?` | `null` | Scroll position in the time popup list. |
| `Step` | `int` | `30` | Time step interval in minutes. |

### Properties Inherited from SfDatePicker

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Value` | `TValue` | `null` | The selected date and time. |
| `AllowEdit` | `bool` | `true` | Allow editing in the input. |
| `Placeholder` | `string` | `null` | Placeholder text. |
| `Format` | `string` | `string.Empty` | DateTime format string. |
| `Readonly` | `bool` | `false` | Read‑only mode. |
| `OpenOnFocus` | `bool` | `false` | Open popup when input is focused. |
| `ShowClearButton` | `bool` | `false` | Show clear button. |
| `EnableMask` | `bool` | `false` | Enable input mask. |
| `FloatLabelType` | `FloatLabelType` | `Never` | Floating label behavior. |
| `InputFormats` | `string[]` | `null` | Input format strings for parsing. |
| `StrictMode` | `bool` | `false` | Strict mode validation. |
| `FullScreen` | `bool` | `false` | Full screen on mobile. |
| `ZIndex` | `int` | `1000` | Popup Z-index. |
| `Width` | `string` | `null` | Component width. |
| `TabIndex` | `int` | `0` | Tab index. |
| `Disabled` | `bool` | `false` | Disable the component. |
| `HtmlAttributes` | `Dictionary<string, object>?` | `null` | HTML attributes for root element. |
| `InputAttributes` | `Dictionary<string, object>?` | `null` | HTML attributes for input element. |

### CalendarBase Properties (Inherited by SfDatePicker)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Min` | `DateTime` | 01-Jan-1900 | Minimum selectable date. |
| `Max` | `DateTime` | 31-Dec-2099 | Maximum selectable date. |
| `FirstDayOfWeek` | `int` | Culture-based | First day of week (0=Sunday). |
| `Depth` | `CalendarView` | `Month` | Maximum navigation depth. |
| `Start` | `CalendarView` | `Month` | Initial view when popup opens. |
| `WeekNumber` | `bool` | `false` | Show week numbers. |
| `DayHeaderFormat` | `DayHeaderFormats` | `Short` | Day header format. |
| `ShowTodayButton` | `bool` | `true` | Show Today button. |
| `CalendarMode` | `CalendarType` | `Gregorian` | Calendar system. |
| `KeyConfigs` | `Dictionary<string, object>` | `null` | Custom keyboard shortcuts. |

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
<SfDateTimePicker TValue="DateTime?" @bind-Value="selectedDateTime"></SfDateTimePicker>

Selected: @selectedDateTime?.ToString("g")

@code {
    private DateTime? selectedDateTime;
}
```

## DateTime Format

```razor
<!-- Full format with seconds -->
<SfDateTimePicker TValue="DateTime?" Format="MM/dd/yyyy HH:mm:ss"></SfDateTimePicker>

<!-- Format without seconds -->
<SfDateTimePicker TValue="DateTime?" Format="MM/dd/yyyy HH:mm"></SfDateTimePicker>

<!-- 12-hour format -->
<SfDateTimePicker TValue="DateTime?" Format="MM/dd/yyyy hh:mm tt"></SfDateTimePicker>

<!-- ISO format -->
<SfDateTimePicker TValue="DateTime?" Format="yyyy-MM-ddTHH:mm:ss"></SfDateTimePicker>
```

## Separate Time Format

The `TimeFormat` property allows you to customize how time values appear in the dropdown list

```razor
<SfDateTimePicker TValue="DateTime?"
    Format="dd/MM/yyyy"
    TimeFormat="HH:mm:ss">
</SfDateTimePicker>
```

## Min/Max Time Restrictions

Control the time selection range independently of dates:

```razor
<SfDateTimePicker TValue="DateTime?"
    MinTime="@minTime"
    MaxTime="@maxTime"
    Step="15">
</SfDateTimePicker>

@code {
    private DateTime minTime = new DateTime(2026, 1, 1, 9, 0, 0);  // 9:00 AM
    private DateTime maxTime = new DateTime(2026, 12, 1, 17, 0, 0); // 5:00 PM
}
```

## ScrollTo Property

Set the initial scroll position in the time popup:

```razor
<SfDateTimePicker TValue="DateTime?" ScrollTo="@scrollToTime"></SfDateTimePicker>

@code {
    private DateTime? scrollToTime = new DateTime(2024, 1, 1, 14, 30, 0); // Scroll to 2:30 PM
}
```

## Value Change Events

```razor
<SfDateTimePicker TValue="DateTime?"
    ValueChange="@OnDateTimeChanged">
</SfDateTimePicker>

@code {
    private void OnDateTimeChanged(ChangedEventArgs<DateTime?> args)
    {
        if (args.Value.HasValue)
        {
            Console.WriteLine($"Selected: {args.Value:g}");
        }
    }
}
```

## DateTime Range Restrictions

```razor
<SfDateTimePicker TValue="DateTime?"
    Min="@minDateTime"
    Max="@maxDateTime">
</SfDateTimePicker>

@code {
    private DateTime minDateTime = DateTime.Now;
    private DateTime maxDateTime = DateTime.Now.AddDays(7);
}
```

## Time Step Interval

Control the increment/decrement step for time selection:

```razor
<!-- 15-minute steps -->
<SfDateTimePicker TValue="DateTime?" Step="15"></SfDateTimePicker>

<!-- 30-minute steps -->
<SfDateTimePicker TValue="DateTime?" Step="30"></SfDateTimePicker>

<!-- 1-hour steps -->
<SfDateTimePicker TValue="DateTime?" Step="60"></SfDateTimePicker>
```

## Popup Behavior

```razor
<SfDateTimePicker TValue="DateTime?"
    OpenOnFocus="true"
    ShowClearButton="true"
    ShowTodayButton="true">
</SfDateTimePicker>
```

## States

### Readonly
```razor
<SfDateTimePicker TValue="DateTime?" Readonly="true"></SfDateTimePicker>
```

## Events

### SfDateTimePicker-Specific Events

| Event | Args | Description |
|-------|------|-------------|
| `OnItemRender` | `ItemEventArgs<TValue>` | Item in time list is being rendered |
| `OnKeyDown` | `KeyboardEventArgs` | Key pressed down |

### Events Inherited from SfDatePicker

| Event | Args | Description |
|-------|------|-------------|
| `ValueChange` | `ChangedEventArgs<TValue>` | DateTime value changed |
| `OnOpen` | `PopupObjectArgs` | Popup opening |
| `OnClose` | `PopupObjectArgs` | Popup closing |
| `OnFocus` | `FocusEventArgs` | Component received focus |
| `OnBlur` | `BlurEventArgs` | Component lost focus |
| `OnInput` | `ChangeEventArgs` | User input in the field |
| `Selected` | `SelectedEventArgs<TValue>` | Date selected from calendar |
| `Cleared` | `ClearedEventArgs` | Value cleared via clear button |
| `Navigated` | `NavigatedEventArgs` | Calendar view navigated |
| `DayCellRendering` | `RenderDayCellEventArgs` | Day cell is being rendered |
| `Created` | `EventCallback<object>` | Component initialized |
| `Destroyed` | `EventCallback<object>` | Component destroyed |

```razor
<SfDateTimePicker TValue="DateTime?" OnItemRender="@OnItemRender" />

@code {
    private void OnItemRender(ItemEventArgs<DateTime?> args)
    {
        // Customize item rendering
    }
}
```

```razor
<SfDateTimePicker TValue="DateTime" OnKeyDown="HandleKeyDown">
</SfDateTimePicker>

@code {
    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "F2")
        {
            // Custom functionality
        }
    }
}
```

## Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `ShowDatePopupAsync()` | `Task` | Opens only the date popup |
| `ShowTimePopupAsync()` | `Task` | Opens only the time popup |
| `ShowPopupAsync(EventArgs? args)` | `Task` | Opens the popup calendar (inherited from DatePicker) |
| `HidePopupAsync(EventArgs? args)` | `Task` | Closes the popup calendar (inherited from DatePicker) |
| `FocusAsync()` | `Task` | Sets focus to the component (inherited from DatePicker) |
| `FocusOutAsync()` | `Task` | Removes focus from the component (inherited from DatePicker) |
| `NavigateAsync(CalendarView view, TValue date)` | `Task` | Navigate to specific view and date (inherited from DatePicker) |
| `CurrentView()` | `string` | Gets the current calendar view (inherited from DatePicker) |
| `GetPersistDataAsync()` | `Task<string>` | Gets persisted state as JSON string (inherited from DatePicker) |

```razor
<SfDateTimePicker TValue="DateTime?" @ref="dateTimePickerRef"></SfDateTimePicker>
<button @onclick="OpenDatePicker">Open Date</button>
<button @onclick="OpenTimePicker">Open Time</button>
<button @onclick="SetFocus">Focus</button>

@code {
    private SfDateTimePicker<DateTime?> dateTimePickerRef;
    
    private async Task OpenDatePicker()
    {
        await dateTimePickerRef.ShowDatePopupAsync();
    }
    
    private async Task OpenTimePicker()
    {
        await dateTimePickerRef.ShowTimePopupAsync();
    }
    
    private async Task SetFocus()
    {
        await dateTimePickerRef.FocusAsync();
    }
}
```

## Keyboard Navigation

### Calendar navigation
| **Windows Key** | **Mac Key** | **Description** |
| --- | --- | --- |
| ↑ | ↑ | Focuses the same day of the previous week |
| ↓ | ↓ | Focuses the same day of the next week |
| ← | ← | Focuses the previous day |
| → | → | Focuses the next day |
| Home | Home | Focuses the first day of the month |
| End | End | Focuses the last day of the month |
| Page Up | Page Up | Focuses the same date of the previous month |
| Page Down | Page Down | Focuses the same date of the next month |
| Enter | Enter | Selects the currently focused date |
| Shift + Page Up | ⇧ + Page Up | Focuses the same date of the previous year |
| Shift + Page Down | ⇧ + Page Down | Focuses the same date of the next year |
| Ctrl + ↑ | ⌘ + ↑ | Moves to the inner level of view (Month → Year, Year → Decade) |
| Ctrl + ↓ | ⌘ + ↓ | Moves out from the depth level view (Decade → Year, Year → Month) |
| Ctrl + Home | ⌘ + Home | Focuses the first date of the current year |
| Ctrl + End | ⌘ + End | Focuses the last date of the current year |

### Input navigation

| **Windows Key** | **Mac Key** | **Description** |
| --- | --- | --- |
| **Alt + ↓** | **⌥ + ↓** | Opens the popup |
| **Alt + ↑** | **⌥ + ↑** | Closes the popup |



## Form Integration

```razor
@using System.ComponentModel.DataAnnotations

<EditForm Model="@model" OnValidSubmit="@HandleSubmit">
    <div class="form-group">
        <label for="appointmentTime">Appointment Time</label>
        <SfDateTimePicker TValue="DateTime?" 
            @bind-Value="model.AppointmentTime"
            Min="@DateTime.Now"
            Step="30"
            ID="appointmentTime">
        </SfDateTimePicker>
        <ValidationMessage For="@(() => model.AppointmentTime)" />
    </div>
    <button type="submit" class="btn btn-primary">Book Appointment</button>
</EditForm>

@code {
    private AppointmentModel model = new();
    
    private void HandleSubmit()
    {
        Console.WriteLine($"Appointment: {model.AppointmentTime}");
    }
    
    class AppointmentModel
    {
        public DateTime? AppointmentTime { get; set; }
    }
}
```

## Common Patterns

### Appointment Scheduling
```razor
@{
    var now = DateTime.Now;
    var tomorrow = now.AddDays(1).Date.AddHours(9); // 9 AM tomorrow
    var maxDate = now.AddDays(30); // 30 days in future
}

<SfDateTimePicker TValue="DateTime?"
    Min="@tomorrow"
    Max="@maxDate"
    Step="30"
    Format="MM/dd/yyyy hh:mm tt">
</SfDateTimePicker>
```

### Business Hours Only
```razor
<SfDateTimePicker TValue="DateTime?"
    MinTime="@businessStart"
    MaxTime="@businessEnd"
    Format="MM/dd/yyyy HH:mm">
</SfDateTimePicker>

@code {
    private DateTime businessStart = new DateTime(2024, 1, 1, 9, 0, 0);
    private DateTime businessEnd = new DateTime(2024, 1, 1, 17, 0, 0);
}
```

### Current DateTime Default
```razor
<SfDateTimePicker TValue="DateTime?" Value="@DateTime.Now"></SfDateTimePicker>
```

### Custom Time List Item Rendering
```razor
<SfDateTimePicker TValue="DateTime?" OnItemRender="@OnTimeItemRender"></SfDateTimePicker>

@code {
    private void OnTimeItemRender(ItemEventArgs<DateTime?> args)
    {
        if (args.Value.HasValue && args.Value.Value.Hour < 9)
        {
            // Style early morning times differently
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
<label for="datetimepicker">Select appointment date and time</label>
<SfDateTimePicker TValue="DateTime?" ID="datetimepicker"></SfDateTimePicker>
```

## Performance Tips

- Use reasonable `Min` and `Max` to limit selectable range
- Use `Step` to control time granularity
- Avoid frequent value updates in loops
- Consider using `Readonly` for display-only scenarios

## See Also

- [Calendar](calendar.md) - Calendar component
- [DatePicker](datepicker.md) - Date-only selection
- [TimePicker](timepicker.md) - Time-only selection
- [Features](features.md) - Core features overview
