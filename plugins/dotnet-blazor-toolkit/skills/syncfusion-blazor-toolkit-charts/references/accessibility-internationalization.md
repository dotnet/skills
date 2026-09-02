# Accessibility & Internationalization

> **Verified against source** — `AccessibilityDescription` parameter,
> `AccessibilityRole` parameter, `Theme` enum, and the `EnableRtl` flag
> (read from the global service, **not** settable per chart) verified
> against `src/Components/Charts/Chart/SfChart.razor.Members.cs` and
> `src/Base/SyncfusionService.cs`. Last source audit: **2026-08-24**.

Covers (a) WAI-ARIA / keyboard / high-contrast / color-blind friendliness, and
(b) per-locale formatting, RTL, and custom resource loading for `SfChart`.

Only includes properties & techniques that are **actually wired** in the
`Syncfusion.Blazor.Toolkit.Charts` assembly. Anything invented in older docs
(chart-specific keyboard shortcuts, custom `TouchTarget` rules, etc.) is
deliberately omitted.

> **Sample data** — see [`_includes/sample-data.md`](_includes/sample-data.md).
> This file's snippets bind to `Data : List<CategoryValue>` where
> `CategoryValue(string Name, double Value)`.

## Table of contents

- Accessibility surface (`Title`, `AccessibilityDescription`, `AccessibilityRole`, `Theme`)
- Keyboard interaction (focus order, tooltip on focus)
- Color-blind palettes
- Internationalization (Locale, LabelFormat)
- RTL support
- Localization (`Syncfusion.Blazor.Resources`)
- Testing checklist

## Accessibility surface

```razor
<SfChart Title="Q4 Revenue"
         AccessibilityDescription="Bar chart showing Q4 revenue by region"
         AccessibilityRole="img">
    <ChartPrimaryXAxis Title="Region"
                       ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category">
    </ChartPrimaryXAxis>
    <ChartPrimaryYAxis Title="Revenue ($M)" LabelFormat="c0" />

    <ChartSeries DataSource="@Data" XName="Category" YName="Value"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Bar"
                 Name="Revenue" />
</SfChart>
```

- `Title` is exposed to assistive technology as the chart's accessible name.
- `AccessibilityDescription` provides a longer announcement if supported by
  the reader. (The property is **not** called `Description`; that name is
  reserved on `SfBaseComponent` for a different purpose.)
- `AccessibilityRole` overrides the implicit role on the chart container
  (default `null` → chart is treated as decorative). Set this when the
  chart replaces a table or conveys unique information.
- Axis `Title` is announced alongside the tick labels — always set both.

## Keyboard interaction

`SfChart` does **not** register custom keyboard shortcuts. Keyboard behavior
is restricted to:

- `Tab` into the chart container → focus the chart container.
- `Tab` again → focus any enabled sub-control (zoom toolbar buttons, legend
  toggle).
- `Enter` / `Space` → activate (e.g. zoom toolbar button, legend item).
- `Esc` → close any open tooltip or context menu.

If accessibility requires direct keyboard navigation **between data points**,
that's not built in. Either:

1. Add an off-screen `<table>` summary of the same data with the chart's role
   set to `presentation`, or
2. Render the data both as a chart *and* a semantic `<table>` and let users
   pick the view via a toggle button.

## Color-blind palettes

| Need | Approach |
|------|----------|
| Built-in light theme (default) | `Theme="Syncfusion.Blazor.Toolkit.Theme.Fluent"` |
| Built-in dark theme (the candidate for high-contrast application chrome) | `Theme="Syncfusion.Blazor.Toolkit.Theme.FluentDark"` |
| Custom safe palette | Pass `string[]` to `<SfChart Palettes>` — see `appearance-styling.md` for the recommended palette (Wong/Okabe-Ito). |
| Pattern fallback | Set `SelectionPattern="Syncfusion.Blazor.Toolkit.SelectionPattern.DiagonalForward"` (or `Dots`, `Crosshatch`, `Chessboard`, `Circle`, `Triangle`, etc.) — useful when the chart has selection. The 20-member `SelectionPattern` enum is verified in `src/Base/Enumeration.cs`. |

> **Older docs reference `Theme.HighContrast`, `Theme.Material`,
> `Theme.Bootstrap5`, `Theme.Tailwind`, etc.** None of those values exist
> in `Syncfusion.Blazor.Toolkit.Theme` — only `Fluent` and `FluentDark`
> ship. `Theme` is a (strongly typed) enum parameter, so a non-existent
> member **fails to compile** — the error surfaces immediately, not at
> runtime.

Default `Palettes=` is *not* color-blind safe. Replace it whenever the chart
displays a categorical comparison to user-facing readers.

## Internationalization

`SfChart` does **not** currently expose a `Locale` parameter. The toolkit
honors the host's `CultureInfo.CurrentCulture` automatically; configure
per-axis formatting via `LabelFormat`.

| Property | Effect |
|----------|--------|
| `<ChartPrimaryXAxis LabelFormat="…">` | Date and numeric formats (e.g. `"c2"`, `"dd MMM yyyy"`) |
| `<ChartPrimaryYAxis LabelFormat="…">` | Same as X axis — affects number / currency / compact formatting |
| Host's `CultureInfo.CurrentCulture` | Toggling the host culture re-formats axes automatically |

A standard pattern:

```razor
<SfChart Title="@Loc["Title"]">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartPrimaryYAxis LabelFormat="@numericFormat" />
</SfChart>

@code {
    private string numericFormat = "n0";   // picks up CurrentCulture
}
```

### Number / date / currency format examples

```razor
<!-- Plain number with thousands separator (en-US) -->
<ChartPrimaryYAxis LabelFormat="n0" />     <!-- 1,250,000 -->

<!-- Currency, two decimals (depends on CurrentCulture) -->
<ChartPrimaryYAxis LabelFormat="c2" />     <!-- $1,250,000.00 -->

<!-- Compact (1.2M) -->
<ChartPrimaryYAxis LabelFormat="n1" Minimum="0" />

<!-- DateTime axis -->
<ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.DateTime"
                   LabelFormat="MMM dd, yyyy"
                   IntervalType="IntervalType.Months" />
```

`LabelFormat` for `DateTime` accepts standard .NET format strings; the
separator and month names follow the host culture.

## RTL support

```razor
@* RTL is NOT a per-chart setting on SfChart. *@
@* The flag lives on the global options; set it once at startup: *@
@code {
    // Program.cs:
    // builder.Services.AddSyncfusionBlazorToolkit(o => o.EnableRtl = true);
}

<SfChart Title="مخطط المبيعات">
    <ChartPrimaryXAxis Title="المنتجات"
                       ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartPrimaryYAxis Title="المبيعات" LabelFormat="n0" />
    <ChartLegendSettings Visible="true" Position="LegendPosition.Top" />

    <ChartSeries DataSource="@Data" XName="Category" YName="Value"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Bar"
                 Name="المبيعات" />
</SfChart>
```

The `EnableRtl` flag on the global `SyncfusionBlazorToolkitOptions`
flips the chart container, axes, legend, and tooltips. It is **read
internally** by `SfChart.EnableRtl` (verified at
`src/Components/Charts/Chart/SfChart.razor.Members.cs:401`,
`internal bool EnableRtl => …`), so do not try to set it on
`<SfChart EnableRtl="true" />` — that parameter does not exist and the
compiler will reject it. Combined with setting the host's
`CultureInfo.CurrentCulture` to the target locale (Arabic, Hebrew, etc.)
so numeric/date formatting follows.

## Localization resource loading

For non-built-in locales, use the host application's standard localization
plumbing. `SfChart` does not expose a `Locale` parameter — it picks up the
host's `CurrentCulture` automatically:

```csharp
builder.Services.AddSyncfusionBlazorToolkit();
builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");
```

The satellite resource files ship with the toolkit under
`Syncfusion.Blazor.Resources.*.resx`. Pick the culture you need, add it as
`ResourcesPath`, then set both `CultureInfo.DefaultThreadCurrentCulture`
and `UICulture` to that name.

## Accessibility-Tuned Properties (verified at source)

These properties are public, real, and ship in the current toolkit. The
prior checklist treated them as out-of-scope — they're not. Use them
when shipping user-facing analytics.

| Property | Where | Effect | When to use |
|----------|-------|--------|-------------|
| `AccessibilityDescription` | `<SfChart>` | Long-form announcement for assistive tech | Replace a table you'll otherwise hide |
| `AccessibilityRole` | `<SfChart>` | WAI-ARIA role on the chart container (e.g. `"img"`, `"figure"`) | Default `null` treats the chart as decorative |
| `Title`        | `<SfChart>` | Short accessible name | Every chart |
| `ChartPrimaryXAxis Title` / `ChartPrimaryYAxis Title` | axis | Announces the variable mapping | Every axis |
| `ChartLegendSettings EnableHighlight="true"` | legend | On hover, the matching series is highlighted and others dim | Multi-series dashboards where discoverability matters |
| `ChartLegendSettings TabIndex="…"` | legend | Default `3`; lower it to push the legend earlier in tab order, raise it to skip | Page-level focus ordering |
| `ChartLegendSettings Width` / `Height` (px) | legend | Constrains legend size so paging kicks in when series grow | > 6 series |
| `Palettes="@CvdSafePalette"` | chart | Replaces the default palette | When the chart is used by users with CVD |
| `SelectionPattern="…DiagonalForward"` (or `Dots`, `Crosshatch`, etc.) | chart | Replaces color-only encoding with a fill pattern | When selection may otherwise be invisible to colorblind users |

The accessibility surface for axis labels is largely delegated to the
axis `Title`. The chart does not expose per-axis `aria-*` props — host
your chart inside a labelled container (`<figure><figcaption>…</figcaption>…</figure>`
or `<section aria-labelledby="chart-id">…</section>`) if you need a
documented label.

## Testing checklist

### Mandatory (baseline)

- [ ] Every chart has a non-empty `Title`.
- [ ] Axis titles exist for X and Y.
- [ ] `AccessibilityDescription` is set when the chart replaces a table.
- [ ] `LabelFormat` produces locale-correct output.
- [ ] Pick a `Theme` that matches host intent (`Fluent` for light, `FluentDark`
      for dark) OR override `Palettes` with a color-blind safe palette.
- [ ] When RTL: set `options.EnableRtl = true` at toolkit registration
      AND host `CultureInfo.CurrentCulture` match.
- [ ] Keyboard `Tab` reaches the chart and any toggleable bits in expected
      order; `Esc` dismisses any open tooltip.
- [ ] bUnit snapshot includes `aria-label` / `role` if declared on the
      container.

### Recommended for user-facing analytics

- [ ] Multi-series charts set `ChartLegendSettings EnableHighlight="true"`
      so keyboard / hover discoverability is provided.
- [ ] Tab order is reviewed: if the legend usually comes first in the
      visual flow, lower `ChartLegendSettings TabIndex` (default `3`); if
      it should be skipped, raise it.
- [ ] For charts with `> 6` series, constrain legend `Width`/`Height` so
      paging kicks in instead of overflowing.
- [ ] Color-blind safe `Palettes` are configured (default palette is **not**
      CVD-friendly — use Wong/Okabe-Ito or pause to ask the design team).
- [ ] If selection encodes meaning (filters / drill-down candidates),
      set a `SelectionPattern` so the encoding survives grayscale or
      color-blind rendering.

**What this reference deliberately omits** (no-op in this toolkit): custom
keyboard shortcut customization, `TouchTarget` size hints. Don't add these
— they'd be hallucinated APIs the agent will patch into a build that
never had them.