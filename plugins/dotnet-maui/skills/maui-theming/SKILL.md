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

## Choosing an Approach

| Approach | Best for | Limitation |
|----------|----------|------------|
| **AppThemeBinding** | Automatic light/dark with OS — minimal code | Only two themes (light + dark) |
| **ResourceDictionary swap** | Custom branded themes, more than two themes, user preference | More setup; must use `DynamicResource` everywhere |
| **Both combined** | OS-driven response plus custom theme colors | Most flexible but most complex |

## AppThemeBinding (OS Light/Dark)

`AppThemeBinding` selects a value based on the current system theme. It supports `Light`, `Dark`, and an optional `Default` fallback.

### XAML

```xml
<Label Text="Themed text"
       TextColor="{AppThemeBinding Light=Green, Dark=Red}"
       BackgroundColor="{AppThemeBinding Light=White, Dark=Black}" />

<!-- With resource references -->
<Label TextColor="{AppThemeBinding Light={StaticResource LightPrimary},
                                   Dark={StaticResource DarkPrimary}}" />
```

### C# Extension Methods

```csharp
var label = new Label();

// Color-specific helper
label.SetAppThemeColor(Label.TextColorProperty, Colors.Green, Colors.Red);

// Generic helper for any bindable property type
label.SetAppTheme<Color>(Label.TextColorProperty, Colors.Green, Colors.Red);
```

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

```csharp
void ApplyTheme(ResourceDictionary theme)
{
    // Assumes theme dictionaries are the only merged dictionaries.
    // If your App.xaml merges non-theme dictionaries (e.g., converters),
    // move them to Application.Resources directly instead.
    var mergedDictionaries = Application.Current!.Resources.MergedDictionaries;
    mergedDictionaries.Clear();
    mergedDictionaries.Add(theme);
}

// Usage
ApplyTheme(new DarkTheme());
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

Use `AppThemeBinding` with `DynamicResource` values for maximum flexibility:

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

Without `UiMode`, toggling dark mode in Android settings causes a full activity restart — losing navigation state and appearing as a crash.

### DynamicResource vs StaticResource

When using ResourceDictionary theme switching, you **must** use `DynamicResource`:

```xml
<!-- ✅ Updates when theme dictionary changes -->
<Label TextColor="{DynamicResource PrimaryTextColor}" />

<!-- ❌ Frozen at first load — won't update on theme switch -->
<Label TextColor="{StaticResource PrimaryTextColor}" />
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
- **Custom switching** → Swap `ResourceDictionary` in `MergedDictionaries`
- **Runtime bindings** → **`DynamicResource`** (not `StaticResource`)
- **Persist choice** → `Preferences.Set` / `Preferences.Get`
