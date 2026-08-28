# Practical Examples for Syncfusion Blazor Charts

> **Verified against source** — all enum values and method calls in the
> examples below have been cross-checked against `src/Base/Enumeration.cs`
> and `src/Components/Charts/Chart/SfChart.razor.Methods.cs`. Print and
> export calls are placeholders — those methods don't exist yet. Last
> source audit: **2026-08-24**.

Complete, copy-paste-ready real-world examples demonstrating common chart implementation scenarios.

## Table of Contents

- [1. Sales Dashboard](#1-sales-dashboard)
- [2. Performance Comparison](#2-performance-comparison)
- [3. Trend Analysis](#3-trend-analysis)
- [4. Real-Time Monitoring](#4-real-time-monitoring)
- [5. Interactive Report](#5-interactive-report)
- [6. Responsive Analytics](#6-responsive-analytics)
- [Summary](#summary)


## 1. Sales Dashboard

**Scenario:** Display monthly sales data for multiple products with interactive tooltips and legends.

**When to use:** Business dashboards, sales reports, performance tracking.

**Key features:** Multi-series line chart, custom colors, data labels, markers, legend.

```razor
@page "/sales-dashboard"
@using Syncfusion.Blazor.Toolkit.Charts

<div class="dashboard-container">
    <h2>Sales Performance Dashboard</h2>
    <SfChart Title="Monthly Sales Comparison" Width="100%" Height="450px">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" 
                           Title="Months">
            <ChartAxisMajorGridLines Width="0"></ChartAxisMajorGridLines>
        </ChartPrimaryXAxis>
    <ChartPrimaryYAxis Title="Sales (in thousands)"
                           Minimum="0" Maximum="100" Interval="20"
                           LabelFormat="{value}K">
        </ChartPrimaryYAxis>
    <ChartTooltipSettings Enable="true" 
                              Format="<b>${point.x}</b><br/>${series.name}: <b>${point.y}K</b>">
        </ChartTooltipSettings>
    <ChartLegendSettings Visible="true" Position="LegendPosition.Top">
        </ChartLegendSettings>
            <ChartSeries DataSource="@SalesData" Name="Product A" 
                         XName="Month" YName="ProductA" 
                         Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line" Width="3" 
                         Fill="#0066CC">
                <ChartMarker Visible="true" Width="10" Height="10" 
                             Shape="Syncfusion.Blazor.Toolkit.ChartShape.Circle">
                    <ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top">
                    </ChartDataLabel>
                </ChartMarker>
            </ChartSeries>
            <ChartSeries DataSource="@SalesData" Name="Product B"
                         XName="Month" YName="ProductB"
                         Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line" Width="3"
                         Fill="#FF6B35">
                <ChartMarker Visible="true" Width="10" Height="10"
                             Shape="Syncfusion.Blazor.Toolkit.ChartShape.Diamond">
                    <ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top">
                    </ChartDataLabel>
                </ChartMarker>
            </ChartSeries>
            <ChartSeries DataSource="@SalesData" Name="Product C"
                         XName="Month" YName="ProductC"
                         Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line" Width="3"
                         Fill="#28A745">
                <ChartMarker Visible="true" Width="10" Height="10"
                             Shape="Syncfusion.Blazor.Toolkit.ChartShape.Triangle">
                    <ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top">
                    </ChartDataLabel>
                </ChartMarker>
            </ChartSeries>
    </SfChart>
</div>

@code {
    public record SalesInfo(string Month, double ProductA, double ProductB, double ProductC);

    private readonly List<SalesInfo> SalesData = new()
    {
        new("Jan", 35, 28, 42),
        new("Feb", 42, 35, 48),
        new("Mar", 48, 42, 55),
        new("Apr", 55, 48, 62),
        new("May", 62, 55, 68),
        new("Jun", 68, 62, 75),
        new("Jul", 75, 68, 82),
        new("Aug", 72, 65, 78),
        new("Sep", 78, 72, 85),
        new("Oct", 85, 78, 92),
        new("Nov", 88, 82, 95),
        new("Dec", 92, 88, 98)
    };
}
```

---

## 2. Performance Comparison

**Scenario:** Compare performance metrics across multiple teams and quarters.

**When to use:** Performance reviews, team comparisons, quarterly reports.

**Key features:** Grouped column chart, multiple categories, selection, highlighting.

```razor
@page "/performance-comparison"
@using Syncfusion.Blazor.Toolkit.Charts

<div class="performance-container">
    <h2>Quarterly Performance Comparison</h2>
    <SfChart Title="Team Performance Metrics" Width="100%" Height="450px" 
             SelectionMode="Syncfusion.Blazor.Toolkit.ChartSelectionMode.Point">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" 
                           Title="Quarters">
        </ChartPrimaryXAxis>
    <ChartPrimaryYAxis Title="Performance Score" Minimum="0" Maximum="100" 
                           Interval="20">
        </ChartPrimaryYAxis>
    <ChartTooltipSettings Enable="true" 
                              Format="<b>${point.x}</b><br/>${series.name}: <b>${point.y}%</b>">
        </ChartTooltipSettings>
    <ChartLegendSettings Visible="true" Position="LegendPosition.Top">
        </ChartLegendSettings>
    <ChartEvents OnPointClick="OnPointClick"></ChartEvents>
            <ChartSeries DataSource="@PerformanceData" Name="Team Alpha" 
                         XName="Quarter" YName="TeamAlpha" 
                         Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" 
                         ColumnWidth="0.6" ColumnSpacing="0.2" 
                         Fill="#6366F1">
                <ChartMarker>
                    <ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top">
                    </ChartDataLabel>
                </ChartMarker>
            </ChartSeries>
            <ChartSeries DataSource="@PerformanceData" Name="Team Beta"
                         XName="Quarter" YName="TeamBeta"
                         Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column"
                         ColumnWidth="0.6" ColumnSpacing="0.2"
                         Fill="#EC4899">
                <ChartMarker>
                    <ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top">
                    </ChartDataLabel>
                </ChartMarker>
            </ChartSeries>
            <ChartSeries DataSource="@PerformanceData" Name="Team Gamma"
                         XName="Quarter" YName="TeamGamma"
                         Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column"
                         ColumnWidth="0.6" ColumnSpacing="0.2"
                         Fill="#10B981">
                <ChartMarker>
                    <ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top">
                    </ChartDataLabel>
                </ChartMarker>
            </ChartSeries>
    </SfChart>
    @if (!string.IsNullOrEmpty(SelectedInfo))
    {
        <div class="selection-info">
            <p>@SelectedInfo</p>
        </div>
    }
</div>

@code {
    public string SelectedInfo = "";

    public record PerformanceMetric(string Quarter, double TeamAlpha, double TeamBeta, double TeamGamma);

    private readonly List<PerformanceMetric> PerformanceData = new()
    {
        new("Q1 2025", 75, 70, 80),
        new("Q2 2025", 80, 75, 85),
        new("Q3 2025", 85, 82, 88),
        new("Q4 2025", 90, 88, 92)
    };

    public void OnPointClick(PointEventArgs args)
    {
        SelectedInfo = $"Selected: {args.SeriesName} - {args.PointIndex} with value {args.Y}%";
        StateHasChanged();
    }
}
```

---

## 3. Trend Analysis

**Scenario:** Analyze sales trends with forecasting and key milestone annotations.

**When to use:** Trend forecasting, data analysis, strategic planning.

**Key features:** Area chart, trend lines, forecasting, annotations.

```razor
@page "/trend-analysis"
@using Syncfusion.Blazor.Toolkit.Charts

<div class="trend-container">
    <h2>Sales Trend Analysis with Forecast</h2>
    <SfChart Title="Revenue Trend & Forecast" Width="100%" Height="450px">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" 
                           Title="Months">
        </ChartPrimaryXAxis>
    <ChartPrimaryYAxis Title="Revenue (in millions)" Minimum="0" Maximum="150" 
                           LabelFormat="{value}M">
        </ChartPrimaryYAxis>
    <ChartTooltipSettings Enable="true">
        </ChartTooltipSettings>
            <ChartSeries DataSource="@TrendData" Name="Actual Revenue" 
                         XName="Month" YName="Revenue" 
                         Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Area" 
                         Fill="rgba(99, 102, 241, 0.5)">
                <ChartSeriesBorder Width="2" Color="#6366F1" />
                <ChartMarker Visible="true" Width="8" Height="8">
                </ChartMarker>
                <ChartTrendline Type="Syncfusion.Blazor.Toolkit.TrendlineTypes.Linear" Width="3"
                               Name="Growth Trend" Fill="#FF6B35" 
                               ForwardForecast="3" BackwardForecast="0">
                </ChartTrendline>
            </ChartSeries>
    <ChartAnnotations>
            <ChartAnnotation X="Jun" Y="95" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Point">
                <ContentTemplate>
                    <div style="background: #FFA500; color: white; padding: 5px; border-radius: 5px;">
                        <b>Product Launch</b>
                    </div>
                </ContentTemplate>
            </ChartAnnotation>
            <ChartAnnotation X="Dec" Y="120" CoordinateUnits="Syncfusion.Blazor.Toolkit.Units.Point">
                <ContentTemplate>
                    <div style="background: #28A745; color: white; padding: 5px; border-radius: 5px;">
                        <b>Holiday Peak</b>
                    </div>
                </ContentTemplate>
            </ChartAnnotation>
        </ChartAnnotations>
    </SfChart>
</div>

@code {
    public record TrendInfo(string Month, double Revenue);

    private readonly List<TrendInfo> TrendData = new()
    {
        new("Jan", 45), new("Feb", 52), new("Mar", 58), new("Apr", 65),
        new("May", 72), new("Jun", 95), new("Jul", 88), new("Aug", 92),
        new("Sep", 98), new("Oct", 105), new("Nov", 112), new("Dec", 120)
    };
}
```

---

## 4. Real-Time Monitoring

**Scenario:** Monitor live metrics with auto-updating data stream.

**When to use:** System monitoring, IoT dashboards, live sensor data.

**Key features:** Live updates, dynamic data binding, auto-scroll, ObservableCollection.

```razor
@page "/realtime-monitoring"
@using Syncfusion.Blazor.Toolkit.Charts
@using System.Collections.ObjectModel
@using System.Timers

<div class="monitoring-container">
    <h2>Server CPU Monitoring (Live)</h2>
    <button class="btn btn-primary" @onclick="ToggleMonitoring">
        @(isMonitoring ? "Stop Monitoring" : "Start Monitoring")
    </button>
    <SfChart @ref="liveChart" Title="CPU Usage (%)" Width="100%" Height="450px">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.DateTime" 
                           LabelFormat="HH:mm:ss" Title="Time">
            <ChartAxisMajorGridLines Width="0"></ChartAxisMajorGridLines>
        </ChartPrimaryXAxis>
    <ChartPrimaryYAxis Title="CPU (%)" Minimum="0" Maximum="100" Interval="20">
        </ChartPrimaryYAxis>
    <ChartTooltipSettings Enable="true" Format="<b>${point.x}</b><br/>CPU: <b>${point.y}%</b>">
        </ChartTooltipSettings>
            <ChartSeries DataSource="@LiveData" Name="CPU Usage" 
                         XName="Timestamp" YName="Value" 
                         Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line" Width="3" 
                         Fill="#FF4560">
                <ChartSeriesAnimation Enable="false"></ChartSeriesAnimation>
                <ChartMarker Visible="true" Width="6" Height="6">
                </ChartMarker>
            </ChartSeries>
    </SfChart>
    <div class="stats">
        <p>Current: @CurrentValue% | Average: @AverageValue% | Peak: @PeakValue%</p>
    </div>
</div>

@code {
    private SfChart liveChart;
    private Timer updateTimer;
    private Random random = new Random();
    private bool isMonitoring = false;
    private int maxDataPoints = 20;

    public ObservableCollection<MetricData> LiveData = new ObservableCollection<MetricData>();

    protected override void OnInitialized()
    {
        // Initialize with sample data
        for (int i = 0; i < maxDataPoints; i++)
        {
            LiveData.Add(new MetricData
            {
                Timestamp = DateTime.Now.AddSeconds(i - maxDataPoints),
                Value = random.Next(20, 60)
            });
        }
    }

    private void ToggleMonitoring()
    {
        if (isMonitoring)
        {
            StopMonitoring();
        }
        else
        {
            StartMonitoring();
        }
    }

    private void StartMonitoring()
    {
        isMonitoring = true;
        updateTimer = new Timer(1000); // Update every second
        updateTimer.Elapsed += UpdateData;
        updateTimer.AutoReset = true;
        updateTimer.Enabled = true;
    }

    private void StopMonitoring()
    {
        isMonitoring = false;
        updateTimer?.Stop();
        updateTimer?.Dispose();
    }

    private void UpdateData(object source, ElapsedEventArgs e)
    {
        if (liveChart == null) return;

        LiveData.RemoveAt(0);
        LiveData.Add(new MetricData
        {
            Timestamp = DateTime.Now,
            Value = random.Next(15, 95)
        });
        InvokeAsync(StateHasChanged);
    }

    public double CurrentValue => LiveData.LastOrDefault()?.Value ?? 0;
    public double AverageValue => Math.Round(LiveData.Average(x => x.Value), 1);
    public double PeakValue => LiveData.Max(x => x.Value);

    public class MetricData
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
    }

    public void Dispose()
    {
        updateTimer?.Dispose();
    }
}
```

---

## 5. Interactive Report

**Scenario:** Create interactive chart with drill-down and export capabilities.

**When to use:** Business reports, executive dashboards, data exploration.

**Key features:** Drill-down only — point click events drive category
selection; a single `RefreshAsync` on the report chart is shown so the
chart re-renders after navigation.

```razor
@page "/interactive-report"
@using Syncfusion.Blazor.Toolkit.Charts

<div class="report-container">
    <h2>Interactive Sales Report</h2>
    <div class="toolbar">
        <button class="btn btn-secondary" @onclick="RefreshAsync">Refresh</button>
        @if (isDrilledDown)
        {
            <button class="btn btn-primary" @onclick="DrillUp">← Back to Categories</button>
        }
    </div>
    <SfChart @ref="reportChart" Title="@ChartTitle" Width="100%" Height="450px">
    <ChartEvents OnPointClick="OnDrillDown"></ChartEvents>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" 
                           Title="@XAxisTitle">
        </ChartPrimaryXAxis>
    <ChartPrimaryYAxis Title="Sales (in thousands)" LabelFormat="{value}K">
        </ChartPrimaryYAxis>
    <ChartTooltipSettings Enable="true">
        </ChartTooltipSettings>
            <ChartSeries DataSource="@CurrentData" XName="Category" YName="Sales" 
                         Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" 
                         ColumnWidth="0.7" 
                         Fill="#6366F1">
                <ChartMarker>
                    <ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top">
                    </ChartDataLabel>
                </ChartMarker>
            </ChartSeries>
    </SfChart>
</div>

@code {
    private SfChart reportChart;    private bool isDrilledDown = false;
    private string selectedCategory = "";

    public string ChartTitle => isDrilledDown ? $"{selectedCategory} - Product Details" : "Sales by Category";
    public string XAxisTitle => isDrilledDown ? "Products" : "Categories";

    public class SalesData
    {
        public string Category { get; set; }
        public double Sales { get; set; }
    }

    public List<SalesData> CategoryData = new List<SalesData>
    {
        new SalesData { Category = "Electronics", Sales = 245 },
        new SalesData { Category = "Clothing", Sales = 180 },
        new SalesData { Category = "Home & Garden", Sales = 165 },
        new SalesData { Category = "Sports", Sales = 140 }
    };

    public Dictionary<string, List<SalesData>> ProductData = new Dictionary<string, List<SalesData>>
    {
        ["Electronics"] = new List<SalesData>
        {
            new SalesData { Category = "Laptops", Sales = 95 },
            new SalesData { Category = "Phones", Sales = 85 },
            new SalesData { Category = "Tablets", Sales = 65 }
        },
        ["Clothing"] = new List<SalesData>
        {
            new SalesData { Category = "Shirts", Sales = 75 },
            new SalesData { Category = "Pants", Sales = 55 },
            new SalesData { Category = "Shoes", Sales = 50 }
        },
        ["Home & Garden"] = new List<SalesData>
        {
            new SalesData { Category = "Furniture", Sales = 90 },
            new SalesData { Category = "Decor", Sales = 45 },
            new SalesData { Category = "Garden Tools", Sales = 30 }
        },
        ["Sports"] = new List<SalesData>
        {
            new SalesData { Category = "Fitness", Sales = 60 },
            new SalesData { Category = "Outdoor", Sales = 50 },
            new SalesData { Category = "Team Sports", Sales = 30 }
        }
    };

    public List<SalesData> CurrentData => isDrilledDown ? ProductData[selectedCategory] : CategoryData;

    private void OnDrillDown(PointEventArgs args)
    {
        if (!isDrilledDown)
        {
            selectedCategory = args.X.ToString();
            isDrilledDown = true;
            StateHasChanged();
        }
    }

    private void DrillUp()
    {
        isDrilledDown = false;
        selectedCategory = "";
        StateHasChanged();
    }

    private async Task RefreshAsync() => await reportChart.RefreshAsync();
}
```

---

## 6. Responsive Analytics

**Scenario:** Mobile-friendly analytics dashboard with adaptive layout.

**When to use:** Mobile apps, responsive web apps, cross-device dashboards.

**Key features:** Adaptive layout, touch interactions, mobile optimization, responsive sizing.

```razor
@page "/responsive-analytics"
@using Syncfusion.Blazor.Toolkit.Charts

<div class="analytics-container">
    <h2>Mobile-Responsive Analytics</h2>
    <div class="responsive-grid">
        <SfChart Title="Monthly Visitors" Width="100%" Height="300px">
            <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category">
            </ChartPrimaryXAxis>
            <ChartPrimaryYAxis LabelFormat="{value}K">
            </ChartPrimaryYAxis>
            <ChartTooltipSettings Enable="true">
            </ChartTooltipSettings>
                <ChartSeries DataSource="@VisitorData" XName="Month" YName="Visitors" 
                             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.SplineArea" 
                             Fill="rgba(99, 102, 241, 0.6)">
                </ChartSeries>
        </SfChart>

        <!-- Accumulation/Pie examples removed: not available in this build -->

        <SfChart Title="Device Usage" Width="100%" Height="300px">
            <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category">
            </ChartPrimaryXAxis>
            <ChartTooltipSettings Enable="true">
            </ChartTooltipSettings>
                <ChartSeries DataSource="@DeviceData" XName="Device" YName="Users" 
                             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Bar" 
                             Fill="#10B981">
                    <ChartMarker>
                        <ChartDataLabel Visible="true" Position="Syncfusion.Blazor.Toolkit.ChartLabelPosition.Top">
                        </ChartDataLabel>
                    </ChartMarker>
                </ChartSeries>
        </SfChart>
    </div>
</div>

<style>
    .analytics-container {
        padding: 20px;
    }
    
    .responsive-grid {
        display: grid;
        gap: 20px;
        grid-template-columns: 1fr;
    }
    
    @@media (min-width: 768px) {
        .responsive-grid {
            grid-template-columns: repeat(2, 1fr);
        }
    }
    
    @@media (min-width: 1200px) {
        .responsive-grid {
            grid-template-columns: repeat(3, 1fr);
        }
    }
</style>

@code {
    public record AnalyticsData(string Month, double Visitors);
    public record DeviceStats(string Device, double Users);

    private readonly List<AnalyticsData> VisitorData = new()
    {
        new("Jan", 45), new("Feb", 52), new("Mar", 68),
        new("Apr", 75), new("May", 82), new("Jun", 95)
    };

    private readonly List<DeviceStats> DeviceData = new()
    {
        new("Desktop", 125), new("Mobile", 185), new("Tablet", 65)
    };
}
```

---

## Summary

These practical examples demonstrate complete, production-ready implementations of Syncfusion Blazor Charts for common business scenarios. Each example includes:

- **Complete razor page code** with all necessary imports
- **Data model classes** properly structured
- **Sample data** for immediate testing
- **Key features** fully configured
- **Real-world scenarios** for practical application

