---
name: maui-collectionview
description: >
  Guidance for implementing CollectionView in .NET MAUI apps — data display,
  layouts (list & grid), selection, grouping, scrolling, empty views, templates,
  incremental loading, swipe actions, and pull-to-refresh.
  USE FOR: "CollectionView", "list view", "grid layout", "data template",
  "item template", "grouping", "pull to refresh", "incremental loading",
  "swipe actions", "empty view", "selection mode", "scroll to item",
  displaying scrollable data, replacing ListView.
  DO NOT USE FOR: simple static layouts without scrollable data (use Grid or
  StackLayout), map pin lists (use Microsoft.Maui.Controls.Maps), table-based
  data entry forms, non-MAUI list controls, CarouselView or BindableLayout
  questions, platform-specific handler or renderer customization, diagnosing
  CollectionView bugs in the MAUI framework itself, or binding-path/x:DataType
  defects and general MVVM questions, even inside item templates (use maui-data-binding).
license: MIT
---

# CollectionView — .NET MAUI

`CollectionView` is the primary control for displaying scrollable lists and grids of data in .NET MAUI. It replaces `ListView` with better performance, flexible layouts, and no `ViewCell` requirement.

## When to Use

- Displaying a scrollable list or grid of data items
- Binding a collection of objects to a templated item layout
- Adding selection (single or multiple), grouping, or pull-to-refresh
- Implementing infinite scroll / incremental loading
- Showing swipe actions on list items
- Displaying an empty state when no data is available

## When Not to Use

- Static layouts with a fixed number of items — use `Grid` or `StackLayout` directly
- Map pin lists — use the `Microsoft.Maui.Controls.Maps` NuGet package
- Table-based data entry forms — use standard form controls
- Simple text-only lists with no interaction — consider `BindableLayout` on a `StackLayout`

## Scope Control — Answer Only What Was Asked

Answer only the requested capability, using the relevant sections below.
Preserve working user code; do not rename, reorder or restructure it for style.
Do not append unrequested grouping, swipe, empty-view, snap-point or performance
features. `CarouselView` and `BindableLayout` have different contracts.
Leave maintained `ListView` code alone unless migration/control choice is asked;
then recommend `CollectionView` (see [migration](#migrating-from-listview)).
For a binding, DI or navigation defect, use the corresponding sibling skill.

Keep these correctness rules when writing a relevant CollectionView change:

1. Never use `ViewCell` as a `DataTemplate` root in `CollectionView`.
2. Use `ObservableCollection<T>` when the list mutates after first render.
3. Update `ItemsSource` and mutate bound collections on the UI thread.
4. Set `x:DataType` on every `DataTemplate` (and on the page root) for compiled bindings.
5. Preserve an existing runtime context or assign the object exposing the collection;
   `x:DataType` alone does not instantiate a ViewModel or supply `ItemsSource`.

## Inputs

- A data source (typically `ObservableCollection<T>`) bound to `ItemsSource`
- A `DataTemplate` defining how each item renders
- Optional: layout configuration, selection mode, grouping model, empty view

## Basic Setup

A complete, copy-pasteable page. Two things are load-bearing: the `xmlns:models`
declaration that every `x:DataType="models:Item"` in this skill assumes, and the
**root `x:DataType`** — without it the outer `ItemsSource` binding is not compiled:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:models="clr-namespace:MyApp.Models"
             xmlns:vm="clr-namespace:MyApp.ViewModels"
             x:DataType="vm:ItemsViewModel"
             x:Class="MyApp.ItemsPage">
    <ContentPage.BindingContext>
        <vm:ItemsViewModel />
    </ContentPage.BindingContext>
    <CollectionView ItemsSource="{Binding Items}">
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="models:Item">
                <HorizontalStackLayout Padding="8" Spacing="8">
                    <Image Source="{Binding Icon}" WidthRequest="40" HeightRequest="40" />
                    <Label Text="{Binding Name}" VerticalOptions="Center" />
                </HorizontalStackLayout>
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>
```

Later snippets show only the `CollectionView` element. When you hand a snippet to a
user, include the matching `xmlns:` declaration for any prefix it uses, or the XAML
will not compile.

The inline `<ContentPage.BindingContext>` above keeps the example self-contained. In
an app that uses dependency injection, register the ViewModel instead and assign it
through constructor injection (`BindingContext = vm;`) — see the
**maui-dependency-injection** skill.

For a new-page request, also supply the item model and the ViewModel exposing the
collection, plus code-behind calling `InitializeComponent()`. Declaring
`x:DataType` checks binding paths; it does not create a `BindingContext`. If those
types already exist in the user's input, reuse them rather than inventing replacements.

```csharp
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using MyApp.Models;

namespace MyApp.Models
{
    public sealed record Item(string Name, string Icon);
}

namespace MyApp.ViewModels
{
    public sealed class ItemsViewModel
    {
        public ObservableCollection<Item> Items { get; } =
            new() { new Item("First item", "dotnet_bot.png") };
    }
}

namespace MyApp
{
    public partial class ItemsPage : ContentPage
    {
        public ItemsPage() => InitializeComponent();
    }
}
```

The namespaces match the XAML prefixes; the page constructor uses its generated
`InitializeComponent`. These declarations are
part of a new-page answer, not optional notes that the reader must implement.

**Key rules:**

- Bind `ItemsSource` to an `ObservableCollection<T>` so the UI updates on add/remove.
- Each item template root must be a `Layout` or `View` — **never use `ViewCell`**.
- Always set `x:DataType` on `DataTemplate` for compiled bindings.

## Layouts

Set `ItemsLayout` to control arrangement. Default is `VerticalList`.

| Layout | XAML value |
|---|---|
| Vertical list | `VerticalList` (default) |
| Horizontal list | `HorizontalList` |
| Vertical grid | `GridItemsLayout` with `Orientation="Vertical"` |
| Horizontal grid | `GridItemsLayout` with `Orientation="Horizontal"` |

### Grid Layout

```xml
<CollectionView ItemsSource="{Binding Items}">
    <CollectionView.ItemsLayout>
        <GridItemsLayout Orientation="Vertical"
                         Span="2"
                         VerticalItemSpacing="8"
                         HorizontalItemSpacing="8" />
    </CollectionView.ItemsLayout>
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="models:Item">
            <Border Padding="8" StrokeThickness="0">
                <VerticalStackLayout>
                    <Image Source="{Binding Image}" HeightRequest="120" Aspect="AspectFill" />
                    <Label Text="{Binding Name}" FontAttributes="Bold" />
                </VerticalStackLayout>
            </Border>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

### Horizontal List

```xml
<CollectionView ItemsSource="{Binding Items}"
                ItemsLayout="HorizontalList" />
```

## Selection

### Selection Mode

| Mode | Property to bind | Binding mode |
|---|---|---|
| `None` | — | — |
| `Single` | `SelectedItem` | `TwoWay` |
| `Multiple` | `SelectedItems` | `OneWay` |

```xml
<CollectionView ItemsSource="{Binding Items}"
                SelectionMode="Single"
                SelectedItem="{Binding CurrentItem, Mode=TwoWay}"
                SelectionChangedCommand="{Binding ItemSelectedCommand}" />
```

For `Multiple` selection, bind `SelectedItems` (type `IList<object>`):

```xml
<CollectionView SelectionMode="Multiple"
                SelectedItems="{Binding ChosenItems, Mode=OneWay}" />
```

For navigation on single selection, reset `SelectedItem` so the same item can be
selected again after returning. Resetting also raises `SelectionChanged`: ignore
empty `e.CurrentSelection`, and guard navigation already in flight.

For an **MVVM** request, keep the `SelectedItem` binding and command rather than
substituting a code-behind event. Resetting a notifying bound property works just
as well for repeat selection. For example, in an existing partial
`ObservableObject` ViewModel using CommunityToolkit.Mvvm:

```xml
<CollectionView SelectionMode="Single"
                SelectedItem="{Binding CurrentItem, Mode=TwoWay}"
                SelectionChangedCommand="{Binding OpenSelectedCommand}" />
```

```csharp
[ObservableProperty] private Item? _currentItem;

[RelayCommand]
private async Task OpenSelectedAsync()
{
    var item = CurrentItem;
    if (item is null)
        return; // Reset also triggers SelectionChanged.
    CurrentItem = null;
    try
    {
        await Shell.Current.GoToAsync("details",
            new ShellNavigationQueryParameters { ["item"] = item });
    }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
}
```

The generated async command prevents concurrent execution by default; preserve
that guard. Bind `RefreshView.IsRefreshing` and `RefreshView.Command` to the same
ViewModel, with `IsRefreshing` set/reset around its refresh in `try/finally`.
Complete ViewModel examples must declare and initialize any API dependency used
by refresh. Inject the existing service rather than emitting an undefined `api`
field; no new HTTP implementation is needed. Identify assumed dependencies when
showing only a method fragment.
The event-based alternative below is for requests that permit code-behind, not
a replacement for an explicitly requested selection binding.

```csharp
bool _openingDetails;

async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (_openingDetails || e.CurrentSelection.FirstOrDefault() is not Item item)
        return;
    _openingDetails = true;
    try
    {
        ((CollectionView)sender).SelectedItem = null;
        await Shell.Current.GoToAsync("details",
            new ShellNavigationQueryParameters { ["item"] = item });
    }
    finally { _openingDetails = false; }
}
```

Keep `SelectionMode="Single"` when selected visuals are required. A tap gesture
is also valid when the requested action is tapping rather than selection.

Read [optional templates](references/optional-templates.md) only when the request
needs selected visuals, grouping, swipe commands, custom empty views, headers,
footers, or scrolling/snap points. These examples are not part of a basic-page,
selection-reset, sizing, or paging answer.

## Pull-to-Refresh

Wrap `CollectionView` in a `RefreshView`. Set `IsRefreshing` back to `false` when done:

```xml
<RefreshView IsRefreshing="{Binding IsRefreshing}"
             Command="{Binding RefreshCommand}">
    <CollectionView ItemsSource="{Binding Items}" />
</RefreshView>
```

## Incremental Loading (Infinite Scroll)

```xml
<CollectionView ItemsSource="{Binding Items}"
                RemainingItemsThreshold="5"
                RemainingItemsThresholdReachedCommand="{Binding LoadMoreCommand}" />
```

Threshold callbacks can overlap. Set a busy guard **before** awaiting a fetch,
release it in `finally`, and stop once the API reports exhaustion. Prefer its
`HasMore`/next-cursor signal; for a fixed-page-size API that guarantees all
non-final pages are full, a short non-empty page is already the last page.
Do not infer exhaustion from page length when that is not the server's contract.

```csharp
if (_loading || !_hasMore)
    return;
_loading = true;
try
{
    var page = await api.GetPageAsync(_nextPage, PageSize);
    await MainThread.InvokeOnMainThreadAsync(() =>
    {
        foreach (var item in page)
            Items.Add(item);
    });
    _nextPage++; // Advance only after a successful fetch/publication.
    _hasMore = page.Count == PageSize; // Fixed-size API contract assumed here.
}
finally { _loading = false; }
```

This UI-thread command guard is not a cross-thread lock. If callers can enter
concurrently from worker threads, serialize them too. Keep the CollectionView in
a height-constrained container (usually a Grid star row), not inside a
ScrollView or unbounded stacking layout; a BindableLayout has no virtualization.

## Migrating from ListView

`ListView` still compiles, but **as of .NET 10** it is marked `[Obsolete]`
("*ListView is deprecated. Please use CollectionView instead.*"). It is **not**
obsolete on .NET 9 and earlier, so check the project's target framework before
describing it as deprecated. **If the user asks
which control to use, or is migrating from Xamarin.Forms, recommend
`CollectionView`** — it is faster, needs no `ViewCell`, and supports flexible
layouts. What to avoid is silently rewriting `ListView` code the user did not ask
you to touch.

| `ListView` | `CollectionView` equivalent |
|---|---|
| `ViewCell` template root | Any `View`/`Layout` root — **`ViewCell` is not supported** |
| `ItemSelected` event | `SelectionChanged` event, or `SelectionChangedCommand` |
| `ItemTapped` event | A `TapGestureRecognizer` in the item template — `SelectionChanged` only fires when the selection *changes*, so it will not re-fire on tapping the already-selected item |
| `IsPullToRefreshEnabled` + `Refreshing` | Wrap the `CollectionView` in a `RefreshView` |
| `IsGroupingEnabled` | `IsGrouped` |
| `HasUnevenRows="True"` | Explicit `CollectionView.ItemSizingStrategy="MeasureAllItems"`; preserve variable-height templates rather than applying fixed-height requests or `MeasureFirstItem` |
| `RowHeight` (fixed height) | Set the height in the item template. `MeasureFirstItem` only reuses the first item's measured size — it is not an explicit row height |
| `SeparatorVisibility` / `SeparatorColor` | **No equivalent** — draw a `BoxView`/`Border` in the item template |

The missing separator API is the most common migration surprise: `CollectionView`
has no built-in separators, so add one to the template yourself.

Show the actual template conversion when migration is requested, not just a
property-mapping table. Keep the bindings/content but remove the cell wrapper:

```xml
<!-- Existing ListView template -->
<DataTemplate>
    <ViewCell>
        <Grid Padding="8"><Label Text="{Binding Name}" /></Grid>
    </ViewCell>
</DataTemplate>

<!-- CollectionView.ItemTemplate; models points to the existing item namespace -->
<DataTemplate x:DataType="models:Item">
    <Grid Padding="8"><Label Text="{Binding Name}" /></Grid>
</DataTemplate>
```

## Performance Tips

Apply these only when the user reports a performance problem or explicitly asks
about performance — they are not a default checklist.

- **For sizing questions, name the default `MeasureAllItems`**, which measures each item.
  Recommend `MeasureFirstItem` only for uniform measured sizes. Set it on the `CollectionView`
  itself (it is declared on `StructuredItemsView`), **not** on `LinearItemsLayout` /
  `GridItemsLayout`:
  ```xml
  <CollectionView ItemsSource="{Binding Items}"
                  ItemSizingStrategy="MeasureFirstItem">
      <CollectionView.ItemTemplate>
          <DataTemplate x:DataType="models:Item">
              <Grid Padding="8" ColumnDefinitions="44,*" ColumnSpacing="8">
                  <Image WidthRequest="44" HeightRequest="44" />
                  <Label Grid.Column="1" Text="{Binding Name}" VerticalOptions="Center" />
              </Grid>
          </DataTemplate>
      </CollectionView.ItemTemplate>
  </CollectionView>
  ```
  State the positive condition as well as the exceptions: it is safe when every
  row has the same measured size at the current width and font scale, including
  later-loaded rows. A fixed-height avatar plus one non-wrapping line can satisfy
  that condition; a fixed avatar alone does not constrain wrapping text.
  Explain explicitly that a supported font-scale or width change can invalidate
  that condition if it introduces wrapping or changes measured heights; do not
  treat package/object-model checks as proof at every accessibility text size.

  **When `MeasureFirstItem` is the wrong choice** — keep the default `MeasureAllItems` if:
  - Items vary in height (wrapping text, optional rows, images of differing aspect) — the
    first item's size is applied to all, so the rest are clipped or stretched.
  - A `DataTemplateSelector` returns different templates — the first item won't represent
    the others.
  - The first item is atypical (a header-like or "featured" row) — every item inherits its
    size. Fixing this by reordering data is a smell; use `MeasureAllItems` instead.
  - Item size depends on runtime data that isn't loaded yet when the first item is measured.
- **Use `ObservableCollection<T>` when the list mutates after first render.** It implements
  `INotifyCollectionChanged`, so in-place `Add`/`Remove`/`Insert` update the UI incrementally.
  A `List<T>` is fine for a list that never changes after it is bound. Note that *replacing*
  `ItemsSource` re-renders everything regardless of the collection type — so mutate the bound
  collection in place rather than reassigning it.
## Validation

Before returning CollectionView markup you wrote or edited, confirm:

- [ ] The `DataTemplate` root is a `View`/`Layout` — **not** `ViewCell`.
- [ ] `DataTemplate` declares `x:DataType` for compiled bindings.
- [ ] `ItemsSource` is bound to `ObservableCollection<T>` if the list mutates.
- [ ] `ItemSizingStrategy` (if used) is on `<CollectionView>`, not on the layout.
- [ ] `Multiple` selection binds `SelectedItems`; `Single` binds `SelectedItem` (`TwoWay`).
- [ ] `RefreshView.IsRefreshing` is set back to `false` when the refresh completes.
- [ ] The answer covers **only** what the user asked — no unrequested sections.
- [ ] Report actual checks and preserve passing supplied files; package/object-model checks do not prove XAML compilation, native layout, scrolling or device behavior.

## References

- [CollectionView overview](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/)
- [CollectionView layout](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/layout)
- [CollectionView selection](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/selection)
- [CollectionView grouping](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/grouping)
- [CollectionView scrolling](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/scrolling)
- [CollectionView EmptyView](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/emptyview)
