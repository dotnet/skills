---
name: maui-current-apis
description: >-
  Prevents deprecated and removed API usage in .NET MAUI projects. Covers
  controls, navigation, animation, Device class, accessibility, and NuGet
  package replacements across .NET 8/9/10, plus MauiReactor and Blazor Hybrid.
  USE FOR: generating MAUI code, reviewing MAUI code for API currency,
  migrating between .NET MAUI versions (8→9→10), fixing MAUI deprecation
  warnings. Triggers on: Device.RuntimePlatform, MessagingCenter, ListView
  deprecated, DisplayAlert sync, FadeTo, Color.FromHex, Xamarin.Forms namespace,
  Compatibility namespace, DependencyService, SYSLIB warnings.
  DO NOT USE FOR: layout/architecture patterns (use maui-coding-guardrails),
  environment setup (use dotnet-maui-doctor).
---

# .NET MAUI Current APIs

Prevents generating code that uses deprecated, obsolete, or removed APIs.

**Before generating MAUI code:** Check the project's `.csproj` for
`<TargetFramework>` and key `<PackageReference>` versions — API availability
depends on .NET version (8/9/10) and library versions (CommunityToolkit,
MauiReactor). If a project targets `net8.0`, don't suggest .NET 10 APIs like
`DisplayAlertAsync` or `FadeToAsync`.

## When to Use

- Before generating any .NET MAUI C# or XAML code
- When reviewing existing MAUI code for API currency
- When migrating between .NET MAUI versions
- When fixing deprecation warnings or `SYSLIB` diagnostics

## When Not to Use

- Non-MAUI .NET projects
- Layout and architecture patterns — use `maui-coding-guardrails`
- Environment setup — use `dotnet-maui-doctor`

## Key Rules

1. **Read the `.csproj` first.** API availability varies by version — don't assume.
2. **When in doubt, use the newer API.** If two ways exist, the newer one is correct.
3. **Xamarin namespaces don't exist in MAUI.** `Xamarin.Forms` and `Xamarin.Essentials` are separate API surfaces — code using them won't compile.
4. **Avoid `Compatibility.*` types in new projects.** They map to Xamarin.Forms layout logic with subtle measurement differences that cause unexpected behavior. They exist only to ease migration.
5. **The `Device` class is fully deprecated.** Every `Device.*` API has a modern replacement (see table below). The class was split because it was a grab-bag of unrelated functionality.
6. **Use `*Async` method names in .NET 10+.** Animation and dialog methods were renamed for consistency with async patterns.
7. **Verify third-party package versions** — CommunityToolkit and MauiReactor break between major versions.

---

## Deprecated API Tables — .NET MAUI 10

### Controls

| ❌ Deprecated / Removed | ✅ Use Instead | Why |
|--------------------------|----------------|-----|
| `ListView` | `CollectionView` | Deprecated in .NET 10 along with all cell types; lacks virtualization improvements |
| `TableView` | `CollectionView` or custom layout | Deprecated in .NET 10 |
| `Frame` | `Border` | Legacy control; `Border` supports `StrokeShape` for rounded corners |
| `Compatibility.RelativeLayout` | `Grid` | Migration-only; removed from templates in .NET 10 |
| `Compatibility.StackLayout` | `VerticalStackLayout` / `HorizontalStackLayout` | Uses Xamarin layout logic with subtle measurement differences |

### Gestures & Input

| ❌ Deprecated / Removed | ✅ Use Instead | Why |
|--------------------------|----------------|-----|
| `ClickGestureRecognizer` | `TapGestureRecognizer` | Removed in .NET 10 |
| `Accelerator` | `KeyboardAccelerator` | Removed in .NET 10 |

### Page & Navigation

| ❌ Deprecated / Removed | ✅ Use Instead | Why |
|--------------------------|----------------|-----|
| `Page.IsBusy` | `ActivityIndicator` | Obsolete in .NET 10 |
| `DisplayAlert()` | `DisplayAlertAsync()` | Sync-named versions deprecated for async consistency |
| `DisplayActionSheet()` | `DisplayActionSheetAsync()` | Same |
| `MessagingCenter` | `WeakReferenceMessenger` (CommunityToolkit.Mvvm) | Made internal in .NET 10 — was leaking subscriptions without weak references |

### Animation

All animation extension methods renamed to `*Async` in .NET 10:

| ❌ Deprecated | ✅ Use Instead |
|---------------|----------------|
| `FadeTo()` | `FadeToAsync()` |
| `RotateTo()`, `ScaleTo()`, `TranslateTo()` | `RotateToAsync()`, `ScaleToAsync()`, `TranslateToAsync()` |
| `RelRotateTo()`, `RelScaleTo()`, `LayoutTo()` | `RelRotateToAsync()`, `RelScaleToAsync()`, `LayoutToAsync()` |

### Device & Platform APIs

The `Device` class was a grab-bag of unrelated functionality — it was split into focused services:

| ❌ Deprecated | ✅ Use Instead | Why |
|---------------|----------------|-----|
| `Device.RuntimePlatform` | `DeviceInfo.Platform` | `Device` class fully deprecated |
| `Device.BeginInvokeOnMainThread()` | `MainThread.BeginInvokeOnMainThread()` | Use `Microsoft.Maui.ApplicationModel.MainThread` |
| `Device.InvokeOnMainThreadAsync()` | `MainThread.InvokeOnMainThreadAsync()` | Same |
| `Device.OpenUri()` | `Launcher.OpenAsync()` | Use `Microsoft.Maui.ApplicationModel.Launcher` |
| `Device.StartTimer()` | `Dispatcher.StartTimer()` or `PeriodicTimer` | |
| `DependencyService` | Constructor injection via `builder.Services` | Service locator anti-pattern; DI is testable and explicit |

### XAML & Markup

| ❌ Deprecated | ✅ Use Instead |
|---------------|----------------|
| `Color.FromHex()` | `Color.FromArgb()` |

### Safe Area & Layout

| ❌ Deprecated | ✅ Use Instead | Why |
|---------------|----------------|-----|
| `Page.UseSafeArea` (iOS) | `SafeAreaEdges` property | New unified API in .NET 10 |
| `Layout.IgnoreSafeArea` | `SafeAreaEdges` property | Single API replaces both old approaches |

### Accessibility

| ❌ Deprecated | ✅ Use Instead | Why |
|---------------|----------------|-----|
| `AutomationProperties.Name` | `SemanticProperties.Description` | `SemanticProperties` is the MAUI-native approach |
| `AutomationProperties.HelpText` | `SemanticProperties.Hint` | Same |

### NuGet Packages

| ❌ Deprecated Package | ✅ Use Instead | Why |
|------------------------|----------------|-----|
| `Xamarin.Forms` | `Microsoft.Maui.Controls` | Completely different API surface — won't compile |
| `Xamarin.Essentials` | Built-in MAUI APIs (`Microsoft.Maui.Devices`, etc.) | Essentials merged into MAUI SDK |
| `Xamarin.CommunityToolkit` | `CommunityToolkit.Maui` | Different namespace and API surface |
| `Microsoft.Toolkit.Mvvm` | `CommunityToolkit.Mvvm` | Package was renamed |

---

## MauiReactor-Specific Guidance

MauiReactor v3+ (for .NET MAUI 9/10) differs significantly from v2:

- **Hot reload**: v3+ uses a `<RuntimeHostConfigurationOption>` feature switch in `.csproj`. The v2 `EnableMauiReactorHotReload()` builder call doesn't exist in v3.
- **API wrappers**: MauiReactor auto-generates C# wrappers around MAUI controls. When a MAUI control is deprecated, the MauiReactor wrapper is too.
- **State management**: Use `State<T>` and `Props<T>` — `RxComponent` patterns from v2 are outdated.
- **Navigation**: Use MauiReactor's built-in navigation — don't mix in Shell `GoToAsync` unless deliberately integrating Shell.

## Blazor Hybrid-Specific Guidance

- Use **`BlazorWebView`** for Razor components — not the general `WebView`.
- **JS interop**: Use `IJSRuntime.InvokeAsync<T>()` — the synchronous `IJSInProcessRuntime` patterns cause deadlocks on mobile.
- **Safe areas**: Use CSS `env(safe-area-inset-*)` in Blazor Hybrid — don't combine XAML `SafeAreaEdges` and CSS safe area padding on the same element (causes double-padding).

## Version Detection

| TFM Pattern | .NET Version | MAUI Version |
|-------------|-------------|--------------|
| `net10.0-*` | .NET 10 (latest) | MAUI 10 |
| `net9.0-*` | .NET 9 | MAUI 9 |
| `net8.0-*` | .NET 8 (LTS) | MAUI 8 |

| CommunityToolkit.Maui | Compatible .NET |
|------------------------|----------------|
| v11+ | .NET 10 |
| v9-10 | .NET 9 |
| v5-7 | .NET 8 |
