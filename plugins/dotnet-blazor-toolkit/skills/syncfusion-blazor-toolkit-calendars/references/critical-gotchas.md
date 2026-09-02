---
license: MIT
name: critical-gotchas
description: >
  The 8 critical Syncfusion Blazor calendar gotchas every coding agent must
  internalize. Loaded automatically by syncfusion-blazor-toolkit-calendars
  before any Calendar / DatePicker / DateTimePicker / TimePicker code is
  generated. Covers UTC vs local time, TValue type selection, Format string
  precedence, Min/Max inclusivity, DayCellRendering mutation rules, and
  accessibility labelling.
---

# Calendar Critical Gotchas

**Always read this before generating calendar code.** Syncfusion Blazor Toolkit
calendar components share one event model but have four component-specific
quirks. The eight gotchas below cover ~95% of bugs reported in the
`#calendar-bugs` channel.

## 1. `TValue` is *not* optional — pick the right type

| Need | `TValue` | Why |
|---|---|---|
| Date without time | `DateOnly?` (or `DateTime?` for legacy models) | Avoids accidental timezone drift |
| Date + time | `DateTime?` | Default; min/max round-trip cleanly |
| Time-only | `TimeOnly?` with `Format="HH:mm"` (or `DateTime?` for legacy models that carry a date component) | `SfTimePicker` supports `DateTime` / `TimeOnly` / `TimeSpan` / `DateTimeOffset` (each nullable) — pick the one that matches the bound property's type |
| UTC-stable | `DateTime?` with `EnableUtc="true"` | Server thinks UTC, browser thinks local |
| Pre-blazor-1 model | `DateTime` (non-nullable) | Avoid — no longer recommended |

```razor
<!-- ✅ Date-only calendar -->
<SfCalendar TValue="DateOnly?" @bind-Value="@selectedDay" />

<!-- ✅ Date+time picker -->
<SfDateTimePicker TValue="DateTime?" @bind-Value="@selectedAt" />

<!-- ✅ Time-only picker — TimeOnly? is supported on SfTimePicker -->
<SfTimePicker TValue="TimeOnly?" @bind-Value="@t" Format="HH:mm" />
```

**Don't** use `DateTime` (non-nullable) for forms — `EditForm` annotates the
property as nullable and binds `null` on first render, which crashes a
non-nullable `TValue`.

## 2. UTC vs local time — the #1 production bug

`SfCalendar.Value` (and friends) is **stored as the calendar component's local
timezone** unless `EnableUtc="true"`.

```razor
<SfCalendar TValue="DateTime?"
            EnableUtc="true"   <!-- calendar reads/writes UTC -->
            @bind-Value="@value" />
```

Without `EnableUtc="true"`:
- Server (UTC) reads `2026-03-15 00:00:00` from the user in Tokyo (UTC+9) who
  selected "15 Mar" — that's the previous day in Tokyo's interpretation.
- The displayed date is correct (because format uses local), but the bound
  `DateTime` is the wrong absolute instant.

**Rule:** In Server / Auto render modes that persist dates to a database,
**always set `EnableUtc="true"`** and `ToLocalTime()` only on display.

## 3. `Format` vs culture — string is culture-invariant

| Value | Meaning | Bind target |
|---|---|---|
| `Format="dd/MM/yyyy"` | Literal `dd/MM/yyyy` regardless of culture | Stateless |
| `Format="d"` | Short date from `CultureInfo.CurrentCulture` | `CurrentCulture` at render |
| `Format="D"` | Long date from `CultureInfo.CurrentCulture` | `CurrentCulture` at render |
| `Format="MMMM yyyy"` | Custom invariant pattern | Stateless |

```razor
<!-- ✅ Use literal pattern when the user-facing format must not change with browser locale -->
<SfDatePicker TValue="DateTime?" Format="yyyy-MM-dd" />

<!-- ✅ Use culture token when internationalization is the goal -->
<SfDatePicker TValue="DateTime?" Format="d" />
```

**Don't** mix the two in the same form — you get inconsistent error messages
(`formatException("The string 'dd/MM/yyyy' was not recognized")`).

## 4. `Min` and `Max` are *inclusive* AND *local-timezone-relative*

The component compares `Min`/`Max` against `Value` after **converting Value to
the calendar's timezone**. If `Min = new DateTime(2026, 1, 1)` and the user is
in Sydney (UTC+11), the calendar disables everything before
`2026-01-01 00:00 Sydney` = `2025-12-31 13:00 UTC` on the value side.

```razor
<SfDatePicker TValue="DateTime?"
              Min="@new DateTime(2026, 1, 1)"
              Max="@new DateTime(2026, 12, 31)" />
```

**Rule:** Always provide `Min`/`Max` in the calendar's local timezone, never in
UTC, unless `EnableUtc="true"`.

## 5. `DayCellRendering` mutation rules

`DayCellRendering` runs **per cell, per render** (~42 calls for a month view).
Mutation inside the handler is allowed, but **don't** set `args.IsDisabled`
on a non-day property — only `args.IsDisabled`, `args.CellText`, and
`args.CellClass` are supported.

```razor
<SfCalendar TValue="DateTime"
            DayCellRendering="@OnCell">
    @code {
        private void OnCell(RenderDayCellEventArgs args)
        {
            if (args.Date.DayOfWeek is DayOfWeek.Saturday
                                  or DayOfWeek.Sunday)
            {
                args.IsDisabled = true;
            }
            // args.IsHighlight  ← ❌ does not exist
            // args.AddClass    ← ❌ use args.CellClass = "..." instead
        }
    }
</SfCalendar>
```

## 6. Accessibility — `aria-label` is NOT auto-applied

Wrapping a calendar in a `<label>` is not enough — the calendar renders
multiple `<button>` cells. Use the `AriaLabel` property:

```razor
<SfDatePicker TValue="DateTime?"
              AriaLabel="Appointment date"
              @bind-Value="@date" />
<ValidationMessage For="() => date" />
```

If the component is part of a label-required form, **also** add `<label>` to
the parent so screen readers announce the field.

## 7. Don't combine `@bind-Value` and `ValueChanged`

`ValueChanged` fires **after** `@bind-Value` commits the new value. If you wire
both, you'll fire the handler twice on every change.

```razor
<!-- ❌ Double-fire -->
<SfDatePicker TValue="DateTime?"
              @bind-Value="@d"
              ValueChanged="@OnD" />

<!-- ✅ Pick one -->
<SfDatePicker TValue="DateTime?" @bind-Value="@d" @bind-Value:after="OnDChanged" />
```

`@bind-Value:after` is the dotnet-8 way to run logic after binding commits.

## 8. `DateTime.Now` snapshot — calendars don't re-render on `tick`

If you build a "today" calendar that should highlight the current day
continuously, the component **does not** re-render to advance the highlight at
midnight. You must trigger a render:

```razor
@implements IDisposable
@code {
    private Timer? _t;

    protected override void OnInitialized()
    {
        _t = new Timer(_ => InvokeAsync(StateHasChanged),
                      null,
                      TimeSpan.Zero,
                      TimeSpan.FromHours(1));
    }

    public void Dispose() => _t?.Dispose();
}
```

For most cases this is overkill — a `@bind-Value` on user click already
re-renders. Add the timer only if the calendar is read-only and the page is
expected to stay open across midnight.

---

## When in doubt

- Date-only ⇒ `DateOnly?` + `Format="yyyy-MM-dd"` + `EnableUtc="false"`
- Date + time ⇒ `DateTime?` + sensible `Format` + decide UTC per row
- Time-only ⇒ `TimeOnly?` + `Format="HH:mm"` (use `DateTime?` only for legacy
  models that carry a date component)
- Range restricted ⇒ Set `Min`/`Max` in the *display* timezone + use
  `DayCellRendering` for per-cell disable (weekends, holidays)