## Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Installation Steps](#installation-steps)
   - [Option 1: Visual Studio](#option-1-visual-studio)
   - [Option 2: Visual Studio Code](#option-2-visual-studio-code)
   - [Option 3: .NET CLI](#option-3-net-cli)
- [Configuration](#configuration)
   - [Step 1: Add Namespaces](#step-1-add-namespaces)
   - [Step 2: Register Syncfusion Services](#step-2-register-syncfusion-services)
   - [Step 3: Add Script Reference](#step-3-add-script-reference)
- [Creating Your First Chart](#creating-your-first-chart)
   - [Basic Empty Chart](#basic-empty-chart)
   - [Chart with Data](#chart-with-data)
   - [Adding Titles](#adding-titles)
   - [Adding Data Labels](#adding-data-labels)
   - [Enabling Tooltips](#enabling-tooltips)
   - [Adding Legend](#adding-legend)
- [Complete Example](#complete-example)
- [Running the Application](#running-the-application)
   - [Visual Studio](#visual-studio)
   - [Visual Studio Code / .NET CLI](#visual-studio-code-net-cli)
- [Multiple Series Example](#multiple-series-example)
- [Common Setup Issues](#common-setup-issues)
   - [Issue: Chart Not Rendering](#issue-chart-not-rendering)
   - [Issue: No Data Displayed](#issue-no-data-displayed)
   - [Issue: Interactive Features Inactive Under Static SSR](#issue-interactive-features-inactive-under-static-ssr)
- [Next Steps](#next-steps)
- [Additional Resources](#additional-resources)

# Getting Started with Blazor Chart Component

> **Verified against source** — enum members cross-checked against
> `src/Base/Enumeration.cs`; method surface and render-mode behaviour
> cross-checked against `src/Components/.../SfChart.razor.*.cs`. When
> this and the source code disagree, **source wins** — file a backlog
> task to update this file. Last source audit: **2026-08-24**.

This guide covers everything you need to set up and create your first Blazor Chart component, including installation, configuration, and basic chart implementation.

## Overview

The Syncfusion Blazor Chart component can be integrated into Blazor Server or WebAssembly applications. This guide walks through the setup process using Visual Studio, Visual Studio Code, or .NET CLI.

## Prerequisites

- **.NET SDK:** Version 6.0 or later
- **Development Environment:** Visual Studio 2022, Visual Studio Code, or .NET CLI
- **Blazor App:** Server, WebAssembly, or Hybrid app template
- **System Requirements:** Check [Syncfusion Blazor system requirements](https://blazor.syncfusion.com/documentation/system-requirements)

## Installation Steps

### Option 1: Visual Studio

#### Step 1: Create Blazor App
1. Open Visual Studio 2022
2. Create new project → **Blazor Web App** template
3. Configure project name and location
4. Select **Interactive render mode** (Server, WebAssembly, or Auto)
5. Choose **Interactivity location** (Global or Per page/component)

#### Step 2: Install NuGet Package
1. Right-click project → **Manage NuGet Packages**
2. Search for `Syncfusion.Blazor.Toolkit`
3. Install the package

**Or use Package Manager Console:**
```powershell
Install-Package Syncfusion.Blazor.Toolkit
```

### Option 2: Visual Studio Code

#### Step 1: Create Blazor App
```bash
dotnet new blazor -o BlazorChartApp -int Server
cd BlazorChartApp
```

#### Step 2: Install NuGet Package
```bash
dotnet add package Syncfusion.Blazor.Toolkit
dotnet restore
```

### Option 3: .NET CLI

#### Step 1: Verify .NET SDK
```bash
dotnet --version
```

#### Step 2: Create Blazor App
```bash
dotnet new blazor -o BlazorChartApp -int Server
cd BlazorChartApp
```

#### Step 3: Install Package
```bash
dotnet add package Syncfusion.Blazor.Toolkit
dotnet restore
```

## Configuration

### Step 1: Add Namespaces

Open `_Imports.razor` and add:

```razor
@using Syncfusion.Blazor.Toolkit
@using Syncfusion.Blazor.Toolkit.Charts
```

### Step 2: Register Syncfusion Services

In `Program.cs`, register the toolkit once. The chart (and any other
toolkit components used in the same app) depend on this registration;
do **not** add any separate component-package registration.

```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register the Syncfusion Blazor Toolkit (one call per project — for Auto/WASM,
// register in BOTH the server Program.cs and .Client Program.cs).
builder.Services.AddSyncfusionBlazorToolkit();

var app = builder.Build();
```

> **JS modules are loaded by the chart itself.** Once `Chart.OnAfterRenderAsync`
> runs, the chart imports these modules from the NuGet static content
> (served by the host's static-files middleware, NOT by
> `_framework/blazor.web.js`):
>
> - `_content/Syncfusion.Blazor.Toolkit/scripts/svgbase.js`
> - `_content/Syncfusion.Blazor.Toolkit/scripts/touch.js`
> - `_content/Syncfusion.Blazor.Toolkit/scripts/animation.js`
> - `_content/Syncfusion.Blazor.Toolkit/scripts/chart.js`
>
> Do **not** add manual `<script>` tags for `syncfusion-blazor.min.js`,
> `sf-chart.min.js`, or anything under `_content/Syncfusion.Blazor.*`.
> For Auto/WASM with prerendering, call `AddSyncfusionBlazorToolkit()` in
> **both** `Program.cs` files (the server bootstrap and the `.Client` bootstrap).

## Creating Your First Chart

### Basic Empty Chart

Create a new Razor page (e.g., `ChartDemo.razor`) and add:

```razor
@page "/chart-demo"
@using Syncfusion.Blazor.Toolkit.Charts

<SfChart>
</SfChart>
```

This renders an empty chart container.

### Chart with Data

#### Step 1: Define Data Model

```razor
@code {
    // Records are recommended for chart row types — they're immutable,
    // concise, and readable. Class-with-mutable-strings still works.
    public record SalesInfo(string Month, double SalesValue);

    private readonly List<SalesInfo> SalesData = new()
    {
        new("Jan", 35), new("Feb", 28), new("Mar", 34),
        new("Apr", 32), new("May", 40), new("Jun", 32), new("Jul", 35)
    };
}
```

#### Step 2: Configure Chart with Series

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category">
    </ChartPrimaryXAxis>
    
    <ChartSeries DataSource="@SalesData" 
                     XName="Month" 
                     YName="SalesValue" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        </ChartSeries>
</SfChart>
```

**Key Properties:**
- `ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"` - Treats X-axis as discrete categories
- `DataSource` - Binds the data collection
- `XName` - Property name for X-axis values (`Month`)
- `YName` - Property name for Y-axis values (`SalesValue`)
- `Type` - Chart type (`Column`, `Line`, `Bar`, etc.)

### Adding Titles

Add titles to the chart and axes for context:

```razor
<SfChart Title="Sales Analysis">
    <ChartPrimaryXAxis Title="Month" 
                        ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category">
    </ChartPrimaryXAxis>
    
    <ChartPrimaryYAxis Title="Sales in Dollar">
    </ChartPrimaryYAxis>
    
    <ChartSeries DataSource="@SalesData" 
                     XName="Month" 
                     YName="SalesValue" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        </ChartSeries>
</SfChart>
```

### Adding Data Labels

Display values on data points:

```razor
<SfChart Title="Sales Analysis">
    <ChartPrimaryXAxis Title="Month" ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    <ChartPrimaryYAxis Title="Sales in Dollar"></ChartPrimaryYAxis>
    
    <ChartSeries DataSource="@SalesData" 
                     XName="Month" 
                     YName="SalesValue" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
            <ChartMarker>
                <ChartDataLabel Visible="true"></ChartDataLabel>
            </ChartMarker>
        </ChartSeries>
</SfChart>
```

### Enabling Tooltips

Show data on hover:

```razor
<SfChart Title="Sales Analysis">
    <ChartPrimaryXAxis Title="Month" ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    <ChartPrimaryYAxis Title="Sales in Dollar"></ChartPrimaryYAxis>
    
    <ChartTooltipSettings Enable="true"></ChartTooltipSettings>
    
    <ChartSeries DataSource="@SalesData" 
                     XName="Month" 
                     YName="SalesValue" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        </ChartSeries>
</SfChart>
```

### Adding Legend

Enable legend for multi-series charts:

```razor
<SfChart Title="Sales Analysis">
    <ChartPrimaryXAxis Title="Month" ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    <ChartPrimaryYAxis Title="Sales in Dollar"></ChartPrimaryYAxis>
    
    <ChartLegendSettings Visible="true"></ChartLegendSettings>
    
    <ChartSeries DataSource="@SalesData" 
                     Name="Sales"
                     XName="Month" 
                     YName="SalesValue" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        </ChartSeries>
</SfChart>
```

**Note:** The `Name` property sets the legend text for each series.

## Complete Example

Here's a complete working example combining all elements:

```razor
@page "/chart-demo"
@using Syncfusion.Blazor.Toolkit.Charts

<h3>Monthly Sales Analysis</h3>

<SfChart Title="Sales Analysis" Width="90%">
    <ChartPrimaryXAxis Title="Month" 
                        ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category">
    </ChartPrimaryXAxis>
    
    <ChartPrimaryYAxis Title="Sales in Dollar" 
                        Minimum="0" 
                        Maximum="50" 
                        Interval="10">
    </ChartPrimaryYAxis>
    
    <ChartTooltipSettings Enable="true"></ChartTooltipSettings>
    <ChartLegendSettings Visible="true"></ChartLegendSettings>
    
    <ChartSeries DataSource="@SalesData" 
                     Name="Sales"
                     XName="Month" 
                     YName="SalesValue" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column"
                     Fill="#0066CC">
            <ChartMarker>
                <ChartDataLabel Visible="true"></ChartDataLabel>
            </ChartMarker>
        </ChartSeries>
</SfChart>

@code {
    public record SalesInfo(string Month, double SalesValue);

    private readonly List<SalesInfo> SalesData = new()
    {
        new("Jan", 35), new("Feb", 28), new("Mar", 34),
        new("Apr", 32), new("May", 40), new("Jun", 32), new("Jul", 35)
    };
}
```

## Running the Application

### Visual Studio
- Press `Ctrl + F5` (Windows) or `⌘ + F5` (macOS)

### Visual Studio Code / .NET CLI
```bash
dotnet run
```

Navigate to the chart page URL (e.g., `https://localhost:5001/chart-demo`)

## Multiple Series Example

To compare multiple datasets:

```razor
<SfChart Title="Product Comparison">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category"></ChartPrimaryXAxis>
    
    <ChartLegendSettings Visible="true"></ChartLegendSettings>
    <ChartTooltipSettings Enable="true"></ChartTooltipSettings>
    
    <ChartSeries DataSource="@Product1Data" 
                     Name="Product A"
                     XName="Month" 
                     YName="Sales" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        </ChartSeries>
        
    <ChartSeries DataSource="@Product2Data" 
                     Name="Product B"
                     XName="Month" 
                     YName="Sales" 
                     Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        </ChartSeries>
</SfChart>

@code {
    public record ProductSales(string Month, double Sales);

    private readonly List<ProductSales> Product1Data = new()
    {
        new("Jan", 35), new("Feb", 28), new("Mar", 34)
    };

    private readonly List<ProductSales> Product2Data = new()
    {
        new("Jan", 20), new("Feb", 35), new("Mar", 30)
    };
}
```

## Common Setup Issues

### Issue: Chart Not Rendering (truly empty container)

**Cause:** Missing service registration, wrong render mode, or missing namespace

**Solution:**
1. Verify `AddSyncfusionBlazorToolkit()` (not `AddSyncfusionBlazor`) is in `Program.cs`
2. For Auto/WASM with prerender, call it in **both** `Program.cs` files
3. Verify `using Syncfusion.Blazor.Toolkit.Charts;` is in `_Imports.razor` (or `@using` at top of page)
4. Confirm the hosting page uses an **interactive** render mode (`InteractiveServer`, `InteractiveWebAssembly`, or `InteractiveAuto`) for JS-driven features (tooltip, zoom, crosshair, selection, export). The SVG frame renders in Static SSR at 600×450 — only interactivity requires Server/WASM/Auto. If the host stays Static, place the chart in an interactive child component. See `../SKILL.md` Step 3.

### Issue: No Data Displayed

**Cause:** Incorrect property binding

**Solution:**
- Verify `XName` and `YName` match data model properties exactly (case-sensitive)
- Ensure `DataSource` is populated before rendering

### Issue: Interactive Features Inactive Under Static SSR

**Cause:** The JS modules that drive tooltip/crosshair/zoom/selection
only load once an interactive circuit is active (`OnAfterRenderAsync`
runs interop imports). In pure Static SSR the chart renders the SVG
frame, but interactivity never wires.

**Solution:** Either switch the host page to an interactive render mode
(`@rendermode InteractiveServer`, `InteractiveWebAssembly`, or
`InteractiveAuto`), or wrap the chart in an interactive child component
so the parent's static-render behaviour doesn't block JS module load.

## Next Steps

After setting up your first chart:
- Explore different chart types (Line, Area, Column/Bar, Scatter, Bubble, Spline)
- Customize axes (numeric, datetime, logarithmic)
- Add interactive features (zooming, selection, crosshair)
- Implement data binding with DataManager
- Style with themes and custom colors
- Add annotations and markers

## Toolkit Registers `SyncfusionBlazorToolkitService` as Scoped

`AddSyncfusionBlazorToolkit()` registers
`SyncfusionBlazorToolkitService` as **scoped** — the service
`SfBaseComponent` reaches for `IsDeviceMode` and `IsJsInProcess`. Don't
re-register it; one call per project is enough. Confirmed sites:

- `samples/Blazor.Toolkit.Samples/Program.cs`
- `samples/Blazor.Toolkit.Samples.Client/Program.cs`

For Auto/WASM with prerendering, the registration must appear in **both**
`Program.cs` files. The Server bootstrap provides the service during
prerender; the `.Client` bootstrap re-provides it for the interactive
circuit.

## Render-mode matrix at a glance

| Data source | Render mode | Why |
|-------------|-------------|-----|
| Static `List<T>` baked into the page | Server, WebAssembly, or Auto (interactive) | SSR renders the SVG frame at defaults (600×450); JS loads interactively for tooltips, zoom, export |
| Pure SSR (no JS) | Static SSR (frame only) | Tooltip / crosshair / selection / zoom JS features need interactive |
| `IQueryable` / live-streaming binding | Server, WebAssembly, or Auto | Needs `OnAfterRenderAsync` to apply updates — Static SSR can't refresh |
| `SfDataManager` calling a remote API | Auto or WebAssembly | The API call crosses the runtime boundary |
| Toolkit services available app-wide | Server (one DI container) / Auto (register in **both** projects) | Render-mode aware |

`SfChart.razor.OnInitialized` short-circuits to `600×450` under
`IsStaticServerRendering()`:

```csharp
// From SfChart.razor.OnInitialized
if (IsStaticServerRendering())
{
    _svgWidth = "600";
    _svgHeight = "450";
}
```

The JS module loader (`chart.js`, `svgbase.js`, `touch.js`,
`animation.js` under `_content/Syncfusion.Blazor.Toolkit/scripts/*`)
runs only once the interactive circuit is wired. Tooltip, crosshair,
zoom, and selection are inert until then. If the host page must stay
Static SSR, place the chart in an interactive child component
(per-page or per-component `@rendermode`).

## SCSS pipeline — focus and interaction rules only

`src/wwwroot/styles/chart.scss` is wired into the combined `fluent.scss`
via `componentThemeOrder` in `gulpfile.js`. It provides **interactive /
structural** styles that aren't theme colors:

- `:focus-visible` outline (`.e-chart-focused`)
- `.e-legend-cursor`, `.e-legend-pointer`
- `.e-series-outline`, `.e-trendline-outline` (suppress default browser focus rectangles)
- `.e-stacklabel-visible` / `.e-stacklabel-hidden`
- `.e-lastlabel-visible` / `.e-lastlabel-hidden`

Theme colors (`Fluent`, `FluentDark`) come from the `Theme` parameter,
not from CSS variables. The first build runs `gulp blazor-toolkit-themes`
automatically (see `codestudio-instructions.md` Build & Test Discipline);
later builds skip it. If chart focus / legend-cursor rules appear stale,
run `gulp blazor-toolkit-themes` once from the repo root.

