# Data Handling

> **Verified against source** — exposed binding source is
> `IEnumerable`-shaped via the `SfBaseComponent`-derived `ChartSeries`
> `DataSource` parameter. Verified against
> `src/Components/Charts/Chart/Series/ChartSeries.cs`. Last source
> audit: **2026-08-24**.

`SfChart` accepts a few different data sources, when each is appropriate,
and how to mutate them at runtime.

> **Sample data** — see [`_includes/sample-data.md`](_includes/sample-data.md).
> This file's snippets use `LatestData : List<SeriesPoint>` (in-memory)
> and `Live : ObservableCollection<SeriesPoint>` (live-streaming),
> with `SeriesPoint(string X, double Y)` and `DatePoint(DateTime When,
> double Value)`.

## Table of contents

- Pick a data source (decision table)
- In-memory `List<T>` (most common)
- `DataManager` / `SfDataManager` (server-side paging, filtering)
- Dynamic update (replace the list entirely)
- Live / streaming updates (observable + state notification)
- Sort (`ChartSorting`, `Sort(propertyName, direction)`)
- Clearing sort
- Data editing (drag-to-edit — see also `advanced-features.md`)
- Empty points (see also `advanced-features.md`)

## Pick a data source

| Need | Recipe |
|------|--------|
| ≤ 1,000 items, host-rendered | `DataSource="@MyList"` |
| Server paging/filtering | `DataSource="@new SfDataManager() { Url = … }"` or adapter |
| Re-render on push | `ObservableCollection<T>` + `OnInitialized` subscribe, or `IDataManager` event |
| Static set with `Sort()` | `List<T>` is enough; `Sort()` reorders at the chart layer |
| Streaming telemetry | Re-set `_data = NewData` then call `StateHasChanged` + `RefreshAsync` |

All paths require **interactive** render mode unless the data is pre-baked
and you are OK with the chart not responding to post-render state changes.

## In-memory `List<T>`

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartSeries DataSource="@LatestData" XName="X" YName="Y"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column" />
</SfChart>
```

`XName` / `YName` are **case-sensitive string property names that match
your record's CLR properties**. This is the single most common reason a
chart silently renders empty. If you can't get `YName` working, verify the
property casing in the toolbar pane of bUnit (or with a break on the chart
series render event).

## `SfDataManager` (server or OData)

```razor
<SfChart>
    <ChartPrimaryXAxis ValueType="Syncfusion.Blazor.Toolkit.ValueType.Category" />
    <ChartSeries DataSource="@manager" XName="Quarter" YName="Amount"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Column">
    <ChartDataManager Adaptor="Adaptors.ODataAdaptor" />
    </ChartSeries>
</SfChart>

@code {
    SfDataManager manager = new() {
        Url = "/api/sales",
        CrossDomain = true,
        Adaptor = Adaptors.WebApiAdaptor
    };
}
```

Adaptor values ship with the Blazor Datas package: `UrlAdaptor`,
`WebApiAdaptor`, `ODataAdaptor`, `WebMethodAdaptor`. See
`Syncfusion.Blazor.Data` for the full list and their parameters.

## Dynamic update (replace the list)

```razor
<SfChart @ref="ChartRef">…</SfChart>

<SfButton @onclick="Reload">Reload</SfButton>

@code {
    SfChart ChartRef = default!;
    List<SeriesPoint> LatestData = new() { /* … */ };

    async Task Reload()
    {
        // Replace the field reference; Blazor's standard diff+kick triggers re-render.
        LatestData = await FetchSeriesAsync();
        await ChartRef.RefreshAsync();
    }
}
```

`RefreshAsync(bool shouldAnimate = true)` is the canonical way to mark
"data changed, redraw". Wait for the await — sync refresh is **not**
exposed; the toolkit only has the `Task`-returning variant.

## Live / streaming updates

The chart re-renders when the bound reference changes. Two flavors work:

```razor
@implements IDisposable

<SfChart @ref="ChartRef">
    <ChartSeries DataSource="@Live" XName="X" YName="Y"
                 Type="Syncfusion.Blazor.Toolkit.ChartSeriesType.Line"
                 Width="2" />
</SfChart>

@code {
    ObservableCollection<SeriesPoint> Live = new();
    SfChart ChartRef = default!;

    protected override void OnInitialized()
    {
        Live.CollectionChanged += (_, _) => _ = ChartRef.RefreshAsync();
        _ = Task.Run(StreamLoopAsync);
    }

    async Task StreamLoopAsync()
    {
        while (true)
        {
            Live.Add(new(DateTime.Now.Second.ToString(), Random.Shared.NextDouble() * 50));
            await Task.Delay(500);
        }
    }

    public void Dispose()
    {
        Live.CollectionChanged -= (_, _) => ChartRef.RefreshAsync();
    }
}
```

| Pattern | Caveat |
|---------|--------|
| `ObservableCollection<T>` + `CollectionChanged` | Works in interactive render modes only |
| Replace the field with a new list | Always triggers re-render — simplest, lowest-friction |
| Replace with a `new List<>` after mutation | Don't — reference equality won't change → no re-render |

## Sort

There are two parallel mechanisms — pick the one you need:

```razor
<!-- 1. Declarative: configured at chart-creation time -->
<!-- `Syncfusion.Blazor.Toolkit.Data.ListSortDirection` lives in this toolkit;
     DO NOT use `System.ComponentModel.ListSortDirection` (won't compile). -->
<ChartSorting PropertyName="Y"
              Direction="Syncfusion.Blazor.Toolkit.Data.ListSortDirection.Descending" />

<!-- 2. Programmatic: called from a method -->
@code {
    SfChart ChartRef = default!;

    void SortByY() =>
        ChartRef.Sort("Y", Syncfusion.Blazor.Toolkit.Data.ListSortDirection.Descending);

    void ClearSort() => ChartRef.ClearSort();
}
```

Both go through the same underlying code. When you need flip-flopping between
sort orders, use programmatic. When it's a fixed order at construction,
use `<ChartSorting>`.

## Common gotchas

- `XName` / `YName` property casing mismatches render an empty chart — verify
  the property names match exactly.
- Replacing an `ObservableCollection`'s mutating in-place gives you no
  reference change; Blazor may skip the diff. Use `Add`/`Remove` on the live
  collection, or assign a new backing list.
- Data-driven render requires interactive render mode (`@rendermode
  InteractiveServer` or `InteractiveWebAssembly`).
- `RefreshAsync` doesn't skip animation by default; pass `false` for live
  feeds where steady-state is more important than visual transitions.
- `DataManager` paging is **not** automatic; you need the server endpoint
  to honor `take` / `skip` like a Syncfusion DataManager-aware scaffold.