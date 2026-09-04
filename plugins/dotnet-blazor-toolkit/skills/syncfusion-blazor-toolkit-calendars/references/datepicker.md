# DatePicker Component

The DatePicker component provides single date selection with an optional popup calendar and keyboard input support.

## Table of Contents
- [Basic Usage](#basic-usage)
- [Properties](#properties)
- [Two-Way Binding](#two-way-binding)
- [Date Format](#date-format)
- [Value Change Events](#value-change-events)
- [Date Range Restrictions](#date-range-restrictions)
- [Disabled Dates](#disabled-dates)
- [Popup Behavior](#popup-behavior)
- [States](#states)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Events](#events)
- [Methods](#methods)
- [CSS Classes](#css-classes)
- [Form Integration](#form-integration)
- [Accessibility](#accessibility)

## Basic Usage

```razor
<SfDatePicker TValue="DateTime?" Placeholder="Select a date"></SfDatePicker>
```

## Properties

### SfDatePicker-Specific Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Format` | `string` | `string.Empty` | Date format string (e.g., "MM/dd/yyyy") |
| `Placeholder` | `string` | `null` | Placeholder text when empty |
| `Readonly` | `bool` | `false` | Read-only mode (prevents popup opening) |
| `AllowEdit` | `bool` | `true` | Allow editing value in input field |
| `OpenOnFocus` | `bool` | `false` | Open calendar on input focus |
| `ShowClearButton` | `bool` | `false` | Show clear button |
| `StrictMode` | `bool` | `false` | Strict mode - resets invalid values |
| `FloatLabelType` | `FloatLabelType` | `Never` | Floating label behavior (Auto, Always, Never) |
| `FullScreen` | `bool` | `false` | Full screen popup on mobile/tablet |
| `EnableMask` | `bool` | `false` | Enable input mask rendering |
| `Width` | `string` | `null` | Component width (e.g., "280px") |
| `ZIndex` | `int` | `1000` | Popup Z-index |
| `TabIndex` | `int` | `0` | Tab index for keyboard navigation |
| `InputFormats` | `string[]` | `null` | Array of acceptable input format strings |
| `HtmlAttributes` | `Dictionary<string, object>?` | `null` | Additional HTML attributes for root element |
| `InputAttributes` | `Dictionary<string, object>?` | `null` | Additional attributes for input element |
| `Disabled` | `bool` | `false` | Disable the component |

### Properties Inherited from CalendarBase

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Min` | `DateTime` | 01-Jan-1900 | Minimum selectable date |
| `Max` | `DateTime` | 31-Dec-2099 | Maximum selectable date |
| `FirstDayOfWeek` | `int` | Culture-based | First day of calendar week (0=Sunday) |
| `Depth` | `CalendarView` | `Month` | Maximum navigation depth (Month, Year, Decade) |
| `Start` | `CalendarView` | `Month` | Initial view when popup opens |
| `WeekNumber` | `bool` | `false` | Show week numbers |
| `DayHeaderFormat` | `DayHeaderFormats` | `Short` | Day header format |
| `ShowTodayButton` | `bool` | `true` | Show Today button |
| `CalendarMode` | `CalendarType` | `Gregorian` | Calendar system (Gregorian or Hijri) |
| `KeyConfigs` | `Dictionary<string, object>` | `null` | Custom keyboard shortcuts |
| `WeekRule` | `CalendarWeekRule` | Culture-based | Week rule for first week of year |

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
<SfDatePicker TValue="DateTime?" @bind-Value="selectedDate"></SfDatePicker>

Selected date: @selectedDate?.ToString("MMM dd, yyyy")

@code {
    private DateTime? selectedDate;
}
```

## Date Format

```razor
<!-- MM/dd/yyyy format -->
<SfDatePicker TValue="DateTime?" Format="MM/dd/yyyy"></SfDatePicker>

<!-- dd/MM/yyyy format -->
<SfDatePicker TValue="DateTime?" Format="dd/MM/yyyy"></SfDatePicker>

<!-- Long date format -->
<SfDatePicker TValue="DateTime?" Format="dddd, MMMM dd, yyyy"></SfDatePicker>

<!-- ISO format -->
<SfDatePicker TValue="DateTime?" Format="yyyy-MM-dd"></SfDatePicker>
```

## Value Change Events

```razor
<SfDatePicker TValue="DateTime?"
    ValueChange="@OnDateChanged">
</SfDatePicker>

@code {
    private void OnDateChanged(ChangedEventArgs<DateTime?> args)
    {
        if (args.Value.HasValue)
        {
            Console.WriteLine($"Selected: {args.Value:d}");
        }
    }
}
```

## Date Range Restrictions

```razor
<SfDatePicker TValue="DateTime?"
    Min="@minDate"
    Max="@maxDate"
    Placeholder="Select a date">
</SfDatePicker>

@code {
    private DateTime minDate = new DateTime(2026, 1, 1);
    private DateTime maxDate = new DateTime(2026, 12, 31);
}
```

## Disabled Dates

The calendar supports disabling specific dates via the `DayCellRendering` event:

```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfDatePicker TValue="DateTime?"
    DayCellRendering="@OnDayCellRendering">
</SfDatePicker>

@code {
    private void OnDayCellRendering(RenderDayCellEventArgs args)
    {
        // Disable weekends (Saturday=6, Sunday=0)
        if (args.Date.DayOfWeek == DayOfWeek.Saturday || 
            args.Date.DayOfWeek == DayOfWeek.Sunday)
        {
            args.IsDisabled = true;
        }
    }
}
```

## Popup Behavior

### Auto-open on Focus
```razor
<SfDatePicker TValue="DateTime?" OpenOnFocus="true"></SfDatePicker>
```

### Show Additional Buttons
```razor
<SfDatePicker TValue="DateTime?"
    ShowClearButton="true"
    ShowTodayButton="true">
</SfDatePicker>
```

## States

### Readonly State
```razor
<SfDatePicker TValue="DateTime?" Readonly="true"></SfDatePicker>
```

## Keyboard Shortcuts

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
| **Esc** | **Esc** | Closes the popup |

## Events

| Event | Args | Description |
|-------|------|-------------|
| `ValueChange` | `ChangedEventArgs<TValue>` | Date value changed |
| `Created` | `EventCallback<object>` | Component initialized |
| `Destroyed` | `EventCallback<object>` | Component destroyed |
| `OnOpen` | `PopupObjectArgs` | Popup is about to open (can cancel) |
| `OnClose` | `PopupObjectArgs` | Popup is about to close (can cancel) |
| `OnBlur` | `BlurEventArgs` | Component loses focus |
| `OnFocus` | `FocusEventArgs` | Component receives focus |
| `OnInput` | `ChangeEventArgs` | User inputs or modifies value |
| `Selected` | `SelectedEventArgs<TValue>` | Value selected from calendar |
| `Cleared` | `ClearedEventArgs` | Value cleared using clear button |
| `Navigated` | `NavigatedEventArgs` | Calendar view navigated |
| `DayCellRendering` | `RenderDayCellEventArgs` | Day cell is being rendered |

```razor
<SfDatePicker TValue="DateTime?"
    ValueChange="@OnValueChanged"
    OnOpen="@OnOpen"
    OnClose="@OnClose"
    OnFocus="@OnFocusHandler"
    OnBlur="@OnBlurHandler"
    Selected="@OnSelected"
    Navigated="@OnNavigated"
    DayCellRendering="@OnDayCellRendering">
</SfDatePicker>

@code {
    private void OnValueChanged(ChangedEventArgs<DateTime?> args)
    {
        Console.WriteLine($"Date changed: {args.Value}");
    }

    private void OnOpen(PopupObjectArgs args)
    {
        Console.WriteLine("Calendar popup opening");
        // args.Cancel = true; // To prevent opening
    }

    private void OnClose(PopupObjectArgs args)
    {
        Console.WriteLine("Calendar popup closing");
        // args.Cancel = true; // To prevent closing
    }

    private void OnFocusHandler(FocusEventArgs args)
    {
        Console.WriteLine("Focused"); // use Syncfusion.Blazor.Toolkit.Calendars.FocusEventArgs if you face ambiguous reference error
    }

    private void OnBlurHandler(BlurEventArgs args)
    {
        Console.WriteLine("Blurred");
    }

    private void OnSelected(SelectedEventArgs<DateTime?> args)
    {
        Console.WriteLine($"Selected: {args.Value}");
    }

    private void OnNavigated(NavigatedEventArgs args)
    {
        Console.WriteLine($"Navigated to view: {args.View}");
    }

    private void OnDayCellRendering(RenderDayCellEventArgs args)
    {
        // Custom cell styling
    }
}
```

## Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `FocusAsync()` | `Task` | Sets focus to the DatePicker component |
| `FocusOutAsync()` | `Task` | Removes focus from the component |
| `ShowPopupAsync(EventArgs? args)` | `Task` | Opens the calendar popup |
| `HidePopupAsync(EventArgs? args)` | `Task` | Closes the calendar popup |
| `NavigateAsync(CalendarView view, TValue date)` | `Task` | Navigates to specified view and date |
| `CurrentView()` | `string` | Gets the current calendar view |
| `GetPersistDataAsync()` | `Task<string>` | Gets persisted state as JSON string |

```razor
<SfDatePicker TValue="DateTime?" @ref="datePickerRef"></SfDatePicker>
<button @onclick="OpenPopup">Open Popup</button>
<button @onclick="ClosePopup">Close Popup</button>
<button @onclick="NavigateToYear">Navigate to Year</button>
<button @onclick="GetCurrentView">CurrentView</button>

@code {
    private SfDatePicker<DateTime?> datePickerRef;

    private async Task OpenPopup()
    {
        await datePickerRef.ShowPopupAsync();
    }

    private async Task ClosePopup()
    {
        await datePickerRef.HidePopupAsync();
    }

    private async Task NavigateToYear()
    {
        await datePickerRef.NavigateAsync(CalendarView.Year, new DateTime(2003, 8, 14));
    }

    private async Task GetCurrentView()
    {
        string view = datePickerRef.CurrentView();
        Console.WriteLine($"Current view: {view}");
    }
}
```

### CSS Classes

| Class | Usage |
|-------|-------|
| `.e-datepicker` | Main container |
| `.e-input-group` | Input wrapper |
| `.e-input` | Input field |
| `.e-toolkit-icons` | Calendar icons |
| `.e-popup` | Calendar popup container |

## Form Integration

### With EditForm

```razor
@using Syncfusion.Blazor.Toolkit.Calendars
@using System.ComponentModel.DataAnnotations

<EditForm Model="@model" OnValidSubmit="@HandleSubmit">
    <div class="form-group">
        <label for="birthDate">Birth Date</label>
        <SfDatePicker TValue="DateTime?" @bind-Value="model.BirthDate" ID="birthDate"></SfDatePicker>
        <ValidationMessage For="@(() => model.BirthDate)" />
    </div>
    <button type="submit" class="btn btn-primary">Submit</button>
</EditForm>

@code {
    private Person model = new();
    
    private void HandleSubmit()
    {
        Console.WriteLine($"Birth date: {model.BirthDate}");
    }
    
    class Person
    {
        public DateTime? BirthDate { get; set; }
    }
}
```

## Accessibility

- ARIA labels and descriptions
- Keyboard navigation support
- Screen reader friendly
- High contrast mode support

```razor
<label for="datepicker">Select your date of birth</label>
<SfDatePicker TValue="DateTime?" ID="datepicker"></SfDatePicker>
```

## Common Patterns

### Current Date Default
```razor
<SfDatePicker TValue="DateTime?" Value="@DateTime.Today"></SfDatePicker>
```

### Age Restriction (18+)
```razor
<SfDatePicker TValue="DateTime?"
    Max="@DateTime.Today.AddYears(-18)">
</SfDatePicker>
```

## See Also

- [Calendar](calendar.md) - Calendar component with multiple selection
- [DateTimePicker](datetimepicker.md) - Date and time selection
- [Features](features.md) - Core features overview
