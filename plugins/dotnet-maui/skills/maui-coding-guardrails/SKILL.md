---
name: maui-coding-guardrails
description: >-
  Prevents common .NET MAUI mistakes: broken layouts, obsolete controls, renderer
  usage. USE FOR: any MAUI code generation or review. DO NOT USE FOR: API
  deprecation checks (use maui-current-apis), environment setup (use
  dotnet-maui-doctor).
---

# .NET MAUI Coding Guardrails

Always-active guardrail that prevents common .NET MAUI mistakes. These rules
apply to **every** MAUI code generation or editing task regardless of which
other skills are active.

For deprecated API replacements across .NET versions, see `maui-current-apis`.

## When to Use

- Any time you generate, edit, or review .NET MAUI XAML or C# code
- When scaffolding new pages, views, or controls
- When suggesting layout structures or control choices

## When Not to Use

- Non-MAUI .NET projects (ASP.NET, Blazor Server, WPF, WinForms)
- Checking API currency across .NET versions — use `maui-current-apis`
- Environment setup and workload troubleshooting — use `dotnet-maui-doctor`

## Layout Rules

### Don't put ScrollView or CollectionView inside StackLayout

StackLayout measures children with **infinite height** — it asks "how tall do you
want to be?" ScrollView answers "infinite" and never thinks it needs to scroll.
CollectionView loses virtualization for the same reason. Grid constrains children
to available space, which is what scrollable views need.

```xml
<!-- ❌ StackLayout gives infinite height — ScrollView never scrolls -->
<StackLayout>
    <ScrollView>
        <VerticalStackLayout>...</VerticalStackLayout>
    </ScrollView>
</StackLayout>

<!-- ✅ Grid constrains height — ScrollView knows when to scroll -->
<Grid RowDefinitions="Auto,*,Auto">
    <Label Text="Header" />
    <ScrollView Grid.Row="1">
        <VerticalStackLayout>...</VerticalStackLayout>
    </ScrollView>
    <Button Grid.Row="2" Text="Submit" />
</Grid>
```

### Prefer specific layout controls over StackLayout

MAUI simplified layout by splitting `StackLayout` into `VerticalStackLayout` and
`HorizontalStackLayout`. These skip the legacy `Orientation` property check on
every measure pass, so they're faster. Avoid the generic `StackLayout`.

| Scenario | Use |
|----------|-----|
| Complex multi-area layout | `Grid` |
| Simple vertical stacking | `VerticalStackLayout` |
| Simple horizontal row | `HorizontalStackLayout` |
| Wrapping content | `FlexLayout` |

### Don't use AndExpand layout options

MAUI redesigned the layout engine from Xamarin.Forms. The `AndExpand` suffix
(`FillAndExpand`, `CenterAndExpand`, etc.) has no defined behavior in MAUI —
it's a no-op that silently does nothing. Use `Grid` with row/column definitions
for expansion control.

### Flatten deeply nested layouts

Every nesting level adds a measure/arrange pass. A `Grid` inside a
`VerticalStackLayout` inside another `Grid` forces three layout cycles. Prefer
flat `Grid` layouts with row/column definitions over nested stack trees.

## Control Rules

### ⚠️ DO NOT USE `Frame` — Use `Border` Instead

`Frame` is a Xamarin.Forms holdover with limited styling. `Border` is the MAUI
replacement — it supports `StrokeShape` for rounded corners, custom strokes, and
clipping. The only reason to keep `Frame` is the `HasShadow` property, which
`Border` doesn't have.

```xml
<!-- ✅ Border with rounded corners -->
<Border StrokeShape="RoundRectangle 10"
        Stroke="Gray"
        StrokeThickness="1"
        Padding="12">
    <Label Text="Content" />
</Border>
```

### Use CollectionView instead of ListView

`ListView` is deprecated in .NET 10 along with all its cell types (`TextCell`,
`ViewCell`, `ImageCell`, etc.). It also lacks features that `CollectionView`
provides: horizontal layouts, multi-selection, snap points, and incremental
loading. For small static lists (≤ 20 items), use `BindableLayout` on any layout
container instead.

### Use Background instead of BackgroundColor

`BackgroundColor` only accepts `Color` values. `Background` accepts both `Color`
and `Brush` (gradients, images), making it strictly more capable. MAUI is
standardizing on `Background` across controls.

### Reference images as .png, not .svg

SVG files in `Resources/Images/` are converted to PNG at build time. The `.svg`
file doesn't exist in the app bundle at runtime — referencing it causes a missing
image. Always use the `.png` extension.

```xml
<!-- ✅ Correct — references the build output -->
<Image Source="logo.png" />

<!-- ❌ Fails at runtime — .svg doesn't exist in the bundle -->
<Image Source="logo.svg" />
```

## Navigation Rules

### Don't mix Shell with NavigationPage, TabbedPage, or FlyoutPage

Shell maintains its own navigation stack internally. Wrapping a Shell page inside
`NavigationPage` creates two competing navigation stacks — one managed by Shell
and one by NavigationPage — leading to corruption, double headers, and undefined
back-button behavior. Pick one navigation paradigm at app startup.

### Set MainPage once

Set `App.MainPage` once during startup. After that, use Shell routing
(`GoToAsync`) or `NavigationPage.PushAsync` for navigation. Changing `MainPage`
mid-lifecycle can leak pages and handlers.

## Handler Architecture

Use **handlers** and `Mapper` methods instead of custom renderers. Renderers are
a Xamarin.Forms concept that doesn't exist in MAUI. Handlers are the MAUI
replacement — they're lighter, composable, and support platform-specific
customization without subclassing.

```csharp
// In MauiProgram.cs — customize Entry to remove border
Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, view) =>
{
#if ANDROID
    handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
#elif IOS
    handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
});
```

- `AppendToMapping` — runs **after** the default property mapping
- `PrependToMapping` — runs **before** the default mapping
- `ModifyMapping` — wraps or replaces a specific property mapping

## Compiled Bindings

Always declare `x:DataType` on pages and DataTemplates. Without it, bindings use
runtime reflection which is 8–20× slower than compiled bindings. Compiled bindings
also catch typos at build time instead of failing silently at runtime.

```xml
<ContentPage x:DataType="viewmodels:MainViewModel">
    <CollectionView ItemsSource="{Binding Items}">
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="viewmodels:ItemViewModel">
                <Label Text="{Binding Name}" />
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>
```

## Common Pitfalls

| Pitfall | Why it happens | Fix |
|---------|---------------|-----|
| Gesture recognizers on both parent and child | Parent intercepts touch events before child sees them | Set `InputTransparent="True"` on overlay, or restructure so only one element owns the gesture |
| Unsubscribed event handlers | Pages stay in memory because handler references pin them | Unsubscribe in `OnDisappearing` or use `WeakReferenceMessenger` from CommunityToolkit.Mvvm |
| Testing only on emulators | Emulators don't surface real-device issues like gesture timing, GPU rendering, and thermal throttling | Always validate on physical devices before shipping |

## Additional Reference

For a complete control quick-reference (layout, input, list, and display
controls with usage notes), see `references/control-reference.md`.
