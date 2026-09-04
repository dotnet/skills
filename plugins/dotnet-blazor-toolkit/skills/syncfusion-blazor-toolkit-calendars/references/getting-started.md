# Getting Started with Calendar Components

## Table of Contents
- [Overview](#overview)
- [Installation & Setup](#installation--setup)
- [Component Types](#component-types)
- [Basic Examples](#basic-examples)
- [Common Configuration](#common-configuration)

## Overview

The Syncfusion Blazor Toolkit provides four calendar-based components for different date/time selection scenarios:

| Component | Purpose | Use Case |
|-----------|---------|----------|
| **Calendar** | Display a calendar view with date selection | Month/year view with multiple selection modes |
| **DatePicker** | Single date selection with popup | Quick date selection in forms |
| **DateTimePicker** | Select both date and time | Appointment scheduling, timestamps |
| **TimePicker** | Select time only | Opening hours, time slots |

## Installation & Setup

All calendar components are included in the main Syncfusion Blazor Toolkit package.

### 1. Verify Package Reference
Ensure `Syncfusion.Blazor.Toolkit` is referenced in your project file:

```xml
<ItemGroup>
    <PackageReference Include="Syncfusion.Blazor.Toolkit" Version="*" />
</ItemGroup>
```

### 2. Global Imports
Add the following to your `_Imports.razor` or place the same @using directive at the top of a specific Razor page if you only need it locally.:

```razor
@using Syncfusion.Blazor.Toolkit.Calendars
```

### 3. Register Services (if required)
In `Program.cs`, register calendar services:

```csharp
builder.Services.AddSyncfusionBlazorToolkit();
```

### 4. Include Styles
Add the calendar CSS to your `index.html`:

```html
<link id="syncfusion-theme" href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```

## Component Types

### Calendar
A calendar control that displays dates in a grid format with selection modes.

```razor
<SfCalendar TValue="DateTime" Value="@selectedDate"></SfCalendar>

@code {
    private DateTime selectedDate = DateTime.Now;
}
```

**Key Features:**
- Multiple selection modes (Single, Multiple)
- Keyboard navigation
- Customizable first day of week

### DatePicker
A date input with an optional popup calendar.

```razor
<SfDatePicker TValue="DateTime?" Placeholder="Choose a date"></SfDatePicker>
```

**Key Features:**
- Keyboard input and calendar popup
- Format customization
- Validation
- Disabled/readonly states

### DateTimePicker
Combined date and time selection.

```razor
<SfDateTimePicker TValue="DateTime?"></SfDateTimePicker>
```

**Key Features:**
- Date + time selection in one control
- Separate time picker panel
- Format customization (date and time)
- Range validation

### TimePicker
Select only the time portion.

```razor
<SfTimePicker TValue="DateTime?"></SfTimePicker>
```

**Key Features:**
- Hour, minute, second selection
- Format customization (HH:mm, HH:mm:ss, etc.)
- Keyboard input
- Step intervals

## Basic Examples

### Example 1: Simple Date Selection
```razor
<SfDatePicker TValue="DateTime?" Placeholder="Select date"></SfDatePicker>
```

### Example 2: Two-Way Binding
```razor
<SfDatePicker TValue="DateTime?" @bind-Value="myDate"></SfDatePicker>

Selected date: @myDate?.ToString("dd MMMM yyyy")

@code {
    private DateTime? myDate;
}
```

### Example 3: Calendar with Month/Year Navigation

This calendar starts in the decade view (showing a grid of years) and only allows navigation down to the year view, making it useful when the user should select a year instead of a specific month or day.

```razor
<SfCalendar TValue="DateTime?" Start="CalendarView.Decade" Depth="CalendarView.Year"></SfCalendar>
```

## Common Configuration

### Disable Specific Dates
Use the `DayCellRendering` event to disable dates:

```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfCalendar TValue="DateTime"
    DayCellRendering="@DisableSundays">
</SfCalendar>

@code {
    private void DisableSundays(RenderDayCellEventArgs args)
    {
        if (args.Date.DayOfWeek == DayOfWeek.Sunday)
        {
            args.IsDisabled = true;
        }
    }
}
```

### Set Date Format
```razor
<SfDatePicker TValue="DateTime?" Format="dd/MM/yyyy"></SfDatePicker>
```

### Readonly State
```razor
<SfDatePicker TValue="DateTime?" Readonly="true"></SfDatePicker>
```

## Next Steps

- Read **[features.md](features.md)** for detailed feature descriptions
- Read component-specific guides for advanced usage
- Check **[troubleshooting.md](troubleshooting.md)** for common issues
