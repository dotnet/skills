# Appearance & Styling

> **Verified against source** — `Theme` enum, `Palettes` parameter,
> animation and chart-area wiring verified against
> `src/Base/Enumeration.cs` and
> `src/Components/Charts/Chart/SfChart.razor.Members.cs`. Last source
> audit: **2026-08-24**.

Covers dimensions, colors, background, borders, margins, title/subtitle,
themes, series styling, and responsive sizing for `SfChart`.

> **Convention for every snippet in this reference.** Add the namespace
> once and reuse a single sample data set:
>
> ```razor
> @using Syncfusion.Blazor.Toolkit.Charts
>
> @code {
>     // One record covers most snippets; pick one of:
>     public record CategoryValue(string Name, double Value);
>     public record MonthSeries(string Month, double Sales, double Target = 0, int Count = 0);
>
>     private readonly List<CategoryValue> SampleData = new()
>     {
>         new("Jan", 35), new("Feb", 28), new("Mar", 34), new("Apr", 32),
>         new("May", 40), new("Jun", 32)
>     };
>
>     private readonly List<MonthSeries> DashboardData = new()
>     {
>         new("Jan", 35000, 40000), new("Feb", 42000, 40000),
>         new("Mar", 38000, 40000), new("Apr", 45000, 45000)
>     };
>
>     private string[] CorporatePalette = new[] { "#1976D2", "#388E3C", "#F57C00", "#7B1FA2" };
>     private string[] CvdSafePalette   = new[] { "#0173B2", "#DE8F05", "#029E73", "#CA9161" };
> }
> ```
>
> Replace `DataSource="@SampleData"` with `@DashboardData` and adapt
> `XName` / `YName` as needed. Treat all snippets in this file as
> templates — paste, then change the data class name.

## Table of contents

- Dimensions (pixels, %, container)
- Color customization (palette, per-series, point-level)
- Background & border (chart, area, margin)
- Title & subtitle styling
- Themes (built-in + custom palette)
- Series styling (fill, opacity, border, animation)
- Responsive sizing
- Best practices

## Dimensions

```razor
<!-- 1. Pixel -->
<SfChart Width="800px" Height="400px">…</SfChart>

<!-- 2. Percentage (parent must have height) -->
<SfChart Width="80%" Height="90%">…</SfChart>

<!-- 3. Sized container -->
<div style="height: 500px"><SfChart Width="100%" Height="100%">…</SfChart></div>
```

Default is **600×450px**; collapse to zero if you forget the parent height.

## Color customization

```razor
<!-- a) Palette applies in order to series -->
<SfChart Palettes="@CorporatePalette">…</SfChart>

<!-- b) Per-series Fill overrides palette -->
<ChartSeries Fill="#00bdae" … />

<!-- c) Point-level map (different color per row) -->
<ChartSeries PointColorMapping="Color" … />
```

`PointColorMapping` expects a *string* property on each row holding any CSS color.

## Background, border, area, margin

```razor
<SfChart Background="#FFFFFF">
    <ChartBorder Color="#4A90E2" Width="3" />
    <ChartMargin Left="50" Right="50" Top="60" Bottom="40" />
    <ChartArea Background="#F0F8FF" Width="85%">
    <ChartAreaBorder Color="#4169E1" Width="1" />
    </ChartArea>
    …
</SfChart>
```

`Width`/`Height` of `ChartArea` are **percent of the inner plot area**, not of
the whole chart. Use `ChartMargin` to leaves room for title/legend.

## Title & subtitle

```razor
<SfChart Title="Annual Revenue Report" SubTitle="FY2024">
    <ChartTitleStyle Size="22px" Color="#1565C0" FontWeight="600" FontFamily="Segoe UI" />
    <ChartSubTitleStyle Size="14px" Color="#757575" FontStyle="italic" />

    <ChartTitleStyle Position="Syncfusion.Blazor.Toolkit.ChartTitlePosition.Bottom" />
</SfChart>
```

`ChartTitlePosition` values: `Top`, `Bottom`. Use subclass `ChartTitleFont` /
`ChartSubTitleFont` for **legacy** examples in older docs; new code uses the
*Style child above.

## Themes (`SfChart.Theme` parameter)

`SfChart` themes are an **in-C# enum**, declared in
`src/Base/Enumeration.cs`. They are a `SfChart` parameter, not a CSS layer —
do not generate a separate SCSS file for the chart. The enum currently
exposes **two values**:

| Value | Visual | When to use |
|---|---|---|
| `Syncfusion.Blazor.Toolkit.Theme.Fluent` *(default)* | Light background, dark text, neutral accents | Light-mode app, default look |
| `Syncfusion.Blazor.Toolkit.Theme.FluentDark` | Dark background, light text, adjusted accents | Dark-mode app, dark-mode toggle |

> **Older docs reference `Theme.Material`, `Theme.Bootstrap5`,
> `Theme.Tailwind`, or `Theme.HighContrast`.** None of those values exist
> in `Syncfusion.Blazor.Toolkit.Theme` — and since `Theme` is a strongly
> typed enum parameter, an unrecognized member **fails to compile**
> rather than slipping through. Stick to `Fluent` / `FluentDark`.

```razor
<SfChart Theme="Syncfusion.Blazor.Toolkit.Theme.FluentDark">…</SfChart>
```

### Runtime theme switching

```razor
<SfChart Theme="@(_isDark ? Syncfusion.Blazor.Toolkit.Theme.FluentDark
                          : Syncfusion.Blazor.Toolkit.Theme.Fluent)">
    …
</SfChart>
```

Pair `_isDark` with your host app's dark-mode signal. `Theme` re-renders in
place — no `RefreshAsync` required.

### Why NO service registration is needed for themes

The toolkit is registered once at app startup via
`builder.Services.AddSyncfusionBlazorToolkit()` (blazor/Startup — see
`getting-started.md`). `Theme` does **not** require any separate
`AddSyncfusionBlazor()` call. Older guidance pointing you at that call is
for the legacy `Syncfusion.Blazor` product, not for
`Syncfusion.Blazor.Toolkit`.

### Custom theme = start from a built-in + override `Palettes`

`Theme` shapes background, text, grid lines, and axis/label colors. The
series palette is **separate** and is overridden by passing `Palettes` on
`<SfChart>` — applies in order to series, *over and above* `Theme`. There is
no CSS-variable layer you can poke from the host page.

```razor
<SfChart Theme="Syncfusion.Blazor.Toolkit.Theme.Fluent"
         Palettes="@CorporatePalette">…</SfChart>
```

Use `PointColorMapping` to override per-data-point (string property on the
row holding any CSS color). Use `Fill` on `<ChartSeries>` to override for a
single series.

## Series styling

```razor
<ChartSeries Fill="#4A90E2" Opacity="0.8" Type="…Column">
    <ChartSeriesBorder Color="#2E5C8A" Width="2" />
    <ChartSeriesAnimation Enable="true" Duration="1500" Delay="200" />
</ChartSeries>
```

- `Opacity` 0–1 (rendered, not paint). Same opacity affects both line fill
  and series border.
- `ChartSeriesAnimation` is per-series, not per-chart.

## Responsive sizing

```razor
<div class="chart-shell" style="height: clamp(280px, 60vh, 600px);">
    <SfChart Width="100%" Height="100%">…</SfChart>
</div>
```

For multi-breakpoint logic, render a `JSInterop` measurement and re-render
explicitly: read
`/memories/repo/` project conventions for the resize pattern, or compute
`Height` from the host's `ElementReference` (the agent should look up its
project's resize helper, not invent one).

## Best practices

| Area | Recommendation |
|------|----------------|
| Palette size | 5–7 distinct colors max; reuse the same palette across all charts in a view |
| Fonts | Axis labels ≥ 12px; titles ≥ 18px; ensure WCAG AA contrast on label/bg pairs |
| Grid lines | Use the chart area's default grid; only enable minor grid when axes are numeric with sub-intervals |
| Color blindness | Default to `CvdSafePalette` (above) when the chart is user-facing analytics |
| Animation | Disable when dataset > 1,000 points (`ChartSeriesAnimation Enable="false"`) |
| Margin | Default 30–50 px each side; increase RHS if you have a vertical axis title that wraps |
| Fonts | Avoid `inherit`; the chart sets the font on its root and the axis labels override only when needed |

## Complete styling example

```razor
<SfChart Title="Complete Styling Example"
         SubTitle="Combining multiple appearance features"
         Width="100%" Height="450px"
         Background="#FFFFFF"
         Palettes="@CorporatePalette">
    <ChartBorder Color="#2196F3" Width="2" />
    <ChartMargin Top="70" Bottom="50" />
    <ChartArea Background="#F5F5F5" Width="85%">
    <ChartAreaBorder Color="#BDBDBD" Width="1" />
    </ChartArea>
    <ChartTitleStyle Size="22px" Color="#1565C0" FontWeight="600" />
    <ChartSubTitleStyle Size="14px" Color="#757575" FontStyle="italic" />

    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />

    <ChartSeries DataSource="@DashboardData" XName="Month" YName="Sales"
                 Name="Sales"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column"
                 Opacity="0.9">
    <ChartSeriesBorder Color="#1976D2" Width="1" />
    <ChartSeriesAnimation Duration="1500" />
    </ChartSeries>
    <ChartSeries DataSource="@DashboardData" XName="Month" YName="Target"
                 Name="Target"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line" Width="3">
    <ChartSeriesAnimation Duration="1500" Delay="200" />
    </ChartSeries>

    <ChartLegendSettings Visible="true" Position="LegendPosition.Bottom" />
</SfChart>
```

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Chart height is 0 | Parent of `<SfChart>` doesn't have a defined height |
| Background doesn't fill the chart | Use `<ChartArea Background>` not the body background |
| Title font not applying | Use `<ChartTitleStyle>` (modern) not `<ChartTitleFont>` (legacy) |
| Palette is ignored | `Palettes` length < number of series → fallback to default fills |
| Animation replay on every render | Disable animation with `Duration="0"` or `Enable="false"` |