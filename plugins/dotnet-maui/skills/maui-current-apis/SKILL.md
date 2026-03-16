---
name: maui-current-apis
description: >-
  Always-on guardrail for .NET MAUI API currency. Prevents use of deprecated,
  obsolete, or removed APIs across XAML/C#, Blazor Hybrid, and MauiReactor.
  Includes a reasoning framework for detecting project target framework and
  library versions, plus a curated table of deprecated API traps in .NET MAUI 10.
  USE FOR: deprecated API detection, obsolete API replacement, API migration,
  MAUI breaking changes, reviewing or generating MAUI code.
  DO NOT USE FOR: learning new MAUI features (use feature-specific skills),
  performance optimization, testing guidance, or general coding guardrails
  (use maui-coding-guardrails).
---

# .NET MAUI Current APIs

This skill prevents generating code that uses deprecated, obsolete, or removed APIs.
**Read this before writing any .NET MAUI, Blazor Hybrid, or MauiReactor code.**

## When to Use

- Before generating any .NET MAUI C# or XAML code
- When reviewing existing MAUI code for API currency
- When migrating between .NET MAUI versions (8 → 9 → 10)
- When using CommunityToolkit, MauiReactor, or Blazor Hybrid APIs

## When Not to Use

- Non-MAUI .NET projects
- Learning new MAUI features — use feature-specific skills
- General coding patterns — use `maui-coding-guardrails`

## Reasoning Framework

Follow these steps **before** generating any MAUI-related code:

### Step 1 — Detect the Target Framework

Read the project's `.csproj` file and find `<TargetFramework>` or `<TargetFrameworks>`:

```xml
<!-- Multi-target (typical MAUI project) -->
<TargetFrameworks>net10.0-android;net10.0-ios;net10.0-maccatalyst;net10.0-windows10.0.19041.0</TargetFrameworks>
```

The version number (`net10.0`, `net9.0`, `net8.0`) determines which APIs are available.
**Always target the version in the project file — never assume a version.**

### Step 2 — Detect Library Versions

Scan `<PackageReference>` entries for key packages:

| Package | What it tells you |
|---------|-------------------|
| `Microsoft.Maui.Controls` | MAUI version (if explicit) |
| `CommunityToolkit.Maui` | Toolkit version — APIs change between major versions |
| `CommunityToolkit.Mvvm` | MVVM Toolkit version |
| `Reactor.Maui` | MauiReactor version — v3+ has different APIs than v2 |
| `Microsoft.AspNetCore.Components.WebView.Maui` | Blazor Hybrid version |

If no explicit MAUI package version is listed, the MAUI SDK version matches the TFM .NET version.

### Step 3 — Verify API Currency

Before using any API, check:

1. **Does this API exist in the detected version?** If unsure, prefer the newer pattern.
2. **Is this a Xamarin.Forms API?** MAUI is a different API surface — never assume compatibility.
3. **Is this in the deprecated table below?** If so, use the replacement.
4. **Am I using the Compatibility namespace?** `Microsoft.Maui.Controls.Compatibility.*` types are migration aids, not recommended for new code.

### Step 4 — Apply Decision Rules

- **Always use the newest form** of an API when both old and new exist.
- **Never generate `using Xamarin.Forms`** or `using Xamarin.Essentials` — these are not MAUI.
- **Never use the `Compatibility` namespace** in new projects.
- **Prefer `*Async` method names** when both sync and async versions exist.
- **Check the project's NuGet versions** before using CommunityToolkit or third-party APIs.

---

## Deprecated API Table — .NET MAUI 10

### Controls

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `ListView` | `CollectionView` | `ListView`, `EntryCell`, `ImageCell`, `SwitchCell`, `TextCell`, `ViewCell`, and `Cell` are all deprecated in .NET 10 |
| `TableView` | `CollectionView` or custom layout | Deprecated in .NET 10 |
| `Frame` | `Border` | Legacy control; `Border` supports `StrokeShape` |
| `Compatibility.RelativeLayout` | `Grid` | Migration-only; removed from templates in .NET 10 |
| `Compatibility.StackLayout` | `VerticalStackLayout` / `HorizontalStackLayout` | Compatibility wrapper uses Xamarin layout logic |

### Gestures & Input

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `ClickGestureRecognizer` | `TapGestureRecognizer` | Removed in .NET 10 |
| `Accelerator` | `KeyboardAccelerator` | Removed in .NET 10 |

### Page & Navigation

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `Page.IsBusy` | `ActivityIndicator` | Obsolete in .NET 10 |
| `DisplayAlert()` | `DisplayAlertAsync()` | Sync-named versions deprecated |
| `DisplayActionSheet()` | `DisplayActionSheetAsync()` | Same |
| `MessagingCenter` | `WeakReferenceMessenger` (CommunityToolkit.Mvvm) | Made internal in .NET 10 |

### Animation

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `FadeTo()` | `FadeToAsync()` | All animation methods renamed to `*Async` in .NET 10 |
| `RotateTo()`, `ScaleTo()`, `TranslateTo()`, etc. | `RotateToAsync()`, `ScaleToAsync()`, `TranslateToAsync()`, etc. | Same pattern for all animation extensions |

### Device & Platform APIs

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `Device.RuntimePlatform` | `DeviceInfo.Platform` | Entire `Device` class is deprecated |
| `Device.BeginInvokeOnMainThread()` | `MainThread.BeginInvokeOnMainThread()` | Use `Microsoft.Maui.ApplicationModel.MainThread` |
| `Device.OpenUri()` | `Launcher.OpenAsync()` | Use `Microsoft.Maui.ApplicationModel.Launcher` |
| `Device.StartTimer()` | `Dispatcher.StartTimer()` or `PeriodicTimer` | |
| `DependencyService` | Constructor injection via `IServiceProvider` | Register in `MauiProgram.cs` with `builder.Services` |

### XAML & Markup

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `Color.FromHex()` | `Color.FromArgb()` | `FromHex` is obsolete |

### Safe Area & Layout

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `Page.UseSafeArea` (iOS platform-specific) | `SafeAreaEdges` property | New in .NET 10 |
| `Layout.IgnoreSafeArea` | `SafeAreaEdges` property | Unified safe area API |

### Accessibility

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `AutomationProperties.Name` | `SemanticProperties.Description` | `SemanticProperties` is the MAUI-native approach |
| `AutomationProperties.HelpText` | `SemanticProperties.Hint` | Same |

### NuGet Packages

| ❌ Deprecated Package | ✅ Use Instead | Notes |
|------------------------|----------------|-------|
| `Xamarin.Forms` | `Microsoft.Maui.Controls` | Completely different API surface |
| `Xamarin.Essentials` | Built-in MAUI APIs (`Microsoft.Maui.Devices`, etc.) | Essentials is merged into MAUI |
| `Xamarin.CommunityToolkit` | `CommunityToolkit.Maui` | Different namespace and API surface |
| `Microsoft.Toolkit.Mvvm` | `CommunityToolkit.Mvvm` | Package was renamed |

---

## MauiReactor-Specific Guidance

MauiReactor v3+ (for .NET MAUI 9/10):

- **Hot reload**: v3+ uses a feature switch in `.csproj` via `<RuntimeHostConfigurationOption>`. Do NOT use the v2 `EnableMauiReactorHotReload()` call.
- **API wrappers**: When a MAUI control is deprecated (e.g., `ListView`), the MauiReactor wrapper is also deprecated. Use the wrapper for the replacement control.
- **State management**: Use `State<T>` and `Props<T>` — not older `RxComponent` patterns.
- **Navigation**: Use MauiReactor's built-in navigation — do NOT mix in Shell `GoToAsync` unless deliberately integrating Shell.

## Blazor Hybrid-Specific Guidance

- Use **`BlazorWebView`** — not the legacy `WebView` — for hosting Razor components.
- Use **.NET 10 Razor syntax**: `@rendermode` directives, `[SupplyParameterFromQuery]`.
- **JS interop**: Use `IJSRuntime.InvokeAsync<T>()` — not the obsolete synchronous `IJSInProcessRuntime` patterns.
- **Safe areas**: Use CSS `env(safe-area-inset-*)` — do NOT combine XAML `SafeAreaEdges` and CSS safe area padding on the same element (causes double-padding).

---

## Version Detection Cheat Sheet

| TFM Pattern | .NET Version |
|-------------|-------------|
| `net10.0-*` | .NET 10 (latest) |
| `net9.0-*` | .NET 9 |
| `net8.0-*` | .NET 8 (LTS) |

### CommunityToolkit Version Mapping

| CommunityToolkit.Maui | Compatible .NET |
|------------------------|----------------|
| v11+ | .NET 10 |
| v9-10 | .NET 9 |
| v5-7 | .NET 8 |

## Quick Rules

1. **Read the `.csproj` first.** Never generate code without knowing the target framework.
2. **When in doubt, use the newer API.** If two ways exist, the newer way is correct.
3. **Never use `Xamarin.*` namespaces.** They do not exist in MAUI.
4. **Never use `Compatibility.*` types in new projects.** They are migration aids only.
5. **Check this table before using any `Device.*` API.** The `Device` class is fully deprecated.
6. **Use `*Async` method names** for animations, alerts, and action sheets in .NET 10+.
7. **Verify third-party package versions** before using their APIs.
