# Common Chart Types

> **Verified against source** — series-type & shape enums verified
> against `src/Base/Enumeration.cs`. Last source audit: **2026-08-24**.

## Table of Contents

- [Overview](#overview)
- [Line Chart](#line-chart)
   - [Description](#description)
   - [Basic Implementation](#basic-implementation)
   - [Customization](#customization)
   - [Multicolored Line](#multicolored-line)
- [Area Chart](#area-chart)
   - [Description](#description)
   - [Basic Implementation](#basic-implementation)
   - [Customization](#customization)
   - [Multicolored Area](#multicolored-area)
- [Column Chart](#column-chart)
   - [Description](#description)
   - [Basic Implementation](#basic-implementation)
   - [Customization](#customization)
   - [Rounded Corners](#rounded-corners)
   - [Grouped Columns](#grouped-columns)
- [Bar Chart](#bar-chart)
   - [Description](#description)
   - [Basic Implementation](#basic-implementation)
   - [Customization](#customization)
- [Spline Chart](#spline-chart)
   - [Description](#description)
   - [Basic Implementation](#basic-implementation)
   - [Customization](#customization)
- [Spline Area Chart](#spline-area-chart)
   - [Description](#description)
   - [Basic Implementation](#basic-implementation)
   - [Customization](#customization)
- [Step Line Chart](#step-line-chart)
   - [Description](#description)
   - [Basic Implementation](#basic-implementation)
- [Step Area Chart](#step-area-chart)
   - [Description](#description)
   - [Basic Implementation](#basic-implementation)
- [When to Use Each Type](#when-to-use-each-type)
- [Empty Point Handling](#empty-point-handling)
- [Common Series Properties](#common-series-properties)
   - [Fill](#fill)
   - [Opacity](#opacity)
   - [Width](#width)
   - [DashArray](#dasharray)
   - [Name](#name)
- [Series Events](#series-events)
   - [OnSeriesRender](#onseriesrender)
   - [OnPointRender](#onpointrender)
- [Multi-Series Example](#multi-series-example)
- [Performance Tips](#performance-tips)
- [Troubleshooting](#troubleshooting)
- [Related Features](#related-features)


## Overview

This guide covers the most frequently-used chart types in Syncfusion Blazor Charts. These chart types are ideal for everyday data visualization needs including trends, comparisons, and distributions.

All chart types share common features:
- Data binding with `DataSource`, `XName`, and `YName` properties
- Customization via `Fill`, `Opacity`, `Width`, and `DashArray`
- Empty point handling
- Series and point render events
- Support for multiple series

## Line Chart

### Description

Line charts visualize time-dependent data by connecting data points with lines, showing trends at equal intervals. Ideal for displaying continuous data and identifying patterns over time.

### Basic Implementation

```razor
@using Syncfusion.Blazor.Toolkit.Charts

<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@SalesData" 
             XName="Month" 
             YName="Sales" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line">
    </ChartSeries>
</SfChart>

@code {
    public record ChartData(string Month, double Sales);

    private readonly List<ChartData> SalesData = new()
    {
        new("Jan", 35), new("Feb", 28), new("Mar", 34),
        new("Apr", 32), new("May", 40)
    };
}
```

### Customization

**Line Color and Width:**
```razor
<ChartSeries DataSource="@SalesData" 
             XName="Month" 
             YName="Sales" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line"
             Fill="blue"
             Width="3">
</ChartSeries>
```

**Dashed Line:**
```razor
<ChartSeries DataSource="@SalesData" 
             XName="Month" 
             YName="Sales" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line"
             DashArray="5,5"
             Width="2">
</ChartSeries>
```

**Gradient Line:**
```razor
<ChartSeries DataSource="@SalesData" 
             XName="Month" 
             YName="Sales" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line"
             Fill="url(#lineGradient)">
</ChartSeries>

<svg style="height: 0">
    <defs>
        <linearGradient id="lineGradient" x1="0%" y1="0%" x2="0%" y2="100%">
            <stop offset="0%" style="stop-color:orange;stop-opacity:1" />
            <stop offset="100%" style="stop-color:red;stop-opacity:1" />
        </linearGradient>
    </defs>
</svg>
```

### Multicolored Line

Display different colors for each line segment:

```razor
<ChartSeries DataSource="@ColoredData" 
             XName="Month" 
             YName="Value" 
             PointColorMapping="Color"
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.MultiColoredLine">
</ChartSeries>

@code {
    public record ColoredChartData(string Month, double Value, string Color);

    private readonly List<ColoredChartData> ColoredData = new()
    {
        new("Jan", 35, "#1f77b4"),
        new("Feb", 28, "#ff7f0e"),
        new("Mar", 34, "#2ca02c")
    };
}
```

---

## Area Chart

### Description

Area charts fill the region below the line, emphasizing magnitude and trends over time. Useful for showing cumulative totals or comparing multiple datasets.

### Basic Implementation

```razor
<SfChart>
    <ChartPrimaryXAxis Minimum="1900" Maximum="2000" EdgeLabelPlacement="EdgeLabelPlacement.Shift"></ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@PopulationData" 
                     XName="Year" 
                     YName="Population" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Area">
        </ChartSeries>
</SfChart>
```

### Customization

**Fill Color:**
```razor
<ChartSeries DataSource="@PopulationData" 
             XName="Year" 
             YName="Population" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Area"
             Fill="#0066CC"
             Opacity="0.5">
</ChartSeries>
```

**Border:**
```razor
<ChartSeries DataSource="@PopulationData" 
             XName="Year" 
             YName="Population" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Area">
    <ChartSeriesBorder Width="2" Color="#0066CC"></ChartSeriesBorder>
</ChartSeries>
```

**Gradient Fill:**
```razor
<ChartSeries DataSource="@PopulationData" 
             XName="Year" 
             YName="Population" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Area"
             Fill="url(#areaGradient)">
</ChartSeries>

<svg style="height: 0">
    <defs>
        <linearGradient id="areaGradient" x1="0%" y1="0%" x2="0%" y2="100%">
            <stop offset="0%" style="stop-color:lightblue;stop-opacity:0.8" />
            <stop offset="100%" style="stop-color:darkblue;stop-opacity:0.3" />
        </linearGradient>
    </defs>
</svg>
```

### Multicolored Area

```razor
<ChartSeries DataSource="@SegmentData" 
             XName="Year" 
             YName="Value" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.MultiColoredArea">
    <ChartSegments>
    <ChartSegment Value="2007" Color="blue" />
    <ChartSegment Value="2009" Color="red" />
    <ChartSegment Color="green"></ChartSegment>
    </ChartSegments>
</ChartSeries>
```

---

## Column Chart

### Description

Column charts compare values across categories using vertical bars. Most common chart type for frequency, count, total, or average comparisons.

### Basic Implementation

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@SalesData" 
                     XName="Month" 
                     YName="Sales" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        </ChartSeries>
</SfChart>
```

### Customization

**Column Color:**
```razor
<ChartSeries DataSource="@SalesData" 
             XName="Month" 
             YName="Sales" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column"
             Fill="#FF6347">
</ChartSeries>
```

**Column Spacing and Width:**
```razor
<ChartSeries DataSource="@SalesData" 
             XName="Month" 
             YName="Sales" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column"
             ColumnSpacing="0.2"
             ColumnWidth="0.5">
</ChartSeries>
```

**Column Width in Pixels:**
```razor
<ChartSeries DataSource="@SalesData" 
             XName="Month" 
             YName="Sales" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column"
             ColumnWidthInPixel="50">
</ChartSeries>
```

### Rounded Corners

```razor
<ChartSeries DataSource="@SalesData" 
             XName="Month" 
             YName="Sales" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
    <ChartCornerRadius TopLeft="10" TopRight="10"></ChartCornerRadius>
</ChartSeries>
```

**Dynamic Corner Radius:**
```razor
<SfChart>
    <ChartEvents OnPointRender="PointRender"></ChartEvents>
    
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@SalesData" 
                     XName="Month" 
                     YName="Sales" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        </ChartSeries>
</SfChart>

@code {
    public void PointRender(PointRenderEventArgs args)
    {
        if ((args.Point.X as string) == "Jan" || (args.Point.X as string) == "May")
        {
            args.CornerRadius.TopLeft = 10;
            args.CornerRadius.TopRight = 10;
        }
    }
}
```

### Grouped Columns

Compare multiple series side-by-side:

```razor
    <ChartSeries DataSource="@Product1Data" 
                 XName="Quarter" 
                 YName="Sales" 
                 Name="Product A"
                 GroupName="Group1"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
    </ChartSeries>
    <ChartSeries DataSource="@Product2Data" 
                 XName="Quarter" 
                 YName="Sales" 
                 Name="Product B"
                 GroupName="Group1"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
    </ChartSeries>
```

---

## Bar Chart

### Description

Bar charts display horizontal bars for comparing categories. Ideal when category names are long or when emphasizing ranking.

### Basic Implementation

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@SalesData" 
                     XName="Product" 
                     YName="Sales" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Bar">
        </ChartSeries>
</SfChart>
```

### Customization

Bar charts support the same customization options as column charts, including:
- Fill color and gradients
- Spacing and width (`ColumnSpacing`, `ColumnWidth`, `ColumnWidthInPixel`)
- Rounded corners (`ChartCornerRadius`)
- Grouping (`GroupName`)

**Horizontal Bars with Rounded Corners:**
```razor
<ChartSeries DataSource="@SalesData" 
             XName="Product" 
             YName="Sales" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Bar">
    <ChartCornerRadius TopRight="5" BottomRight="5"></ChartCornerRadius>
</ChartSeries>
```

---

## Spline Chart

### Description

Spline charts draw smooth curves through data points using cubic spline interpolation. Better for showing trends without sharp angles.

### Basic Implementation

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@TemperatureData" 
                     XName="Month" 
                     YName="Temperature" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Spline">
        </ChartSeries>
</SfChart>
```

### Customization

Spline charts support the same customization as line charts:
- Line color (`Fill`)
- Line width (`Width`)
- Dash patterns (`DashArray`)
- Opacity

```razor
<ChartSeries DataSource="@TemperatureData" 
             XName="Month" 
             YName="Temperature" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Spline"
             Fill="#FF6347"
             Width="3">
</ChartSeries>
```

---

## Spline Area Chart

### Description

Combines smooth curves of spline charts with filled areas. Shows trends with smooth transitions while emphasizing magnitude.

### Basic Implementation

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@Data" 
                     XName="Month" 
                     YName="Value" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.SplineArea">
        </ChartSeries>
</SfChart>
```

### Customization

Supports area chart customizations:
- Fill color and opacity
- Border styling
- Gradient fills

---

## Step Line Chart

### Description

Step line charts display data as a series of horizontal and vertical line segments, creating a stepped appearance. Ideal for showing data that changes at specific intervals.

### Basic Implementation

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@StockData" 
                     XName="Date" 
                     YName="Price" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.StepLine">
        </ChartSeries>
</SfChart>
```

---

## Step Area Chart

### Description

Step area charts combine stepped lines with filled areas below. Shows discrete changes in values with magnitude emphasis.

### Basic Implementation

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@Data" 
                     XName="Category" 
                     YName="Value" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.StepArea">
        </ChartSeries>
</SfChart>
```

---

## When to Use Each Type

| Chart Type | Best For | Use Case Examples |
|-----------|----------|-------------------|
| **Line** | Continuous data trends over time | Stock prices, temperature changes, website traffic |
| **Area** | Magnitude and cumulative totals | Revenue over time, resource usage, market share |
| **Column** | Categorical comparisons | Monthly sales, survey results, product comparisons |
| **Bar** | Long category names, rankings | Top products, employee performance, country data |
| **Spline** | Smooth trends without noise | Projected growth, averaged data, forecasts |
| **Spline Area** | Smooth magnitude visualization | Smooth cumulative data, overlapping datasets |
| **Step Line** | Discrete interval changes | Inventory levels, rate changes, step functions |
| **Step Area** | Discrete changes with magnitude | Pricing tiers, capacity levels, state transitions |

---

## Empty Point Handling

All chart types support empty point handling for `null`, `double.NaN`, or `undefined` values:

```razor
<ChartSeries DataSource="@DataWithGaps" 
             XName="Month" 
             YName="Value" 
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line">
    <ChartEmptyPointSettings Mode="Syncfusion.Blazor.Toolkit.EmptyPointMode.Average" 
                              Fill="red">
    <ChartEmptyPointBorder Color="green" Width="2"></ChartEmptyPointBorder>
    </ChartEmptyPointSettings>
    <ChartMarker Visible="true" Width="7" Height="7"></ChartMarker>
</ChartSeries>
```

**Empty Point Modes:** (source order — verified at
`src/Base/Enumeration.cs:1062`)
- `Gap` (default) - Leave gap in series
- `Zero` - Plot as zero value
- `Drop` - Drop the point entirely
- `Average` - Use average of adjacent points

---

## Common Series Properties

These properties apply to all common chart types:

### Fill
Sets the series color:
```razor
Fill="blue"
Fill="url(#gradientId)"
Fill="#FF6347"
```

### Opacity
Controls transparency (0 to 1):
```razor
Opacity="0.5"
```

### Width
Line/border thickness (for line-based charts):
```razor
Width="3"
```

### DashArray
Dash pattern for lines:
```razor
DashArray="5,5"     <!-- 5px dash, 5px gap -->
DashArray="10,5,2"  <!-- Complex pattern -->
```

### Name
Legend label:
```razor
Name="Sales 2024"
```

---

## Series Events

### OnSeriesRender

Customize series before rendering:

```razor
<ChartEvents OnSeriesRender="SeriesRender"></ChartEvents>

@code {
    public void SeriesRender(SeriesRenderEventArgs args)
    {
        args.Fill = "#FF4081";
    }
}
```

### OnPointRender

Customize individual points:

```razor
<ChartEvents OnPointRender="PointRender"></ChartEvents>

@code {
    public void PointRender(PointRenderEventArgs args)
    {
        // Alternate colors
        args.Fill = (args.Point.Index % 2 == 0) ? "#009cb8" : "#ff6347";
    }
}
```

---

## Multi-Series Example

Display multiple chart types together:

```razor
<SfChart Title="Sales Comparison">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartLegendSettings Visible="true"></ChartLegendSettings>
    <ChartTooltipSettings Enable="true"></ChartTooltipSettings>
    
    <ChartSeries DataSource="@ActualSales" 
                     Name="Actual"
                     XName="Month" 
                     YName="Sales" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        </ChartSeries>
        
    <ChartSeries DataSource="@TargetSales" 
                     Name="Target"
                     XName="Month" 
                     YName="Sales" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line"
                     Width="3">
            <ChartMarker Visible="true" Width="10" Height="10"></ChartMarker>
        </ChartSeries>
</SfChart>
```

---

## Performance Tips

1. **Limit Data Points:** For line/spline charts, downsample data beyond 1000 points
2. **Use Column Width in Pixels:** For fixed-width columns with many categories
3. **Optimize Empty Points:** Use `Mode="Gap"` for better performance with sparse data
4. **Batch Updates:** Update data source once rather than per-point modifications

---

## Troubleshooting

**Chart not displaying:**
- Verify `Type` property is set correctly
- Check `DataSource` is populated
- Ensure `XName` and `YName` match data model properties (case-sensitive)

**Lines/columns not visible:**
- Check `Fill` color isn't transparent
- Verify `Opacity` isn't set to 0
- Ensure data values aren't all zeros or nulls

**Spacing issues:**
- Adjust `ColumnSpacing` (0 to 1 range)
- Use `ColumnWidth` or `ColumnWidthInPixel` for sizing

---

## Related Features

- Add data markers for point visualization
- Enable data labels to show values
- Configure tooltips for interactivity
- Apply themes for consistent styling
- Use zooming/panning for large datasets
