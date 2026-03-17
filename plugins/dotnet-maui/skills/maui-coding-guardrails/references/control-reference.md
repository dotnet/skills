# .NET MAUI Control Quick Reference

## Layout Controls

| Control | Purpose | Notes |
|---------|---------|-------|
| `Grid` | Complex multi-row/column layouts | Preferred for most layouts |
| `VerticalStackLayout` | Simple vertical stacking | Faster than `StackLayout` — no orientation check |
| `HorizontalStackLayout` | Simple horizontal stacking | Same performance benefit |
| `FlexLayout` | CSS Flexbox-style wrapping layouts | Good for tag clouds, chip lists |
| `AbsoluteLayout` | Pixel/proportional positioning | Use sparingly — hard to maintain |
| `Border` | Rounded corners, borders, clipping | Replaces `Frame` — supports `StrokeShape` |
| `ContentView` | Custom control base class | Wrap reusable UI components |
| `ScrollView` | Scrollable content | Must be inside `Grid`, not `StackLayout` |

> ⚠️ **DO NOT USE `Frame`** — it is a legacy Xamarin.Forms control. Use `Border` for all new code.

## Input Controls

| Control | Purpose | Notes |
|---------|---------|-------|
| `Button` | Tap actions | Use `Command` binding |
| `ImageButton` | Image-based tap actions | Use `Command` binding |
| `CheckBox` | Boolean toggle | Bind `IsChecked` |
| `Switch` | On/off toggle | Bind `IsToggled` |
| `Entry` | Single-line text input | Use `Keyboard` for input type |
| `Editor` | Multi-line text input | Set `AutoSize="TextChanges"` |
| `Picker` | Drop-down selection | Bind `ItemsSource` and `SelectedItem` |
| `DatePicker` | Date selection | Bind `Date` |
| `TimePicker` | Time selection | Bind `Time` |
| `Slider` | Numeric range input | Bind `Value` |
| `Stepper` | Increment/decrement | Bind `Value` |
| `SearchBar` | Search input | Use `SearchCommand` |
| `RadioButton` | Single selection from group | Group with `GroupName` |

## List & Data Display

| Control | When to Use | Notes |
|---------|-------------|-------|
| `CollectionView` | > 20 items or dynamic data | Virtualized, supports selection, grouping, horizontal layouts |
| `BindableLayout` | ≤ 20 items or static lists | Attach to any `Layout` — no virtualization overhead |
| `CarouselView` | Swipeable card/page UI | Set `PeekAreaInsets` for peek effect |

```xml
<!-- BindableLayout for small static lists -->
<VerticalStackLayout BindableLayout.ItemsSource="{Binding SmallList}">
    <BindableLayout.ItemTemplate>
        <DataTemplate x:DataType="models:Option">
            <Label Text="{Binding Label}" />
        </DataTemplate>
    </BindableLayout.ItemTemplate>
</VerticalStackLayout>
```

## Display Controls

| Control | Purpose | Notes |
|---------|---------|-------|
| `Image` | Display images | Reference as `.png` only (SVGs convert at build time) |
| `Label` | Text display | Supports `FormattedText` for rich text |
| `WebView` | Embedded web content | Use `Source` property |
| `GraphicsView` | Custom 2D drawing | Implement `IDrawable` |
| `Map` | Interactive maps | Requires `Microsoft.Maui.Controls.Maps` NuGet |

## Status Indicators

| Control | Purpose | Notes |
|---------|---------|-------|
| `ActivityIndicator` | Indeterminate loading spinner | Use `IsRunning` to toggle |
| `ProgressBar` | Determinate progress (0.0–1.0) | Bind `Progress` property |

## Resource Directory Conventions

| Path | Content | Notes |
|------|---------|-------|
| `Resources/Images/` | App images | PNG, JPG, or SVG **source** files |
| `Resources/Fonts/` | Custom fonts | TTF, OTF — register in `MauiProgram.cs` |
| `Resources/Raw/` | Raw assets | JSON, TXT, HTML — accessed via `FileSystem.OpenAppPackageFileAsync` |
| `Resources/Styles/` | XAML styles & colors | `Colors.xaml`, `Styles.xaml` |
