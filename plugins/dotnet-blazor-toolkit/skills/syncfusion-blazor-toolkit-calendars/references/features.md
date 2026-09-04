# Core Features

## Table of Contents
- [Date Selection](#date-selection)
- [Time Selection](#time-selection)
- [Date Formatting](#date-formatting)
- [Date Validation](#date-validation)
- [Appearance & States](#appearance--states)
- [Week Configuration](#week-configuration)
- [Events](#events)

## Date Selection

### Selection Modes
Calendar supports multiple selection modes:

```razor
<!-- Single Selection (default) -->
<SfCalendar TValue="DateTime" 
    Value="@selectedDate">
</SfCalendar>

<!-- Multiple Selection -->
<SfCalendar TValue="DateTime" 
    IsMultiSelection="true"
    Values="@selectedDates">
</SfCalendar>

@code {
    private DateTime selectedDate = DateTime.Now;
    private DateTime[] selectedDates = new DateTime[] { };
}
```

**Note:** The Calendar component uses `IsMultiSelection` property for multi-date selection. It does not have a `SelectionType` property.

## Time Selection

### Time Picker Usage
```razor
<SfTimePicker TValue="DateTime?"></SfTimePicker>
```

### DateTime Selection
```razor
<SfDateTimePicker TValue="DateTime?" Placeholder="Date and time"></SfDateTimePicker>
```

### Time Format
```razor
<SfTimePicker TValue="DateTime?" Format="HH:mm:ss"></SfTimePicker>
```

## Date Formatting

### Format Strings
Calendar components support standard date format strings:

```razor
<!-- Short date -->
<SfDatePicker TValue="DateTime?" Format="MM/dd/yyyy"></SfDatePicker>

<!-- Long date -->
<SfDatePicker TValue="DateTime?" Format="dddd, MMMM dd, yyyy"></SfDatePicker>

<!-- ISO format -->
<SfDatePicker TValue="DateTime?" Format="yyyy-MM-dd"></SfDatePicker>

<!-- Custom format -->
<SfDatePicker TValue="DateTime?" Format="dd.MM.yyyy"></SfDatePicker>
```

## Date Validation

### Min and Max Dates
```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfDatePicker TValue="DateTime?"
    Min="@minDate"
    Max="@maxDate">
</SfDatePicker>

@code {
    private DateTime minDate = new DateTime(2026, 6, 1);
    private DateTime maxDate = new DateTime(2026, 6, 30);
}
```

### Disabled Dates
Disable specific days using the `DayCellRendering` event:

```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfCalendar TValue="DateTime"
    DayCellRendering="@DisableWeekends">
</SfCalendar>

@code {
    private void DisableWeekends(RenderDayCellEventArgs args)
    {
        if (args.Date.DayOfWeek == DayOfWeek.Saturday || args.Date.DayOfWeek == DayOfWeek.Sunday)
        {
            args.IsDisabled = true;
        }
    }
}
```

> **Note:** The Calendar component does not have a `DisabledDates` property. Use the `DayCellRendering` event to dynamically disable specific dates based on your criteria.

### Custom Validation
```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfDatePicker TValue="DateTime?"
    ValueChange="@ValidateDate">
</SfDatePicker>

<p style="color:red">@validationError</p>

@code {
    private string validationError = "";

    private void ValidateDate(ChangedEventArgs<DateTime?> args)
    {
        if (args.Value.HasValue && args.Value.Value.DayOfWeek == DayOfWeek.Sunday)
        {
            validationError = "Sundays are not allowed.";
        }
        else
        {
            validationError = "";
        }
    }
}
```

## Appearance & States

### Readonly State
```razor
<SfDatePicker TValue="DateTime?" Readonly="true"></SfDatePicker>
```

### Placeholder Text
```razor
<SfDatePicker TValue="DateTime?" Placeholder="Select a date"></SfDatePicker>
```

### Popup Properties
```razor
<SfDatePicker TValue="DateTime?"
    OpenOnFocus="true"
    ShowTodayButton="true"
    ShowClearButton="true">
</SfDatePicker>
```

## Week Configuration

```razor
<!-- Change first day of week -->
<!-- Set the calendar's first day of the week to Monday -->
<SfCalendar TValue="DateTime"
    FirstDayOfWeek="1"> 
</SfCalendar>

<!-- Show week numbers -->
<SfCalendar TValue="DateTime" WeekNumber="true"></SfCalendar>
```

## Events

Key events for calendar components:

| Event | Fired When |
|-------|-----------|
| `ValueChange` | Date value changes (via selection or programmatic) |
| `ValuesChanged` | Multiple dates selected/deselected (multi-selection mode) |
| `Navigated` | Calendar view navigation occurs |
| `Selected` | Date selected from calendar |
| `DayCellRendering` | Day cell is being rendered (for customization) |
| `Created` | Component initialized |
| `Destroyed` | Component destroyed |

```razor
<SfDatePicker TValue="DateTime?"
    ValueChange="@OnDateChanged"
    Created="@OnCreated">
</SfDatePicker>

@code {
    private void OnDateChanged(ChangedEventArgs<DateTime?> args)
    {
        Console.WriteLine($"Date changed to: {args.Value}");
    }
    
    private void OnCreated()
    {
        Console.WriteLine("DatePicker created");
    }
}
```
