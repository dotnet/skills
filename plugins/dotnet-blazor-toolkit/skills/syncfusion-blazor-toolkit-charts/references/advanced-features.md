# Advanced Features

> **Verified against source** — verified against
> `src/Components/Charts/Chart/SfChart.razor.Methods.cs`,
> `src/Components/Charts/Chart/StriplineRenderer/`,
> `src/Components/Charts/Chart/UserInteractions/`. Last source audit:
> **2026-08-24**.

Covers multi-pane layouts, trend lines, strip lines,
multiple axes, data editing, empty points, RTL, and notes that print
and export are **not implemented** in this toolkit. Each section links
out to the place it inherits from — this file focuses on **what's
specific** to each feature.

> **Sample data** — see [`_includes/sample-data.md`](_includes/sample-data.md).
> This file declares `SamplePoint` (string X, double Y, double Y2 = 0)
> and `FinancePoint` records; snippets bind to the `Data` list of
> `SamplePoint`.

## Table of contents

- Multiple panes (rows / columns)
- Trend lines (Linear, Polynomial, Exponential, Logarithmic, Power, Moving-Avg) + forecasting
- Strip lines (see also `axes-and-scales.md`)
- Multiple axes (see also `axes-and-scales.md`)
- Data editing
- Empty points
- RTL (configured via global `options.EnableRtl`)

## Multiple panes — split the chart into rows or columns

Use `<ChartRows>` for horizontal split, `<ChartColumns>` for vertical.
Series bound to a pane use `<ChartAxis … RowIndex="N" />` in `<ChartAxes>`.

```razor
<SfChart Title="Temp + Rain">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />

    <ChartRows>
    <ChartRow Height="60%" />
    <ChartRow Height="40%" />
    </ChartRows>

    <ChartAxes>
    <ChartAxis Name="Rain" OpposedPosition="true"
                   RowIndex="1" Minimum="0" Maximum="200"
                   Title="Rain (mm)" />
    </ChartAxes>

    <ChartSeries DataSource="@Data" XName="X" YName="Y"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" />

    <ChartSeries DataSource="@Data" XName="X" YName="Y2"
                 YAxisName="Rain"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line"
                 Width="2" />
</SfChart>
```

`<ChartColumns>` works the same way with `ColumnIndex` instead of
`RowIndex`. Percentages in `Height` / `Width` resolve against the chart
area, not the page.

## Trend lines

`<ChartTrendlines>` is required (plural). It lives inside the *series*.

```razor
<ChartSeries DataSource="@Data" XName="X" YName="Y"
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Scatter">
    <ChartMarker Visible="true" />
    <ChartTrendlines>
    <ChartTrendline Type="Syncfusion.Blazor.Toolkit.TrendlineTypes.Linear" Width="3"
                        Name="Linear fit" Fill="#FF6B6B" />
    <ChartTrendline Type="Syncfusion.Blazor.Toolkit.TrendlineTypes.Polynomial" Width="3"
                        PolynomialOrder="3"
                        Name="Poly-3" Fill="#4ECDC4" />
    <ChartTrendline Type="Syncfusion.Blazor.Toolkit.TrendlineTypes.Exponential" Width="3" />
    <ChartTrendline Type="Syncfusion.Blazor.Toolkit.TrendlineTypes.Logarithmic" Width="3" />
    <ChartTrendline Type="Syncfusion.Blazor.Toolkit.TrendlineTypes.MovingAverage" Width="3"
                        Period="5" />
    </ChartTrendlines>
</ChartSeries>
```

Add `ForwardForecast="N" BackwardForecast="M"` to extrapolate outside the
data range.

## Strip lines — see `axes-and-scales.md`

Quick form:

```razor
<ChartPrimaryYAxis>
    <ChartStriplines>
    <ChartStripline Start="0"  End="60" Color="#FFCDD2" ZIndex="Syncfusion.Blazor.Toolkit.ZIndexPosition.Behind" />
    <ChartStripline Start="60" End="80" Color="#FFF9C4" ZIndex="Syncfusion.Blazor.Toolkit.ZIndexPosition.Behind" />
    <ChartStripline Start="80" End="100" Color="#C8E6C9" ZIndex="Syncfusion.Blazor.Toolkit.ZIndexPosition.Behind" />
    </ChartStriplines>
</ChartPrimaryYAxis>
```

`ZIndex="Syncfusion.Blazor.Toolkit.ZIndexPosition.Behind"` paints the strip line below the data
(`ZIndexPosition` lives in `Syncfusion.Blazor.Toolkit`). Strip lines
work on both `X` and `Y` axes. See `axes-and-scales.md` for `IsSegmented`,
custom text, and tooltip-enabled striplines.

## Multiple axes — see `axes-and-scales.md`

Quick form:

```razor
<ChartAxes>
    <ChartAxis Name="Margin" OpposedPosition="true"
               Title="Margin" LabelFormat="{value}%" />
</ChartAxes>
<ChartSeries … YAxisName="Margin" />
```

See `axes-and-scales.md` for axis-range, label-format, and naming rules.

## Data editing

Enables drag-to-edit on each point of a series. Bind `OnDataEdit` /
`OnDataEditCompleted` (see `events.md`).

```razor
<ChartSeries DataSource="@Data" XName="X" YName="Y"
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
    <ChartMarker Visible="true" />
    <ChartDataEditSettings Enable="true" Fill="#FF6B6B"
                           MinY="0" MaxY="100" />
</ChartSeries>
```

Always set `MinY` / `MaxY` to the same scale as your Y axis or you'd end up
with a dragged bar outside the visible chart area.

## Empty points

Inside a series:

```razor
<ChartSeries DataSource="@DataWithNulls" XName="X" YName="Y">
    <ChartEmptyPointSettings Mode="Syncfusion.Blazor.Toolkit.EmptyPointMode.Gap" Fill="#BDBDBD" />
</ChartSeries>
```

| `EmptyPointMode` | Effect |
|------------------|--------|
| `Zero` | null becomes 0 (only reasonable when interpretation is numeric) |
| `Average` | interpolates between neighbours |
| `Gap` | leaves a hole in line/area charts |
| `Drop` | drops the point entirely |

## RTL

```razor
@* RTL is configured globally at toolkit registration, not per-chart. *@
@* Program.cs: *@
@* builder.Services.AddSyncfusionBlazorToolkit(o => o.EnableRtl = true); *@

<SfChart Title="مخطط المبيعات">
    <ChartLegendSettings Position="Syncfusion.Blazor.Toolkit.LegendPosition.Right" />
    …
</SfChart>
```

The `EnableRtl` flag on `SyncfusionBlazorToolkitOptions` flips the chart
direction (axes, legend, tooltips). It is **read internally** by
`SfChart.EnableRtl` (verified at
`src/Components/Charts/Chart/SfChart.razor.Members.cs:401`,
`internal bool EnableRtl => …`), so do not put `EnableRtl="true"` on
`<SfChart>` — that parameter does not exist. To localize the labels,
also set the host's `CultureInfo.CurrentCulture` to the target locale;
the chart picks it up. See `accessibility-internationalization.md` for the
full i18n setup.

## Common implementation patterns

| Pattern | Use it for | Read |
|---------|-----------|------|
| Financial dashboard | Ticker + indicator (MACD/Bollinger) | Indicators section above |
| Performance monitor | Metric + threshold strip lines | Strip lines + `axes-and-scales.md` |
| Sales by region (multi-axis) | Two metrics, different units | Multiple axes section above |
| Live updating chart | Streaming feed | `data-handling.md` (real-time updates) |