# API Reference

## Table of Contents
- [InputBase Component (Base Class)](#inputbase-component-base-class)
- [CalendarBase Component (Base Class)](#calendarbase-component-base-class)
- [Range Restriction](#range-restriction)
- [Calendar Component](#calendar-component)
- [DatePicker Component](#datepicker-component)
- [DateTimePicker Component](#datetimepicker-component)
- [TimePicker Component](#timepicker-component)
- [Common Event Args](#common-event-args)

## Range Restriction

All calendar components support date range restriction through `Min` and `Max` properties. These properties define the boundaries of selectable dates.

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Min` | `DateTime` | `01-Jan-1900` | Minimum selectable date |
| `Max` | `DateTime` | `31-Dec-2099` | Maximum selectable date |

### Basic Usage

```csharp
// Restrict to year 2026
<SfDatePicker TValue="DateTime?"
    Min="@(new DateTime(2026, 1, 1))"
    Max="@(new DateTime(2026, 12, 31))">
</SfDatePicker>
```

### Component-Specific Notes

- **SfCalendar**: `Min`/`Max` restrict all date navigation
- **SfDatePicker**: `Min`/`Max` restrict both input and popup selection
- **SfDateTimePicker**: `Min`/`Max` restrict date portion; use `MinTime`/`MaxTime` for time restrictions
- **SfTimePicker**: `Min`/`Max` restrict time selection range

### TimePicker Time Restrictions

```csharp
// Restrict business hours (9 AM to 5 PM)
<SfTimePicker TValue="DateTime?"
    Min="@(new DateTime(2026, 1, 1, 9, 0, 0))"
    Max="@(new DateTime(2026, 1, 1, 17, 0, 0))">
</SfTimePicker>
```

---

## InputBase Component (Base Class)

The `SfInputBase<TValue>` is the base class for all text-based input components. It provides shared functionality such as validation, floating labels, clear button support, and accessibility features.

### Properties Inherited from SfInputBase

```csharp
// States
public bool Disabled { get; set; }                        // Disable the component
public bool EnablePersistence { get; set; }                // Persist state in localStorage

// Styling
public string CssClass { get; set; }                      // Custom CSS class
```

### Properties Inherited from InputBase to all other input components

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ID` | `string` | `null` | Unique identifier for the component |
| `ValidateOnInput` | `bool` | `true` | Validate input on keystroke events |
| `Disabled` | `bool` | `false` | Disable the component interactions |
| `CssClass` | `string` | `null` | Custom CSS class for styling |
| `EnablePersistence` | `bool` | `false` | Persist component state in browser localStorage |

> **Important:** When using `EnablePersistence`, you must also set an `ID` property on the component. The persistence mechanism uses the component's `ID` as the storage key in localStorage. Without a unique `ID`, the persistence behavior may not work correctly across multiple component instances.

## CalendarBase Component (Base Class)

The `CalendarBase<T>` is the base class for all calendar components. It contains shared properties and logic.

### Properties (Inherited by SfCalendar, SfDatePicker, etc.)

```csharp
// Range Restrictions
public DateTime Min { get; set; }           // Default: 01-Jan-1900
public DateTime Max { get; set; }           // Default: 31-Dec-2099

// Display
public int FirstDayOfWeek { get; set; }     // 0=Sunday, 1=Monday, etc.
public bool WeekNumber { get; set; }        // Show week numbers
public CalendarView Depth { get; set; }     // Max navigation depth (Month/Year/Decade)
public CalendarView Start { get; set; }     // Initial view when calendar opens. Default is Month
public DayHeaderFormats DayHeaderFormat { get; set; }  // Day name format(Short, Narrow, Abbreviated, Wide)
public CalendarWeekRule WeekRule { get; set; }         // First week of year rule

// Buttons & UI
public bool ShowTodayButton { get; set; }   // Show Today button (default: true)

// Calendar System
public CalendarType CalendarMode { get; set; }  // Gregorian or Hijri

// Keyboard
public Dictionary<string, object> KeyConfigs { get; set; }  // Custom keyboard shortcuts
```

---

### Properties Inherited from CalendarBase to all other calendar components

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Min` | `DateTime` | 01-Jan-1900 | Minimum selectable date |
| `Max` | `DateTime` | 31-Dec-2099 | Maximum selectable date |
| `FirstDayOfWeek` | `int` | Culture-based | First day of week (0=Sunday, 1=Monday, ..., 5=Friday, 6=Saturday) |
| `WeekNumber` | `bool` | false | Show week numbers |
| `Depth` | `CalendarView` | Month | Maximum navigation depth |
| `Start` | `CalendarView` | Month | Initial view when opened |
| `DayHeaderFormat` | `DayHeaderFormats` | Short | Day name format |
| `WeekRule` | `CalendarWeekRule` | Culture-based | First week rule |
| `ShowTodayButton` | `bool` | true | Show Today button |
| `CalendarMode` | `CalendarType` | Gregorian | Calendar system type |
| `KeyConfigs` | `Dictionary<string, object>` | null | Custom keyboard shortcuts |

---

## Calendar Component

The `SfCalendar<TValue>` component provides a standalone calendar view with date selection capabilities.

### Class Definition

```csharp
public partial class SfCalendar<TValue> : CalendarBase<TValue>
```

### SfCalendar-Specific Properties

```csharp
// Multi-Selection
public DateTime[] Values { get; set; }                    // Array of selected dates
public bool IsMultiSelection { get; set; }                // Enable multi-date selection (default: false)
public Expression<Func<DateTime[]>> ValuesExpression { get; set; }  // For form validation

// Layout
public int TabIndex { get; set; }                         // Tab order for keyboard navigation
public Dictionary<string, object> HtmlAttributes { get; set; }      // Custom HTML attributes
```

### Events

```csharp
// Value Events
[Parameter]
public EventCallback<ChangedEventArgs<TValue>> ValueChange { get; set; }

[Parameter]
public EventCallback<DateTime[]> ValuesChanged { get; set; }

[Parameter]
public EventCallback<SelectedEventArgs<TValue>> Selected { get; set; }

[Parameter]
public EventCallback<DeSelectedEventArgs<TValue>> DeSelected { get; set; }

// Navigation Events
[Parameter]
public EventCallback<NavigatedEventArgs> Navigated { get; set; }

// Rendering Events
[Parameter]
public EventCallback<RenderDayCellEventArgs> DayCellRendering { get; set; }

// Lifecycle Events
[Parameter]
public EventCallback<object> Created { get; set; }

[Parameter]
public EventCallback<object> Destroyed { get; set; }
```

### CalendarView Enum

```csharp
public enum CalendarView
{
    Month,   // Day view - shows individual days
    Year,    // Month view - shows all months
    Decade   // Year view - shows year range
}
```

### DayHeaderFormats Enum

```csharp
public enum DayHeaderFormats
{
    Short,      // Su, Mo, Tu, We, Th, Fr, Sa
    Narrow,     // S, M, T, W, T, F, S
    Abbreviated,// Sun, Mon, Tue, Wed, Thu, Fri, Sat
    Wide        // Sunday, Monday, Tuesday, etc.
}
```

### CalendarType Enum

```csharp
public enum CalendarType
{
    Gregorian,   // Standard Gregorian calendar
    Hijri       // Islamic Hijri calendar
}
```

### CalendarWeekRule Enum

```csharp
public enum CalendarWeekRule
{
    FirstDay,      // Week begins on Sunday (or culture's first day)
    FirstFullWeek, // Week containing the first Thursday of the year
    FirstFourDayWeek // Week containing the first 4-day week of the year
}
```

---

## DatePicker Component

### Properties

```csharp
public class SfDatePicker<TValue>
{
    // Format
    public string Format { get; set; }
    public string[] InputFormats { get; set; }

    // Input
    public string Placeholder { get; set; }
    public bool AllowEdit { get; set; }
    public bool StrictMode { get; set; }
    public FloatLabelType FloatLabelType { get; set; }
    public bool ValidateOnInput { get; set; }

    // Popup
    public bool OpenOnFocus { get; set; }
    public bool ShowClearButton { get; set; }
    public bool FullScreen { get; set; }
    public bool EnableMask { get; set; }
    public int ZIndex { get; set; }

    // States
    public bool Readonly { get; set; }

    // Layout
    public string Width { get; set; }
    public int TabIndex { get; set; }

    // Attributes
    public Dictionary<string, object> HtmlAttributes { get; set; }
    public Dictionary<string, object> InputAttributes { get; set; }
}
```

### Events

```csharp
[Parameter]
public EventCallback<ChangedEventArgs<TValue>> ValueChange { get; set; }

[Parameter]
public EventCallback<PopupObjectArgs> OnOpen { get; set; }

[Parameter]
public EventCallback<PopupObjectArgs> OnClose { get; set; }

[Parameter]
public EventCallback<FocusEventArgs> OnFocus { get; set; }

[Parameter]
public EventCallback<BlurEventArgs> OnBlur { get; set; }

[Parameter]
public EventCallback<ChangeEventArgs> OnInput { get; set; }

[Parameter]
public EventCallback<SelectedEventArgs<TValue>> Selected { get; set; }

[Parameter]
public EventCallback<ClearedEventArgs> Cleared { get; set; }

[Parameter]
public EventCallback<NavigatedEventArgs> Navigated { get; set; }

[Parameter]
public EventCallback<RenderDayCellEventArgs> DayCellRendering { get; set; }

[Parameter]
public EventCallback<object> Created { get; set; }

[Parameter]
public EventCallback<object> Destroyed { get; set; }
```

### Methods

```csharp
public async Task FocusAsync()
public async Task FocusOutAsync()
public async Task ShowPopupAsync(EventArgs? args = null)
public async Task HidePopupAsync(EventArgs? args = null)
public async Task NavigateAsync(CalendarView view, TValue date)
public string CurrentView()
public async Task<string> GetPersistDataAsync()
```

---

## DateTimePicker Component

All the properties, methods and Events are inherited to DateTimePicker component from the DatePicker component.

### Properties

```csharp
public class SfDateTimePicker<TValue>
{
    
    // Format
    public string TimeFormat { get; set; }
    
    // Range Restrictions
    public override DateTime Min { get; set; }
    public override DateTime Max { get; set; }
    public DateTime MaxTime { get; set; }
    public DateTime MinTime { get; set; }

    //scroll
    public Nullable<DateTime> ScrollTo { get; set; }

    // Time
    public int Step { get; set; }

}
```

### Events

```csharp

[Parameter]
public EventCallback<ItemEventArgs<TValue>> OnItemRender { get; set; }

[Parameter]
public EventCallback<KeyboardEventArgs> OnKeyDown { get; set; }
```

### Methods

```csharp
public Task ShowDatePopupAsync()
public Task ShowTimePopupAsync()
```

---

## TimePicker Component

### Properties

```csharp
public class SfTimePicker<TValue>
{
    // Format and Input
    public string Format { get; set; }
    public string[] InputFormats { get; set; }
    public bool AllowEdit { get; set; }
    public bool EnableMask { get; set; }
    public bool ValidateOnInput { get; set; }

    // Range Restrictions
    public DateTime Min { get; set; }
    public DateTime Max { get; set; }
    public bool StrictMode { get; set; }

    // Input Configuration
    public string Placeholder { get; set; }
    public FloatLabelType FloatLabelType { get; set; }
    public Dictionary<string, object> KeyConfigs { get; set; }

    // Time
    public int Step { get; set; }
    public Nullable<DateTime> ScrollTo { get; set; }

    // Popup
    public bool OpenOnFocus { get; set; }
    public bool FullScreen { get; set; }
    public int ZIndex { get; set; }

    // States
    public bool Readonly { get; set; }

    // Clear Button
    public bool ShowClearButton { get; set; }

    // Layout
    public string Width { get; set; }
    public int TabIndex { get; set; }

    // HTML Attributes
    public Dictionary<string, object> HtmlAttributes { get; set; }
    public Dictionary<string, object> InputAttributes { get; set; }
}
```

### Events

```csharp
// Value and Input Events
[Parameter]
public EventCallback<ChangeEventArgs<TValue>> ValueChange { get; set; }

[Parameter]
public EventCallback<ChangeEventArgs> OnInput { get; set; }

[Parameter]
public EventCallback<SelectedEventArgs<TValue>> Selected { get; set; }

[Parameter]
public EventCallback<ClearedEventArgs> Cleared { get; set; }

// Focus Events
[Parameter]
public EventCallback<FocusEventArgs> OnFocus { get; set; }

[Parameter]
public EventCallback<BlurEventArgs> OnBlur { get; set; }

// Popup Events
[Parameter]
public EventCallback<PopupEventArgs> OnOpen { get; set; }

[Parameter]
public EventCallback<PopupEventArgs> OnClose { get; set; }

// Item Render Event
[Parameter]
public EventCallback<ItemEventArgs<TValue>> OnItemRender { get; set; }

// Lifecycle Events
[Parameter]
public EventCallback<object> Created { get; set; }

[Parameter]
public EventCallback<object> Destroyed { get; set; }
```

### Methods

```csharp
public async Task FocusAsync()
public async Task FocusOutAsync()
public async Task ShowPopupAsync(EventArgs? args = null)
public async Task HidePopupAsync(EventArgs? args = null)
```

---

## Common Event Args

### ChangeEventArgs<TValue>

```csharp
public class ChangeEventArgs<T>
{
    public object Event { get; set; }
    public bool IsInteracted { get; set; }
    public string Text { get; set; }
    public T Value { get; set; }
}
```

### SelectedEventArgs<TValue>

```csharp
public class SelectedEventArgs<T>
{
    public T Value { get; set; }
}
```

### ClearedEventArgs

```csharp
public class ClearedEventArgs
{
    public object Event { get; set; }
}
```

### PopupEventArgs

```csharp
public class PopupEventArgs
{
    public bool Cancel { get; set; }
    public object Event { get; set; }
    public string Name { get; set; }
}
```

### ItemEventArgs<TValue>

```csharp
public class ItemEventArgs<T>
{
    public bool IsDisabled { get; set; }
    public string Name { get; set; }
    public string Text { get; set; }
    public T Value { get; set; }
}
```

---

## Supported Date Formats

| Format | Example |
|--------|---------|
| `d` | 13 |
| `dd` | 13 |
| `ddd` | Wed |
| `dddd` | Wednesday |
| `M` | 5 |
| `MM` | 05 |
| `MMM` | May |
| `MMMM` | May |
| `yy` | 26 |
| `yyyy` | 2026 |

## Supported Time Formats

| Format | Example |
|--------|---------|
| `h` | 2 |
| `hh` | 02 |
| `H` | 14 |
| `HH` | 14 |
| `m` | 5 |
| `mm` | 05 |
| `s` | 8 |
| `ss` | 08 |
| `t` | P |
| `tt` | PM |

---

## Type Generics

All picker components use generic types:

```csharp
// Single date
<SfDatePicker TValue="DateTime?"></SfDatePicker>

// Calendar with single selection
<SfCalendar TValue="DateTime"></SfCalendar>

// Calendar with multiple selection
<SfCalendar TValue="DateTime[]"></SfCalendar>

```

---

## See Also

- [Features](features.md) - Core feature documentation
- [Calendar](calendar.md) - Calendar component guide
- [DatePicker](datepicker.md) - DatePicker component guide
- [Events & Binding](events-binding.md) - Event handling examples
