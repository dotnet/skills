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
  data entry forms, or non-MAUI list controls.
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

## Inputs

- A data source (typically `ObservableCollection<T>`) bound to `ItemsSource`
- A `DataTemplate` defining how each item renders
- Optional: layout configuration, selection mode, grouping model, empty view

## Basic Setup

```xml
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
```

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

## Performance Tips

- **Use `MeasureFirstItem`** for uniform item sizes — significantly faster than `MeasureAllItems`:
  ```xml
  <LinearItemsLayout Orientation="Vertical" ItemSizingStrategy="MeasureFirstItem" />
  ```
- **Always use `ObservableCollection<T>`**, not `List<T>`. Swapping a `List` forces a full re-render.
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
| Selected state not visible | Add `VisualState Name="Selected"` to the item template root element. |
| Binding errors in SwipeView commands | Use `RelativeSource AncestorType` to reach the ViewModel from inside the item template. |
| Using ListView instead of CollectionView | `CollectionView` replaces `ListView` — it has better performance, no `ViewCell`, and flexible layouts. |

## References

- [CollectionView overview](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/)
- [CollectionView layout](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/layout)
- [CollectionView selection](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/selection)
- [CollectionView grouping](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/grouping)
- [CollectionView scrolling](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/scrolling)
- [CollectionView EmptyView](https://learn.microsoft.com/dotnet/maui/user-interface/controls/collectionview/emptyview)
