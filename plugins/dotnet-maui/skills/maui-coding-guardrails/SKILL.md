---
name: maui-coding-guardrails
description: >-
  Guardrails for .NET MAUI layouts, controls, and handlers. USE FOR: any MAUI
  code generation/review. NOT FOR: API deprecations (maui-current-apis) or
  environment setup (dotnet-maui-doctor).
---

# .NET MAUI Coding Guardrails

Apply to all MAUI code generation and editing. For API replacements, use `maui-current-apis`.

## Layout Rules

**Don't put ScrollView/CollectionView inside StackLayout.**
StackLayout gives children infinite height — ScrollView won't scroll, CollectionView loses virtualization. Use Grid.

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

**Prefer `VerticalStackLayout`/`HorizontalStackLayout` over `StackLayout`** — avoids legacy Orientation check each measure pass.

**Don't use `AndExpand` options.** No-ops in MAUI; use Grid row/column sizing.

**Flatten nested layouts.** Each nesting level adds measure/arrange cost; prefer flat Grids.

## Control Rules

### ⚠️ DO NOT USE `Frame` — Use `Border` Instead

`Frame` is Xamarin.Forms legacy. `Border` supports `StrokeShape`, custom strokes. Keep `Frame` only for `HasShadow`.

```xml
<Border StrokeShape="RoundRectangle 10" Stroke="Gray" StrokeThickness="1" Padding="12">
    <Label Text="Content" />
</Border>
```

**Use `CollectionView` over `ListView`.** ListView and all cell types deprecated in .NET 10. For ≤20 items, use `BindableLayout`.

**Use `Background` over `BackgroundColor`.** Accepts Color and Brush (gradients, images).

**Reference images as `.png`, not `.svg`.** SVGs compile to PNG; `.svg` fails at runtime.

## Navigation Rules

**Don't mix Shell with NavigationPage/TabbedPage/FlyoutPage.** Shell has its own stack; wrapping in NavigationPage creates competing stacks, corruption, double headers. Pick one paradigm.

**Set `App.MainPage` once.** Use Shell routing or `NavigationPage.PushAsync` after. Changing MainPage leaks pages/handlers.

## Handler Architecture

Use **handlers** and Mapper methods, not renderers. Renderers are Xamarin.Forms-only.

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

`AppendToMapping` runs after defaults, `PrependToMapping` before, `ModifyMapping` wraps one property.

## Compiled Bindings

Declare `x:DataType` on pages and DataTemplates — without it, bindings use slow reflection (8–20×) and typos fail silently.

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
| Gesture on parent+child — parent intercepts | `InputTransparent="True"` on overlay or restructure ownership |
| Unsubscribed events — pages leak | Unsubscribe in `OnDisappearing` or use `WeakReferenceMessenger` |
| Only testing on emulators | Test on physical devices — emulators hide perf/gesture issues |
