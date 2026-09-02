---
license: MIT
name: syncfusion-blazor-toolkit-charts
description: >
  Implement the Syncfusion Blazor Toolkit SfChart component (namespace
  Syncfusion.Blazor.Toolkit.Charts; shipped inside NuGet package
  Syncfusion.Blazor.Toolkit — there is no separate
  Syncfusion.Blazor.Toolkit.Charts package).
  USE FOR: line, column, bar, area, scatter, bubble, spline, and stacking
  series; category / numeric / DateTime / logarithmic axes; dual axes and
  multiple panes; tooltip, crosshair, zoom, pan, selection; trend lines,
  strip lines; ARIA, keyboard, RTL, i18n; live data via SfDataManager;
  data editing.
  REQUIRES interactive render mode for tooltip / crosshair / zoom /
  selection / live updates. Static SSR renders the SVG frame only
  (default 600×450); for interactivity place the chart in an interactive
  child component or switch the page to Server / WebAssembly / Auto.
  Data binding requires List<T> (or SfDataManager) properties whose names
  match the XName / YName / Size strings exactly (case-sensitive).
  DO NOT USE FOR: any control outside Syncfusion.Blazor.Toolkit (Grid,
  Scheduler, Diagram, Maps belong to other Syncfusion products — stop
  and ask); date / time inputs (syncfusion-blazor-toolkit-calendars);
  form fields, checkbox, radio, switch (syncfusion-blazor-toolkit-inputs);
  buttons only (syncfusion-blazor-toolkit-buttons); SfDialog or
  SfTooltip hosting a chart (syncfusion-blazor-toolkit-popups);
  loading spinner overlay (syncfusion-blazor-toolkit-notifications).
compatibility: ".NET 8+, render-modes: Static SSR, Server, WebAssembly, Auto — interactive features require an interactive circuit. Static SSR renders the SVG frame only."
metadata:
metadata:
  author: "Syncfusion Inc"
  version: "1.0.0"
---

# Syncfusion Blazor Toolkit — Charts

> ⚙️ **Render mode:** interactive features (tooltip, crosshair, zoom, pan,
> selection, live data) require an **interactive** render mode — Server,
> WebAssembly, or Auto. **Static SSR renders the SVG frame only** (default
> 600×450); the chart JS modules load from
> `_content/Syncfusion.Blazor.Toolkit/scripts/*` and need an interactive
> circuit. Read `AGENTS.md` before picking a strategy.

**NuGet:** `Syncfusion.Blazor.Toolkit` *(the `Syncfusion.Blazor.Toolkit.Charts`
package referenced in legacy docs does not exist — the namespace
`Syncfusion.Blazor.Toolkit.Charts` lives inside `Syncfusion.Blazor.Toolkit`)*
**Namespace:** `Syncfusion.Blazor.Toolkit.Charts`
**Component:** `SfChart`

## Purpose

Implement `SfChart` to render any data visualization the `Syncfusion.Blazor.Toolkit`
package supports. Outcome: a working chart in a Blazor Server, WebAssembly,
or Auto app, bound to a `List<T>` or `SfDataManager`, with the common
gotchas (enum fully-qualified, `ChartStriplines` plural, `ChartCrosshairLine`
narrow surface) already handled.

## When NOT to use this skill

Skill conflicts and routing errors are the #1 source of bad agent output.
Re-route before you start.

| If the task is… | Use this skill instead |
|-----------------|------------------------|
| Single date/time picker (`SfDatePicker`, `SfCalendar`, `SfTimePicker`) | `syncfusion-blazor-toolkit-calendars` |
| TextBox, NumericTextBox, CheckBox, Radio, Switch | `syncfusion-blazor-toolkit-inputs` |
| Buttons, button groups, toggle buttons | `syncfusion-blazor-toolkit-buttons` |
| Modal dialogs (SfDialog) or hover tooltips (SfTooltip) | `syncfusion-blazor-toolkit-popups` |
| Loading spinners overlaid on a chart (SfSpinner) | `syncfusion-blazor-toolkit-notifications` |
| Grid, Scheduler, Diagram, Maps, Kanban (different Syncfusion products) | No skill in this repo — stop and ask |

If the task mixes a chart + another control, load this skill **and** the
sibling — never start a chart inside a `SfDialog` without reading
`syncfusion-blazor-toolkit-popups`.

### When you need to compose `SfChart` with another control

| User phrase | Pair this skill with | Why the sibling is needed |
|-------------|----------------------|--------------------------|
| "Refresh the chart" / "Re-fetch button" | `syncfusion-blazor-toolkit-buttons` | `SfButton` with `OnClick="async () => await ChartRef.RefreshAsync()"` |
| "PNG export" / "Download chart" | `syncfusion-blazor-toolkit-buttons` + `references/api-reference.md` | Toolbar item triggers a built-in export method via `@ref` |
| "Show spinner while data loads" | `syncfusion-blazor-toolkit-notifications` | Overlay `SfSpinner` on the chart container; toggle via `Visible` |
| "Filter the chart by date range" | `syncfusion-blazor-toolkit-calendars` | Two `SfDatePicker`s bound to filter parameters drive the data source |
| "Confirm before deleting a series" | `syncfusion-blazor-toolkit-popups` | `SfDialog` hosts the chart and gates the destructive click |
| "Search by name" | `syncfusion-blazor-toolkit-inputs` | `SfTextBox OnChange` filters the `List<T>` bound to `DataSource` |
| "Light/dark mode toggle" | `references/appearance-styling.md` | Drives `Theme` on `SfChart` from a flag in `@code` (no extra control) |

The chart is the source of truth for **`SfChart` itself**; the sibling
skill owns the surrounding control. Load both.

## Step 1 — Read project instructions

Before scaffolding or modifying `SfChart`, read `codestudio-instructions.md`
at the repo root. Then read these four files under `.codestudio/knowledge/`
in order:

1. `.codestudio/knowledge/architecture.md` — module layout, per-component
   partial-class split, JS interop pattern, SCSS pipeline.
2. `.codestudio/knowledge/conventions.md` — naming, Allman braces, no
   `private`, doc-comments, file organization.
3. `.codestudio/knowledge/stack.md` — `net8.0` / `net9.0` / `net10.0`
   targets, bUnit + Playwright test stack.
4. `.codestudio/knowledge/boundaries.md` — Always do / Ask first / Never
   do rules. **Hard-refuse any action that violates the "Never do" list.**
   These rules supersede anything in this skill.

(Readers of this skill from a consumer-app workspace — i.e. writing a Blazor
app that *uses* `Syncfusion.Blazor.Toolkit` rather than modifying the toolkit
itself — can skip this step.)

## Step 2 — Pick the right series type

The `ChartSeriesType` enum currently exposes **21 members** (declared in
order at `src/Base/Enumeration.cs`):

`Line`, `Column`, `Area`, `Bar`, `StackingColumn`, `StackingArea`,
`StackingLine`, `StackingBar`, `StackingStepArea`, `StepLine`, `StepArea`,
`SplineArea`, `Scatter`, `Spline`, `StackingColumn100`, `StackingBar100`,
`StackingLine100`, `StackingArea100`, `Bubble`, `MultiColoredLine`,
`MultiColoredArea`.

Pick from this discriminator, not from a marketing count:

| Data shape | Pick | `ChartSeriesType` |
|------------|------|-------------------|
| Categorical comparison (one bar per item) | Column (vertical) or Bar (horizontal) | `Column`, `Bar` |
| Trend over time/sequence | Line or Spline | `Line`, `Spline` |
| Magnitude over time (filled) | Area or SplineArea | `Area`, `SplineArea` |
| Step changes | Step Line / Step Area | `StepLine`, `StepArea` |
| Step accumulations (filled, stepped) | Stacking Step Area | `StackingStepArea` |
| Part-to-whole, stacked | StackingColumn/Bar/Area/Line + the `…100` variants | `StackingColumn`, `StackingBar`, `StackingArea`, `StackingLine`, `StackingColumn100`, `StackingBar100`, `StackingArea100`, `StackingLine100` |
| Two-variable correlation, point cloud | Scatter | `Scatter` |
| Three-variable (size matters) | Bubble | `Bubble` |
| Per-segment color | MultiColoredLine / MultiColoredArea | `MultiColoredLine`, `MultiColoredArea` |

Types in this repo today: **21** (line + column + area + bar + steps +
splines + scatter + bubble + multi-coloured + stacking variants). Pie,
candle / OHLC / HiLo, polar / radar, range / area-spline variants are
**not** part of the current toolkit — if the task requires them, stop
and confirm with the user before scaffolding. Detail lives in
`references/chart-types-specialized.md`.

## Step 3 — Pick render mode and register services

Charts are interactive components: tooltip / crosshair / zoom / selection
need an interactive circuit. Pick from the table below; for the
`Program.cs` registration snippet and SCSS pipeline plumbing, load
`references/getting-started.md`.

| Data source | Render mode | Why |
|-------------|-------------|-----|
| Static `List<T>` baked into the page | Server, WebAssembly, or Auto (interactive) | SSR renders the SVG frame at defaults (600×450); JS loads interactively for tooltips, zoom, export |
| Pure SSR (no JS) | Works for static-frame rendering only | Tooltip / crosshair / selection / zoom JS features need interactive |
| `IQueryable` / live-streaming binding | Server, WebAssembly, or Auto | Needs `OnAfterRenderAsync` to apply updates — Static SSR can't refresh |
| `SfDataManager` calling a remote API | Auto or WebAssembly | The API call crosses the runtime boundary |
| Toolkit services available app-wide | Server (one DI container) / Auto (register in **both** projects) | Render-mode aware |

Minimal registration (no JS script tags needed — the chart loads its JS
modules from `_content/Syncfusion.Blazor.Toolkit/scripts/*` itself):

```csharp
// Program.cs
using Syncfusion.Blazor.Toolkit;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSyncfusionBlazorToolkit(options =>
{
    options.EnableRtl = false;
    options.Animation = GlobalAnimationMode.Enable;
});
```

For Auto/WASM with prerendering, call `AddSyncfusionBlazorToolkit()` in
**both** `Program.cs` files (server bootstrap and `.Client` bootstrap).
Full walkthrough, including the JS-module loader and the SCSS pipeline
notes: `references/getting-started.md`.

## Step 3.5 — Apply a theme (C# enum, *not* CSS variables)

`SfChart` has its own in-C# theme pipeline, declared in
`src/Base/Enumeration.cs`. **Theme colors come from the C# `Theme`
parameter** — there is no CSS-variable layer. The `Theme` enum currently
exposes **two values**:

| `Theme` value | When to use |
|---|---|
| `Syncfusion.Blazor.Toolkit.Theme.Fluent` *(default)* | Light-mode app, default look |
| `Syncfusion.Blazor.Toolkit.Theme.FluentDark` | Dark-mode app, darkmode toggle |

```razor
<SfChart Title="Sales Analysis" Theme="Syncfusion.Blazor.Toolkit.Theme.FluentDark">
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartSeries DataSource="@SalesData"
                 XName="Month" YName="Revenue"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" />
</SfChart>
```

> **Older docs reference `Theme.Material`, `Theme.Bootstrap5`,
> `Theme.Tailwind`, or `Theme.HighContrast`.** Those values are **not
> part of `Syncfusion.Blazor.Toolkit.Theme`**. Non-existent values
> compile to silence and render nothing — pick `Theme.Fluent` or
> `Theme.FluentDark`.

Runtime theme switching (light/dark toggle):

```razor
<SfChart Theme="@(_isDark ? Syncfusion.Blazor.Toolkit.Theme.FluentDark
                          : Syncfusion.Blazor.Toolkit.Theme.Fluent)">
    …
</SfChart>
```

`Theme` re-renders the chart in place — no `RefreshAsync` call required.
SCSS pipeline notes (`:focus-visible`, legend-cursor, stack-label rules)
live in `references/getting-started.md`.

## Step 4 — Gather inputs

1. **Render mode**: SSR renders the SVG frame at default 600×450; the
   JS module loader (chart.js, svgbase.js, touch.js, animation.js) is
   wired in `SfChart.razor.LifeCycle.cs` and runs once an interactive
   circuit is active. Tooltip / crosshair / zoom / selection only work
   under Server, WebAssembly, or Auto render modes. A pure-Static-SSR
   parent will render the chart frame but interactions stay inert.
   If a host page is Static SSR, place the chart in an interactive
   child component (per-page or per-component `@rendermode`).
2. **`Program.cs` registration**: confirm `AddSyncfusionBlazorToolkit()`
   has been added (Step 3).
3. **NuGet**: `Syncfusion.Blazor.Toolkit` is referenced in the project
   file. (The NuGet ID is *not* `Syncfusion.Blazor.Toolkit.Charts` —
   that name only appears in the namespace.)
4. **Import**: `using Syncfusion.Blazor.Toolkit.Charts` (typically via
   `_Imports.razor`).
5. **Where to drop it**: full-page route (`Pages/Foo.razor`) inside an
   interactive render mode.
6. **Data shape**: `record`/`class` with primitive `XName`/`YName`/`Size`
   properties (case-sensitive). Property names must match the
   `XName`/`YName` strings exactly.
7. **Container size**: parent CSS usually needs an explicit height
   (e.g. `height: 400px`); % sizes need a sized parent.

## Step 5 — Scaffold the chart

The order matters. After Steps 1–4, compose in this sequence:

1. `<SfChart>` + `<ChartPrimaryXAxis>` + `<ChartPrimaryYAxis>`.
2. Set `ValueType` on every axis (Category / Double / DateTime / Logarithmic).
3. Bind `<ChartSeries DataSource … XName … YName … Type …>`.
4. Layer interactivity only when asked (Step 6).
5. Validate (Step 8).

```razor
@page "/chart-demo"
@using Syncfusion.Blazor.Toolkit.Charts

<SfChart Title="Sales Analysis">
    <ChartPrimaryXAxis Title="Month"
                       ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartPrimaryYAxis Title="Sales in $" />

    <ChartTooltipSettings Enable="true" />
    <ChartLegendSettings Visible="true" />

    <ChartSeries DataSource="@SalesData"
                 XName="Month" YName="Revenue"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
        <ChartMarker><ChartDataLabel Visible="true" /></ChartMarker>
    </ChartSeries>
</SfChart>

@code {
    public class SalesPoint
    {
        public string Month { get; set; } = string.Empty;
        public double Revenue { get; set; }
    }

    private readonly List<SalesPoint> SalesData = new()
    {
        new SalesPoint { Month = "Jan", Revenue = 35 },
        new SalesPoint { Month = "Feb", Revenue = 28 },
        new SalesPoint { Month = "Mar", Revenue = 34 },
        new SalesPoint { Month = "Apr", Revenue = 32 },
        new SalesPoint { Month = "May", Revenue = 40 },
        new SalesPoint { Month = "Jun", Revenue = 32 }
    };
}
```

## Step 6 — Layer interactivity only when asked

Tooltips, crosshair, zoom, pan, selection, legend, and secondary axes are
**not** part of the default chart. Add them only when the user explicitly
asks for hover-info, follow-cursor, zoom/pan, click-to-highlight, etc.

| Setting element | Trigger phrase |
|-----------------|----------------|
| `<ChartTooltipSettings>` | "tooltip", "hover info", "labels on hover" |
| `<ChartCrosshairSettings>` | vertical/horizontal follow-cursor line |
| `<ChartZoomSettings>` | pinch / wheel zoom, or selection-zoom rectangle |
| `SfChart.SelectionMode="ChartSelectionMode.Point"` (root parameter) | click-to-highlight a point or region |
| `<ChartLegendSettings>` | 2+ series, each with a different `Name` |

Detail: `references/interactive-features.md`. Visual fittings — markers,
data labels, annotations, gradients: `references/visual-elements.md`.

## Step 7 — Apply critical API rules (load before writing code)

These contradict the agent default. Each rule cites the source it was
verified against.

### 7.1 Enums are always fully qualified

The un-prefixed short names resolve correctly **only** if you also add
`@using Syncfusion.Blazor.Toolkit;`. The prefix is required for the agent
default where the chart namespace is the only one imported.

```razor
<!-- correct -->
<ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
<ChartSeries Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" />

<!-- wrong — fails compilation in the default sample layout -->
<ChartSeries Type="ChartSeriesType.Column" />
```

Always use `Syncfusion.Blazor.Toolkit.` for `ValueType`, `ChartSeriesType`,
`LegendPosition`, `ZoomMode`, `ChartShape`, `TrendlineTypes`, `Theme`,
`EmptyPointMode`, `ToolbarMode`, `ZIndexPosition`, `ChartSelectionMode`,
`HighlightMode`, `ChartRangePadding`, `LineType`,
`EdgeLabelPlacement`, `LabelPlacement`, `LabelIntersectAction`, `Units`,
`Regions`, `Alignment`, `SplineType`, `StepPosition`, `Segment`,
`Orientation`, `RangeIntervalType`, `ToolbarItems`, `SelectionPattern`.

> `TextWrap` is the **odd one out**: it lives in the root
> `Syncfusion.Blazor` namespace (not `Syncfusion.Blazor.Toolkit`). Pages
> that set `TextWrap="…"` on `ChartLegendSettings` need either
> `@using Syncfusion.Blazor;` or the fully qualified
> `Syncfusion.Blazor.TextWrap.Wrap`. Used unqualified, with only the
> toolkit usings in scope, the snippet will fail with **CS0103**.

### 7.2 Striplines are plural: `<ChartStriplines>` → `<ChartStripline>`

The non-existent `<ChartAxisStripLineSettings>` will compile to silence
and render nothing. Always wrap in plural collection.

### 7.3 `ChartCrosshairLine` only takes `Width` + `Color`

DashArray, LineType, opacity are **not** on this child. Use the parent's
`DashArray` / `LineType`.

```razor
<ChartCrosshairSettings Enable="true" DashArray="2,3" LineType="LineType.Both">
    <ChartCrosshairLine Width="1.5" Color="#444" />
</ChartCrosshairSettings>
```

### 7.4 Bubble uses `Size="PropertyName"` — not `ZName`

```razor
<ChartSeries DataSource="@PopulationData"
             XName="LiteracyRate" YName="GrowthRate"
             Size="Population"                              <!-- third dimension -->
             Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Bubble" />
```

### 7.5 Use `@ref` for programmatic methods

Public async methods on `SfChart`: `RefreshAsync`, `ShowTooltipAsync`,
`HideTooltipAsync`, `ShowCrosshairAsync`, `HideCrosshairAsync`. Public
sync: `Sort`, `ClearSort`, `ClearSelection`, `PreventRender`. Call them
through `@ref="ChartRef"`. Internal methods (`AddSeriesAsync`,
`RemoveSeries`, `ClearSeries`, `RefreshLiveData`) are
`[EditorBrowsable(EditorBrowsableState.Never)]` — do not document or
rely on them. Full signatures: `references/api-reference.md`.

```razor
<SfChart @ref="ChartRef">…</SfChart>
@code { SfChart ChartRef = default!; }
```

### 7.6 `@onclick` handlers can't be inside `<ChartSeries>`

Hook `OnPointClick` / `OnSeriesClick` on `<ChartEvents>` instead.

### 7.7 Accessibility surface uses `AccessibilityDescription` (not `Description`)

The accessibility property on `<SfChart>` is `AccessibilityDescription`
(verified at `src/Components/Charts/Chart/SfChart.razor.Members.cs:171`).
A bare `Description="…"` is **not** a parameter on `SfChart` and will fail
to compile. There is also a sibling `AccessibilityRole` (e.g. `"img"`,
`"figure"`) you can set to override the implicit `presentation` role on
the chart container.

```razor
<SfChart Title="Q4 Revenue"
         AccessibilityDescription="Bar chart showing Q4 revenue by region"
         AccessibilityRole="img">
    …
</SfChart>
```

### 7.8 Every event — including per-axis callbacks — lives on a single root-level `<ChartEvents>`

`OnAxisLabelRender`, `OnAxisMultiLevelLabelRender`, and
`OnAxisActualRangeCalculated` are properties on `ChartEvents`. Bind them
on a **single** `<ChartEvents>` child of `<SfChart>` — never nested
inside `<ChartPrimaryXAxis>` or `<ChartPrimaryYAxis>` (verified at
`src/Components/Charts/Chart/ChartEvent/ChartEvents.razor.cs`).

```razor
<SfChart>
    <ChartEvents OnAxisLabelRender="AxisLabelEvent"
                 OnSeriesRender="SeriesRender"
                 OnSelectionChanged="SelectionChanged" />
    …
</SfChart>
```

## Common pitfalls

| Symptom | Cause | Fix |
|---------|-------|-----|
| Chart renders empty | `XName`/`YName` casing mismatch with data class | Property name must match exactly (case-sensitive) |
| `Ctors not found` on `ChartSeriesType` | Short enum (no prefix) | Use `Syncfusion.Blazor.Toolkit.ChartSeriesType.Column` |
| Chart height collapses to zero | `%` on an unsized parent | Use static `height` value or sized container |
| Legend isn't visible | `ChartLegendSettings.Visible="false"` is default | Set `Visible="true"` |
| Strip line preview appears off-axis | Used singular element | Use `<ChartStriplines>` wrapper |
| Crosshair draws but child line is invisible | Passed `DashArray` to `ChartCrosshairLine` | Move `DashArray` to `ChartCrosshairSettings` |
| Bubble shows equal sizes | `Size` property not bound | Set `Size="PropertyName"` (not `ZName`) |
| Tooltip missing on multi-series | Forgot `Shared="true"` | Set `<ChartTooltipSettings Shared="true" />` |
| Multiple axes overlay | Two series with same default `Name` | Set `Name="…"` per series and per axis |
| Screen reader doesn't announce the chart | Set `Description="…"` on `<SfChart>` | `Description` isn't a parameter — use `AccessibilityDescription="…"` and optionally `AccessibilityRole="img"` |
| RTL flag "doesn't take" | Wrote `<SfChart EnableRtl="true" />` | `EnableRtl` is internal. Set `options.EnableRtl = true` on `AddSyncfusionBlazorToolkit(options => …)` once at startup |
| `OnAxisLabelRender` "doesn't fire" | Put `<ChartEvents>` inside `<ChartPrimaryXAxis>` | All events live on a single `<ChartEvents>` block as a child of `<SfChart>` |

## Step 8 — Validate

1. **Build green**: `dotnet build ./src/Syncfusion.Blazor.Toolkit.slnx`
   (covers net8.0, net9.0, net10.0).
2. **BUnit tests**: `dotnet test ./tests/Syncfusion.Blazor.Toolkit.BUnitTest`.
3. **Visual run** (manual): samples app at `samples/Blazor.Toolkit.Samples`
   — chart renders with axis labels, legend, tooltip on hover.
4. **Accessibility scan** (manual): keyboard `Tab` reaches the chart; hover
   tooltip; high-contrast theme applies; `ChartPrimaryXAxis` has a `Title`
   (screen reader announces it); for charts that replace a table,
   `AccessibilityDescription` is set on `<SfChart>` (and `AccessibilityRole`
   if the chart conveys unique information).
5. **Final pre-commit checklist**:
   - [ ] All enums use the `Syncfusion.Blazor.Toolkit.` prefix
   - [ ] `<ChartStriplines>` (plural) wrapper used
   - [ ] `ChartCrosshairLine` carries only `Width` + `Color`
   - [ ] `XName` / `YName` match property casing
   - [ ] Bubble uses `Size=` not `ZName=`
   - [ ] `@ref` for any call to `ChartRef.RefreshAsync` / `ShowTooltipAsync` / `HideCrosshairAsync` etc.
   - [ ] All event handlers are on a single root-level `<ChartEvents>` (no per-axis nesting)
   - [ ] Accessibility uses `AccessibilityDescription` / `AccessibilityRole`; `EnableRtl` is set on global `AddSyncfusionBlazorToolkit(options => …)`
   - [ ] Component reference matches repo conventions (see repo `AGENTS.md`)

## Don'ts

- Don't shorten `Syncfusion.Blazor.Toolkit.ChartSeriesType.Column` —
  it compiles only when `using Syncfusion.Blazor.Toolkit;` is also in
  the same Razor file's imported namespaces. The default sample layout
  imports only `Syncfusion.Blazor.Toolkit.Charts` (per
  `samples/Blazor.Toolkit.Samples/Components/_Imports.razor`), so the
  fully-qualified prefix is required.
- Don't call a made-up extension like `AddSyncfusionToolkit()` — the
  real registration extension is `AddSyncfusionBlazorToolkit()`.
- Don't write `<ChartAxisStripLineSettings>` — it doesn't exist. Wrap
  singular `<ChartStripline>` in `<ChartStriplines>` (plural).
- Don't put `DashArray` / `LineType` on `<ChartCrosshairLine>` — its
  only valid attributes are `Width` and `Color`. Move them to the
  parent `<ChartCrosshairSettings>`.
- Don't use `ZName` for bubble size — use `Size="PropertyName"`.
- Don't call instance methods (`RefreshAsync`, `ShowTooltipAsync`,
  `HideTooltipAsync`, `ShowCrosshairAsync`, `HideCrosshairAsync`,
  `ClearSelection`, `Sort`, etc.) on `SfChart` without `@ref="ChartRef"`.
- Don't put `@onclick` directly on `<ChartSeries>` — hook
  `OnPointClick` / `OnSeriesClick` via `<ChartEvents>`.
- Don't rely on percentage heights without an explicitly-sized parent
  CSS container — the chart will collapse to zero height.
- Don't place a chart inside a route page without first reading
  `codestudio-instructions.md` (Step 1) — partial-class + JS-interop
  conventions will catch you later.
- Don't drop tooltips, crosshair, zoom, pan, selection, legend, or
  secondary axes into the scaffold by default — add them in Step 6 only
  when the user asks.
- Don't expect tooltips, crosshair, zoom, or selection under a pure
  Static-SSR render mode — the chart JS modules load from
  `_content/Syncfusion.Blazor.Toolkit/scripts/*` and require an
  interactive circuit. The SVG frame itself renders in Static SSR at
  default 600×450; only the interactive features need Server /
  WebAssembly / Auto. If you must keep the host page static, place
  the chart in an interactive child component.
- Don't pass a `Theme` value copied from older Syncfusion docs
  (`Theme.Material`, `Theme.Bootstrap5`, `Theme.Tailwind`,
  `Theme.HighContrast`). The `Syncfusion.Blazor.Toolkit.Theme` enum
  has only `Fluent` and `FluentDark`; non-existent values compile to
  silence and render nothing. See Step 3.5.
- Don't call `AddSyncfusionBlazor()` to "register themes" — themes live
  on `SfChart` itself; the toolkit is registered once via
  `AddSyncfusionBlazorToolkit()` (Step 3).
- Don't write `<SfChart EnableRtl="true" />` — `EnableRtl` is an
  **internal** property on `SfChart`. Set the RTL flag once on the
  global options at registration:
  `builder.Services.AddSyncfusionBlazorToolkit(o => o.EnableRtl = true)`.
- Don't write `<SfChart Description="…">` — that parameter doesn't
  exist. Use `AccessibilityDescription="…"` (and optionally
  `AccessibilityRole="…"`) instead. See § 7.7.
- Don't put `<ChartEvents>` inside `<ChartPrimaryXAxis>` or
  `<ChartPrimaryYAxis>` to capture `OnAxisLabelRender` /
  `OnAxisMultiLevelLabelRender` / `OnSelectionChanged` etc. Every event
  binds on a single `<ChartEvents>` child of `<SfChart>`. See § 7.8.

## Which reference to load

| If the agent is doing… | Read |
|------------------------|------|
| First-time setup, NuGet, Program.cs registration | `references/getting-started.md` |
| Picking line / area / column / spline / step | `references/chart-types-common.md` |
| Stacking / scatter / bubble / multi-color / vertical | `references/chart-types-specialized.md` |
| Axis type, range, format, secondary axis | `references/axes-and-scales.md` |
| Tooltip / crosshair / zoom / pan / selection | `references/interactive-features.md` |
| Markers / data labels / annotations / gradients | `references/visual-elements.md` |
| Legend position and customization | `references/legend.md` |
| Theme, palette, dimensions, responsive, print | `references/appearance-styling.md` |
| Indicators, trend lines, striplines, panes, empty points | `references/advanced-features.md` |
| Dynamic data, sort, data manager, real-time | `references/data-handling.md` |
| Lifecycle / click / hover / zoom / selection events | `references/events.md` (skim the top-of-file callout — per-axis callbacks like `OnAxisLabelRender` live on the **root** `<ChartEvents>`, not nested) |
| ARIA, keyboard, RTL, i18n, l10n, color blindness | `references/accessibility-internationalization.md` |
| Public method signatures + property tables | `references/api-reference.md` |
| Drill-down, threshold lines, lazy load, sync | `references/practical-examples.md` |

**Read order rule** (`.NET/blazor` convention): read **at most two**
references per task. If you still don't know which API to call, escalate
to `references/api-reference.md` last, never first.

## Documentation and Navigation Guide

The canonical, external reference lives at the Syncfusion **Blazor Toolkit
Charts overview demo**, which mirrors the live components shipped by this
package:

- **Toolkit Charts overview** — <https://blazor.syncfusion.com/demos/toolkit/charts/overview>
  Use this to confirm any visual / API behaviour before relying on the
  snippets in `references/`. The demo page is regenerated from the
  published package, so when it diverges from this skill, **trust the
  demo** for the exact surface and **trust this skill** for the
  repo-specific gotchas (enum prefix, plural wrappers, `ChartCrosshairLine`
  limits, etc.).

### Internal references — read order

The "Which reference to load" table above is the navigation map. The
recommended staircase is:

1. `references/getting-started.md` — first read when wiring a brand-new app.
2. `references/chart-types-common.md` or `…specialized.md` — when choosing a
   series.
3. `references/axes-and-scales.md`, `interactive-features.md`,
   `visual-elements.md`, `legend.md`, `appearance-styling.md` — when
   deeper configuration is asked for.
4. `references/data-handling.md`, `events.md`,
   `accessibility-internationalization.md` — for live data, hooks, and
   a11y.
5. `references/advanced-features.md`, `practical-examples.md` — for
   trend lines, panes, drill-down, lazy load.
6. `references/api-reference.md` — last resort, never first; it is the
   big property-and-method catalogue.

## Next Steps

Once validation (Step 8) is green, the natural follow-on tasks are:

1. **Pick the next visual layer.** If the user asked for any of
   tooltips, crosshair, zoom / pan, selection, or a secondary axis →
   load `references/interactive-features.md` and apply Step 6.
2. **Tune the visual.** Markers, data labels, annotations, gradients,
   legend positioning → load `references/visual-elements.md` and
   `references/legend.md`.
3. **Confirm accessibility.** Run the accessibility scan in Step 8;
   if anything is missing (`Title` on every axis, keyboard reach,
   contrast for the chosen `Theme`), load
   `references/accessibility-internationalization.md` and fix the gap.
4. **Hook events.** If the user wants point-click, series-click, hover,
   zoom-end, or selection callbacks → load `references/events.md` and
   wire `<ChartEvents>` (do **not** put `@onclick` on `<ChartSeries>` —
   rule 7.6).
5. **Bind real data.** If `List<T>` is no longer enough or the user
   needs paging / web-API backing → load `references/data-handling.md`
   and migrate the `DataSource` to `SfDataManager`.
6. **Compose with another control.** For the chart's neighbours (a
   header filter, refresh button, export-PNG button, loading overlay,
   or confirm dialog) load the matching sibling skill in the same
   folder — decisions are tabled in the "When you need to compose"
   section near the top of this skill.
7. **Cross-check against the live demo.** Re-verify any behaviour the
   user will see against the Syncfusion Toolkit Charts overview
   (`https://blazor.syncfusion.com/demos/toolkit/charts/overview`)
   before declaring done.