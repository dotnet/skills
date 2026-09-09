---
name: maui-theming
description: >-
  Guide for theming .NET MAUI apps — light/dark mode via AppThemeBinding,
  ResourceDictionary theme switching, DynamicResource bindings, system theme
  detection, and user theme preferences.
  Use when: "dark mode", "light mode", "theming", "AppThemeBinding",
  "theme switching", "ResourceDictionary theme", "dynamic resources",
  "system theme detection", "color scheme", "app theme", "DynamicResource".
  Do not use for: localization or language switching (see .NET MAUI localization
  documentation), accessibility visual adjustments (see .NET MAUI accessibility
  documentation), app icons or splash screens (see .NET MAUI app icons
  documentation), or Bootstrap-style class theming (see Plugin.Maui.BootstrapTheme
  NuGet package).
license: MIT
---

# .NET MAUI Theming

Apply light/dark mode support, custom branded themes, and runtime theme switching in .NET MAUI apps using AppThemeBinding, ResourceDictionary swapping, and system theme detection APIs.

## When to Use

- Adding light and dark mode support to a .NET MAUI app
- Creating custom branded themes with ResourceDictionary
- Detecting and responding to system theme changes at runtime
- Letting users choose a preferred theme (light, dark, or system default)
- Combining OS-driven theme response with custom color palettes

## When Not to Use

- Localization or language switching — see [.NET MAUI localization docs](https://learn.microsoft.com/dotnet/maui/fundamentals/localization)
- Accessibility-specific visual adjustments — see [.NET MAUI accessibility docs](https://learn.microsoft.com/dotnet/maui/fundamentals/accessibility)
- App icon or splash screen configuration — see [.NET MAUI app icon docs](https://learn.microsoft.com/dotnet/maui/user-interface/images/app-icons)
- Bootstrap-style class theming — see the `Plugin.Maui.BootstrapTheme` NuGet package

## Inputs

- A .NET MAUI project targeting .NET 8 or later
- XAML pages or C# UI code that need theme-aware styling

## Workflow

1. Detect the current theme approach in the project (AppThemeBinding, ResourceDictionary, or none).
2. Choose the appropriate strategy: AppThemeBinding for simple light/dark, ResourceDictionary swap for custom/multiple themes, or both combined.
3. Define theme resources — inline `AppThemeBinding` values or separate `ResourceDictionary` files with matching keys.
4. Replace hardcoded colors with `DynamicResource` bindings (or `AppThemeBinding` markup) throughout XAML pages.
5. Add system theme detection via `Application.Current.RequestedTheme` and the `RequestedThemeChanged` event.
6. Implement user preference persistence with `Preferences.Set` / `Preferences.Get` and apply on startup.
7. Verify Android `ConfigChanges.UiMode` is set on `MainActivity` to avoid activity restarts on theme change.
8. Test both light and dark themes on at least one target platform, confirming all UI elements respond correctly.

## Rules That Change the Answer

Check these rules against the user's scenario, and apply **only** the ones that
affect what they asked. `UiMode` and dictionary swapping matter for *runtime theme
switching*; they are noise in a question about setting up `AppThemeBinding`.

**Answer narrowly, but completely.** Completeness means showing the code that
implements *what you recommended* — not adding adjacent topics. If you recommend
`DynamicResource`, show the dictionary swap that makes it update. If the user asks
for light/dark colours in C#, show **both** `SetAppThemeColor` (colours) and the
generic `SetAppTheme<T>` (any bindable property type), and prefer resource keys over
scattered hardcoded colours. Do not tack on platform configuration the question
didn't raise.

| Rule | Do this | Not this | Why |
|---|---|---|---|
| **Runtime-swapped values must be dynamic** | `{DynamicResource Key}` | `{StaticResource Key}` | `StaticResource` resolves once at load and never updates when dictionaries are swapped. |
| **Android must declare `UiMode`** *(only for runtime/system theme switching)* | Include `ConfigChanges.UiMode` in the `ConfigurationChanges` list on `MainActivity` | Omitting it | Without it Android restarts the activity on theme change — navigation state is lost and it looks like a crash. Irrelevant to a static `AppThemeBinding` setup |
| **Force a theme via `UserAppTheme`** | `Application.Current.UserAppTheme = AppTheme.Dark` | Manually re-assigning colors | `UserAppTheme` overrides the OS; `AppTheme.Unspecified` returns to following the system. |

**Do not** replace a working `AppThemeBinding` setup with ResourceDictionary
swapping (or vice versa) unless the user needs what the other approach provides —
more than two themes, or a user-selectable theme.

## Choosing an Approach

| Approach | Best for | Limitation |
|----------|----------|------------|
| **AppThemeBinding** | Automatic light/dark with OS — minimal code | Only two themes (light + dark) |
| **ResourceDictionary swap** | Custom branded themes, more than two themes, user preference | More setup; must use `DynamicResource` everywhere |
| **Both combined** | OS-driven response plus custom theme colors | Most flexible but most complex |

## AppThemeBinding (OS Light/Dark)

`AppThemeBinding` selects a value based on the current system theme. It supports `Light`, `Dark`, and an optional `Default` fallback.

### Define the palette once — don't scatter literals

Putting `{AppThemeBinding Light=#333333, Dark=#FFFFFF}` on every element is the
single most common theming mistake: the palette ends up duplicated across dozens of
files and cannot be changed in one place. **Recommend this shape as the final
answer**, not inline literals:

```xml
<!-- App.xaml — one source of truth for the whole app -->
<Application.Resources>
    <ResourceDictionary>

        <!-- 1. Raw palette -->
        <Color x:Key="LightPageBackground">#FFFFFF</Color>
        <Color x:Key="DarkPageBackground">#1E1E1E</Color>
        <Color x:Key="LightPrimaryText">#333333</Color>
        <Color x:Key="DarkPrimaryText">#E0E0E0</Color>

        <!-- 2. Implicit styles bind the pair once; every page inherits them -->
        <Style TargetType="ContentPage" ApplyToDerivedTypes="True">
            <Setter Property="BackgroundColor"
                    Value="{AppThemeBinding Light={StaticResource LightPageBackground},
                                            Dark={StaticResource DarkPageBackground}}" />
        </Style>

        <Style TargetType="Label">
            <Setter Property="TextColor"
                    Value="{AppThemeBinding Light={StaticResource LightPrimaryText},
                                            Dark={StaticResource DarkPrimaryText}}" />
        </Style>

    </ResourceDictionary>
</Application.Resources>
```

Pages then need **no theming markup at all** — they pick the styles up implicitly.
Use an inline `AppThemeBinding` only for genuine one-offs, and even then reference
`{StaticResource}` keys rather than literal hex.

### XAML (inline form, for one-offs)

```xml
<Label Text="Themed text"
       TextColor="{AppThemeBinding Light=Green, Dark=Red}"
       BackgroundColor="{AppThemeBinding Light=White, Dark=Black}" />

<!-- With resource references — preferred over literals -->
<Label TextColor="{AppThemeBinding Light={StaticResource LightPrimary},
                                   Dark={StaticResource DarkPrimary}}" />
```

### C# Extension Methods

Show **both** when answering a "light/dark colours in C#" question — `SetAppThemeColor`
covers `Color` properties, `SetAppTheme<T>` covers everything else:

```csharp
var label = new Label();

// Color-specific helper
label.SetAppThemeColor(Label.TextColorProperty, Colors.Green, Colors.Red);

// Generic helper — works for any bindable property type, not just Color
label.SetAppTheme<Color>(Label.TextColorProperty, Colors.Green, Colors.Red);
label.SetAppTheme<double>(Label.FontSizeProperty, 14, 16);

// The BindableProperty must belong to the object you call it on —
// Image.SourceProperty goes on an Image, not a Label.
var image = new Image();
image.SetAppTheme<ImageSource>(Image.SourceProperty,
    ImageSource.FromFile("logo_light.png"),
    ImageSource.FromFile("logo_dark.png"));
```

Prefer defining the values as resource keys and referencing them, rather than
scattering hardcoded colours across the codebase.

## ResourceDictionary Theming (Custom Themes)

Use separate `ResourceDictionary` files with matching keys to define themes, then swap them at runtime.

### Step 1 — Define Theme Dictionaries

When using compiled XAML with `x:Class` (as shown below), each dictionary needs a code-behind that calls `InitializeComponent()`. Dictionaries loaded via `Source` without `x:Class` do not need code-behind.

**LightTheme.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                    x:Class="MyApp.Themes.LightTheme">
    <Color x:Key="PageBackgroundColor">White</Color>
    <Color x:Key="PrimaryTextColor">#333333</Color>
    <Color x:Key="AccentColor">#2196F3</Color>
</ResourceDictionary>
```

**LightTheme.xaml.cs**

```csharp
namespace MyApp.Themes;

public partial class LightTheme : ResourceDictionary
{
    public LightTheme() => InitializeComponent();
}
```

Create a matching **DarkTheme.xaml / DarkTheme.xaml.cs** with the same keys and different values.

### Step 2 — Consume with DynamicResource

Use `DynamicResource` so values update when the dictionary is swapped at runtime:

```xml
<ContentPage BackgroundColor="{DynamicResource PageBackgroundColor}">
    <Label Text="Hello"
           TextColor="{DynamicResource PrimaryTextColor}" />
    <Button Text="Action"
            BackgroundColor="{DynamicResource AccentColor}" />
</ContentPage>
```

### Step 3 — Switch Themes at Runtime

> 🚨 **Never call `MergedDictionaries.Clear()` to swap a theme.** The default MAUI
> template merges `Resources/Styles/Colors.xaml` and `Styles.xaml` into
> `Application.Resources`. `Clear()` removes **those too**, so every implicit style,
> brush and colour in the app silently disappears — buttons, entries and labels all
> revert to unstyled defaults. Verified: after `Clear()`, `MergedDictionaries` drops
> from 2 to 1 and the template's `Primary` colour no longer resolves.

Remove only the theme you added, and leave everything else alone:

```csharp
static ResourceDictionary? _currentTheme;

void ApplyTheme(ResourceDictionary theme)
{
    var merged = Application.Current!.Resources.MergedDictionaries;

    // ✅ Remove ONLY the previous theme — Colors.xaml / Styles.xaml survive
    if (_currentTheme is not null)
        merged.Remove(_currentTheme);

    merged.Add(theme);
    _currentTheme = theme;
}

// Usage
ApplyTheme(new DarkTheme());
```

```csharp
// ❌ Destroys the app's Colors.xaml and Styles.xaml along with the old theme
var merged = Application.Current!.Resources.MergedDictionaries;
merged.Clear();
merged.Add(theme);
```

## System Theme Detection

### Read the Current Theme

```csharp
AppTheme currentTheme = Application.Current!.RequestedTheme;
// Returns AppTheme.Light, AppTheme.Dark, or AppTheme.Unspecified
```

### Override the System Theme

```csharp
// Force dark mode regardless of OS setting
Application.Current!.UserAppTheme = AppTheme.Dark;

// Reset to follow system theme
Application.Current!.UserAppTheme = AppTheme.Unspecified;
```

### React to Theme Changes

```csharp
Application.Current!.RequestedThemeChanged += (s, e) =>
{
    AppTheme newTheme = e.RequestedTheme;
    // Update UI or switch ResourceDictionaries
};
```

## Combining Both Approaches

Use `AppThemeBinding` with `DynamicResource` values for maximum flexibility — the
nested `DynamicResource` stays live, so swapping the dictionary updates the value
*and* the OS light/dark switch is still honoured:

```xml
<Label TextColor="{AppThemeBinding
    Light={DynamicResource LightPrimary},
    Dark={DynamicResource DarkPrimary}}" />
```

Or react to system changes and swap full dictionaries:

```csharp
Application.Current!.RequestedThemeChanged += (s, e) =>
{
    ApplyTheme(e.RequestedTheme == AppTheme.Dark
        ? new DarkTheme()
        : new LightTheme());
};
```

## Saving and Restoring User Preference

Store the user's choice with `Preferences` and apply it on startup:

```csharp
// Save choice
Preferences.Set("AppTheme", "Dark");

// Restore on startup (in App constructor or CreateWindow)
var saved = Preferences.Get("AppTheme", "System");
Application.Current!.UserAppTheme = saved switch
{
    "Light" => AppTheme.Light,
    "Dark"  => AppTheme.Dark,
    _       => AppTheme.Unspecified
};
```

## Common Pitfalls

### Android: ConfigChanges.UiMode is Required

`MainActivity` **must** include `ConfigChanges.UiMode` or theme-change events will not fire and the activity restarts instead of handling the change gracefully:

```csharp
[Activity(Theme = "@style/Maui.SplashTheme",
          MainLauncher = true,
          ConfigurationChanges = ConfigChanges.ScreenSize
                               | ConfigChanges.Orientation
                               | ConfigChanges.UiMode  // ← Required for theme detection
                               | ConfigChanges.ScreenLayout
                               | ConfigChanges.SmallestScreenSize
                               | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity { }
```

Without `UiMode`, toggling dark mode in Android settings causes a full activity restart — losing navigation state and appearing as a crash. With it declared, the app stays alive and `RequestedThemeChanged` fires, so pair this fix with a handler that re-applies the theme (see below).

### DynamicResource vs StaticResource

When using ResourceDictionary theme switching, you **must** use `DynamicResource`:

```xml
<!-- ✅ Updates when theme dictionary changes -->
<Label TextColor="{DynamicResource PrimaryTextColor}" />

<!-- ❌ Frozen at first load — won't update on theme switch -->
<Label TextColor="{StaticResource PrimaryTextColor}" />
```

`DynamicResource` only helps if something actually swaps the dictionary. When you
diagnose this, always show the swap and the system-theme hook alongside the fix —
otherwise the user has a corrected binding that still never updates:

```csharp
static ResourceDictionary? _currentTheme;

void ApplyTheme(bool useDark)
{
    var merged = Application.Current!.Resources.MergedDictionaries;

    // Remove only the previous theme — never Clear(), which also wipes
    // the template's Colors.xaml / Styles.xaml
    if (_currentTheme is not null)
        merged.Remove(_currentTheme);

    _currentTheme = useDark ? new DarkTheme() : new LightTheme();
    merged.Add(_currentTheme);
}

// React to the OS switching light/dark
Application.Current!.RequestedThemeChanged += (s, e) =>
    ApplyTheme(e.RequestedTheme == AppTheme.Dark);
```

### Hardcoded Colors Break Theming

Avoid inline color values on elements that should respect the theme:

```xml
<!-- ❌ Will not change with theme -->
<Label TextColor="#333333" />

<!-- ✅ Theme-aware -->
<Label TextColor="{DynamicResource PrimaryTextColor}" />
```

### CSS Themes Cannot Be Swapped at Runtime

.NET MAUI supports CSS styling, but CSS-based themes **cannot be swapped dynamically**. Use ResourceDictionary theming for runtime switching.

### Theme Keys Must Match Across Dictionaries

Every `x:Key` used in one theme dictionary must exist in all other theme dictionaries. A missing key causes a silent fallback to the default value, leading to inconsistent appearance.

## Platform Support

| Platform       | Minimum Version |
|----------------|-----------------|
| iOS            | 13+             |
| Android        | 10+ (API 29)    |
| macOS Catalyst | 10.15+          |
| Windows        | 10+             |

## Quick Reference

- **OS light/dark** → `AppThemeBinding` markup extension
- **Theme colors in C#** → `SetAppThemeColor()`, `SetAppTheme<T>()`
- **Read OS theme** → `Application.Current.RequestedTheme`
- **Force theme** → `Application.Current.UserAppTheme = AppTheme.Dark`
- **Theme changes** → `RequestedThemeChanged` event
- **Custom switching** → `Remove` the old theme from `MergedDictionaries`, then `Add` the new one — **never `Clear()`**
- **Runtime bindings** → **`DynamicResource`** (not `StaticResource`)
- **Persist choice** → `Preferences.Set` / `Preferences.Get`
