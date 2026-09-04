# Syncfusion Blazor Charts - Complete API Reference

> **Verified against source** — when this and the source code diverge,
> **source wins**. Enum members verified against
> `src/Base/Enumeration.cs`. Method signatures verified against
> `src/Components/Charts/Chart/SfChart.razor.Methods.cs`. Last source
> audit: **2026-08-24**.

## Table of Contents

- [Overview](#overview)
- [SfChart Component](#sfchart-component)
   - [Public Methods](#public-methods)
- [Enumerations](#enumerations)
   - [ChartSeriesType](#chartseriestype)
   - [ValueType](#valuetype)
   - [SelectionMode](#selectionmode)
   - [HighlightMode](#highlightmode)
   - [SelectionPattern](#selectionpattern)
   - [LegendPosition](#legendposition)
   - [EmptyPointMode](#emptypointmode)
   - [LabelPlacement](#labelplacement)
   - [EdgeLabelPlacement](#edgelabelplacement)
   - [LabelIntersectAction](#labelintersectaction)
   - [ChartShape](#chartshape)
   - [TrendlineTypes](#trendlinetypes)
   - [ZoomMode](#zoommode)
   - [ToolbarItems](#toolbaritems)
   - [Theme](#theme)
- [Key Classes and Components](#key-classes-and-components)
   - [ChartSeries](#chartseries)
   - [ChartPrimaryXAxis / ChartPrimaryYAxis](#chartprimaryxaxis-chartprimaryyaxis)
   - [ChartTooltipSettings](#charttooltipsettings)
   - [ChartLegendSettings](#chartlegendsettings)
   - [ChartZoomSettings](#chartzoomsettings)
- [Important Notes](#important-notes)
- [Common Patterns](#common-patterns)
   - [Basic Chart with Data](#basic-chart-with-data)
   - [Chart with Multiple Series](#chart-with-multiple-series)
   - [Chart with Zooming](#chart-with-zooming)


## Overview

This document provides the complete and accurate API reference for Syncfusion Blazor Charts based on the official Syncfusion API documentation. Use this as the authoritative source for all enum values, method signatures, and property names.

---

## SfChart Component

### Public Methods

The `SfChart` component provides the following public methods for programmatic control:

#### RefreshAsync(bool shouldAnimate = true)

Re-renders the chart with optional animation.

```csharp
public Task RefreshAsync(bool shouldAnimate = true)
```

**Parameters:**
- `shouldAnimate` - Specifies whether the chart should animate during refresh (default: true)

**Returns:** `Task`

**Example:**
```razor
<SfChart @ref="ChartRef">
    <!-- Chart configuration -->
</SfChart>

@code {
    SfChart ChartRef;
    
    async Task UpdateChart()
    {
        await ChartRef.RefreshAsync(true);
    }
}
```

---





#### ShowTooltipAsync(object x, double y, bool isPoint = true)

Displays tooltip at specified coordinates or data points.

```csharp
public Task ShowTooltipAsync(object x, double y, bool isPoint = true)
```

**Parameters:**
- `x` - X-value of the point or x-coordinate
- `y` - Y-value of the point or y-coordinate
- `isPoint` - Whether x and y are data points (true) or coordinates (false). Default `true`.

**Returns:** `Task`

**Example:**
```razor
<SfChart @ref="ChartRef">
    <ChartTooltipSettings Enable="true" />
    <!-- Chart configuration -->
</SfChart>

@code {
    SfChart ChartRef = default!;

    async Task DisplayTooltip()
    {
        await ChartRef.ShowTooltipAsync("January", 35);
    }
}
```

---

#### HideTooltipAsync()

Hides the currently displayed tooltip.

```csharp
public Task HideTooltipAsync()
```

**Example:**
```razor
@code {
    SfChart ChartRef = default!;

    async Task HideChartTooltip() => await ChartRef.HideTooltipAsync();
}
```

---

#### ShowCrosshairAsync(double x, double y)

Displays crosshair at specified coordinates.

```csharp
public Task ShowCrosshairAsync(double x, double y)
```

**Parameters:**
- `x` - X-coordinate on the chart (pixels)
- `y` - Y-coordinate on the chart (pixels)

**Returns:** `Task`

**Example:**
```razor
<SfChart @ref="ChartRef">
    <ChartCrosshairSettings Enable="true" />
    <!-- Chart configuration -->
</SfChart>

@code {
    SfChart ChartRef = default!;

    async Task DisplayCrosshair()
    {
        await ChartRef.ShowCrosshairAsync(100, 50);
    }
}
```

---

#### HideCrosshairAsync()

Hides the currently displayed crosshair.

```csharp
public Task HideCrosshairAsync()
```

**Example:**
```razor
@code {
    SfChart ChartRef = default!;

    async Task HideChartCrosshair() => await ChartRef.HideCrosshairAsync();
}
```

---

#### ClearSelection()

Clears all selections in the chart.

```csharp
public void ClearSelection()
```

**Example:**
```razor
<SfChart @ref="ChartRef" SelectionMode="Syncfusion.Blazor.Toolkit.ChartSelectionMode.Point">
    <!-- Chart configuration -->
</SfChart>

@code {
    SfChart ChartRef = default!;

    void ClearAllSelections() => ChartRef.ClearSelection();
}
```

---

#### Sort(string propertyName, Syncfusion.Blazor.Toolkit.Data.ListSortDirection direction)

Sorts chart data by property name and direction.

```csharp
// Verified at src/Components/Charts/Chart/SfChart.razor.Methods.cs
public void Sort(string propertyName, Syncfusion.Blazor.Toolkit.Data.ListSortDirection direction)
```

> **Don't** use `System.ComponentModel.ListSortDirection`; it is not
> assignable. The toolkit ships its own `Syncfusion.Blazor.Toolkit.Data.ListSortDirection`.

**Parameters:**
- `propertyName` - Property name to sort by
- `direction` - Sort direction (`Ascending` or `Descending`)

**Example:**
```razor
<SfChart @ref="ChartRef" DataSource="@SalesData">
    <ChartSorting PropertyName="X" Direction="ListSortDirection.Ascending" />
    <!-- Chart configuration -->
</SfChart>

@code {
    SfChart ChartRef;
    
    void SortByValue()
    {
        ChartRef.Sort("Y", Syncfusion.Blazor.Toolkit.Data.ListSortDirection.Descending);
    }
}
```

---

#### ClearSort()

Clears the sorting applied to the chart.

```csharp
public void ClearSort()
```

**Example:**
```razor
@code {
    SfChart ChartRef;
    
    void RemoveSorting()
    {
        ChartRef.ClearSort();
    }
}
```

---

#### PreventRender(bool preventRender = true)

Prevents or allows chart rendering.

```csharp
public void PreventRender(bool preventRender = true)
```

**Parameters:**
- `preventRender` - If true, prevents rendering; if false, allows rendering

**Example:**
```razor
@code {
    SfChart ChartRef;
    
    void StopRendering()
    {
        ChartRef.PreventRender(true);
        // Perform batch updates
        ChartRef.PreventRender(false); // Resume rendering
    }
}
```

---

## Enumerations

### ChartSeriesType

Specifies the type of chart series.

```csharp
public enum ChartSeriesType
{
    Line,
    Column,
    Area,
    Bar,
    Bubble,
    Scatter,
    Spline,
    SplineArea,
    StepLine,
    StepArea,
    StackingColumn,
    StackingColumn100,
    StackingBar,
    StackingBar100,
    StackingArea,
    StackingArea100,
    StackingLine,
    StackingLine100,
    MultiColoredLine,
    MultiColoredArea
}
```

**Common Values:**
- `Line` - Line chart
- `Column` - Vertical column chart
- `Area` - Area chart
- `Bar` - Horizontal bar chart
- `Spline` - Smooth line chart
- `Scatter` - Scatter plot
- `Bubble` - Bubble chart
- `StackingColumn` - Stacked column chart
- `StackingColumn100` - 100% stacked column chart

---

### ValueType

Specifies the type of axis.

```csharp
public enum ValueType
{
    Double,
    DateTime,
    Category,
    Logarithmic,
    DateTimeCategory
}
```

**Values:**
- `Double` - Numeric axis
- `DateTime` - DateTime axis
- `Category` - Category axis
- `Logarithmic` - Logarithmic axis
- `DateTimeCategory` - DateTime category axis

---

### ChartSelectionMode

Specifies the selection mode. **Note the `Chart` prefix in the type
name** — it is not bare `SelectionMode`. Defined in
`Syncfusion.Blazor.Toolkit`. NS: `Syncfusion.Blazor.Toolkit`.

```csharp
public enum ChartSelectionMode
{
    None,
    Series,
    Point,
    Cluster,
    DragXY,
    DragY,
    DragX,
    Lasso
}
```

**Values:**
- `None` - No selection
- `Series` - Select entire series
- `Point` - Select individual point
- `Cluster` - Select cluster of points
- `DragXY` - Drag to select in both directions
- `DragX` - Drag to select horizontally
- `DragY` - Drag to select vertically
- `Lasso` - Lasso selection

---

### HighlightMode

Specifies the highlight mode.

```csharp
public enum HighlightMode
{
    None,
    Series,
    Point,
    Cluster
}
```

**Values:**
- `None` - No highlighting
- `Series` - Highlight entire series
- `Point` - Highlight individual point
- `Cluster` - Highlight cluster of points

---

### SelectionPattern

Specifies selection/highlight patterns.

```csharp
public enum SelectionPattern
{
    None,
    DiagonalForward,
    DiagonalBackward,
    Crosshatch,
    Dots,
    Chessboard,
    Grid,
    Turquoise,
    Star,
    Triangle,
    Circle,
    Tile,
    HorizontalDash,
    VerticalDash,
    Rectangle,
    Box,
    VerticalStripe,
    HorizontalStripe,
    Bubble
}
```

---

### ToolbarMode

Controls when the zoom toolbar is visible.

```csharp
public enum ToolbarMode
{
    OnDemand = 0,  // toolbar shown only while chart is zoomed
    Always   = 1,  // always visible
    None     = 2   // never visible
}
```

NS: `Syncfusion.Blazor.Toolkit`.

### ZIndexPosition

Controls whether the strip line paints over or behind the series.

```csharp
public enum ZIndexPosition
{
    Over,
    Behind
}
```

NS: `Syncfusion.Blazor.Toolkit`.

### LegendPosition

Specifies legend position.

```csharp
public enum LegendPosition
{
    Auto,
    Top,
    Left,
    Bottom,
    Right,
    Custom
}
```

**Values:**
- `Auto` - Automatically positions legend
- `Top` - Position at top
- `Left` - Position at left
- `Bottom` - Position at bottom
- `Right` - Position at right
- `Custom` - Custom position using X and Y coordinates

---

### EmptyPointMode

Specifies how to handle empty points.

```csharp
public enum EmptyPointMode
{
    Gap,
    Zero,
    Drop,
    Average
}
```

**Values:**
- `Gap` - Leave gap at empty point
- `Zero` - Treat as zero
- `Drop` - Drop the empty point
- `Average` - Use average of surrounding points

---

### LabelPlacement

Specifies label placement for category axis.

```csharp
public enum LabelPlacement
{
    BetweenTicks,
    OnTicks
}
```

**Values:**
- `BetweenTicks` - Place labels between ticks
- `OnTicks` - Place labels on ticks

---

### EdgeLabelPlacement

Specifies edge label placement.

```csharp
public enum EdgeLabelPlacement
{
    None,
    Hide,
    Shift
}
```

**Values:**
- `None` - No special treatment
- `Hide` - Hide edge labels
- `Shift` - Shift edge labels inside

---

### LabelIntersectAction

Specifies action for intersecting labels.

```csharp
public enum LabelIntersectAction
{
    None,
    Hide,
    Trim,
    Wrap,
    MultipleRows,
    Rotate45,
    Rotate90
}
```

**Values:**
- `None` - No action
- `Hide` - Hide intersecting labels
- `Trim` - Trim labels with ellipsis
- `Wrap` - Wrap label text
- `MultipleRows` - Display in multiple rows
- `Rotate45` - Rotate labels 45 degrees
- `Rotate90` - Rotate labels 90 degrees

---

### ChartShape

Specifies marker shapes. NS: `Syncfusion.Blazor.Toolkit`.

```csharp
public enum ChartShape
{
    Circle = 0,
    Triangle = 1,
    Diamond = 2,
    Rectangle = 3,
    Pentagon = 4,
    InvertedTriangle = 5,
    VerticalLine = 6,
    Cross = 7,
    Plus = 8,
    HorizontalLine = 9,
    Image = 10,
    Auto = 11
}
```

Use `Syncfusion.Blazor.Toolkit.ChartShape.Auto` to let the chart pick a different shape per series
automatically. `Plus` is a valid shape distinct from `Cross`.

---

### TrendlineTypes

Specifies trendline types.

```csharp
public enum TrendlineTypes
{
    Linear,
    Exponential,
    Logarithmic,
    Polynomial,
    Power,
    MovingAverage
}
```

**Values:**
- `Linear` - Linear trendline
- `Exponential` - Exponential trendline
- `Logarithmic` - Logarithmic trendline
- `Polynomial` - Polynomial trendline
- `Power` - Power trendline
- `MovingAverage` - Moving average trendline

---

### ZoomMode

Specifies zooming mode.

```csharp
public enum ZoomMode
{
    X,
    Y,
    XY
}
```

**Values:**
- `X` - Zoom horizontally only
- `Y` - Zoom vertically only
- `XY` - Zoom in both directions

---

### ToolbarItems

Specifies zooming toolbar items.

```csharp
public enum ToolbarItems
{
    Zoom,
    ZoomIn,
    ZoomOut,
    Pan,
    Reset
}
```

---

### Theme

Specifies chart themes. The `Theme` enum lives in the root toolkit
namespace `Syncfusion.Blazor.Toolkit.Theme` (per `src/Base/Enumeration.cs`)
and ships **only** two values in this toolkit: `Fluent` (default) and
`FluentDark`. Older guidance that points to `Syncfusion.Blazor.Theme.*`
or to Material/Bootstrap5/Tailwind/HighContrast members belongs to the
legacy `Syncfusion.Blazor` product and **does not apply here** — `Theme`
is a strongly typed enum parameter, so an unrecognized member **fails to
compile** rather than slipping through silently.

```csharp
public enum Theme
{
    Fluent,       // default — light background, dark text, neutral accents
    FluentDark    // dark background, light text
}
```

**Usage Example:**
```razor
<SfChart Title="Sales Chart"
         Theme="Syncfusion.Blazor.Toolkit.Theme.FluentDark">
    <!-- chart content -->
</SfChart>
```

> **Don't** use `Theme.HighContrast`, `Theme.Material`, `Theme.Bootstrap5`,
> `Theme.Tailwind`, etc. — they are not part of this toolkit.

---

## Key Classes and Components

### ChartSeries

Represents a chart series with its data and configuration.

**Key Properties:**
```csharp
public string DataSource { get; set; }
public string XName { get; set; }
public string YName { get; set; }
public ChartSeriesType Type { get; set; }
public string Name { get; set; }
public string Fill { get; set; }
public double Width { get; set; }
public string DashArray { get; set; }
public double Opacity { get; set; }
```

---

### ChartPrimaryXAxis / ChartPrimaryYAxis

Configures the primary axes.

**Key Properties:**
```csharp
public ValueType ValueType { get; set; }
public string Title { get; set; }
public object Minimum { get; set; }
public object Maximum { get; set; }
public double Interval { get; set; }
public string LabelFormat { get; set; }
public EdgeLabelPlacement EdgeLabelPlacement { get; set; }
public LabelIntersectAction LabelIntersectAction { get; set; }
```

---

### ChartTooltipSettings

Configures tooltip behavior. Verified against
`src/Components/Charts/Chart/UserInteractions/Models/ChartTooltipSettings.cs`.

**Key Properties:**
```csharp
public bool Enable { get; set; }                  // default false
public string Format { get; set; }
public bool Shared { get; set; }
public string Fill { get; set; }
public ChartTooltipTextStyle TextStyle { get; set; }   // *typed*
public RenderFragment? Template { get; set; }
```

---

### ChartLegendSettings

Configures legend appearance and behavior.

**Key Properties:**
```csharp
public bool Visible { get; set; }
public LegendPosition Position { get; set; }
public Alignment Alignment { get; set; }
public LegendShape Shape { get; set; }
public RenderFragment<object> Template { get; set; }
```

---

### ChartZoomSettings

Configures zooming behavior.

**Key Properties:**
```csharp
public bool EnableSelectionZooming { get; set; }
public bool EnablePinchZooming { get; set; }
public bool EnableMouseWheelZooming { get; set; }
public bool EnableScrollbar { get; set; }
public bool EnablePan { get; set; }
public ZoomMode Mode { get; set; }
public ToolbarItems[] ToolbarItems { get; set; }
```

---

## Important Notes

1. **Correct enum usage**: Use fully-qualified enum names in the default
   sample layout (which imports only `Syncfusion.Blazor.Toolkit.Charts`):
   ```razor
   <!-- Default imports: short name won't compile -->
   <ChartSeries Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" />

   <!-- If you add @using Syncfusion.Blazor.Toolkit; at the top of the file,
        both work. The qualified form is the safe one. -->
   <ChartSeries Type="ChartSeriesType.Column" />
   ```

2. **Method naming**: Programmatic methods on `SfChart` are `*Async`
   when they return `Task`, and `void` (no suffix) for sync helpers.

   | Async `Task` (public) | Sync `void` (public) |
   |------------------------|------------------------|
   | `RefreshAsync`, `ShowTooltipAsync`, `HideTooltipAsync`, `ShowCrosshairAsync`, `HideCrosshairAsync` | `Sort`, `ClearSort`, `ClearSelection`, `PreventRender` |

   Methods marked `<exclude/>` (decorated with
   `[EditorBrowsable(EditorBrowsableState.Never)]`) include
   `AddSeriesAsync`, `RemoveSeries`, `ClearSeries`, `RefreshLiveData`.
   They compile and run, but are not visible in IntelliSense. Treat them
   as **internal surface** unless your team has committed to using them;
   the public API at the chart layer is just refresh/show/hide/sort.

3. **Namespaces** — the standard pair:
   ```razor
   @using Syncfusion.Blazor.Toolkit          // enum roots & Theme
   @using Syncfusion.Blazor.Toolkit.Charts   // SfChart, components
   ```

   `TextWrap` lives in the **root** `Syncfusion.Blazor` namespace (not the
   toolkit). Pages that set `TextWrap="…" on ChartLegendSettings also need
   `@using Syncfusion.Blazor;` or must use the fully qualified
   `Syncfusion.Blazor.TextWrap.Wrap`.

4. **Component reference**: To call methods, use `@ref`:
   ```razor
   <SfChart @ref="ChartRef"></SfChart>
   @code { SfChart ChartRef = default!; }
   ```

---

## Common Patterns

### Basic Chart with Data

```razor
@using Syncfusion.Blazor.Toolkit
@using Syncfusion.Blazor.Toolkit.Charts

<SfChart>
    <ChartPrimaryXAxis ValueType="ValueType.Category" />
    <ChartPrimaryYAxis Title="Sales" />

    <ChartSeries DataSource="@SalesData"
                 XName="Month"
                 YName="Sales"
                 Type="ChartSeriesType.Column">
    </ChartSeries>
</SfChart>

@code {
    public class SalesInfo
    {
        public string Month { get; set; }
        public double Sales { get; set; }
    }
    
    public List<SalesInfo> SalesData = new List<SalesInfo>
    {
        new SalesInfo { Month = "Jan", Sales = 35 },
        new SalesInfo { Month = "Feb", Sales = 28 },
        new SalesInfo { Month = "Mar", Sales = 34 }
    };
}
```

### Chart with Multiple Series

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="ValueType.Category" />
    
    <ChartLegendSettings Visible="true" />
    
    <ChartSeries DataSource="@Data1" 
                     Name="Product A"
                     XName="X" 
                     YName="Y" 
                     Type="ChartSeriesType.Column">
        </ChartSeries>
    <ChartSeries DataSource="@Data2" 
                     Name="Product B"
                     XName="X" 
                     YName="Y" 
                     Type="ChartSeriesType.Column">
        </ChartSeries>
</SfChart>
```

### Chart with Zooming

```razor
<SfChart>
    <ChartZoomSettings EnableSelectionZooming="true" 
                       EnableMouseWheelZooming="true"
                       EnablePan="true"
                       Mode="ZoomMode.XY">
    </ChartZoomSettings>
    
        <!-- Series configuration -->
</SfChart>
```

---

This API reference document provides accurate information based on the official Syncfusion Blazor Charts API. Always refer to this document when generating code samples or providing API guidance.
