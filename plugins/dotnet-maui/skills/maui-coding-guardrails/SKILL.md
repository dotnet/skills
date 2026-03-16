---
name: maui-coding-guardrails
description: >-
  Always-on guardrail for .NET MAUI development. Prevents use of obsolete
  controls, deprecated patterns, and common architectural mistakes.
  USE FOR: any .NET MAUI code generation, review, or editing task — this skill
  provides baseline rules that apply universally.
  DO NOT USE FOR: API currency checks across framework versions (use
  maui-current-apis instead), environment setup or workload issues (use
  dotnet-maui-doctor), or non-MAUI .NET projects.
---

# .NET MAUI Coding Guardrails

Always-active guardrail that prevents common .NET MAUI mistakes. These rules
apply to **every** MAUI code generation or editing task regardless of which
other skills are active.

## When to Use

- Any time you generate, edit, or review .NET MAUI XAML or C# code
- When scaffolding new pages, views, or controls
- When suggesting layout structures or control choices

## When Not to Use

- Non-MAUI .NET projects (ASP.NET, Blazor Server, WPF, WinForms)
- Checking API currency across .NET versions — use `maui-current-apis` instead
- Environment setup and workload troubleshooting — use `dotnet-maui-doctor`

## Critical Rules (NEVER Violate)

| Rule | Why | Use Instead |
|------|-----|-------------|
| **NEVER use `ListView`** | Obsolete — deprecated in .NET 10 | `CollectionView` |
| **NEVER use `TableView`** | Obsolete — deprecated in .NET 10 | `Grid` or `VerticalStackLayout` with individual controls |
| **NEVER use `AndExpand` layout options** | Obsolete, behavior is undefined | `Grid` with row/column definitions |
| **NEVER use `BackgroundColor`** | Deprecated property | `Background` (supports `Brush` and `Color`) |
| **NEVER place `ScrollView` or `CollectionView` inside `StackLayout`** | Breaks scrolling and virtualization | Use `Grid` as the parent container |
| **NEVER reference images as `.svg`** | SVG is only a source format for the build tooling | Reference as `.png` — the build generates PNGs from SVGs |
| **NEVER mix `Shell` with `NavigationPage` / `TabbedPage` / `FlyoutPage`** | Causes navigation corruption and undefined behavior | Choose **one** navigation paradigm |
| **NEVER use renderers** | Legacy Xamarin.Forms concept | Use **handlers** and `Mapper` / `CommandMapper` |

### Examples of Violations

```xml
<!-- ❌ WRONG — ListView is obsolete -->
<ListView ItemsSource="{Binding Items}" />

<!-- ✅ CORRECT — use CollectionView -->
<CollectionView ItemsSource="{Binding Items}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="models:Item">
            <Label Text="{Binding Name}" />
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

```xml
<!-- ❌ WRONG — ScrollView inside StackLayout breaks scrolling -->
<StackLayout>
    <ScrollView>
        <VerticalStackLayout>...</VerticalStackLayout>
    </ScrollView>
</StackLayout>

<!-- ✅ CORRECT — use Grid as parent -->
<Grid RowDefinitions="*">
    <ScrollView>
        <VerticalStackLayout>...</VerticalStackLayout>
    </ScrollView>
</Grid>
```

```xml
<!-- ❌ WRONG — BackgroundColor is deprecated -->
<Frame BackgroundColor="Red" />

<!-- ✅ CORRECT — use Background with Border -->
<Border Background="Red" />
```

## Control Quick Reference

### Layout Controls

| Control | Purpose | Notes |
|---------|---------|-------|
| `Grid` | Complex multi-row/column layouts | Preferred for most layouts |
| `VerticalStackLayout` | Simple vertical stacking | Prefer over `StackLayout` |
| `HorizontalStackLayout` | Simple horizontal stacking | Prefer over `StackLayout` |
| `FlexLayout` | CSS Flexbox-style layouts | Good for wrapping content |
| `Border` | Rounded corners, borders, clipping | **Replaces `Frame`** |
| `ContentView` | Custom control base class | Wrap reusable UI |
| `ScrollView` | Scrollable content | **Never** inside `StackLayout` |

> **`Frame` is legacy.** Use `Frame` only when you need a drop shadow.
> For all other cases use `Border` (supports `StrokeShape` for rounded corners).

### List & Data Display

| Control | When to Use | Notes |
|---------|-------------|-------|
| `CollectionView` | **> 20 items** or dynamic data | Supports virtualization, selection, grouping |
| `BindableLayout` | **≤ 20 items** or static lists | Attach to any `Layout` — no virtualization |
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

## Best Practices

### Compiled Bindings (8–20× Performance Improvement)

Always declare `x:DataType` on every `DataTemplate` and page:

```xml
<ContentPage xmlns:viewmodels="clr-namespace:MyApp.ViewModels"
             x:DataType="viewmodels:MainViewModel">

    <Label Text="{Binding Title}" />

    <CollectionView ItemsSource="{Binding Items}">
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="viewmodels:ItemViewModel">
                <Label Text="{Binding Name}" />
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>
```

> Without `x:DataType`, bindings use slow runtime reflection.

### Layout Selection

| Scenario | Use |
|----------|-----|
| Complex multi-area layout | `Grid` |
| Simple vertical list of elements | `VerticalStackLayout` |
| Simple horizontal row of elements | `HorizontalStackLayout` |
| Wrapping content | `FlexLayout` |
| **Avoid** | `StackLayout` (use specific orientation variants) |

### Border over Frame

```xml
<!-- ✅ Preferred — Border with rounded corners -->
<Border StrokeShape="RoundRectangle 10"
        Stroke="Gray"
        StrokeThickness="1"
        Padding="12">
    <Label Text="Content" />
</Border>

<!-- ⚠️ Legacy — Frame (only use when you need BoxShadow) -->
<Frame CornerRadius="10" Padding="12" HasShadow="True">
    <Label Text="Content" />
</Frame>
```

### Handler Customization

Use `Mapper` methods instead of custom renderers:

```csharp
Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, view) =>
{
#if ANDROID
    handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
#elif IOS
    handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
});
```

Available mapper methods:
- `AppendToMapping` — run **after** default mapping
- `PrependToMapping` — run **before** default mapping
- `ModifyMapping` — wrap/replace a specific property mapping

### UI Thread Access

```csharp
// From a ViewModel — inject IDispatcher
public class MyViewModel
{
    private readonly IDispatcher _dispatcher;

    public MyViewModel(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    private void UpdateUI()
    {
        _dispatcher.Dispatch(() => Status = "Done");
    }
}
```

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Mixing Shell with NavigationPage/TabbedPage/FlyoutPage | Choose one navigation paradigm. Shell manages its own navigation stack; wrapping Shell pages in `NavigationPage` causes corruption. |
| Changing `App.MainPage` frequently | Set `MainPage` once at startup. Use Shell routing (`GoToAsync`) or `NavigationPage.PushAsync` after that. |
| Gesture recognizers on both parent and child | Parent swallows events. Set `InputTransparent="True"` on the child, or restructure so only one element owns the gesture. |
| Unsubscribed event handlers causing memory leaks | Always unsubscribe in `OnDisappearing` or use weak references / `CommunityToolkit.Mvvm` messaging. |
| Deeply nested layouts | Flatten the visual hierarchy. Every nesting level adds a measure/arrange pass. Prefer `Grid` over nested `StackLayout` trees. |
| Testing only on emulators/simulators | Always validate on real devices. Emulators mask performance issues, gesture timing differences, and platform-specific rendering bugs. |

## Resource Management

### Image References

SVG files placed in `Resources/Images/` are converted to PNG at build time.
**Always reference the `.png` extension**, never `.svg`:

```xml
<!-- ✅ CORRECT -->
<Image Source="logo.png" />

<!-- ❌ WRONG — .svg does not exist at runtime -->
<Image Source="logo.svg" />
```

### Directory Conventions

| Path | Content | Notes |
|------|---------|-------|
| `Resources/Images/` | App images | PNG, JPG, or SVG **source** files |
| `Resources/Fonts/` | Custom fonts | TTF, OTF — register in `MauiProgram.cs` |
| `Resources/Raw/` | Raw assets | JSON, TXT, HTML — accessed via `FileSystem.OpenAppPackageFileAsync` |
| `Resources/Styles/` | XAML styles & colors | `Colors.xaml`, `Styles.xaml` |
