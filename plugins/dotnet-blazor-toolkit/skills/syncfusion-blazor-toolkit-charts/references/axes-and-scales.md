# Axes and Scales

> **Verified against source** — strip-line pluralisation, shape enum,
> axis intervals verified against `src/Base/Enumeration.cs`. Last source
> audit: **2026-08-24**.

`SfChart` exposes two primary axes (`<ChartPrimaryXAxis>`,
`<ChartPrimaryYAxis>`) and an unbounded collection of extra axes
(`<ChartAxes><ChartAxis />…</ChartAxes>`). Every axis is one of five value
types: `Category`, `Double`, `DateTime`, `Logarithmic`, `DateTimeCategory`.
Pick the type from the data shape — never default to `Category` "to be safe".

> **Sample data** — see [`_includes/sample-data.md`](_includes/sample-data.md).
> This file declares `Point` (string X, double Y) and `DatePoint` (DateTime
> When, double Y) lists named `Series` and `Dates` used by the snippets
> below.

## Table of contents

- Pick a `ValueType` (decision table)
- Category axis
- Numeric axis (`Double`)
- DateTime axis
- Logarithmic axis
- Axis namespaces, titles, labels, ticks, grids
- Multiple / opposed axes
- Strip lines (cross-axis highlights)
- Best practices + troubleshooting

## Pick a `ValueType`

| Data shape | `ValueType` |
|-----------|-------------|
| Discrete labels ("Jan", "Q1", "North") | `Category` |
| Continuous numeric (`double`) | `Double` |
| Timestamps | `DateTime` |
| Exponential growth/decay data | `Logarithmic` |
| Time-bucketed categories (e.g. months but worth treating as a series) | `DateTimeCategory` |
| **Default if you forget** | `Double` (auto-promotes dates to ordinals — usually wrong) |

Setting the wrong `ValueType` silently sorts/treats data incorrectly. There
is **no warning** — it just looks "off".

## Category axis

```razor
<ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"
                   Title="Quarter"
                   LabelPlacement="LabelPlacement.OnTicks">
    <ChartAxisMajorGridLines Width="1" />
</ChartPrimaryXAxis>

<ChartSeries DataSource="@Series" XName="X" YName="Y"
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" />
```

Useful `Category` controls: `LabelPlacement` (`BetweenTicks` default |
`OnTicks`), `Indexed` (display the row index on the axis instead of any
field value), `Interval` (`2` → show every 2nd label).

## Numeric (Double) axis

```razor
<ChartPrimaryYAxis Title="Revenue ($M)"
                   Minimum="0" Maximum="100" Interval="10"
                   RangePadding="ChartRangePadding.Round"
                   LabelFormat="n0">
    <ChartAxisMajorTickLines Width="2" Color="#999" />
</ChartPrimaryYAxis>
```

`RangePadding` values that matter:

| Padding | When to use |
|---------|-------------|
| `None` | Raw min–max; the chart looks clingy on edges |
| `Round` | Round to nearest interval (default-ish) |
| `Additional` | Keep room for grid lines |
| `Normal` | Recommended for series with zero baseline |
| `Auto` | X uses `None`, Y uses `Normal` |

`LabelFormat="c0"` formats as currency with 0 decimals (uses chart `Locale`).
See `references/appearance-styling.md` for format tokens.

## DateTime axis

```razor
<ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.DateTime"
                   LabelFormat="MMM dd, yyyy"
                   IntervalType="IntervalType.Months"
                   Interval="1"
                   Minimum="new DateTime(2024,1,1)"
                   Maximum="new DateTime(2024,4,30)"
                   EdgeLabelPlacement="EdgeLabelPlacement.Shift" />
```

| When your data is… | Set `IntervalType` to |
|--------------------|----------------------|
| Yearly | `Years` |
| Monthly | `Months` |
| Daily | `Days` |
| Hourly | `Hours` |
| Auto | `Auto` |

## Logarithmic axis

```razor
<ChartPrimaryYAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Logarithmic"
                   LogBase="10"
                   Minimum="1" Maximum="10000" Interval="1"
                   LabelFormat="n0" />
```

Use only when data spans multiple orders of magnitude. `LogBase` must be
`> 1`; default is `10`. Don't use `Logarithmic` for "smaller numbers" — use
`Double` with a tighter `Minimum/Maximum`.

## Titles, labels, ticks, grids

```razor
<ChartPrimaryXAxis Title="Month">
    <ChartAxisTitleStyle Size="14" FontWeight="600" />
    <ChartAxisLabelStyle Size="11" Color="#555" Rotation="0" />
    <ChartAxisMajorTickLines Width="1" />
    <ChartAxisMinorTickLines Width="0.5" />
    <ChartAxisMajorGridLines Width="1" Color="#E0E0E0" />
    <ChartAxisMinorGridLines Width="0.5" Color="#EEEEEE" />
</ChartPrimaryXAxis>
```

`Rotation`: degree rotation; `0`–`90` for typical axis text. Use
`LabelIntersectAction` to handle long category labels:

| Strategy | Use when |
|----------|----------|
| `Hide` | Lots of labels, low importance |
| `Trim` | One or two long words |
| `Wrap` | Multi-word labels |
| `Rotate45` / `Rotate90` | Compact presentation |
| `MultipleRows` | Long phrases that need full width |

## Multiple / opposed axes

Use a separate `<ChartAxis Name="…" OpposedPosition="true" />` in
`<ChartAxes>` and point at it with `YAxisName`:

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartPrimaryYAxis Title="Sales (units)" Minimum="0" />

    <ChartAxes>
    <ChartAxis Name="MarginAxis"
                   OpposedPosition="true"
                   Title="Margin (%)"
                   Minimum="0" Maximum="100" LabelFormat="{value}%" />
    </ChartAxes>

    <ChartSeries DataSource="@Series" XName="X" YName="Y"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" />
    <ChartSeries DataSource="@Series" XName="X" YName="Y"
                 YAxisName="MarginAxis"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line"
                 Width="2">
    <ChartMarker Visible="true" />
    </ChartSeries>
</SfChart>
```

`OpposedPosition="true"` puts the axis on the opposite side of the chart
(right vs left, top vs bottom). The same axis name pattern works for
secondary X axes.

## Strip lines (cross-axis bands)

Strip lines live inside the axis they belong to and **must** use the plural
child (see SKILL.md "Critical API rules" #2):

```razor
<ChartPrimaryYAxis>
    <ChartStriplines>
    <ChartStripline Start="80" End="100" Color="#C8E6C9" Text="Target met" />
    <ChartStripline Start="60" End="80"  Color="#FFF9C4" Text="Watch" />
    <ChartStripline Start="0"  End="60"  Color="#FFCDD2" Text="Below target" />
    </ChartStriplines>
</ChartPrimaryYAxis>
```

Strip lines are useful for highlighting SLA bands, time-of-day windows, or
threshold zones. They work on both X and Y axes.

## Best practices

1. **Set the `ValueType` explicitly** — relying on defaults creates
   categorical axes that "look sorted" but treat data incorrectly.
2. **Match `IntervalType` to your data** — DateTime axes default to auto and
   produce inconsistent labels.
3. **Set `Minimum` / `Maximum`** on at least one axis when the chart has
   thresholds or fixed scales (compare charts and trend targets).
4. **Prefer `RangePadding.Round` for monetary series** — avoid the "phantom
   zero issue" pulled in by `None`.
5. **Use `ChartStriplines` (plural) once per axis** — multiple striplines per
   axis are supported.
6. **Right-side axis for percentage metrics** — `OpposedPosition="true"`
   separates metrics with different units cleanly.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Labels overlap | `LabelIntersectAction="Rotate45"` or `Wrap` |
| "0" between negative and positive bars | `RangePadding="None"` instead of `Additional` |
| Strip line paints outside expected region | Used singular `<ChartStripline>` wrapper | Fix to `<ChartStriplines>` plural (also use `Syncfusion.Blazor.Toolkit.ZIndexPosition.Behind` if setting Z-order) |
| Date axis labels skip months | `IntervalType` not matching your data granularity |
| Numeric axis max isn't the value you set | Verify `RangePadding="None"`, otherwise the chart rounds up |
| Multi-axis series renders on the wrong axis | Missing `Name` on the secondary axis or wrong `YAxisName` on series |