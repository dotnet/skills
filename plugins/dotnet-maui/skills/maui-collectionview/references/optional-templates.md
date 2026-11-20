# Optional CollectionView templates

Read only the section matching the user's feature. Include MAUI/XAML namespace
declarations and the model/ViewModel namespaces when presenting a complete page.
Resource keys such as `Primary` and `Gray100` must exist in the app's resources.

## Selected visuals

The `Selected` visual state belongs on the item template root:

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

Use a collection of group objects and `IsGrouped="True"`. For groups whose members
change after binding, derive from `ObservableCollection<Animal>` instead of
`List<Animal>`. An observable outer collection alone only notifies group changes.

```csharp
public class AnimalGroup : List<Animal>
{
    public string Name { get; }
    public AnimalGroup(string name, List<Animal> animals) : base(animals)
        => Name = name;
}
```

```xml
<CollectionView ItemsSource="{Binding AnimalGroups}" IsGrouped="True">
    <CollectionView.GroupHeaderTemplate>
        <DataTemplate x:DataType="models:AnimalGroup">
            <Label Text="{Binding Name}" FontAttributes="Bold"
                   BackgroundColor="{StaticResource Gray100}" Padding="8" />
        </DataTemplate>
    </CollectionView.GroupHeaderTemplate>
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="models:Animal">
            <Label Text="{Binding Name}" Padding="16,4" />
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

## Swipe commands inside a DataTemplate

The template's context is an item, not the page ViewModel. Reach the ViewModel
using an ancestor binding, and pass the current item separately:

```xml
<CollectionView.ItemTemplate>
    <DataTemplate x:DataType="models:Item">
        <SwipeView>
            <SwipeView.RightItems>
                <SwipeItems>
                    <SwipeItem Text="Delete"
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

If source-binding compilation is enabled, give this command binding a data type
matching its source, not the item's inherited type. A ViewModel ancestor source
is another valid approach; see the data-binding skill's explicit-source guidance.

## EmptyView

`EmptyView` is shown when `ItemsSource` is null or empty:

```xml
<CollectionView ItemsSource="{Binding SearchResults}" EmptyView="No items found." />
```

Wrap a custom layout in `ContentView`:

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

## Headers and footers

```xml
<CollectionView ItemsSource="{Binding Items}">
    <CollectionView.Header>
        <Label Text="Header" Padding="8" />
    </CollectionView.Header>
    <CollectionView.Footer>
        <Label Text="Footer" Padding="8" />
    </CollectionView.Footer>
</CollectionView>
```

Use `HeaderTemplate` / `FooterTemplate` when those values are data-bound.

## Scrolling and snap points

```csharp
collectionView.ScrollTo(index: 10, position: ScrollToPosition.Center, animate: true);
collectionView.ScrollTo(item: myItem, position: ScrollToPosition.MakeVisible, animate: true);
```

`MakeVisible` moves only as far as needed; `Start`, `Center` and `End` choose the
item's alignment in the viewport.

```xml
<CollectionView.ItemsLayout>
    <LinearItemsLayout Orientation="Horizontal"
                       SnapPointsType="MandatorySingle"
                       SnapPointsAlignment="Center" />
</CollectionView.ItemsLayout>
```

`SnapPointsType` values: `None`, `Mandatory`, `MandatorySingle`.
`SnapPointsAlignment` values: `Start`, `Center`, `End`.
