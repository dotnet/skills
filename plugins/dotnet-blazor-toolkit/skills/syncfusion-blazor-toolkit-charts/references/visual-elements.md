# Visual Elements Reference

> **Verified against source** — `ChartShape` (12 members),
> `ChartLabelPosition`, `ChartLinearGradient` / `ChartRadialGradient`,
> and `ChartGradientColorStop` content verified against
> `src/Base/Enumeration.cs`, `src/Components/Charts/Chart/Series/Gradient/`.
> Last source audit: **2026-08-24**.

## Table of Contents

- [Data Markers](#data-markers)
   - [Enabling Markers](#enabling-markers)
   - [Marker Shapes](#marker-shapes)
   - [Auto Marker Shapes](#auto-marker-shapes)
   - [Marker Customization](#marker-customization)
- [Data Labels](#data-labels)
   - [Basic Data Labels](#basic-data-labels)
   - [Label Positioning](#label-positioning)
   - [Label Formatting](#label-formatting)
   - [Text Mapping](#text-mapping)
   - [Label Templates](#label-templates)
   - [Label Margins and Styling](#label-margins-and-styling)
- [Annotations](#annotations)
   - [Adding Annotations](#adding-annotations)
   - [Annotation Regions](#annotation-regions)
   - [Coordinate Units](#coordinate-units)
   - [Annotation Alignment](#annotation-alignment)
- [Gradients](#gradients)
   - [Linear Gradients](#linear-gradients)
   - [Radial Gradients](#radial-gradients)
- [Visual Styling Best Practices](#visual-styling-best-practices)
   - [Marker Usage Guidelines](#marker-usage-guidelines)
   - [Data Label Best Practices](#data-label-best-practices)
   - [Annotation Guidelines](#annotation-guidelines)
   - [Gradient Recommendations](#gradient-recommendations)
   - [Performance Considerations](#performance-considerations)


## Data Markers

Data markers are visual indicators placed on data points to make them more visible and distinguishable.

### Enabling Markers

Enable markers by setting `Visible="true"` in the `ChartMarker` component:

```razor
<SfChart>
    <ChartSeries DataSource="@ChartData" XName="X" YName="Y" Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line">
            <ChartMarker Visible="true" Height="10" Width="10"/>
        </ChartSeries>
</SfChart>

@code {
    public List<DataPoint> ChartData = new List<DataPoint>
    {
        new DataPoint { X = 2005, Y = 28 },
        new DataPoint { X = 2006, Y = 25 },
        new DataPoint { X = 2007, Y = 26 },
        new DataPoint { X = 2008, Y = 27 },
        new DataPoint { X = 2009, Y = 32 }
    };
}
```

### Marker Shapes

Customize marker appearance using the `Shape` property:

```razor
Shape="Syncfusion.Blazor.Toolkit.ChartShape.Diamond"
```

**Available Shapes:** (verified against `src/Base/Enumeration.cs`)
- `Circle` - Default circular marker
- `Triangle` - Triangular marker
- `Diamond` - Diamond-shaped marker
- `Rectangle` - Square/rectangular marker
- `Pentagon` - Five-sided marker
- `InvertedTriangle` - Upside-down triangle
- `VerticalLine` - Vertical line marker
- `Cross` - Cross marker
- `Plus` - Plus marker
- `HorizontalLine` - Horizontal line marker
- `Image` - Custom image marker
- `Auto` - Let the chart pick a different shape per series

### Auto Marker Shapes

When multiple series are present, set `Shape="Syncfusion.Blazor.Toolkit.ChartShape.Auto"` to automatically assign unique shapes to each series:

```razor
<SfChart>
    <ChartSeries DataSource="@Series1Data" XName="X" YName="Y" Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line">
            <ChartMarker Visible="true" Height="10" Width="10" Shape="Syncfusion.Blazor.Toolkit.ChartShape.Auto" IsFilled="true"/>
        </ChartSeries>
    <ChartSeries DataSource="@Series2Data" XName="X" YName="Y" Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line">
            <ChartMarker Visible="true" Height="10" Width="10" Shape="Syncfusion.Blazor.Toolkit.ChartShape.Auto" IsFilled="true"/>
        </ChartSeries>
    <ChartSeries DataSource="@Series3Data" XName="X" YName="Y" Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line">
            <ChartMarker Visible="true" Height="10" Width="10" Shape="Syncfusion.Blazor.Toolkit.ChartShape.Auto" IsFilled="true"/>
        </ChartSeries>
</SfChart>
```

### Marker Customization

Customize marker colors, borders, and opacity:

```razor
<ChartMarker Visible="true" 
             Height="12" 
             Width="12" 
             Shape="Syncfusion.Blazor.Toolkit.ChartShape.Circle"
             Fill="#FF6347"
             Opacity="0.8"
             IsFilled="true">
    <ChartMarkerBorder Width="2" Color="#000000"/>
</ChartMarker>
```

**Key Properties:**
- `Height`, `Width` - Size of the marker (in pixels)
- `Fill` - Fill color
- `Opacity` - Transparency (0 to 1)
- `IsFilled` - Whether marker is filled or hollow
- `ChartMarkerBorder` - Border styling

---

## Data Labels

Data labels display information about data points directly on the chart.

### Basic Data Labels

Enable data labels within the `ChartMarker` component:

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"/>
    <ChartSeries DataSource="@SalesData" XName="Month" YName="Sales" Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
            <ChartMarker>
                <ChartDataLabel Visible="true"/>
            </ChartMarker>
        </ChartSeries>
</SfChart>

@code {
    public record SalesInfo(string Month, double Sales);

    private readonly List<SalesInfo> SalesData = new()
    {
        new("Jan", 35), new("Feb", 28), new("Mar", 42), new("Apr", 38)
    };
}
```

### Label Positioning

Control label placement using the `Position` property:

```razor
<ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top"/>
```

**Available Positions:**
- `Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top` - Above the data point
- `Syncfusion.Blazor.Toolkit.ChartLabelPosition.Bottom` - Below the data point
- `Syncfusion.Blazor.Toolkit.ChartLabelPosition.Middle` - Center of the data point
- `Syncfusion.Blazor.Toolkit.ChartLabelPosition.Outer` - Outside the data point (for Column/Bar charts)
- `Syncfusion.Blazor.Toolkit.ChartLabelPosition.Auto` - Intelligent positioning to avoid overlap

**Position by Chart Type:**

```razor
<!-- Column Chart - Labels on top -->
<ChartSeries Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
    <ChartMarker>
    <ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top"/>
    </ChartMarker>
</ChartSeries>

```

### Label Formatting

Format data label values using the `Format` property:

```razor
<!-- Decimal formatting -->
<ChartDataLabel Visible="true" Format="N2"/>

<!-- Percentage formatting -->
<ChartDataLabel Visible="true" Format="P0"/>

<!-- Currency formatting -->
<ChartDataLabel Visible="true" Format="C0"/>

<!-- Custom format -->
<ChartDataLabel Visible="true" Format="{value}M"/>
```

**Format Specifiers:**
- `N0`, `N1`, `N2` - Number with 0, 1, 2 decimal places
- `C0`, `C1`, `C2` - Currency format
- `P0`, `P1`, `P2` - Percentage format
- `{value}` - Point-value placeholder; combine with a .NET format specifier (e.g. `{value:N1}`, `{value}°C`, `{value}%`). NOTE: `{value}` is the **data-label** surface; `${point.x}` / `${point.y}` belong to the **tooltip** (`ChartTooltipSettings.Format`, `ChartSeries.TooltipFormat`) and accessibility (`ChartSeries.AccessibilityDescriptionFormat`) — they are different surfaces.

### Text Mapping

Map custom text from data source to labels:

```razor
<ChartDataLabel Visible="true" Name="LabelText"/>

@code {
    public class ProductData
    {
        public string Product { get; set; }
        public double Revenue { get; set; }
        public string LabelText { get; set; }
    }

    public record ProductData(string Product, double Revenue, string LabelText);

    private readonly List<ProductData> Products = new()
    {
        new("A", 35, "Product A: $35M"),
        new("B", 28, "Product B: $28M"),
        new("C", 42, "Product C: $42M")
    };
}
```

### Label Templates

Create custom label templates with HTML content. `Template` is a
`RenderFragment<ChartDataPointInfo>` child element, so use
`<Template Context="...">` and reference the alias directly — **don't**
cast `context` inside the fragment:

```razor
<ChartDataLabel Visible="true">
    <Template Context="pointInfo">
        <div style="background-color: #303F9F; color: white; padding: 4px 6px; border-radius: 4px;">
            <b>@pointInfo.X</b><br/>
            Value: @pointInfo.Y
        </div>
    </Template>
</ChartDataLabel>
```

**Template with conditional styling:**

```razor
<ChartDataLabel Visible="true">
    <Template Context="pointInfo">
        @{ var color = pointInfo.Y > 50 ? "green" : "red"; }
        <div style="color: @color; font-weight: bold;">
            @($"{pointInfo.Y:F1}")
        </div>
    </Template>
</ChartDataLabel>
```

> When a custom `Template` is supplied, the `Format` property is **not**
> applied to that data label. See
> `src/Components/Charts/Chart/Series/DataLabel/ChartDataLabel.cs:359`
> for the typed signature (`RenderFragment<ChartDataPointInfo>`).

### Label Margins and Styling

Customize label appearance with margins, borders, and fonts:

```razor
<ChartDataLabel Visible="true" Name="LabelText">
    <ChartDataLabelFont Size="14px" 
                        FontWeight="600" 
                        Color="#333333" 
                        FontFamily="Arial"/>
    <ChartDataLabelBorder Width="1" Color="#999999"/>
    <ChartDataLabelMargin Left="10" Right="10" Top="5" Bottom="5"/>
</ChartDataLabel>
```

**Complete Label Styling Example:**

```razor
<ChartSeries DataSource="@ChartData" XName="X" YName="Y" Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
    <ChartMarker>
    <ChartDataLabel Visible="true" 
                        Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top"
                        Format="N1"
                        Fill="#FFF4E6">
            <ChartDataLabelFont Size="12px" Color="#D84315" FontWeight="bold"/>
            <ChartDataLabelBorder Width="2" Color="#FF6F00"/>
            <ChartDataLabelMargin Left="8" Right="8" Top="4" Bottom="4"/>
        </ChartDataLabel>
    </ChartMarker>
</ChartSeries>
```

---

## Annotations

Annotations add text, shapes, or custom HTML content to highlight chart regions.

### Adding Annotations

Use the `ChartAnnotations` collection to add annotations:

```razor
<SfChart Title="Sales Analysis">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"/>
    
    <ChartAnnotations>
    <ChartAnnotation X="Mar" Y="75" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Point">
            <ContentTemplate>
                <div style="color: #E53935; font-weight: bold;">Peak Sales</div>
            </ContentTemplate>
        </ChartAnnotation>
    </ChartAnnotations>
    
    <ChartSeries DataSource="@SalesData" XName="Month" YName="Sales" Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line"/>
</SfChart>
```

### Annotation Regions

Control annotation positioning relative to chart or series using the `Region` property:

```razor
<!-- Position relative to entire chart area -->
<!-- Region="Regions.Chart" is the default; CoordinateUnits can be Pixel or Point. -->
<ChartAnnotation X="50" Y="50" Region="Syncfusion.Blazor.Toolkit.Regions.Chart" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Pixel">
    <ContentTemplate>
        <div>Chart Center</div>
    </ContentTemplate>
</ChartAnnotation>

<!-- Or position relative to series using point coords -->
<ChartAnnotation X="Feb" Y="50" Region="Syncfusion.Blazor.Toolkit.Regions.Series" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Point">
    <ContentTemplate>
        <div>Series Annotation</div>
    </ContentTemplate>
</ChartAnnotation>
```

**Region Types:** (verified at `src/Base/Enumeration.cs` — `Regions`)
- `Syncfusion.Blazor.Toolkit.Regions.Chart` - Position relative to entire chart (default)
- `Syncfusion.Blazor.Toolkit.Regions.Series` - Position relative to series area

### Coordinate Units

Specify annotation positioning units:

```razor
<!-- Pixel-based positioning -->
<ChartAnnotation X="250" Y="150" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Pixel">
    <ContentTemplate>
        <div>Fixed at 250px, 150px</div>
    </ContentTemplate>
</ChartAnnotation>

<!-- Point-based positioning (data coordinates) -->
<ChartAnnotation X="Category3" Y="85" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Point">
    <ContentTemplate>
        <div>At data point</div>
    </ContentTemplate>
</ChartAnnotation>
```

### Annotation Alignment

Control horizontal and vertical alignment:

```razor
<ChartAnnotation X="50"
                 Y="50"
                 CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Pixel"
                 HorizontalAlignment="Syncfusion.Blazor.Toolkit.Alignment.Center"
                 VerticalAlignment="Syncfusion.Blazor.Toolkit.Alignment.Top">
    <ContentTemplate>
        <div style="background: #FFF3E0; padding: 10px; border: 1px solid #FF9800;">
            <b>Important Notice</b><br/>
            This is a centered annotation
        </div>
    </ContentTemplate>
</ChartAnnotation>
```

**Alignment Options:** (verified at `src/Base/Enumeration.cs` — `Alignment`)
- `Syncfusion.Blazor.Toolkit.Alignment.Near` - Left/Top alignment
- `Syncfusion.Blazor.Toolkit.Alignment.Center` - Center alignment
- `Syncfusion.Blazor.Toolkit.Alignment.Far` - Right/Bottom alignment

**Multiple Annotations Example:**

```razor
<ChartAnnotations>
    <!-- Highlight maximum point -->
    <ChartAnnotation X="@MaxPoint.X" Y="@MaxPoint.Y" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Point">
        <ContentTemplate>
            <div style="color: green; font-weight: bold;">▲ Peak</div>
        </ContentTemplate>
    </ChartAnnotation>
    
    <!-- Highlight minimum point -->
    <ChartAnnotation X="@MinPoint.X" Y="@MinPoint.Y" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Point">
        <ContentTemplate>
            <div style="color: red; font-weight: bold;">▼ Low</div>
        </ContentTemplate>
    </ChartAnnotation>
    
    <!-- Chart title annotation -->
    <ChartAnnotation X="50" Y="10" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Pixel" Region="Syncfusion.Blazor.Toolkit.Regions.Chart">
        <ContentTemplate>
            <div style="font-size: 18px; font-weight: bold;">Q4 Performance</div>
        </ContentTemplate>
    </ChartAnnotation>
</ChartAnnotations>

@code {
    private DataPoint MaxPoint = new DataPoint { X = "Jun", Y = 92 };
    private DataPoint MinPoint = new DataPoint { X = "Feb", Y = 35 };
}
```

---

## Gradients

Apply gradient fills to chart series and trendlines for enhanced visual appeal. Gradients can be linear or radial, with customizable color stops.

### Linear Gradients

A linear gradient blends colors along a straight path from a defined start point to an end point. Configure it by adding `ChartLinearGradient` inside the series and define one or more color stops using `ChartGradientColorStop`.

**Linear Gradient Properties:**
- `X1` - Horizontal start position (0 to 1)
- `Y1` - Vertical start position (0 to 1)
- `X2` - Horizontal end position (0 to 1)
- `Y2` - Vertical end position (0 to 1)

**Color Stop Properties:**
- `Offset` - Position of the color stop (0 to 100)
- `Color` - Color at the stop
- `Opacity` - Transparency (0 to 1)
- `Lighten` - Adjust lightness (positive values lighten, negative darken)
- `Brighten` - Adjust brightness (positive increases, negative decreases)

**Basic Vertical Linear Gradient (Series):**

```razor
@using Syncfusion.Blazor.Toolkit.Charts

<SfChart Title="Monthly Sales Performance">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartPrimaryYAxis LabelFormat="{value}k" />

    <ChartSeries Name="Sales" Type="ChartSeriesType.Column" DataSource="@SalesData" XName="Month" YName="Amount">
            <ChartLinearGradient X1="0" Y1="0" X2="0" Y2="1">
                <ChartGradientColorStops>
                    <ChartGradientColorStop Offset="0" Color="#4F46E5" Opacity="1" Lighten="0" Brighten="1" />
                    <ChartGradientColorStop Offset="100" Color="#22D3EE" Opacity="0.95" Lighten="-0.05" Brighten="0.9" />
                </ChartGradientColorStops>
            </ChartLinearGradient>

            <ChartMarker Visible="true" IsFilled="true">
                <ChartDataLabel Visible="true"></ChartDataLabel>
            </ChartMarker>
        </ChartSeries>

    <ChartTooltipSettings Enable="true"></ChartTooltipSettings>
    <ChartLegendSettings Visible="true"></ChartLegendSettings>
</SfChart>

@code {
    public class SalesPoint
    {
        public string Month { get; set; }
        public double Amount { get; set; }
    }

    public List<SalesPoint> SalesData = new ()
    {
        new SalesPoint { Month = "Jan", Amount = 78 },
        new SalesPoint { Month = "Feb", Amount = 88 },
        new SalesPoint { Month = "Mar", Amount = 99 },
        new SalesPoint { Month = "Apr", Amount = 92 },
        new SalesPoint { Month = "May", Amount = 95 },
        new SalesPoint { Month = "Jun", Amount = 102 },
        new SalesPoint { Month = "Jul", Amount = 110 },
        new SalesPoint { Month = "Aug", Amount = 105 }
    };
}
```

**Horizontal Linear Gradient:**

```razor
<ChartLinearGradient X1="0" Y1="0" X2="1" Y2="0">
    <ChartGradientColorStops>
    <ChartGradientColorStop Offset="0" Color="#2196F3" Opacity="1" />
    <ChartGradientColorStop Offset="100" Color="#E91E63" Opacity="1" />
    </ChartGradientColorStops>
</ChartLinearGradient>
```

**Multi-Color Linear Gradient:**

```razor
<ChartLinearGradient X1="0" Y1="0" X2="0" Y2="1">
    <ChartGradientColorStops>
    <ChartGradientColorStop Offset="0" Color="#FF5722" Opacity="1" />
    <ChartGradientColorStop Offset="50" Color="#FFC107" Opacity="1" />
    <ChartGradientColorStop Offset="100" Color="#4CAF50" Opacity="1" />
    </ChartGradientColorStops>
</ChartLinearGradient>
```

**Linear Gradient on Trendline:**

```razor
<ChartTrendline Type="TrendlineTypes.Linear" Width="3" Name="Trend">
    <ChartLinearGradient X1="0" Y1="0" X2="1" Y2="0">
    <ChartGradientColorStops>
            <ChartGradientColorStop Offset="0" Color="#F97316" Opacity="1" />
            <ChartGradientColorStop Offset="100" Color="#4F46E5" Opacity="1" />
        </ChartGradientColorStops>
    </ChartLinearGradient>
</ChartTrendline>
```

### Radial Gradients

A radial gradient blends colors outward from a central point, creating a circular or elliptical color progression. Configure it by adding `ChartRadialGradient` inside the series and define one or more color stops.

**Radial Gradient Properties:**
- `Cx` - Horizontal center of gradient (0 to 1)
- `Cy` - Vertical center of gradient (0 to 1)
- `Fx` - Horizontal focal point (0 to 1)
- `Fy` - Vertical focal point (0 to 1)
- `R` - Radius of gradient circle (0 to 1)

**Basic Radial Gradient (Series):**

```razor
@using Syncfusion.Blazor.Toolkit.Charts

<SfChart Title="Monthly Sales Performance">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartPrimaryYAxis LabelFormat="{value}k" />

    <ChartSeries Name="Sales" Type="ChartSeriesType.Column" DataSource="@SalesData" XName="Month" YName="Amount">
            <ChartRadialGradient Cx="0.5" Cy="0.5" Fx="0.5" Fy="0.5" R="0.5">
                <ChartGradientColorStops>
                    <ChartGradientColorStop Offset="0" Color="#FFFF00" Opacity="1" Lighten="0" Brighten="1" />
                    <ChartGradientColorStop Offset="100" Color="#7C3AED" Opacity="0.95" Lighten="-0.05" Brighten="0.9" />
                </ChartGradientColorStops>
            </ChartRadialGradient>

            <ChartMarker Visible="true" IsFilled="true">
                <ChartDataLabel Visible="true"></ChartDataLabel>
            </ChartMarker>
        </ChartSeries>

    <ChartTooltipSettings Enable="true"></ChartTooltipSettings>
    <ChartLegendSettings Visible="true"></ChartLegendSettings>
</SfChart>

@code {
    public class SalesPoint
    {
        public string Month { get; set; }
        public double Amount { get; set; }
    }

    public List<SalesPoint> SalesData = new ()
    {
        new SalesPoint { Month = "Jan", Amount = 78 },
        new SalesPoint { Month = "Feb", Amount = 88 },
        new SalesPoint { Month = "Mar", Amount = 99 },
        new SalesPoint { Month = "Apr", Amount = 92 },
        new SalesPoint { Month = "May", Amount = 95 },
        new SalesPoint { Month = "Jun", Amount = 102 },
        new SalesPoint { Month = "Jul", Amount = 110 },
        new SalesPoint { Month = "Aug", Amount = 105 }
    };
}
```

**Radial Gradient on Trendline or Indicator:**

```razor
<ChartRadialGradient Cx="0.5" Cy="0.5" Fx="0.5" Fy="0.5" R="0.5">
    <ChartGradientColorStops>
    <ChartGradientColorStop Offset="0" Color="#7C3AED" Opacity="1" />
    <ChartGradientColorStop Offset="100" Color="#F59E0B" Opacity="1" />
    </ChartGradientColorStops>
</ChartRadialGradient>
```

---

## Visual Styling Best Practices

### Marker Usage Guidelines

1. **Use markers for line charts** with fewer than 20 data points
2. **Enable auto shapes** for multiple series to ensure differentiation
3. **Keep marker size proportional** to chart size (8-12px typical)
4. **Use filled markers** for better visibility against complex backgrounds

### Data Label Best Practices

1. **Avoid label overlap** by using intelligent positioning or rotating labels
2. **Format consistently** across all series (same decimal places, units)
3. **Use templates** for complex information display
4. **Position labels outside** for dense data sets

### Annotation Guidelines

1. **Limit annotations** to 3-5 important highlights per chart
2. **Use contrasting colors** to ensure annotations are visible
3. **Keep text concise** - use short, impactful phrases
4. **Position strategically** to avoid obscuring data

### Gradient Recommendations

1. **Use subtle gradients** - avoid harsh color transitions
2. **Maintain readability** - ensure sufficient contrast for data labels
3. **Be consistent** - use similar gradient styles across related charts
4. **Test accessibility** - ensure gradients work for colorblind users

### Performance Considerations

- **Limit visible markers** on large datasets (>100 points)
- **Use simple annotations** instead of complex HTML for better performance
- **Minimize gradient complexity** for charts with many series
- **Consider data label templates** only when standard formatting is insufficient
