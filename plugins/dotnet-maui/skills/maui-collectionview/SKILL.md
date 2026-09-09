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
  CollectionView bugs in the MAUI framework itself, or general MVVM/binding
  questions that merely happen to mention a list (use maui-data-binding).
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

This skill is a **reference you consult**, not a checklist you apply. Most requests
need one or two sections from it. Pulling in the rest makes the answer worse.

**Stop conditions — do NOT act when:**

- **The user asked a narrow question.** Answer that question only. Do not append
  grouping, swipe actions, empty views, snap points, or performance tips that
  were not asked about.
- **The user's existing code already works.** Do not rewrite working markup to
  match the examples here. Point out a concrete defect; if there is none, say so
  and answer the question that was asked.
- **The change is stylistic.** Renaming, reordering attributes, or restructuring
  a template that already behaves correctly is churn, not a fix.
- **The control isn't `CollectionView`.** `CarouselView`, `BindableLayout`, and
  `ListView`-in-maintenance code have different rules. Do not rewrite `ListView`
  code the user did not ask about — but if they ask *which* control to use, or are
  migrating from Xamarin.Forms, recommend `CollectionView` (see
  [Migrating from ListView](#migrating-from-listview)).
- **The problem is really a binding, DI, or navigation problem** that happens to
  involve a list — defer to `maui-data-binding`, `maui-dependency-injection`, or
  `maui-shell-navigation`.

**The API sections below are a reference, not a checklist — offer them only when
relevant.** Four rules are non-negotiable, because violating them produces code that
does not work or silently loses compile-time checking:

1. Never use `ViewCell` as a `DataTemplate` root in `CollectionView`.
2. Use `ObservableCollection<T>` when the list mutates after first render.
3. Mutate the bound collection on the UI thread.
4. Set `x:DataType` on every `DataTemplate` (and on the page root) for compiled bindings.

Everything else — sizing strategy, snap points, header/footer, empty views — is
optional and should be offered only when it addresses the user's actual problem.

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

### Selected Visual State

Highlight selected items using `VisualStateManager`:

```xml
<CollectionView.ItemTemplate>
    <DataTemplate x:DataType="models:Item">
        <Grid Padding="8">
            <VisualStateManager.VisualStateGroups>
                <VisualStateGroup Name="CommonStates">
                    <VisualState Name="Normal">
                        <VisualState.Setters>
                            <Setter Property="BackgroundColor" Value="Transparent" />
                        </VisualState.Setters>
                    </VisualState>
                    <VisualState Name="Selected">
                        <VisualState.Setters>
                            <Setter Property="BackgroundColor"
                                    Value="{AppThemeBinding Light={StaticResource Primary}, Dark={StaticResource PrimaryDark}}" />
                        </VisualState.Setters>
                    </VisualState>
                </VisualStateGroup>
            </VisualStateManager.VisualStateGroups>
            <Label Text="{Binding Name}" />
        </Grid>
    </DataTemplate>
</CollectionView.ItemTemplate>
```

## Grouping

1. Create a group class inheriting from `List<T>`:

```csharp
public class AnimalGroup : List<Animal>
{
    public string Name { get; }
    public AnimalGroup(string name, List<Animal> animals) : base(animals)
    {
        Name = name;
    }
}
```

2. Bind to `ObservableCollection<AnimalGroup>` and set `IsGrouped="True"`:

```xml
<CollectionView ItemsSource="{Binding AnimalGroups}"
                IsGrouped="True">
    <CollectionView.GroupHeaderTemplate>
        <DataTemplate x:DataType="models:AnimalGroup">
            <Label Text="{Binding Name}"
                   FontAttributes="Bold"
                   BackgroundColor="{StaticResource Gray100}"
                   Padding="8" />
        </DataTemplate>
    </CollectionView.GroupHeaderTemplate>
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="models:Animal">
            <Label Text="{Binding Name}" Padding="16,4" />
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

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

> ⚠️ **Do NOT use with non-virtualizing layouts.** `LinearItemsLayout` and `GridItemsLayout` support virtualization. Using `BindableLayout` on a `StackLayout` as an alternative to `CollectionView` has no virtualization, which triggers infinite threshold-reached events.

## SwipeView — Binding from Inside DataTemplate

Commands inside a `DataTemplate` can't directly reach your ViewModel. Use `RelativeSource AncestorType`:

```xml
<CollectionView.ItemTemplate>
    <DataTemplate x:DataType="models:Item">
        <SwipeView>
            <SwipeView.RightItems>
                <SwipeItems>
                    <SwipeItem Text="Delete"
                               BackgroundColor="Red"
                               Command="{Binding BindingContext.DeleteCommand, Source={RelativeSource AncestorType={x:Type ContentPage}}}"
                               CommandParameter="{Binding}" />
                </SwipeItems>
            </SwipeView.RightItems>
            <Grid Padding="8">
                <Label Text="{Binding Name}" />
            </Grid>
        </SwipeView>
    </DataTemplate>
</CollectionView.ItemTemplate>
```

## EmptyView

Shown when `ItemsSource` is empty or null.

```xml
<CollectionView ItemsSource="{Binding SearchResults}"
                EmptyView="No items found." />
```

For a custom empty view, wrap in `ContentView`:

```xml
<CollectionView ItemsSource="{Binding SearchResults}">
    <CollectionView.EmptyView>
        <ContentView>
            <VerticalStackLayout HorizontalOptions="Center" VerticalOptions="Center">
                <Image Source="empty_state.png" WidthRequest="120" />
                <Label Text="Nothing here yet" HorizontalTextAlignment="Center" />
            </VerticalStackLayout>
        </ContentView>
    </CollectionView.EmptyView>
</CollectionView>
```

## Headers and Footers

```xml
<CollectionView ItemsSource="{Binding Items}">
    <CollectionView.Header>
        <Label Text="Header" FontAttributes="Bold" Padding="8" />
    </CollectionView.Header>
    <CollectionView.Footer>
        <Label Text="Footer" FontAttributes="Italic" Padding="8" />
    </CollectionView.Footer>
</CollectionView>
```

Use `HeaderTemplate` / `FooterTemplate` when headers or footers are data-bound.

## Scrolling

### ScrollTo

Programmatically scroll by index or item:

```csharp
// Scroll to index
collectionView.ScrollTo(index: 10, position: ScrollToPosition.Center, animate: true);

// Scroll to item
collectionView.ScrollTo(item: myItem, position: ScrollToPosition.MakeVisible, animate: true);
```

| ScrollToPosition | Behavior |
|---|---|
| `MakeVisible` | Scrolls just enough to make the item visible |
| `Start` | Scrolls item to the start of the viewport |
| `Center` | Scrolls item to the center of the viewport |
| `End` | Scrolls item to the end of the viewport |

### Snap Points

```xml
<CollectionView.ItemsLayout>
    <LinearItemsLayout Orientation="Horizontal"
                       SnapPointsType="MandatorySingle"
                       SnapPointsAlignment="Center" />
</CollectionView.ItemsLayout>
```

- `SnapPointsType`: `None`, `Mandatory`, `MandatorySingle`
- `SnapPointsAlignment`: `Start`, `Center`, `End`

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
| `HasUnevenRows="True"` | Default `ItemSizingStrategy="MeasureAllItems"` |
| `RowHeight` (fixed height) | Set the height in the item template. `MeasureFirstItem` only reuses the first item's measured size — it is not an explicit row height |
| `SeparatorVisibility` / `SeparatorColor` | **No equivalent** — draw a `BoxView`/`Border` in the item template |

The missing separator API is the most common migration surprise: `CollectionView`
has no built-in separators, so add one to the template yourself.

## Performance Tips

Apply these only when the user reports a performance problem or explicitly asks
about performance — they are not a default checklist.

- **Use `MeasureFirstItem`** for uniform item sizes — significantly faster than the default
  `MeasureAllItems`, which measures every item individually. Set it on the `CollectionView`
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
- **Update collections on the UI thread** — `MainThread.BeginInvokeOnMainThread(() => Items.Add(item))`.

## Common Pitfalls

| Issue | Fix |
|---|---|
| UI doesn't update when items change | Use `ObservableCollection<T>`, not `List<T>`. |
| App crashes or blank items | **Never use `ViewCell`** — use `Grid`, `StackLayout`, or any `View` as template root. |
| Items disappear or layout breaks | Always update `ItemsSource` and the collection on the **UI thread** (`MainThread.BeginInvokeOnMainThread`). |
| Incremental loading fires endlessly | Don't use `StackLayout` as layout; use `LinearItemsLayout` or `GridItemsLayout`. |
| EmptyView doesn't render correctly | Wrap custom empty views in `ContentView`. |
| Poor scroll performance | Use `MeasureFirstItem` sizing strategy for uniform item sizes. |
| `ItemSizingStrategy` doesn't compile | It is declared on `StructuredItemsView` — set it on `<CollectionView>`, not on `<LinearItemsLayout>` / `<GridItemsLayout>`. |
| Items clipped or stretched | `MeasureFirstItem` assumes uniform item size. Use the default `MeasureAllItems` for variable-height items. |
| Selected state not visible | Add `VisualState Name="Selected"` to the item template root element. |
| Binding errors in SwipeView commands | Use `RelativeSource AncestorType` to reach the ViewModel from inside the item template. |

## Validation

Before returning CollectionView markup you wrote or edited, confirm:

- [ ] The `DataTemplate` root is a `View`/`Layout` — **not** `ViewCell`.
- [ ] `DataTemplate` declares `x:DataType` for compiled bindings.
- [ ] `ItemsSource` is bound to `ObservableCollection<T>` if the list mutates.
- [ ] `ItemSizingStrategy` (if used) is on `<CollectionView>`, not on the layout.
- [ ] `Multiple` selection binds `SelectedItems`; `Single` binds `SelectedItem` (`TwoWay`).
- [ ] `RefreshView.IsRefreshing` is set back to `false` when the refresh completes.
- [ ] The answer covers **only** what the user asked — no unrequested sections.

## References

- [CollectionView overview](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/)
- [CollectionView layout](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/layout)
- [CollectionView selection](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/selection)
- [CollectionView grouping](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/grouping)
- [CollectionView scrolling](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/scrolling)
- [CollectionView EmptyView](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/emptyview)
