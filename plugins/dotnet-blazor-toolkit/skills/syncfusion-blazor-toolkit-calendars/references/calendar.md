# Calendar Component

The Calendar component displays a calendar view with date selection capabilities and multiple selection modes.

## Table of Contents
- [Basic Usage](#basic-usage)
- [Properties](#properties)
- [Selection Modes](#selection-modes)
- [View Modes](#view-modes)
- [Date Range Restrictions](#date-range-restrictions)
- [Week Configuration](#week-configuration)
- [Events](#events)
- [Styling](#styling)
- [Keyboard Navigation](#keyboard-navigation)
- [Programmatic Navigation](#programmatic-navigation)
- [Accessibility](#accessibility)
- [Performance Considerations](#performance-considerations)

## Basic Usage

```razor
<SfCalendar TValue="DateTime" Value="@selectedDate"></SfCalendar>

@code {
    private DateTime selectedDate = DateTime.Now;
}
```

## Properties

### SfCalendar-Specific Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Value` | `TValue` | `null` | Selected date value (for single selection mode) |
| `Values` | `DateTime[]` | `null` | Array of selected dates (for multi-selection mode) |
| `IsMultiSelection` | `bool` | `false` | Enable multiple date selection |
| `ValuesExpression` | `Expression<Func<DateTime[]>>` | `null` | Expression for form validation binding |
| `TabIndex` | `int` | `0` | Tab index for keyboard navigation |
| `HtmlAttributes` | `Dictionary<string, object>` | `null` | Custom HTML attributes to apply to the root element |

### Properties Inherited from CalendarBase

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Min` | `DateTime` | `01-Jan-1900` | Minimum selectable date |
| `Max` | `DateTime` | `31-Dec-2099` | Maximum selectable date |
| `FirstDayOfWeek` | `int` | Culture-based | First day of week (0=Sunday, 1=Monday, etc.) |
| `WeekNumber` | `bool` | `false` | Show week numbers |
| `Depth` | `CalendarView` | `Month` | Maximum navigation depth (Month/Year/Decade) |
| `Start` | `CalendarView` | `Month` | Initial view when calendar opens |
| `DayHeaderFormat` | `DayHeaderFormats` | `Short` | Day header format (Short/Narrow/Abbreviated/Wide) |
| `WeekRule` | `CalendarWeekRule` | Culture-based | Week rule for first week of year |
| `ShowTodayButton` | `bool` | `true` | Show the Today button |
| `CalendarMode` | `CalendarType` | `Gregorian` | Calendar system (Gregorian or Hijri) |
| `KeyConfigs` | `Dictionary<string, object>` | `null` | Custom keyboard shortcuts |

### Properties Inherited from SfInputBase

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ID` | `string` | `null` | Unique identifier for the component |
| `ValidateOnInput` | `bool` | `true` | Validate input on keystroke events |
| `Disabled` | `bool` | `false` | Disable the component interactions |
| `CssClass` | `string` | `null` | Custom CSS class for styling |
| `EnablePersistence` | `bool` | `false` | Persist component state in browser localStorage |

> **Important:** When using `EnablePersistence`, you must also set an `ID` property on the component. The persistence mechanism uses the component's `ID` as the storage key in localStorage. Without a unique `ID`, the persistence behavior may not work correctly across multiple component instances.

> **Note:** `SfCalendar` inherits from `CalendarBase<TValue>`, which inherits from `SfInputBase<TValue>`. Properties like `Min`, `Max`, `FirstDayOfWeek`, `Depth`, `Start`, `WeekNumber`, `DayHeaderFormat`, `WeekRule`, `ShowTodayButton`, `CalendarMode`, and `KeyConfigs` are defined in `CalendarBase<T>`, while `ID`, `ValidateOnInput`, `Disabled`, `CssClass`, and `EnablePersistence` come from `SfInputBase<T>.

## Selection Modes

### Single Selection (Default)
```razor
<SfCalendar TValue="DateTime" 
    Value="@selectedDate"
    ValueChange="@OnDateChanged">
</SfCalendar>

@code {
    private DateTime selectedDate = DateTime.Now;
    
    private void OnDateChanged(ChangedEventArgs<DateTime> args)
    {
        selectedDate = args.Value;
        Console.WriteLine($"Selected: {selectedDate:d}");
    }
}
```

### Multiple Selection
```razor
<SfCalendar TValue="DateTime" 
    IsMultiSelection="true"
    Values="@selectedDates"
    ValuesChanged="@OnMultipleSelect">
</SfCalendar>

@code {
    private DateTime[] selectedDates = new DateTime[] { };
    
    private void OnMultipleSelect(DateTime[] newValues)
    {
        selectedDates = newValues;
    }
}
```

### Programmatic Multi-Selection Methods
For multi-selection mode, use the `AddDatesAsync` and `RemoveDatesAsync` methods:

```razor
<SfCalendar TValue="DateTime" 
    IsMultiSelection="true"
    @bind-Values="selectedDates"
    @ref="calendarRef">
</SfCalendar>
<button @onclick="AddDates">Add Dates</button>
<button @onclick="RemoveDates">Remove Dates</button>

@code {
    private SfCalendar<DateTime> calendarRef;
    private DateTime[] selectedDates = new DateTime[] { };
    
    private async Task AddDates()
    {
        await calendarRef.AddDatesAsync(new DateTime[] { DateTime.Now, DateTime.Now.AddDays(7) });
    }
    
    private async Task RemoveDates()
    {
        await calendarRef.RemoveDatesAsync(new DateTime[] { DateTime.Now });
    }
}
```

## View Modes

Control the calendar view display and navigation:

> **Note:** The `Depth` property restricts how deep users can navigate in the calendar hierarchy. The `Start` property sets the initial view when the calendar opens. To restrict navigation:
> - Set `Depth` equal to `Start` to allow navigation ONLY within that level (no deeper navigation)
> - For example, if `Start=Month` and `Depth=Month`, users start in Month view and cannot navigate to Year/Decade
> - If `Start=Year` and `Depth=Year`, users start in Year view and cannot navigate to Month
>
> **Key insight:** `Depth` determines the deepest view users can reach, while `Start` determines where they begin.

```razor
<!-- Month view - opens in Month, can only stay in Month (can't go deeper) -->
<SfCalendar TValue="DateTime" Start="CalendarView.Month" Depth="CalendarView.Month"></SfCalendar>

<!-- Year view - opens in Year, can only stay in Year (can't go deeper to Month) -->
<SfCalendar TValue="DateTime" Start="CalendarView.Year" Depth="CalendarView.Year"></SfCalendar>

<!-- Decade view - opens in Decade, can only stay in Decade -->
<SfCalendar TValue="DateTime" Start="CalendarView.Decade" Depth="CalendarView.Decade"></SfCalendar>
```

## Date Range Restrictions

```razor
<SfCalendar TValue="DateTime"
    Min="@minDate"
    Max="@maxDate"
    Value="@selectedDate">
</SfCalendar>

@code {
    private DateTime minDate = new DateTime(2026, 1, 1);
    private DateTime maxDate = new DateTime(2026, 12, 31);
    private DateTime selectedDate = DateTime.Now;
}
```

## Week Configuration

```razor
<!-- Set Monday as first day of week -->
<SfCalendar TValue="DateTime"
    FirstDayOfWeek="1">
</SfCalendar>

<!-- Show week numbers -->
<SfCalendar TValue="DateTime"
    WeekNumber="true">
</SfCalendar>

<!-- Specify week rule -->
@using System.Globalization
@using Syncfusion.Blazor.Toolkit.Calendars

<SfCalendar TValue="DateTime"
    WeekNumber="true"
    WeekRule="CalendarWeekRule.FirstFourDayWeek">
</SfCalendar>
```

## Events

### Calendar-Specific Events

| Event | Args | Description |
|-------|------|-------------|
| `ValueChange` | `ChangedEventArgs<TValue>` | Fired when the single date value changes |
| `ValuesChanged` | `EventCallback<DateTime[]>` | Fired when when the Values property changes |
| `Navigated` | `NavigatedEventArgs` | Fired when calendar view navigation occurs |
| `Selected` | `SelectedEventArgs<TValue>` | Fired after a date is selected |
| `DeSelected` | `DeSelectedEventArgs<TValue>` | Fired when a date is deselected (multi-selection only) |
| `DayCellRendering` | `RenderDayCellEventArgs` | Fired when each day cell is rendered |
| `Created` | `EventCallback<object>` | Fired when component is initialized |
| `Destroyed` | `EventCallback<object>` | Fired when component is destroyed |

> **Note:** Unlike DatePicker, DateTimePicker, and TimePicker, the Calendar component does **not** inherit `OnFocus`, `OnBlur`, or `OnInput` events from `SfInputBase`. The Calendar is a standalone calendar view without an input field.

## Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GetPersistDataAsync()` | `Task<string>` | Retrieves persisted state data for browser storage |
| `CurrentView()` | `string` | Returns the current view name ("Month", "Year", or "Decade") |
| `NavigateAsync(CalendarView view, TValue date)` | `Task` | Navigates programmatically to a specified view and focuses a date |
| `AddDatesAsync(DateTime[] dates)` | `Task` | Adds dates to selection in multi-selection mode |
| `RemoveDatesAsync(DateTime[] dates)` | `Task` | Removes dates from selection in multi-selection mode |

```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfCalendar TValue="DateTime"
    ValueChange="@OnValueChanged"
    Navigated="@OnNavigated"
    DayCellRendering="@OnDayCellRendering"
    Created="@OnCreated"
    Destroyed="@OnDestroyed">
</SfCalendar>

@code {
    private void OnValueChanged(ChangedEventArgs<DateTime> args)
    {
        Console.WriteLine($"Date selected: {args.Value:D}");
    }
    
    private void OnNavigated(NavigatedEventArgs args)
    {
        Console.WriteLine($"Navigated to: {args.View} - {args.Date}");
    }
    
    private void OnDayCellRendering(RenderDayCellEventArgs args)
    {
        // Disable weekends
        if (args.Date.DayOfWeek == DayOfWeek.Saturday || args.Date.DayOfWeek == DayOfWeek.Sunday)
        {
            args.IsDisabled = true;
        }
    }
    
    private void OnCreated(object args)
    {
        Console.WriteLine("Calendar initialized");
    }
    
    private void OnDestroyed(object args)
    {
        Console.WriteLine("Calendar destroyed");
    }
}
```

## Styling

### CSS Classes

| Class | Usage |
|-------|-------|
| `.e-calendar` | Main container |
| `.e-header` | Header with month/year navigation |
| `.e-content` | Calendar grid content |
| `.e-calendar-content-table` | Calendar grid table |
| `.e-selected` | Selected date cell |
| `.e-disabled` | Disabled date cell |
| `.e-today` | Today's date cell |
| `.e-week-number` | Week number column |

### Custom Styling

```html
<style>
    /* Customizing the background color for the Calendar */
    .e-calendar {
        background-color: peachpuff;
        border: 3px solid red;
    }

    /* Customizing the Calendar date elements on hovering, To specify background color, color, and border */
    .e-calendar .e-content td:hover span.e-day, .e-calendar .e-content td:focus span.e-day, .e-bigger.e-small .e-calendar .e-content td:hover span.e-day, .e-bigger.e-small .e-calendar .e-content td:focus span.e-day {
        background-color: red;
        border: 2px solid;
        color: #212529;
    }

    /* Customizing the selected date cell grid To specify background color and color  */
    .e-calendar .e-content td.e-selected.e-focused-date span.e-day {
        background-color: maroon;
        color: #fff;
    }

    /* Customizing the previous and next icon, To specify color and border  */
    .e-calendar .e-header span, .e-bigger.e-small .e-calendar .e-header span {
        border: 1px solid;
        color: chocolate;
    }
</style>
```

## Keyboard Navigation

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

Use `KeyConfigs` to customize keyboard shortcuts different keyboard layouts:

below is a example custom key actions for German keyboards:

```razor
<SfCalendar TValue="DateTime" KeyConfigs="@keyConfigs"></SfCalendar>

@code {
    private Dictionary<string, object> keyConfigs = new()
    {
        { "moveRight", "Alt+RightArrow" }
    };
}
```

## Programmatic Navigation

Use the `NavigateAsync` method to navigate programmatically:

```razor
<SfCalendar TValue="DateTime" @ref="calendarRef" Value="@selectedDate"></SfCalendar>
<button @onclick="GoToYear">Go to Year View</button>

@code {
    private SfCalendar<DateTime> calendarRef;
    private DateTime selectedDate = DateTime.Now;
    
    private async Task GoToYear()
    {
        await calendarRef.NavigateAsync(CalendarView.Year, DateTime.Now);
    }
}
```

## Accessibility

The Calendar component includes:
- ARIA labels and roles
- Keyboard navigation
- Screen reader support
- High contrast mode support

Ensure you provide descriptive labels:

```razor
<label for="calendar">Select a date</label>
<SfCalendar TValue="DateTime" ID="calendar"></SfCalendar>
```

## Performance Considerations

For performance with large date ranges:
- Use date range restrictions (`Min`, `Max`)
- Disable unnecessary dates sparingly
- Consider using `Depth` to limit view complexity
- Avoid rapid value updates; batch changes when possible

## See Also

- [DatePicker](datepicker.md) - Single date selection with popup
- [Features](features.md) - Core features overview
