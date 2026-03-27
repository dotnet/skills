# Safe Area API Reference

## SafeAreaRegions Enum (flags)

```csharp
[Flags]
public enum SafeAreaRegions
{
    None      = 0,       // Edge-to-edge — no safe area padding
    SoftInput = 1 << 0,  // Pad to avoid keyboard
    Container = 1 << 1,  // Stay out of bars/notch, flow under keyboard
    Default   = -1,      // Platform default for the control type
    All       = 1 << 15  // Obey all safe area insets (most restrictive)
}
```

`SoftInput` and `Container` are flags and can be combined:
`SafeAreaRegions.Container | SafeAreaRegions.SoftInput` = respect bars AND keyboard.

## SafeAreaEdges Struct

```csharp
public readonly struct SafeAreaEdges
{
    public SafeAreaRegions Left { get; }
    public SafeAreaRegions Top { get; }
    public SafeAreaRegions Right { get; }
    public SafeAreaRegions Bottom { get; }

    // Uniform — same value for all edges
    public SafeAreaEdges(SafeAreaRegions uniformValue)

    // Horizontal/Vertical
    public SafeAreaEdges(SafeAreaRegions horizontal, SafeAreaRegions vertical)

    // Per-edge
    public SafeAreaEdges(SafeAreaRegions left, SafeAreaRegions top,
                         SafeAreaRegions right, SafeAreaRegions bottom)
}
```

**Static presets:** `SafeAreaEdges.None`, `SafeAreaEdges.All`, `SafeAreaEdges.Default`

## XAML Type Converter

The XAML type converter follows Thickness-like syntax with comma-separated values:

```xaml
<!-- Uniform: all edges = Container -->
SafeAreaEdges="Container"

<!-- Horizontal, Vertical -->
SafeAreaEdges="Container, SoftInput"

<!-- Left, Top, Right, Bottom -->
SafeAreaEdges="Container, Container, Container, SoftInput"
```

## Controls That Support SafeAreaEdges

| Control | Default value | Notes |
|---------|--------------|-------|
| `ContentPage` | `None` | Edge-to-edge. **Breaking change from .NET 9 Android.** |
| `Layout` (Grid, StackLayout, etc.) | `Container` | Respects bars/notch, flows under keyboard |
| `ScrollView` | `Default` | iOS: maps to `UIScrollViewContentInsetAdjustmentBehavior.Automatic`. Only `Container` and `None` have effect. |
| `ContentView` | `None` | Inherits parent behavior |
| `Border` | `None` | Inherits parent behavior |

## Usage Pattern Examples

### Edge-to-edge content (background images, immersive UIs)

```xaml
<ContentPage SafeAreaEdges="None">
    <Grid SafeAreaEdges="None">
        <Image Source="background.jpg" Aspect="AspectFill" />
        <VerticalStackLayout Padding="20"
                             VerticalOptions="End">
            <Label Text="Overlay text"
                   TextColor="White"
                   FontSize="24" />
        </VerticalStackLayout>
    </Grid>
</ContentPage>
```

### Respect all safe areas (forms, critical content)

```xaml
<ContentPage SafeAreaEdges="All">
    <VerticalStackLayout Padding="20">
        <Label Text="Safe content" FontSize="18" />
        <Entry Placeholder="Enter text" />
        <Button Text="Submit" />
    </VerticalStackLayout>
</ContentPage>
```

### Keyboard-aware chat/messaging layout

```xaml
<ContentPage>
    <Grid RowDefinitions="*,Auto"
          SafeAreaEdges="Container, Container, Container, SoftInput">
        <ScrollView Grid.Row="0">
            <VerticalStackLayout Padding="20" Spacing="10">
                <Label Text="Messages" FontSize="24" />
            </VerticalStackLayout>
        </ScrollView>

        <Border Grid.Row="1"
                BackgroundColor="LightGray"
                Padding="20">
            <HorizontalStackLayout Spacing="10">
                <Entry Placeholder="Type a message..."
                       HorizontalOptions="Fill" />
                <Button Text="Send" />
            </HorizontalStackLayout>
        </Border>
    </Grid>
</ContentPage>
```

### Mixed layout — edge-to-edge header, safe body

```xaml
<ContentPage SafeAreaEdges="None">
    <Grid RowDefinitions="Auto,*,Auto">
        <!-- Header: edge-to-edge behind status bar -->
        <Grid BackgroundColor="Primary">
            <Label Text="App Header"
                   TextColor="White"
                   Margin="20,40,20,20" />
        </Grid>

        <!-- Body: respect safe areas -->
        <ScrollView Grid.Row="1" SafeAreaEdges="All">
            <VerticalStackLayout Padding="20">
                <Label Text="Main content" />
            </VerticalStackLayout>
        </ScrollView>

        <!-- Footer: keyboard-aware -->
        <Grid Grid.Row="2"
              SafeAreaEdges="SoftInput"
              BackgroundColor="LightGray"
              Padding="20">
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
    // Per-edge: Container on top/left/right, SoftInput on bottom
    SafeAreaEdges = new SafeAreaEdges(
        left: SafeAreaRegions.Container,
        top: SafeAreaRegions.Container,
        right: SafeAreaRegions.Container,
        bottom: SafeAreaRegions.SoftInput)
};
```

## Blazor Hybrid Setup

### Recommended approach

1. **Set the page to edge-to-edge** (default in .NET 10):

```xaml
<ContentPage SafeAreaEdges="None">
    <BlazorWebView HostPage="wwwroot/index.html">
        <BlazorWebView.RootComponents>
            <RootComponent Selector="#app" ComponentType="{x:Type local:Routes}" />
        </BlazorWebView.RootComponents>
    </BlazorWebView>
</ContentPage>
```

2. **Add `viewport-fit=cover`** in `index.html` to let CSS access safe area insets:

```html
<meta name="viewport" content="width=device-width, initial-scale=1.0,
      maximum-scale=1.0, user-scalable=no, viewport-fit=cover" />
```

3. **Use CSS `env()` functions** for safe area insets:

```css
/* Status bar spacer for iOS */
.status-bar-safe-area {
    display: none;
}

@supports (-webkit-touch-callout: none) {
    .status-bar-safe-area {
        display: flex;
        position: sticky;
        top: 0;
        height: env(safe-area-inset-top);
        background-color: var(--header-bg, #f7f7f7);
        width: 100%;
        z-index: 1;
    }
}

/* General safe area padding */
body {
    padding-top: env(safe-area-inset-top);
    padding-bottom: env(safe-area-inset-bottom);
    padding-left: env(safe-area-inset-left);
    padding-right: env(safe-area-inset-right);
}
```

Available CSS environment variables:
- `env(safe-area-inset-top)` — status bar, notch, Dynamic Island
- `env(safe-area-inset-bottom)` — home indicator, navigation bar
- `env(safe-area-inset-left)` — landscape left edge
- `env(safe-area-inset-right)` — landscape right edge

## Migration from Legacy APIs

### From ios:Page.UseSafeArea (iOS-only)

```xaml
<!-- .NET 9 (legacy) -->
<ContentPage xmlns:ios="clr-namespace:Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;assembly=Microsoft.Maui.Controls"
             ios:Page.UseSafeArea="True">

<!-- .NET 10+ (cross-platform) -->
<ContentPage SafeAreaEdges="Container">
```

### From Layout.IgnoreSafeArea

```xaml
<!-- .NET 9 (legacy) -->
<Grid IgnoreSafeArea="True">

<!-- .NET 10+ -->
<Grid SafeAreaEdges="None">
```

The legacy properties still work but are obsolete. `IgnoreSafeArea="True"` maps
internally to `SafeAreaRegions.None`.

## Platform-Specific Behavior Details

### iOS & Mac Catalyst

- Safe area insets include: status bar, navigation bar, tab bar, notch/Dynamic
  Island, home indicator
- `SoftInput` includes the keyboard when visible
- Insets update automatically on rotation and UI visibility changes
- `ScrollView` with `Default` maps to `UIScrollViewContentInsetAdjustmentBehavior.Automatic`

**Reading safe area insets at runtime (iOS only):**

```csharp
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

Thickness insets = On<iOS>().SafeAreaInsets();
// insets.Top, insets.Bottom, insets.Left, insets.Right
```

**Transparent navigation bar for edge-to-edge under nav bar:**

```xaml
<!-- Shell -->
<Shell Shell.BackgroundColor="#80000000"
       Shell.NavBarHasShadow="False" />

<!-- NavigationPage -->
<NavigationPage BarBackgroundColor="#80000000"
    ios:NavigationPage.HideNavigationBarSeparator="True" />
```

### Android

- Safe area insets include: system bars (status/navigation) and display cutouts
- `SoftInput` includes the soft keyboard
- Behavior varies by Android version and edge-to-edge settings
- MAUI uses `WindowInsetsCompat` and `WindowInsetsAnimationCompat` internally
