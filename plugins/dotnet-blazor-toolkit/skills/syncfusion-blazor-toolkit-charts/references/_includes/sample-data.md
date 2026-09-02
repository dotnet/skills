---
license: MIT
name: sample-data
description: >
  Shared record declarations and sample initializer lists used by the
  syncfusion-blazor-toolkit-charts reference snippets. Included by other
  references under `references/` via the `_includes/sample-data.md` link in
  their top-of-file "Sample data" callout. Not intended to be loaded
  directly by the skill router.
---

# Sample data — shared record declarations

The chart reference files (`axes-and-scales.md`, `interactive-features.md`,
`events.md`, `data-handling.md`, `advanced-features.md`, etc.) all bind their
`<ChartSeries DataSource="...">` snippets to the same canonical list and record
definitions, declared once here so every snippet in the reference set stays
type-compatible.

> **Sample data** — this file is the single source. If a snippet needs another
> field, extend the record here (not in the snippet file) so the rest of the
> reference set still compiles.

## Records

```csharp
// Categorical / basic numeric binding (default).
public record SeriesPoint(string X, double Y);

// Secondary axis + multi-series Y (e.g. axis-on-the-right, range charts).
public record SamplePoint(string X, double Y, double Y2 = 0);

// Date / time axis binding.
public record DatePoint(DateTime When, double Value);

// Point (string X, double Y) — alias used in `axes-and-scales.md` snippets.
public record Point(string X, double Y);

// Finance / OHLC-style binding.
public record FinancePoint(DateTime Date, double High, double Low, double Open, double Close);

// Category-value binding (used in appearance / palette chains).
public record CategoryValue(string Name, double Value);
```

## Default `Data` list bound by the simplest snippets

```csharp
private readonly List<SeriesPoint> Data = new()
{
    new SeriesPoint("Jan", 35),
    new SeriesPoint("Feb", 28),
    new SeriesPoint("Mar", 34),
    new SeriesPoint("Apr", 32),
    new SeriesPoint("May", 40),
    new SeriesPoint("Jun", 32)
};
```

## Date-point list (used by axis `ValueType.DateTime` snippets)

```csharp
private readonly List<DatePoint> Dates = new()
{
    new DatePoint(new DateTime(2026, 1, 1), 12),
    new DatePoint(new DateTime(2026, 2, 1), 19),
    new DatePoint(new DateTime(2026, 3, 1), 15),
    new DatePoint(new DateTime(2026, 4, 1), 22),
    new DatePoint(new DateTime(2026, 5, 1), 30),
    new DatePoint(new DateTime(2026, 6, 1), 26)
};
```

## Live-streaming backing list (`data-handling.md`)

```csharp
// In-memory snapshot.
private List<SeriesPoint> LatestData = new();

// Live-streaming collection — feeds `OnAfterRenderAsync` → `SfChart.RefreshAsync`.
private ObservableCollection<SeriesPoint> Live = new();
```

> When you write a new snippet, prefer binding to one of the named lists above.
> Only declare a new record here when none of the existing shapes fit — and
> then update the lines block-quote callout in your snippet file to name the
> new record.
