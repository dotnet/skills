# Specialized Blazor Chart Types Reference

> **Verified against source** — series-type & shape enums verified
> against `src/Base/Enumeration.cs`. Last source audit: **2026-08-24**.

A comprehensive guide to implementing specialized Syncfusion Blazor chart types including stacked, scatter, and bubble charts. This document is self-contained with complete examples and best practices.

## Table of Contents

- [Stacked Charts](#stacked-charts)
    - [Stacked Area](#stacked-area)
    - [Stacked Column/Bar](#stacked-columnbar)
    - [Stacked Line](#stacked-line)
- [Scatter and Bubble Charts](#scatter-and-bubble-charts)
    - [Scatter Chart](#scatter-chart)
    - [Bubble Chart](#bubble-chart)
- [Vertical Chart Orientation](#vertical-chart-orientation)
- [Best Practices](#best-practices)
    - [Empty Point Handling](#empty-point-handling)
    - [Series Customization Event](#series-customization-event)
    - [Point Customization Event](#point-customization-event)
    - [Gradient Fill](#gradient-fill)
- [Common Properties](#common-properties)
    - [All Chart Types Support:](#all-chart-types-support)
    - [Data Binding:](#data-binding)
- [Quick Reference](#quick-reference)


## Stacked Charts

### Stacked Area

**Overview**: Shows contribution of multiple series to total over time.

**Multi-Series Implementation**:
```cshtml
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    
    <ChartSeries DataSource="@RevenueData" 
                     XName="Year" 
                     YName="ProductA" 
                     Name="Product A"
                     Fill="red" 
                     Opacity="0.7"
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.StackingArea">
            <ChartSeriesBorder Width="2" Color="black" />
        </ChartSeries>
        
    <ChartSeries DataSource="@RevenueData" 
                     XName="Year" 
                     YName="ProductB" 
                     Name="Product B"
                     Fill="blue" 
                     Opacity="0.7"
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.StackingArea">
            <ChartSeriesBorder Width="2" Color="black" />
        </ChartSeries>
        
    <ChartSeries DataSource="@RevenueData" 
                     XName="Year" 
                     YName="ProductC" 
                     Name="Product C"
                     Fill="green" 
                     Opacity="0.7"
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.StackingArea">
            <ChartSeriesBorder Width="2" Color="black" />
        </ChartSeries>
    
    <ChartStackLabelSettings Visible="true" Format="{value}" Fill="#ADD8E6">
    <ChartStackLabelFont FontWeight="600" Color="blue" />
    </ChartStackLabelSettings>
</SfChart>

@code {
    public record StackedData(double Year, double ProductA, double ProductB, double ProductC);

    private readonly List<StackedData> RevenueData = new()
    {
        new(2020, 0.61, 0.03, 0.48),
        new(2021, 0.81, 0.05, 0.53),
        new(2022, 0.91, 0.06, 0.57)
    };
}
```

**Stack Labels**: Display cumulative totals with `ChartStackLabelSettings`

---

### Stacked Column/Bar

**Similar to Stacked Area but with columns/bars**:
```cshtml
<ChartSeries Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.StackingColumn" />
<ChartSeries Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.StackingBar" />
```

---

### Stacked Line

```cshtml
<ChartSeries Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.StackingLine" Width="2" />
```

---

## Scatter and Bubble Charts

### Scatter Chart

**Overview**: Plots individual data points to show correlation between two variables.

```cshtml
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />

    <ChartSeries DataSource="@ScatterData"
                     XName="Country"
                     YName="GoldMedals"
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Scatter"
                     Fill="green"
                     Opacity="0.5">
            <ChartMarker Height="10" Width="10" Shape="Syncfusion.Blazor.Toolkit.ChartShape.Triangle" />
        </ChartSeries>

    <ChartSeries DataSource="@ScatterData"
                     XName="Country"
                     YName="SilverMedals"
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Scatter"
                     Fill="blue"
                     Opacity="0.5">
            <ChartMarker Height="10" Width="10" Shape="Syncfusion.Blazor.Toolkit.ChartShape.Rectangle" />
        </ChartSeries>
</SfChart>

@code {
    public record ScatterPoint(string Country, double GoldMedals, double SilverMedals);

    private readonly List<ScatterPoint> ScatterData = new()
    {
        new("USA", 39, 41),
        new("China", 38, 32),
        new("Japan", 27, 14),
        new("UK", 22, 20),
        new("Australia", 17, 7)
    };
}
```

---

### Bubble Chart

**Overview**: Three-dimensional scatter chart where bubble size represents third parameter.

**Data Requirements**: XName, YName, Size

```cshtml
<SfChart>
    <ChartSeries DataSource="@PopulationData" 
                     XName="LiteracyRate" 
                     YName="GrowthRate" 
                     Size="Population"
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Bubble"
                     Fill="blue"
                     Opacity="0.5">
        </ChartSeries>
</SfChart>

@code {
    public record BubbleData(double LiteracyRate, double GrowthRate, double Population, string Country);

    private readonly List<BubbleData> PopulationData = new()
    {
        new(92.2, 7.8, 1.347, "China"),
        new(74.0, 6.5, 1.241, "India")
    };
}
```

---

## Vertical Chart Orientation

**Apply to any chart type**:
```cshtml
<SfChart IsTransposed="true">
    <ChartSeries DataSource="@Data" 
                     XName="X" 
                     YName="Y" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Spline" />
</SfChart>
```

---

## Best Practices

### Empty Point Handling
```cshtml
<ChartEmptyPointSettings Mode="Syncfusion.Blazor.Toolkit.EmptyPointMode.Average" Fill="#FFDE59">
    <ChartEmptyPointBorder Color="red" Width="2" />
</ChartEmptyPointSettings>
```

### Series Customization Event
```cshtml
<ChartEvents OnSeriesRender="SeriesRender" />

@code {
    void SeriesRender(SeriesRenderEventArgs args)
    {
        args.Fill = "#FF4081";
    }
}
```

### Point Customization Event
```cshtml
<ChartEvents OnPointRender="PointRender" />

@code {
    void PointRender(PointRenderEventArgs args)
    {
        args.Fill = (args.Point.Index % 2 != 0) ? "#ff6347" : "#009cb8";
    }
}
```

### Gradient Fill
```cshtml
<ChartSeries Fill="url(#grad1)" />

<svg style="height: 0">
    <defs>
        <linearGradient id="grad1" x1="0%" y1="0%" x2="0%" y2="100%">
            <stop offset="20%" style="stop-color:orange;stop-opacity:1" />
            <stop offset="100%" style="stop-color:black;stop-opacity:1" />
        </linearGradient>
    </defs>
</svg>
```

---

## Common Properties

### All Chart Types Support:
- **Fill**: Series color
- **Opacity**: Transparency (0-1)
- **DashArray**: Border pattern
- **ChartSeriesBorder**: Border width and color
- **ChartMarker**: Data point markers
- **ChartDataLabel**: Label customization
- **ChartEmptyPointSettings**: Handling null values

### Data Binding:
- Use `DataSource` property
- Map fields with `XName`, `YName`, `High`, `Low`, etc.
- Supports `SfDataManager` for remote data

---

## Quick Reference

| Chart Type | Data Fields | Use Case |
|------------|-------------|----------|
| Stacked Area | X, Y (multiple) | Contribution analysis |
| Scatter | X, Y | Correlation |
| Bubble | X, Y, Size | 3D relationships |

---

**Document Version:** 1.1  
**Last Source Audit:** 2026-08-24  
**Total Lines:** ~388
