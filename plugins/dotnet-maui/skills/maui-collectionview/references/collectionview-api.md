# CollectionView API Reference

## Layouts

Set `ItemsLayout` to control arrangement. Default is `VerticalList`.

| Layout | XAML value |
|---|---|
| Vertical list | `VerticalList` (default) |
| Horizontal list | `HorizontalList` |
| Vertical grid | `GridItemsLayout` with `Orientation="Vertical"` |
| Horizontal grid | `GridItemsLayout` with `Orientation="Horizontal"` |

### Grid layout

```xml
<CollectionView ItemsSource="{Binding Items}">
    <CollectionView.ItemsLayout>
        <GridItemsLayout Orientation="Vertical"
                         Span="2"
                         VerticalItemSpacing="8"
                         HorizontalItemSpacing="8" />
    </CollectionView.ItemsLayout>
</CollectionView>
```

### Horizontal list

```xml
<CollectionView ItemsSource="{Binding Items}"
                ItemsLayout="HorizontalList" />
```

## ItemSizingStrategy

Controls how items are measured. Set on `ItemsLayout`.

| Value | Behavior |
|---|---|
| `MeasureAllItems` | Measures every item individually (default). Accurate but slower for heterogeneous sizes. |
| `MeasureFirstItem` | Measures only the first item and applies that size to all. Much faster for uniform items. |

```xml
<CollectionView.ItemsLayout>
    <LinearItemsLayout Orientation="Vertical"
                       ItemSizingStrategy="MeasureFirstItem" />
</CollectionView.ItemsLayout>
```

## ItemSpacing

Use `ItemSpacing` on `LinearItemsLayout` or `VerticalItemSpacing` / `HorizontalItemSpacing` on `GridItemsLayout`:

```xml
<CollectionView.ItemsLayout>
    <LinearItemsLayout Orientation="Vertical" ItemSpacing="8" />
</CollectionView.ItemsLayout>
```

## Headers and Footers

Supports string, view, or templated:

```xml
<!-- Simple string -->
<CollectionView Header="My Items" Footer="End of list" />

<!-- Custom view -->
<CollectionView ItemsSource="{Binding Items}">
    <CollectionView.Header>
        <Label Text="Header" FontAttributes="Bold" Padding="8" />
    </CollectionView.Header>
    <CollectionView.Footer>
        <Label Text="Footer" FontAttributes="Italic" Padding="8" />
    </CollectionView.Footer>
</CollectionView>
```

Use `HeaderTemplate` / `FooterTemplate` when headers/footers are data-bound.

## Selection

### Selection mode

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

### Selected visual state

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
                            <Setter Property="BackgroundColor" Value="{AppThemeBinding Light={StaticResource Primary}, Dark={StaticResource PrimaryDark}}" />
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
    <CollectionView.GroupFooterTemplate>
        <DataTemplate x:DataType="models:AnimalGroup">
            <Label Text="{Binding Count, StringFormat='{0} items'}"
                   FontAttributes="Italic"
                   Padding="4,0" />
        </DataTemplate>
    </CollectionView.GroupFooterTemplate>
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="models:Animal">
            <Label Text="{Binding Name}" Padding="16,4" />
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

## EmptyView

Shown when `ItemsSource` is empty or null.

```xml
<!-- Simple string -->
<CollectionView EmptyView="No items found." />

<!-- Custom view — wrap in a ContentView -->
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

You can also use `EmptyViewTemplate` with a `DataTemplateSelector` to swap empty views based on state.

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

### Snap points

Control snap behavior after scrolling:

```xml
<CollectionView.ItemsLayout>
    <LinearItemsLayout Orientation="Horizontal"
                       SnapPointsType="MandatorySingle"
                       SnapPointsAlignment="Center" />
</CollectionView.ItemsLayout>
```

- `SnapPointsType`: `None`, `Mandatory`, `MandatorySingle`
- `SnapPointsAlignment`: `Start`, `Center`, `End`
