# Interactive Features

> **Verified against source** — `ChartTooltipSettings`, `ChartZoomSettings`,
> `ChartSelectionMode`, `SelectionPattern`, and `ToolbarMode` verified
> against `src/Base/Enumeration.cs` and the corresponding
> `src/Components/Charts/Chart/UserInteractions/*.cs`. Last source
> audit: **2026-08-24**.

Tooltips, crosshair, trackball, selection, zoom, pan, and the related public
methods on `SfChart`. Each section leads with the property shape and finishes
with the public method that pairs with it.

> **Sample data** — see [`_includes/sample-data.md`](_includes/sample-data.md).
> This file's snippets bind to `Data : List<SeriesPoint>` where
> `SeriesPoint(string X, double Y)`.

## Table of contents

- Tooltip (single + shared, custom templates, render-event)
- Crosshair / trackball
- Selection (modes + patterns)
- Highlight
- Zoom and pan (mouse, selection, pinch, toolbar)
- Programmatic show/hide of tooltip + crosshair
- Common pitfalls

## Tooltip

```razor
<ChartTooltipSettings Enable="true" Shared="true"
                      Format="${series.name} : ${point.y}"
                      Header="Monthly Sales" />

<ChartEvents TooltipRender="OnTip" />

@code {
    void OnTip(TooltipRenderEventArgs a)
    {
        if (a.Data.Point.Y < 30) a.Text += " (low)";
    }
}
```

| Option | Effect |
|--------|--------|
| `Shared="true"` | One tool-tip aggregates all series at the hovered X |
| `Format` | Inline format string with template placeholders `${point.x}`/`${point.y}`/`${series.name}` (placeholder tokens are documented on `Format` only — `Header` is plain text) |
| `Template` | A `<RenderFragment>` for fully custom markup |
| `Enable="true"` | Required for any tooltip to render — there's no auto-show |

If `Shared` is `false` (default), only the *topmost* series' value appears at
the hover; multi-series users should set this `true` first.

## Crosshair

```razor
<ChartCrosshairSettings Enable="true" DashArray="2,3" LineType="Syncfusion.Blazor.Toolkit.LineType.Both">
    <ChartCrosshairLine Width="1.5" Color="#333" />
</ChartCrosshairSettings>

<ChartEvents OnSeriesRender="SeriesRender" />

@code {
    void SeriesRender(SeriesRenderEventArgs _) { /* indicate intersect per series */ }
}
```

`LineType` values: `Vertical`, `Horizontal`, `Both`. Reminder (also in
SKILL.md): `ChartCrosshairLine` only carries `Width` + `Color`.

### Show / hide programmatically

These are `Task`-returning methods (`*Async`) — always `await` them:

```razor
@code {
    SfChart ChartRef = default!;

    async Task Explore() =>
        await ChartRef.ShowCrosshairAsync(100, 50);   // pixel coords

    async Task Reset() => await ChartRef.HideCrosshairAsync();
}
```

## Selection

Selection is configured directly on `<SfChart>` (there is no
`<ChartSelectionSettings>` child component in this repo).

```razor
<SfChart @* SelectionMode lives in Syncfusion.Blazor.Toolkit *@
         SelectionMode="Syncfusion.Blazor.Toolkit.ChartSelectionMode.Point"
         SelectionPattern="Syncfusion.Blazor.Toolkit.SelectionPattern.Dots">
    <ChartSeries … />
</SfChart>

@code {
    SfChart ChartRef = default!;

    void Clear() => ChartRef.ClearSelection();
}
```

> The enum type is `Syncfusion.Blazor.Toolkit.ChartSelectionMode` (NOT
> `SelectionMode.Point`), per `src/Base/Enumeration.cs`. Qualify fully —
> the un-qualified short name only resolves when
`@using Syncfusion.Blazor.Toolkit;` is in the same file. Same rule applies
to `SelectionPattern`, `ZoomMode`, `ToolbarItems`, `ToolbarMode`, and
`HighlightMode` (all under `Syncfusion.Blazor.Toolkit`).

| `ChartSelectionMode` | What gets selected |
|-----------------------|--------------------|
| `None` (default) | — |
| `Series` | Whole series when one of its points is clicked |
| `Point` | Single point |
| `Cluster` | All points under the same category |
| `DragXY` / `DragX` / `DragY` | Rectangle-drag in either axis |
| `Lasso` | Freehand lasso |

`SelectionPattern` values: `None`, `Chessboard`, `Dots`, `DiagonalForward`,
`Crosshatch`, `Pacman`, `DiagonalBackward`, `Grid`, `Turquoise`, `Star`,
`Triangle`, `Circle`, `Tile`, `HorizontalDash`, `VerticalDash`,
`Rectangle`, `Box`, `VerticalStripe`, `HorizontalStripe`, `Bubble`
(20 members — full list in `references/api-reference.md`).

## Highlight

```razor
<SfChart HighlightMode="Syncfusion.Blazor.Toolkit.HighlightMode.Point" />
```

Set `HighlightMode` separately from `SelectionMode`. Highlights grey out
other series; selection marks only what's chosen.

## Zoom and pan

```razor
<ChartZoomSettings EnableSelectionZooming="true"
                   EnableMouseWheelZooming="true"
                   EnablePinchZooming="true"
                   EnablePan="true"
                   Mode="Syncfusion.Blazor.Toolkit.ZoomMode.X"
                   ToolbarDisplayMode="Syncfusion.Blazor.Toolkit.ToolbarMode.OnDemand"
                   ToolbarItems="@(new List<Syncfusion.Blazor.Toolkit.ToolbarItems> {
                       Syncfusion.Blazor.Toolkit.ToolbarItems.Zoom,
                       Syncfusion.Blazor.Toolkit.ToolbarItems.ZoomIn,
                       Syncfusion.Blazor.Toolkit.ToolbarItems.ZoomOut,
                       Syncfusion.Blazor.Toolkit.ToolbarItems.Pan,
                       Syncfusion.Blazor.Toolkit.ToolbarItems.Reset
                   })" />
```

| `ZoomMode` | Effect |
|-----------|--------|
| `X` | Horizontal only (default) |
| `Y` | Vertical only |
| `XY` | Both axes |

Toolbar item commands live on `Syncfusion.Blazor.Toolkit.ToolbarItems`
(verify with `src/Base/Enumeration.cs:1135`): `Zoom`, `ZoomIn`, `ZoomOut`,
`Pan`, `Reset`. Assign via `ChartZoomSettings.ToolbarItems` as
`List<Syncfusion.Blazor.Toolkit.ToolbarItems>`. Toolbar visibility is
driven by `Syncfusion.Blazor.Toolkit.ToolbarMode`
(`OnDemand` | `Always` | `None`) and is assigned to the **`ToolbarDisplayMode`**
parameter on `<ChartZoomSettings>` (NOT `ToolboxMode` — that name does
not exist; verified against `src/Components/Charts/Chart/UserInteractions/ChartZoomSettings.cs:261`).
There is **no `ToolBarCommand` enum** in this toolkit — pass `ToolText`
on the chart-level settings if you need a label override.

## Programmatic tooltip / crosshair / selection

```razor
@code {
    SfChart ChartRef = default!;

    async Task ShowAt() =>
        await ChartRef.ShowTooltipAsync("Mar", 34);   // data coords (xName, y)
    async Task Hide() => await ChartRef.HideTooltipAsync();

    void ResetSelection() => ChartRef.ClearSelection();   // sync, void

    async Task ShowCrosshair() => await ChartRef.ShowCrosshairAsync(300, 120);
    async Task HideCrosshair() => await ChartRef.HideCrosshairAsync();
}
```

Verified method surface (from `src/Components/Charts/Chart/SfChart.razor.Methods.cs`):

| Method | Returns | Notes |
|--------|---------|-------|
| `RefreshAsync(bool shouldAnimate = true)` | `Task` | |
| `ShowTooltipAsync(object, double, bool = true)` | `Task` | |
| `HideTooltipAsync()` | `Task` | |
| `ShowCrosshairAsync(double, double)` | `Task` | |
| `HideCrosshairAsync()` | `Task` | |
| `AddSeriesAsync(List<ChartSeries>)` | `Task` | `<exclude/>` — not in IntelliSense |
| `RemoveSeries(int)` / `ClearSeries()` | `void` | `<exclude/>` — not in IntelliSense |
| `RefreshLiveData()` | `void` | `<exclude/>` — not in IntelliSense |
| `Sort(string, ListSortDirection)` | `void` | |
| `ClearSort()` | `void` | |
| `ClearSelection()` | `void` | |
| `PreventRender(bool = true)` | `void` | |
| `RefreshLiveData()` | `void` | internal-only |

## Events — see `events.md`

`OnPointClick`, `OnSeriesClick`, `OnSelectionChanged`, `OnZoomStart`,
`OnZoomEnd`, `OnScrollChanged`, `TooltipRender`, `SharedTooltipRender` all
bind via `<ChartEvents … />`. Positioning and source lives in
`references/events.md`.

## Pitfalls

| Symptom | Likely cause |
|---------|--------------|
| Tooltip never appears | `Enable="false"` (default) — set `Enable="true"` |
| Multi-series tooltip only shows one row | Set `Shared="true"` |
| Crosshair child line not visible | Passing `DashArray` to `ChartCrosshairLine` (it doesn't accept it) |
| Zoom disables click | `EnableSelectionZooming="true"` swipes → click isn't a click any more; pick wheel or drag |
| Pan doesn't work | Missing `EnablePan="true"` on `<ChartZoomSettings>` |
| Toolbar buttons don't render | `ToolbarItems` is unset (or contains no members) |
| `ChartMouseClick` fires multiple times | Subscribed in both `ChartEvents` and code — choose one |