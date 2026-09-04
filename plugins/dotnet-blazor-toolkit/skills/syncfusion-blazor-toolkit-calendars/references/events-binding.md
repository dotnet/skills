# Event Handling & Data Binding

## Table of Contents
- [Data Binding](#data-binding)
- [Event Callbacks](#event-callbacks)
- [Programmatic Interaction](#programmatic-interaction)
- [Validation Integration](#validation-integration)
- [Reactive Programming](#reactive-programming)
- [Event and Binding Combinations](#event-and-binding-combinations)
- [Performance Best Practices](#performance-best-practices)

## Data Binding

### Two-Way Binding with @bind-Value

```razor
<p><label>Selected Date:</label> @DateValue</p>

<SfDatePicker TValue="DateTime?" Width="270px" @bind-Value="@DateValue" ShowClearButton="true"></SfDatePicker>
	
@code {
	public DateTime? DateValue { get; set; } = DateTime.Today;
}
```

All calendar components support two-way binding through the `@bind-Value` directive.

### One-Way Binding

```razor
<SfDatePicker TValue="DateTime?" Value="@selectedDate"></SfDatePicker>

@code {
    private DateTime? selectedDate = DateTime.Today.AddDays(1);
}
```

## Event Callbacks

### ValueChange Event

Fired when the date value changes:

```razor
<SfDatePicker TValue="DateTime?"
    ValueChange="@OnDateChanged">
</SfDatePicker>

@code {
    private DateTime? previousDate;
    
    private void OnDateChanged(ChangedEventArgs<DateTime?> args)
    {
        Console.WriteLine($"New value: {args.Value}");
        Console.WriteLine($"Previous value was: {previousDate}");
        previousDate = args.Value;
    }
}
```

### Lifecycle Events

```razor
<SfCalendar TValue="DateTime"
    Created="@OnCreated"
    Destroyed="@OnDestroyed">
</SfCalendar>

@code {
    private void OnCreated()
    {
        Console.WriteLine("Calendar component created");
    }
    
    private void OnDestroyed()
    {
        Console.WriteLine("Calendar component destroyed");
    }
}
```

### Popup Events

DatePicker, DateTimePicker, and TimePicker support popup events:

```razor
<SfDatePicker TValue="DateTime?"
    OnOpen="@OnOpen"
    OnClose="@OnClose">
</SfDatePicker>

@code {
    private void OnOpen(PopupObjectArgs args)
    {
        Console.WriteLine("Popup opening");
        // args.Cancel = true; // To cancel opening
    }
    
    private void OnClose(PopupObjectArgs args)
    {
        Console.WriteLine("Popup closing");
        // args.Cancel = true; // To cancel closing
    }
}
```

## Programmatic Interaction

### Using Reference to Component

```razor
<SfDatePicker TValue="DateTime?" @ref="datePickerRef"></SfDatePicker>
<button @onclick="UpdateDate">Set Date</button>

@code {
    private SfDatePicker<DateTime?> datePickerRef;
    
    private async Task UpdateDate()
    {
        // Open the popup
        await datePickerRef.ShowPopupAsync();
        await Task.Delay(2000);
        
        // Close the popup
        await datePickerRef.HidePopupAsync();
        
        // Focus the component
        await datePickerRef.FocusAsync();
    }
}
```

## Validation Integration

### EditForm and DataAnnotations

```razor
@using Syncfusion.Blazor.Toolkit.Calendars
@using System.ComponentModel.DataAnnotations

<EditForm Model="@model" OnValidSubmit="@HandleSubmit">
    <DataAnnotationsValidator />
    
    <div class="form-group">
        <label for="eventDate">Event Date</label>
        <SfDatePicker TValue="DateTime?"
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
        [Range(typeof(DateTime), 
            minimum: "2026-01-01", 
            maximum: "2026-12-31",
            ErrorMessage = "Event must be in 2026")]
        public DateTime? EventDate { get; set; }
    }
}
```

### Custom Validation

```razor
<SfDatePicker TValue="DateTime?"
    ValueChange="@ValidateAge">
</SfDatePicker>

<p style="color:red">@validationError</p>

@code {
    private string validationError = "";
    
    private void ValidateAge(ChangedEventArgs<DateTime?> args)
    {
        if (!args.Value.HasValue) return;
        
        var birthDate = args.Value.Value;
        var today = DateTime.Today;
        var age = today.Year - birthDate.Year;
        
        if (birthDate.Date > today.AddYears(-age))
            age--;
        
        validationError = age < 18 ? "Must be at least 18 years old" : "";
    }
}
```

## Reactive Programming

### Using StateHasChanged

```razor
<SfCalendar TValue="DateTime"
    ValueChange="@OnDateChanged">
</SfCalendar>

<p>Selected: @(selectedDate.ToString("d"))</p>
<p>Day of week: @GetDayOfWeek()</p>

@code {
    private DateTime selectedDate = DateTime.Now;
    
    private void OnDateChanged(ChangedEventArgs<DateTime> args)
    {
        selectedDate = args.Value;
        StateHasChanged(); // Trigger re-render
    }
    
    private string GetDayOfWeek()
    {
        return selectedDate.ToString("dddd");
    }
}
```

### Multiple Components Coordination

```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfDatePicker TValue="DateTime?"
    ValueChange="@OnStartDateChanged"
    Placeholder="Start date">
</SfDatePicker>

<SfDatePicker TValue="DateTime?"
    Min="@startDate"
    Placeholder="End date">
</SfDatePicker>

@code {
    private DateTime startDate;
    
    private void OnStartDateChanged(ChangedEventArgs<DateTime?> args)
    {
        if (args.Value.HasValue)
        {
            startDate = args.Value.Value;
        }
    }
}
```

## Event and Binding Combinations

### Complete Example with Multiple Features

```razor
@using Syncfusion.Blazor.Toolkit.Calendars
@using System.ComponentModel.DataAnnotations

<EditForm Model="@booking" OnValidSubmit="@SubmitBooking">
    <DataAnnotationsValidator />
    <ValidationSummary />
    
    <div class="form-group">
        <label>Check-in Date</label>
        <SfDatePicker TValue="DateTime?"
            @bind-Value="booking.CheckIn"
            Min="@DateTime.Today"
            ValueChange="@OnCheckInChanged">
        </SfDatePicker>
        <ValidationMessage For="@(() => booking.CheckIn)" />
    </div>
    
    <div class="form-group">
        <label>Check-out Date</label>
        <SfDatePicker TValue="DateTime?"
            @bind-Value="booking.CheckOut"
            Min="@(booking.CheckIn?.AddDays(1) ?? DateTime.Today.AddDays(1))"
            Max="@(booking.CheckIn?.AddDays(30) ?? DateTime.Today.AddDays(30))">
        </SfDatePicker>
        <ValidationMessage For="@(() => booking.CheckOut)" />
    </div>
    
    <div class="form-group">
        <label>Check-in Time</label>
        <SfTimePicker TValue="DateTime?"
            @bind-Value="booking.CheckInTime"
            Format="hh:mm tt">
        </SfTimePicker>
    </div>
    
    <p>Total nights: @CalculateNights()</p>
    
    <button type="submit" class="btn btn-primary">Book Now</button>
</EditForm>

@code {
    private BookingModel booking = new();
    
    private void OnCheckInChanged(ChangedEventArgs<DateTime?> args)
    {
        if (args.Value.HasValue)
        {
            // Suggest check-out 3 days later
            booking.CheckOut = args.Value.Value.AddDays(3);
            StateHasChanged();
        }
    }
    
    private int CalculateNights()
    {
        if (booking.CheckIn.HasValue && booking.CheckOut.HasValue)
        {
            return (booking.CheckOut.Value.Date - booking.CheckIn.Value.Date).Days;
        }
        return 0;
    }
    
    private void SubmitBooking()
    {
        Console.WriteLine($"Booking confirmed for {CalculateNights()} nights");
    }
    
    class BookingModel
    {
        [Required]
        public DateTime? CheckIn { get; set; }
        
        [Required]
        public DateTime? CheckOut { get; set; }
        
        public DateTime? CheckInTime { get; set; }
    }
}
```

## Performance Best Practices

1. **Debounce rapid updates** - Avoid excessive StateHasChanged calls
2. **Use @key directive** - Optimize list rendering with calendar lists
3. **Lazy load** - Load calendar components only when needed
4. **Memoize calculations** - Cache derived values from dates

## See Also

- [Calendar](calendar.md) - Calendar component
- [DatePicker](datepicker.md) - Single date selection
- [Features](features.md) - Core features
