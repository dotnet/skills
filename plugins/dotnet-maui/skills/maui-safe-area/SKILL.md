---
name: maui-safe-area
description: >-
  .NET MAUI safe area and edge-to-edge layout guidance for .NET 10+. Covers the
  new SafeAreaEdges property, SafeAreaRegions enum, per-edge control, keyboard
  avoidance, Blazor Hybrid CSS safe areas, migration from legacy iOS-only APIs,
  and platform-specific behavior for Android, iOS, and Mac Catalyst.
  USE FOR: "safe area", "edge-to-edge", "SafeAreaEdges", "SafeAreaRegions",
  "keyboard avoidance", "notch insets", "status bar overlap", "iOS safe area",
  "Android edge-to-edge", "content behind status bar", "UseSafeArea migration",
  "soft input keyboard", "IgnoreSafeArea replacement".
  DO NOT USE FOR: general layout or grid design (use Grid and StackLayout),
  app lifecycle handling (use maui-app-lifecycle), theming or styling
  (use maui-theming), or Shell navigation structure.
---

# Safe Area & Edge-to-Edge Layout (.NET 10+)

.NET 10 introduces a **brand-new, cross-platform safe area API** that replaces the legacy iOS-only `UseSafeArea` and the layout-level `IgnoreSafeArea` properties. The new `SafeAreaEdges` property and `SafeAreaRegions` flags enum give you per-edge, per-control safe area management on Android, iOS, and Mac Catalyst from a single API surface.

> **This is new API surface in .NET 10.** If the project targets .NET 9 or earlier, these APIs do not exist. Guide the developer to the legacy `ios:Page.UseSafeArea` and `Layout.IgnoreSafeArea` properties instead.

## When to Use

- Content overlaps status bar, notch, Dynamic Island, or home indicator after upgrading to .NET 10
- Implementing edge-to-edge / immersive layouts (photo viewers, video players, maps)
- Keyboard avoidance for chat or form UIs
- Migrating from `ios:Page.UseSafeArea`, `Layout.IgnoreSafeArea`, or `WindowSoftInputModeAdjust.Resize`
- Blazor Hybrid apps that need CSS `env(safe-area-inset-*)` coordination
- Mixed layouts with an edge-to-edge header but a safe-area-respecting body

## When Not to Use

- Projects targeting .NET 9 or earlier — use the legacy iOS-specific APIs
- General page layout questions unrelated to system bars or keyboard — use standard layout guidance
- App lifecycle or navigation structure — use maui-app-lifecycle or Shell guidance
- Theming or visual styling — use the **maui-theming** skill

## Inputs

- Target framework: must be `net10.0-*` or later for the new APIs
- Target platforms: Android, iOS, Mac Catalyst (Windows does not have system bar insets)
- UI approach: XAML/C#, Blazor Hybrid, or MauiReactor

## SafeAreaRegions Enum

```csharp
[Flags]
public enum SafeAreaRegions
{
    None      = 0,       // Edge-to-edge — no safe area padding
    SoftInput = 1 << 0,  // Pad to avoid the on-screen keyboard
    Container = 1 << 1,  // Stay inside status bar, notch, home indicator
    Default   = -1,      // Use the platform default for the control type
    All       = 1 << 15  // Respect all safe area insets (most restrictive)
}
```

`SoftInput` and `Container` are combinable flags:
`SafeAreaRegions.Container | SafeAreaRegions.SoftInput` = respect system bars **and** keyboard.

## SafeAreaEdges Struct

```csharp
public readonly struct SafeAreaEdges
{
    public SafeAreaRegions Left { get; }
    public SafeAreaRegions Top { get; }
    public SafeAreaRegions Right { get; }
    public SafeAreaRegions Bottom { get; }

    // Uniform — same value for all four edges
    public SafeAreaEdges(SafeAreaRegions uniformValue)

    // Horizontal / Vertical
    public SafeAreaEdges(SafeAreaRegions horizontal, SafeAreaRegions vertical)

    // Per-edge
    public SafeAreaEdges(SafeAreaRegions left, SafeAreaRegions top,
                         SafeAreaRegions right, SafeAreaRegions bottom)
}
```

Static presets: `SafeAreaEdges.None`, `SafeAreaEdges.All`, `SafeAreaEdges.Default`.

### XAML Type Converter

Follows Thickness-like comma-separated syntax:

```xaml
<!-- Uniform -->
SafeAreaEdges="Container"

<!-- Horizontal, Vertical -->
SafeAreaEdges="Container, SoftInput"

<!-- Left, Top, Right, Bottom -->
SafeAreaEdges="Container, Container, Container, SoftInput"
```

## Control Defaults

| Control | Default | Notes |
|---------|---------|-------|
| `ContentPage` | `None` | Edge-to-edge. **Breaking change from .NET 9 on Android.** |
| `Layout` (Grid, StackLayout, etc.) | `Container` | Respects bars/notch, flows under keyboard |
| `ScrollView` | `Default` | iOS maps to automatic content insets. Only `Container` and `None` take effect. |
| `ContentView` | `None` | Inherits parent behavior |
| `Border` | `None` | Inherits parent behavior |

## Breaking Changes from .NET 9

### ContentPage default changed to `None`

In .NET 9, Android `ContentPage` behaved like `Container`. In .NET 10, the default is `None` on **all platforms**. If your Android content goes behind the status bar after upgrading:

```xaml
<!-- .NET 10 default — content extends under status bar -->
<ContentPage>

<!-- Restore .NET 9 Android behavior -->
<ContentPage SafeAreaEdges="Container">
```

### WindowSoftInputModeAdjust.Resize removed

If you used `WindowSoftInputModeAdjust.Resize` in .NET 9, replace it with `SafeAreaEdges="All"` on the ContentPage for equivalent keyboard avoidance.

## Usage Patterns

### Edge-to-edge immersive content

Set `None` on **both** page and layout — layouts default to `Container`:

```xaml
<ContentPage SafeAreaEdges="None">
    <Grid SafeAreaEdges="None">
        <Image Source="background.jpg" Aspect="AspectFill" />
        <VerticalStackLayout Padding="20" VerticalOptions="End">
            <Label Text="Overlay text" TextColor="White" FontSize="24" />
        </VerticalStackLayout>
    </Grid>
</ContentPage>
```

### Forms and critical content

```xaml
<ContentPage SafeAreaEdges="All">
    <VerticalStackLayout Padding="20">
        <Label Text="Safe content" FontSize="18" />
        <Entry Placeholder="Enter text" />
        <Button Text="Submit" />
    </VerticalStackLayout>
</ContentPage>
```

### Keyboard-aware chat layout

```xaml
<ContentPage>
    <Grid RowDefinitions="*,Auto"
          SafeAreaEdges="Container, Container, Container, SoftInput">
        <ScrollView Grid.Row="0">
            <VerticalStackLayout Padding="20" Spacing="10">
                <Label Text="Messages" FontSize="24" />
            </VerticalStackLayout>
        </ScrollView>
        <Border Grid.Row="1" BackgroundColor="LightGray" Padding="20">
            <Grid ColumnDefinitions="*,Auto" Spacing="10">
                <Entry Placeholder="Type a message..." />
                <Button Grid.Column="1" Text="Send" />
            </Grid>
        </Border>
    </Grid>
</ContentPage>
```

### Mixed: edge-to-edge header + safe body + keyboard footer

```xaml
<ContentPage SafeAreaEdges="None">
    <Grid RowDefinitions="Auto,*,Auto">
        <Grid BackgroundColor="{StaticResource Primary}">
            <Label Text="App Header" TextColor="White" Margin="20,40,20,20" />
        </Grid>
        <ScrollView Grid.Row="1" SafeAreaEdges="Container">
            <!-- Use Container, not All — ScrollView only honors Container and None -->
            <VerticalStackLayout Padding="20">
                <Label Text="Main content" />
            </VerticalStackLayout>
        </ScrollView>
        <Grid Grid.Row="2" SafeAreaEdges="SoftInput"
              BackgroundColor="LightGray" Padding="20">
            <Entry Placeholder="Type a message..." />
        </Grid>
    </Grid>
</ContentPage>
```

### Programmatic (C#)

```csharp
var page = new ContentPage
{
    SafeAreaEdges = SafeAreaEdges.All
};

var grid = new Grid
{
    SafeAreaEdges = new SafeAreaEdges(
        left: SafeAreaRegions.Container,
        top: SafeAreaRegions.Container,
        right: SafeAreaRegions.Container,
        bottom: SafeAreaRegions.SoftInput)
};
```

## Decision Framework

| Scenario | SafeAreaEdges value |
|----------|---------------------|
| Forms, critical inputs | `All` |
| Photo viewer, video player, game | `None` (on page **and** layout) |
| Scrollable content with fixed header/footer | `Container` |
| Chat/messaging with bottom input bar | Per-edge: `Container, Container, Container, SoftInput` |
| Blazor Hybrid app | `None` on page; CSS `env()` for insets |

## Blazor Hybrid Integration

For Blazor Hybrid apps, let CSS handle safe areas to avoid double-padding.

1. **Page stays edge-to-edge** (default in .NET 10):

```xaml
<ContentPage SafeAreaEdges="None">
    <BlazorWebView HostPage="wwwroot/index.html">
        <BlazorWebView.RootComponents>
            <RootComponent Selector="#app" ComponentType="{x:Type local:Routes}" />
        </BlazorWebView.RootComponents>
    </BlazorWebView>
</ContentPage>
```

2. **Add `viewport-fit=cover`** in `index.html`:

```html
<meta name="viewport" content="width=device-width, initial-scale=1.0,
      maximum-scale=1.0, user-scalable=no, viewport-fit=cover" />
```

3. **Use CSS `env()` functions**:

```css
body {
    padding-top: env(safe-area-inset-top);
    padding-bottom: env(safe-area-inset-bottom);
    padding-left: env(safe-area-inset-left);
    padding-right: env(safe-area-inset-right);
}
```

Available CSS environment variables: `env(safe-area-inset-top)`, `env(safe-area-inset-bottom)`, `env(safe-area-inset-left)`, `env(safe-area-inset-right)`.

## Migration from Legacy APIs

| Legacy (.NET 9 and earlier) | New (.NET 10+) |
|-----------------------------|----------------|
| `ios:Page.UseSafeArea="True"` | `SafeAreaEdges="Container"` |
| `Layout.IgnoreSafeArea="True"` | `SafeAreaEdges="None"` |
| `WindowSoftInputModeAdjust.Resize` | `SafeAreaEdges="All"` on ContentPage |

The legacy properties still compile but are marked obsolete. `IgnoreSafeArea="True"` maps internally to `SafeAreaRegions.None`.

```xaml
<!-- .NET 9 (legacy, iOS-only) -->
<ContentPage xmlns:ios="clr-namespace:Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;assembly=Microsoft.Maui.Controls"
             ios:Page.UseSafeArea="True">

<!-- .NET 10+ (cross-platform) -->
<ContentPage SafeAreaEdges="Container">
```

## Platform-Specific Behavior

### iOS & Mac Catalyst

- Safe area insets cover: status bar, navigation bar, tab bar, notch/Dynamic Island, home indicator
- `SoftInput` includes the keyboard when visible
- Insets update automatically on rotation and UI visibility changes
- `ScrollView` with `Default` maps to `UIScrollViewContentInsetAdjustmentBehavior.Automatic`

Transparent navigation bar for content behind the nav bar:

```xaml
<Shell Shell.BackgroundColor="#80000000" Shell.NavBarHasShadow="False" />
```

### Android

- Safe area insets cover: system bars (status/navigation) and display cutouts
- `SoftInput` includes the soft keyboard
- MAUI uses `WindowInsetsCompat` and `WindowInsetsAnimationCompat` internally
- Behavior varies by Android version and OEM edge-to-edge settings

## Common Pitfalls

1. **Forgetting to set `None` on the layout too.** `ContentPage SafeAreaEdges="None"` makes the page edge-to-edge, but child layouts default to `Container` and still pad inward. Set `None` on both page and layout for truly immersive content.

2. **Using `SoftInput` directly on ScrollView.** ScrollView manages its own content insets and ignores `SoftInput`. Wrap the ScrollView in a Grid or StackLayout and apply `SoftInput` there.

3. **Confusing `Default` with `None`.** `Default` means "platform default for this control type" — on ScrollView (iOS) this enables automatic content insets. `None` means "no safe area padding at all."

4. **Double-padding in Blazor Hybrid.** Setting `SafeAreaEdges="Container"` on the page **and** using CSS `env(safe-area-inset-*)` results in doubled insets. Pick one approach — CSS is recommended for Blazor.

5. **Missing `viewport-fit=cover` in Blazor.** Without this meta tag, CSS `env(safe-area-inset-*)` values are always zero on iOS.

6. **Assuming .NET 9 behavior on Android.** After upgrading to .NET 10, Android `ContentPage` defaults to `None` (was effectively `Container`). Add `SafeAreaEdges="Container"` to restore the previous behavior.

7. **Using legacy `ios:Page.UseSafeArea` in new code.** The old API is iOS-only and obsolete. Always use `SafeAreaEdges` for cross-platform safe area management.

## Checklist

- [ ] Android upgrade: `SafeAreaEdges="Container"` added if content goes under status bar
- [ ] Edge-to-edge: `None` set on **both** page and layout
- [ ] ScrollView keyboard avoidance uses wrapper Grid, not ScrollView's own `SafeAreaEdges`
- [ ] Blazor Hybrid: using either XAML or CSS safe areas, not both
- [ ] `viewport-fit=cover` in Blazor's `index.html` `<meta viewport>` tag
- [ ] Legacy `UseSafeArea` / `IgnoreSafeArea` migrated to `SafeAreaEdges`
