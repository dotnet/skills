# Events

> **Verified against source** — the events listed below are the ones
> surfaced by `<ChartEvents>` in this toolkit. Last source audit:
> **2026-08-24**.

All chart-scoped events subscribe via `<ChartEvents>`, which is a **child of
`<SfChart>`** (never of an axis). Per-axis callbacks like
`OnAxisLabelRender`, `OnAxisMultiLevelLabelRender`, and
`OnAxisActualRangeCalculated` all live on this same root-level
`<ChartEvents>` — they are *not* nested under `<ChartPrimaryXAxis>` or
`<ChartPrimaryYAxis>`. Verified at
`src/Components/Charts/Chart/ChartEvent/ChartEvents.razor.cs:38, 367, 674,
891` and the dispatcher in
`src/Components/Charts/Chart/Renderer/AxisRenderer/ChartAxisRenderer.cs:1198`.

> **Sample data** — see [`_includes/sample-data.md`](_includes/sample-data.md).
> This file's snippets bind to `Data : List<SeriesPoint>` where
> `SeriesPoint(string X, double Y)`.

This reference only lists events that are **wired** in the
`Syncfusion.Blazor.Toolkit.Charts` assembly. Older docs sometimes list
`ChartMouseDown` / `ChartMouseUp` handlers — those are not exposed at the
chart component level.

## Table of contents

- Lifecycle (`Loaded`, `SizeChanged`)
- Mouse / touch (`ChartMouseMove`, `ChartMouseClick`, `ChartMouseLeave`,
  `OnPointClick`)
- Rendering (`OnPointRender`, `OnSeriesRender`, `OnDataLabelRender`,
  `OnAxisLabelRender`, `OnLegendItemRender`, `OnAxisActualRangeCalculated`,
  `OnAxisMultiLevelLabelRender`)
- Selection / legend / data-edit
- Zoom / scroll
- Tooltip (`TooltipRender`, `SharedTooltipRender`)
- Export / print

All rendering event handlers mutate `args` properties (`args.Fill`,
`args.Text`, …) and return; they cannot raise callbacks into Blazor.

## Lifecycle

```razor
<SfChart>
    <ChartEvents Loaded="OnLoaded" SizeChanged="OnSizeChanged" />
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartSeries DataSource="@Data" XName="X" YName="Y"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" />
</SfChart>

@code {
    void OnLoaded(LoadedEventArgs _) { /* render-ready hook */ }

    void OnSizeChanged(ResizeEventArgs args)
    {
        Console.WriteLine($"size -> {args.CurrentSize.Width}x{args.CurrentSize.Height}");
    }
}
```

## Mouse / touch

```razor
<ChartEvents ChartMouseMove="OnMove"
             ChartMouseClick="OnClick"
             ChartMouseLeave="OnLeave"
             OnPointClick="OnPoint" />

@code {
    void OnMove(ChartMouseEventArgs a)  { /* MouseX, MouseY */ }
    void OnClick(ChartMouseEventArgs a) { /* */ }
    void OnLeave(ChartMouseEventArgs a) { /* */ }
    void OnPoint(PointEventArgs a)
    {
        // a.PointIndex, a.SeriesIndex, a.Point.X, a.Point.Y
    }
}
```

`ChartMouseMove` triggers on every move; do not run heavy work in the
handler — debounce with `InvokeAsync(StateHasChanged)` if you call.

## Rendering customization

```razor
<ChartEvents OnSeriesRender="SeriesRender"
             OnPointRender="PointRender"
             OnDataLabelRender="DataLabelRender"
             OnLegendItemRender="LegendRender" />

@code {
    void SeriesRender(SeriesRenderEventArgs a)
    {
        if (a.Series.Name == "Sales") a.Fill = "#FF4081";
    }

    void PointRender(PointRenderEventArgs a)
    {
        a.Fill = (a.Point.Index % 2 != 0) ? "#ff6347" : "#009cb8";
    }

    void DataLabelRender(TextRenderEventArgs a)
    {
        if (a.Point.Y > 50) a.Font.Color = "green";
    }

    void LegendRender(LegendRenderEventArgs a)
    {
        a.Shape = Syncfusion.Blazor.Toolkit.ChartShape.Circle;
    }
}
```

| Use case | Render handler | Modify |
|----------|----------------|--------|
| Per-series color | `OnSeriesRender` | `args.Fill`, `args.Opacity`, `args.Width` |
| Per-point color | `OnPointRender` | `args.Fill`, `args.Border.Color/Width` |
| Per-label text | `OnDataLabelRender` | `args.Text`, `args.Font.*` |
| Per-axis-label | `OnAxisLabelRender` | `args.Text`, `args.LabelStyle` |
| Per-legend item | `OnLegendItemRender` | `args.Text`, `args.Shape`, `args.Fill` |

These run during render, not as separate dispatches — keep them pure and
allocation-light.

## Selection / legend / data-edit

```razor
<ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />

<ChartLegendSettings Visible="true" />

@* All of these go on a single root-level <ChartEvents> block: *@
<ChartEvents OnSelectionChanged="SelectionChanged"
             OnAxisLabelClick="AxisLabelClick"
             OnLegendClick="LegendClick"
             OnDataEdit="OnEdit"
             OnDataEditCompleted="OnEditDone" />

@code {
    void SelectionChanged(SelectionChangedEventArgs a)
    {
        // a.SelectedData, a.SelectedIndexes
    }

    void LegendClick(LegendClickEventArgs a)   { /* toggle series */ }
    void AxisLabelClick(AxisLabelClickEventArgs a) { /* */ }

    void OnEdit(DataEditingEventArgs a)     { /* drag in progress */ }
    void OnEditDone(DataEditingEventArgs a) { /* drag finished */ }
}
```

`DataEditingEventArgs` is what you want for drag-to-edit pointers (see
`references/advanced-features.md` for the `ChartDataEditSettings` that
enables them).

## Zoom / scroll

```razor
<ChartZoomSettings EnableSelectionZooming="true"
                   EnableMouseWheelZooming="true" />

<ChartEvents OnZoomStart="OnZoomStart"
             OnZoomEnd="OnZoomEnd"
             OnScrollChanged="OnScroll" />

@code {
    void OnZoomStart(ZoomEventArgs a)    { /* before rectangle drawn */ }
    void OnZoomEnd(ZoomEventArgs a)      { /* committed zoom range */ }
    void OnScroll(ScrollEventArgs a)     { /* panning */ }
}
```

`OnScrollChanged` only fires when a `ChartZoomSettings.EnablePan="true"` is
set; without that flag the chart has no scroll surface.

## Tooltip

```razor
<ChartEvents TooltipRender="TipRender"
             SharedTooltipRender="SharedTipRender" />

@code {
    void TipRender(TooltipRenderEventArgs a)
    {
        a.Text = $"Series {a.Data.SeriesName}: {a.Data.Point.Y:0.##}";
    }

    void SharedTipRender(TooltipRenderEventArgs a)
    {
        a.HeaderText = $"{a.Data.Point.X} (combined)";
    }
}
```

Both render args expose `Data.Point` (X / Y from the source) and
`Data.SeriesName`. Mutating `Text` overrides the auto-format.

## Handler-mode gotchas

1. All rendering event handlers *must* mutate `args` (or be no-op); they
   don't return values into the chart.
2. Handlers that call out to Blazor state (e.g. `myField = "x"`) need an
   explicit `InvokeAsync(StateHasChanged)` if downstream UI depends on it.
3. Don't subscribe `OnPointClick` *and* `ChartMouseClick` to do the same
   thing — `OnPointClick` only fires when a real point was clicked, and
   the general click handler fires more frequently; using both produces
   duplicate logic.
4. All events — including per-axis callbacks like `OnAxisLabelRender`,
   `OnAxisMultiLevelLabelRender`, and `OnAxisActualRangeCalculated` —
   live on a single root-level `<ChartEvents>` block. Putting
   `<ChartEvents>` inside `<ChartPrimaryXAxis>` is a common typo and
   will not compile (the axis's `[Parameter]` surface does not
   accept a `ChartEvents` child).
5. The render handlers above never own thresholds or scale — keep business
   logic in `OnChartMouseClick` / `OnSelectionChanged`, *not* in
   `OnPointRender` (which runs once per point per render).