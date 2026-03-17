---
name: maui-coding-guardrails
description: >-
  Prevents common .NET MAUI mistakes: broken layouts, obsolete controls, renderer
  usage. USE FOR: any MAUI code generation or review. DO NOT USE FOR: API
  deprecation checks (use maui-current-apis), environment setup (use
  dotnet-maui-doctor).
---

# .NET MAUI Coding Guardrails

Always-active guardrail for every MAUI code generation or editing task.
For deprecated API replacements, see `maui-current-apis`.

## Layout Rules

**Don't put ScrollView or CollectionView inside StackLayout.**
StackLayout measures children with infinite height, so ScrollView never scrolls
and CollectionView loses virtualization. Use Grid instead.

```xml
<!-- ❌ StackLayout gives infinite height -->
<StackLayout>
    <ScrollView>...</ScrollView>
</StackLayout>

<!-- ✅ Grid constrains height -->
<Grid RowDefinitions="Auto,*,Auto">
    <Label Text="Header" />
    <ScrollView Grid.Row="1">...</ScrollView>
    <Button Grid.Row="2" Text="Submit" />
</Grid>
```

**Use `VerticalStackLayout`/`HorizontalStackLayout` over `StackLayout`.**
They skip the legacy Orientation check on every measure pass — faster.

**Don't use `AndExpand` layout options.** They're no-ops in MAUI — silently do
nothing. Use Grid with row/column definitions for expansion.

**Flatten nested layouts.** Each nesting level adds a measure/arrange pass.
Prefer flat Grid layouts over nested stack trees.

## Control Rules

### ⚠️ DO NOT USE `Frame` — Use `Border` Instead

`Frame` is a Xamarin.Forms holdover. `Border` replaces it with `StrokeShape`
for rounded corners and custom strokes. Only keep `Frame` for `HasShadow`.

```xml
<Border StrokeShape="RoundRectangle 10" Stroke="Gray" StrokeThickness="1" Padding="12">
    <Label Text="Content" />
</Border>
```

**Use `CollectionView` over `ListView`.** ListView and all cell types (TextCell,
ViewCell, etc.) are deprecated in .NET 10. For ≤20 static items, use
`BindableLayout` instead.

**Use `Background` over `BackgroundColor`.** `Background` accepts both Color and
Brush (gradients, images) — strictly more capable.

**Reference images as `.png`, not `.svg`.** SVGs convert to PNG at build time;
`.svg` doesn't exist at runtime.

## Navigation Rules

**Don't mix Shell with NavigationPage/TabbedPage/FlyoutPage.** Shell has its own
navigation stack; wrapping in NavigationPage creates two competing stacks causing
corruption and double headers. Pick one paradigm at startup.

**Set `App.MainPage` once.** Use Shell routing or NavigationPage.PushAsync after
that. Changing MainPage mid-lifecycle leaks pages and handlers.

## Handler Architecture

Use **handlers** and Mapper methods, not renderers. Renderers are a Xamarin.Forms
concept that doesn't exist in MAUI.

```csharp
EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, view) =>
{
#if ANDROID
    handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
#elif IOS
    handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
});
```

`AppendToMapping` runs after defaults, `PrependToMapping` before, `ModifyMapping`
wraps a specific property.

## Compiled Bindings

Declare `x:DataType` on pages and DataTemplates — without it, bindings use
runtime reflection (8–20× slower) and typos fail silently at runtime.

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

| Pitfall | Fix |
|---------|-----|
| Gesture on parent and child — parent intercepts | `InputTransparent="True"` on overlay, or restructure ownership |
| Unsubscribed event handlers — pages leak | Unsubscribe in `OnDisappearing` or use `WeakReferenceMessenger` |
| Testing only on emulators | Validate on physical devices — emulators hide perf/gesture issues |

## Additional Reference

See `references/control-reference.md` for full control quick-reference.
