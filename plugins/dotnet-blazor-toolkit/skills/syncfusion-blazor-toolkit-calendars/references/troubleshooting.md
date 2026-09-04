# Troubleshooting

## Table of Contents
- [Issue: Date Not Displaying Correctly](#issue-date-not-displaying-correctly)
- [Issue: Component Not Responding to Clicks](#issue-component-not-responding-to-clicks)
- [Issue: Validation Messages Not Appearing](#issue-validation-messages-not-appearing)
- [Issue: Keyboard Navigation Not Working](#issue-keyboard-navigation-not-working)
- [Issue: Disabled Dates Not Working](#issue-disabled-dates-not-working)
- [Issue: Component Not Updating with New Value](#issue-component-not-updating-with-new-value)
- [Issue: Popup Won't Close After Selection](#issue-popup-wont-close-after-selection)
- [Issue: Date Format String Not Working](#issue-date-format-string-not-working)
- [Issue: Timezone Issues with DateTime](#issue-timezone-issues-with-datetime)

## Common Issues and Solutions

### Issue: Date Not Displaying Correctly

**Problem:** Selected date doesn't appear in the component.

**Solutions:**

1. **Check date format:**
```razor
<!-- Make sure format matches your data -->
<SfDatePicker TValue="DateTime?" Format="MM/dd/yyyy"></SfDatePicker>
```

2. **Verify nullable types:**
```razor
<!-- Use DateTime? for optional dates -->
<SfDatePicker TValue="DateTime?" Value="@dateValue"></SfDatePicker>

@code {
    private DateTime? dateValue = DateTime.Today;
}
```
---

### Issue: Component Not Responding to Clicks

**Problem:** Calendar popup won't open or dates can't be selected.

**Solutions:**

1. **Verify it's not readonly:**
```razor
<!-- ReadOnly prevents popup -->
<SfDatePicker TValue="DateTime?" Readonly="false"></SfDatePicker>
```

2. **Clear browser cache:**
- Refresh with Ctrl+F5 or Cmd+Shift+R
- Check browser developer console for JavaScript errors

3. **Check event handlers:**
```razor
<SfDatePicker TValue="DateTime?"
    ValueChange="@OnValueChanged">
</SfDatePicker>

@code {
    private void OnValueChanged(ChangedEventArgs<DateTime?> args)
    {
        // Make sure event handler completes
        Console.WriteLine($"Value: {args.Value}");
    }
}
```

---

### Issue: Validation Messages Not Appearing

**Problem:** EditForm validation isn't working with calendar components.

**Solutions:**

1. **Use proper EditForm structure:**
```razor
<EditForm Model="@model" OnValidSubmit="@HandleSubmit">
    <DataAnnotationsValidator />
    
    <div class="form-group">
        <label for="eventDate">Event Date</label>
        <SfDatePicker TValue="DateTime"
            @bind-Value="model.EventDate"
            ID="eventDate">
        </SfDatePicker>
        <ValidationMessage For="@(() => model.EventDate)" />
    </div>
    
    <button type="submit" class="btn btn-primary">Submit</button>
</EditForm>

@code {
    private EventModel model = new();

    private void HandleSubmit()
    {
        Console.WriteLine($"Event date: {model.EventDate}");
    }

    class EventModel
    {
        [Required(ErrorMessage = "Event date is required")]
        [Range(typeof(DateTime), "2026-01-01", "2026-12-31",
        ErrorMessage = "Event must be in 2026")]
        public DateTime EventDate { get; set; }
    }
}
```

2. **Check validation attributes:**
```csharp
class BookingModel
{
    [Required]
    [Range(typeof(DateTime), "2026-01-01", "2026-12-31")]
    public DateTime BookingDate { get; set; }
}
```

3. **Verify imports**

Ensure the following namespaces are included at the top of the Razor page:

```razor
@using Syncfusion.Blazor.Toolkit.Calendars
@using System.ComponentModel.DataAnnotations
```

---

### Issue: Keyboard Navigation Not Working

**Problem:** Arrow keys and other keyboard shortcuts aren't working.

**Solutions:**

1. **Ensure component has focus:**
```razor
<SfDatePicker TValue="DateTime?" @ref="datePickerRef"></SfDatePicker>
<button @onclick="FocusDatePicker">Focus DatePicker</button>

@code {
    private SfDatePicker<DateTime?> datePickerRef;
    
    private async Task FocusDatePicker()
    {
        await datePickerRef.FocusAsync();
    }
}
```

2. **Check browser accessibility features:**
- Ensure keyboard navigation isn't disabled
- Check operating system settings
- Try in a different browser

3. **Verify no conflicting key handlers:**
```razor
<!-- Remove conflicting event handlers -->
<div @onkeydown="YourKeyHandler">
    <SfDatePicker TValue="DateTime?"></SfDatePicker>
</div>
```

---

### Issue: Disabled Dates Not Working

**Problem:** Dates that should be disabled are still selectable.

**Solutions:**

1. **Use DayCellRendering event for custom disabling:**
```razor
<!-- Use DayCellRendering to disable specific dates -->
@using Syncfusion.Blazor.Toolkit.Calendars

<SfCalendar TValue="DateTime"
    DayCellRendering="@DisableWeekends">
</SfCalendar>

@code {
    private void DisableWeekends(RenderDayCellEventArgs args)
    {
        // Disable all Sundays
        if (args.Date.DayOfWeek == DayOfWeek.Sunday)
        {
            args.IsDisabled = true;
        }
        // Disable 6th, 13th, 20th, 27th of every month
        if (args.Date.Day == 6 || args.Date.Day == 13 || args.Date.Day == 20 || args.Date.Day == 27)
        {
            args.IsDisabled = true;
        }
    }
}
```

2. **Use Min/Max for date ranges:**
```razor
<!-- Min/Max restrict entire date ranges -->
<SfDatePicker TValue="DateTime?"
    Min="@minDate"
    Max="@maxDate">
</SfDatePicker>

@code {
    private DateTime minDate = new DateTime(2026, 1, 1);
    private DateTime maxDate = new DateTime(2026, 12, 31);
}
```

---

### Issue: Component Not Updating with New Value

**Problem:** Changing the value in code doesn't update the component UI.

**Solutions:**

1. **Use StateHasChanged():**
```razor
<SfDatePicker TValue="DateTime?" Value="@selectedDate"></SfDatePicker>
<button @onclick="UpdateDate">Update</button>

@code {
    private DateTime selectedDate = DateTime.Today;
    
    private void UpdateDate()
    {
        selectedDate = DateTime.Today.AddDays(7);
        StateHasChanged();
    }
}
```

2. **Use two-way binding:**
```razor
<!-- @bind automatically updates -->
<SfDatePicker TValue="DateTime?" @bind-Value="selectedDate"></SfDatePicker>

@code {
    private DateTime? selectedDate;
}
```

---

### Issue: Popup Won't Close After Selection

**Problem:** Calendar popup remains open after clicking a date.

**Solutions:**

1. **Check if OpenOnFocus is enabled:**
```razor
<!-- OpenOnFocus may reopen popup -->
<SfDatePicker TValue="DateTime?" OpenOnFocus="false"></SfDatePicker>
```

2. **Verify ValueChange event completes:**
```razor
<SfDatePicker TValue="DateTime?"
    ValueChange="@OnDateChanged">
</SfDatePicker>

@code {
    private async Task OnDateChanged(ChangedEventArgs<DateTime?> args)
    {
        // Ensure event handler completes quickly
        await Task.CompletedTask;
    }
}
```

3. **Clear browser developer tools issues:**
- Close DevTools and refresh
- Check for JavaScript errors
- Try in incognito/private mode

---

### Issue: Date Format String Not Working

**Problem:** Format string doesn't produce expected output.

**Solutions:**

1. **Verify format is correct:**
```razor
<!-- Common formats -->
<SfDatePicker TValue="DateTime?" Format="MM/dd/yyyy"></SfDatePicker>  <!-- 05/13/2026 -->
<SfDatePicker TValue="DateTime?" Format="dd/MM/yyyy"></SfDatePicker>  <!-- 13/05/2026 -->
<SfDatePicker TValue="DateTime?" Format="yyyy-MM-dd"></SfDatePicker>   <!-- 2026-05-13 -->
```

2. **Check localization:**
```razor
<!-- Locale determines the default format when Format is not specified -->
<SfDatePicker TValue="DateTime?" 
    Locale="en-US">
</SfDatePicker>

<!-- Explicit Format="MM/dd/yyyy" always produces "05/13/2026" regardless of locale -->
<SfDatePicker TValue="DateTime?" 
    Format="MM/dd/yyyy">
</SfDatePicker>
```

**Note:** The `Format` string is absolute — it always produces the same output regardless of locale. Locale only affects the default format when `Format` is not explicitly set.

3. **Match format to your data:**
```csharp
// If parsing from string, ensure format matches
var date = DateTime.ParseExact(dateString, "MM/dd/yyyy", CultureInfo.InvariantCulture);
```

---

### Issue: Timezone Issues with DateTime

**Problem:** Selected dates show different values in different locations.

**Understanding the issue:**
Calendar components store dates as `DateTime` with `Kind = Unspecified`. This means:
- `2026-06-15 10:00` has no timezone context
- `ToUniversalTime()` on Unspecified DateTime **assumes local time** and converts
- `ToLocalTime()` on UTC DateTime adds the local timezone offset

**Solutions:**

1. **Store UTC dates consistently:**
```razor
<SfDatePicker TValue="DateTime?" 
    ValueChange="@OnDateChanged">
</SfDatePicker>

@code {
    private DateTime? selectedDateUtc;
    
    private void OnDateChanged(ChangedEventArgs<DateTime?> args)
    {
        // Convert local time to UTC (assumes args.Value is user's local time)
        selectedDateUtc = args.Value?.ToUniversalTime();
        Console.WriteLine($"Selected (UTC): {selectedDateUtc}");
        Console.WriteLine($"Kind after conversion: {selectedDateUtc?.Kind}");
    }
}
```

2. **Use DateTimeOffset for timezone-aware storage:**
```razor
<SfDatePicker TValue="DateTime?" 
    ValueChange="@OnDateChanged">
</SfDatePicker>

@code {
    private DateTimeOffset? selectedDateOffset;
    
    private void OnDateChanged(ChangedEventArgs<DateTime?> args)
    {
        if (args.Value.HasValue)
        {
            // Preserve the datetime with its offset from UTC
            selectedDateOffset = new DateTimeOffset(
                args.Value.Value,
                TimeZoneInfo.Local.GetUtcOffset(args.Value.Value)
            );
        }
    }
}
```

3. **Store in database as UTC:**
```csharp
// When saving - only use ToUniversalTime() on Local/Unspecified datetimes
var utcDate = selectedDate?.ToUniversalTime();
await database.SaveAsync(utcDate);

// If date is already UTC, ToUniversalTime() returns same value (safe to call)
```

4. **Convert for display:**
```razor
@code {
    private DateTime? DisplayDateLocal(DateTime? utcDate)
    {
        if (!utcDate.HasValue) return null;
        // Ensure Kind is UTC before calling ToLocalTime()
        var utc = DateTime.SpecifyKind(utcDate.Value, DateTimeKind.Utc);
        return utc.ToLocalTime();
    }
}

<!-- Usage -->
Selected: @DisplayDateLocal(storedUtcDate)?.ToString("MM/dd/yyyy")
```

**Key Points:**
- `ToUniversalTime()` on **Unspecified** datetime → converts from local to UTC
- `ToUniversalTime()` on **UTC** datetime → returns same value (no conversion)
- `ToLocalTime()` on **UTC** datetime → adds local timezone offset
- `ToLocalTime()` on **Unspecified** datetime → assumes it's local, converts to local

**Correct pattern for timezone handling:**
```csharp
// Store
var utcToStore = DateTime.SpecifyKind(selectedDate, DateTimeKind.Utc);

// Display  
var localForDisplay = DateTime.SpecifyKind(utcFromDatabase, DateTimeKind.Utc).ToLocalTime();
```

---

### Issue: Mobile/Touch Not Working

**Problem:** Calendar doesn't respond to touch input on mobile devices.

**Root Cause:** The `SfCalendar` component currently only handles `@onclick` events on day cells and does not have explicit touch event handlers (`@ontouchstart`, `@ontouchend`). On some mobile browsers/devices, this can cause touch taps to not register properly.

**Solutions:**

1. **Ensure proper viewport configuration:**
```html
<!-- Add viewport meta tag in index.html -->
<meta name="viewport" content="width=device-width, initial-scale=1.0, 
    user-scalable=yes, minimum-scale=1.0, maximum-scale=5.0">
```

2. **Test on actual devices:**
- Use Chrome DevTools device emulation
- Test on real iOS/Android devices
- Check for browser touch support

3. **Ensure adequate touch targets** (visual aid only, does not enable touch):
```css
.e-calendar .e-cell {
    min-width: 44px;
    min-height: 44px;
}
```

4. **Check browser console for errors:**
Open DevTools (F12) on mobile or use remote debugging to check for JavaScript errors that may prevent event handling.

**Note:** If touch interaction consistently fails across all devices, this may indicate a bug in the component that requires code-level fix (adding `@ontouchstart` handlers to calendar cells similar to how `SfDatePicker` handles touch on its clear button).

---

### Issue: Performance - Slow Rendering

**Problem:** Component is slow or laggy, especially with large date ranges.

**Solutions:**

1. **Use Min/Max to restrict range:**
```razor
<!-- Large ranges can slow rendering -->
@using Syncfusion.Blazor.Toolkit.Calendars

<SfCalendar TValue="DateTime"
    Min="@minDate"
    Max="@maxDate">
</SfCalendar>

@code {
    private DateTime minDate = DateTime.Now;
    private DateTime maxDate = DateTime.Now.AddYears(1);
}
```

2. **Avoid frequent updates:**
```razor
<!-- Don't update in every render -->
<SfDatePicker TValue="DateTime?"
    ValueChange="@OnValueChanged">
</SfDatePicker>

@code {
    private void OnValueChanged(ChangedEventArgs<DateTime?> args)
    {
        // Batch updates
        StateHasChanged();
    }
}
```

---

## Getting Help

If you can't find a solution:

1. **Check browser console** - Look for JavaScript errors
2. **Enable debug mode** - Add breakpoints in event handlers
3. **Reproduce in sample** - Test with minimal example
4. **Check Syncfusion docs** - Official documentation for API details
5. **File a bug report** - If you find a genuine issue, report to Syncfusion

## See Also

- [API Reference](api-reference.md) - Component API details
- [Features](features.md) - All available features
- [Events & Binding](events-binding.md) - Event handling patterns
