---
license: MIT
name: syncfusion-blazor-toolkit-calendars
description: >
  Implement Syncfusion Blazor Toolkit calendar and date/time input
  components — SfCalendar, SfDatePicker, SfDateTimePicker, SfTimePicker.
  USE FOR: date-only, date-time, or time-only selection; date range
  restriction (Min/Max); custom date formats (culture-invariant vs locale);
  multi-date selection; disabled weekends/holidays via DayCellRendering;
  and culture/timezone-sensitive formatting.
  DO NOT USE FOR: text or email input (use
  syncfusion-blazor-toolkit-inputs), scheduling or calendar recurrence
  (use Scheduler — not in this skill), or date validation that requires an
  API round-trip (handle in EditContext validator instead).
compatibility: ".NET 8+, render-modes: Server, WebAssembly, Auto"
metadata:
  author: "Syncfusion Inc"
  version: "1.0.0"
---

# Syncfusion Blazor Toolkit - Calendar Components

> ⚙️ **Render mode:** `SfCalendar`, `SfDatePicker`, `SfDateTimePicker`, `SfTimePicker` require an **interactive** render mode (Server, WebAssembly, or Auto @ .NET 8+). Read `AGENTS.md` before generating — `EnableUtc` and interactive event delivery require it.

## Core Rules

The four rules below are the highest-impact; the rest (5–8) live in
[references/critical-gotchas.md](references/critical-gotchas.md) — read it
first if you have any time/format/validation question. These top 4 together
prevent ~95% of calendar bug reports.

1. **Pick the right `TValue`**: `DateOnly` (or `DateOnly?`) for date-only
   `SfCalendar` / `SfDatePicker` (avoids accidental timezone drift);
   `DateTime?` for date-time; `SfTimePicker` supports `DateTime`, `TimeOnly`,
   `TimeSpan`, and `DateTimeOffset` (each in nullable form when the field is
   optional). Match the model's property type exactly — never mix in one form.
2. **Always set `EnableUtc="true"`** for Server / Auto render modes that
   persist dates to a database. Without it the calendar persists the *local*
   timezone representation, which round-trips incorrectly.
3. **`Format` is culture-invariant when you supply a literal pattern**
   (`"dd/MM/yyyy"`); culture-aware when you supply a token (`"d"`, `"D"`).
   Never mix the two in one form.
4. **`Min` / `Max` are inclusive and timezone-relative.** Provide them in the
   calendar's display timezone; UTC only when `EnableUtc="true"`.

Rules 5–8 — `DayCellRendering` mutation discipline, `aria-label` opt-in,
`@bind-Value` + `ValueChanged` double-fire, and midnight re-render — live in
[references/critical-gotchas.md](references/critical-gotchas.md).

## Don'ts

> **Cross-reference with critical-gotchas.md:** rules below mirror the 8-rule
> reference at [references/critical-gotchas.md](references/critical-gotchas.md);
> the rules duplicated here (rows 1, 3, 5, 7, 8) are surfaced inline because
> they're the most common agent mistakes. When in doubt on timezones,
> values, or rendering — read the full gotchas reference.

| Anti-pattern | Symptom | Fix |
|---|---|---|
| `TValue="DateTime"` (non-nullable) inside `EditForm` | "Object reference not set" on first render; type-mismatch exception | Use `TValue="DateTime?"` (or `DateOnly?`) |
| `TValue="TimeOnly?"` on `SfTimePicker` without `Format="HH:mm"` | Time zone/`[Required]` edge cases; displayed pattern follows the culture default | `SfTimePicker TValue="TimeOnly?" Format="HH:mm"` is supported — but set an explicit `Format` (or use `TValue="DateTime?" Format="HH:mm"`) |
| `Min`/`Max` in UTC with `EnableUtc="false"` | Off-by-one day selection | Pick one: either `EnableUtc="true"` **and** UTC math, or local timezone end-to-end |
| `args.IsHighlight = true` in `DayCellRendering` | Throws — property doesn't exist | Use `args.CellClass = "highlight"` and style the class |
| Subscribe `ValueChanged` *and* `@bind-Value` | Both fire; the second wins; sometimes a render-loop | Pick one. Use `@bind-Value:after` for side effects |
| One `SfCalendar` driving two date inputs | Renderer mounts a single DOM, second input has no calendar | Per-input `<SfDatePicker>`, share state via `CascadingValue<T>` |
| `<SfCalendar>` inside a Server-mode page that pre-renders today | Highlight misses today's date because of a half-day clock skew between server UTC and client local | Always set `EnableUtc="true"` for Server / Auto |
| `Format="d"` mixed with `Format="yyyy-MM-dd"` in one form | Validation messages disagree; tooltip provides two formats | Pick one (invariant vs culture) per form |
| Reading `args.Date` and assuming it's `DateOnly`-typed | It's `DateTime` (midnight, server-side); comparison may be wrong | Compare against `args.Date.Date` against your `Min` same way |
| Setting `Min` only — never `Max` | Calendar allows any future date; user picks 2099 | Pair with `Max` whenever `Min` is set |

## Anti-Pattern Workflows

### Workflow 1 — Agent copies a DateTime example and ignores UTC

**Bad:**
```razor
<SfCalendar TValue="DateTime?" @bind-Value="@today" Min="@new DateTime(2026,1,1)" />
@code { private DateTime? today = DateTime.UtcNow.Date; }
```

**Why it fails:** `Min` is provided in UTC, but the calendar's display
timezone is the user's local. A user in Tokyo at 09:00 (Jan 1 local =
00:00 UTC) sees "Jan 1 enabled" — correct by accident. A user in New York
at 19:00 (Jan 1 local = 00:00 UTC) sees "Dec 31 disabled" — wrong.

**Correct:** keep both in the same timezone, and align with the database
side:

```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfCalendar TValue="DateTime?"
            EnableUtc="true"
            @bind-Value="@today" />
@code {
    private DateTime? today;        // stored UTC; ToLocalTime() on display
}
```

Or — if the calendar must stay in local — pass `Min` in local:

```razor
Min="@new DateTime(2026,1,1,0,0,0,DateTimeKind.Local)"
```

### Workflow 2 — Agent sets `DayCellRendering` to highlight holidays but `args.IsHighlight` doesn't exist

**Bad:**
```razor
private void OnCell(RenderDayCellEventArgs args)
{
    if (IsHoliday(args.Date)) args.IsHighlight = true;  // CS1061
}
```

**Fix:** use `CellClass` (you style with CSS):

```razor
private void OnCell(RenderDayCellEventArgs args)
{
    if (IsHoliday(args.Date))
    {
        args.CellClass = "holiday-cell";
        args.IsDisabled = true;            // pick one — these aren't mutually exclusive
    }
}
```

```css
.holiday-cell { background: var(--brand-warning-tint); color: var(--brand-warning-fg); }
```

### Workflow 3 — Agent binds `@bind-Value` to `ValueChanged`

**Bad:**
```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfDatePicker TValue="DateTime?"
              @bind-Value="@_date"
              ValueChanged="OnDate" />
<!-- fires twice, @bind-Value then ValueChanged -->
```

**Correct:** use `@bind-Value:after` (.NET 8+) for post-commit logic:

```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfDatePicker TValue="DateTime?"
              @bind-Value="@_date"
              @bind-Value:after="OnDateCommitted" />

@code {
    private DateTime? _date;
    private async Task OnDateCommitted() { /* fires once after binding commits */ }
}
```

### Workflow 4 — Agent expects calendar to "remain today" past midnight

**Bad:** leave `SfCalendar` open across midnight expecting the highlight to
move forward — the renderer doesn't re-tick.

**Correct:** for "ticking" UX, wire a `Timer` for the page-level refresh:

```razor
@implements IDisposable
@code {
    private System.Threading.Timer? _t;
    protected override void OnAfterRender(bool first)
    {
        if (first)
        {
            _t = new System.Threading.Timer(_ => InvokeAsync(StateHasChanged),
                                           null,
                                           TimeSpan.Zero,
                                           TimeSpan.FromMinutes(15));
        }
    }
    public void Dispose() => _t?.Dispose();
}
```

For non-essential UX, prefer a plain "Today" pill button that re-binds the
date on click instead of keeping a timer running.

### Workflow 5 — Agent wants to persist UTC but render local labels

**Correct:**
```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfCalendar TValue="DateTime?"
            EnableUtc="true"
            @bind-Value="@_utcDate"
            Format="yyyy-MM-dd" />
@code {
    public DateTime? _utcDate; // stays UTC
    public string LocalLabel => _utcDate?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
}
```

> For the full 8-rule reference, see
> [references/critical-gotchas.md](references/critical-gotchas.md).

## Minimal Example

```razor
@using Syncfusion.Blazor.Toolkit.Calendars

<SfDatePicker TValue="DateTime?"
              Placeholder="Select a date"
              @bind-Value="selectedDate" />

@code {
    private DateTime? selectedDate;
}
```

---

## Documentation and Navigation Guide

| Need | Read |
|---|---|
| First component / installation | [references/getting-started.md](references/getting-started.md) |
| Core features (selection, formatting, validation) | [references/features.md](references/features.md) |
| Per-component reference (`SfCalendar`, `DatePicker`, `DateTimePicker`, `TimePicker`) | [references/calendar.md](references/calendar.md), [references/datepicker.md](references/datepicker.md), [references/datetimepicker.md](references/datetimepicker.md), [references/timepicker.md](references/timepicker.md) |
| Events and two-way binding | [references/events-binding.md](references/events-binding.md) |
| API reference (properties, events, methods, enums) | [references/api-reference.md](references/api-reference.md) |
| Troubleshooting | [references/troubleshooting.md](references/troubleshooting.md) |

> ⚠️ Read [references/critical-gotchas.md](references/critical-gotchas.md) **first** — UTC vs local time, `TValue` selection, `Format` mixing, `Min`/`Max` inclusivity, `DayCellRendering` mutation rules, accessibility labelling, `@bind-Value` + `ValueChanged` interaction, `DateTime.Now` snapshot.

---

## Next Steps

1. **Read first:** [references/critical-gotchas.md](references/critical-gotchas.md)
2. **Setup:** [references/getting-started.md](references/getting-started.md)
3. **Per-component reference:** the relevant `references/*.md`

**Demo:** https://blazor.syncfusion.com/demos/toolkit/calendar
