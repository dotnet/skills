---
name: maui-current-apis
description: >-
  Tracks deprecated/removed APIs in .NET MAUI 8/9/10, MauiReactor, Blazor
  Hybrid. USE FOR: generating/reviewing MAUI code, fixing deprecation warnings.
  NOT FOR: layout patterns (maui-coding-guardrails) or setup (dotnet-maui-doctor).
---

# .NET MAUI Current APIs

Prevents generating code with deprecated or removed APIs.

**Before generating:** Read `.csproj` TFM and package versions. API availability varies by .NET version; don't suggest .NET 10 APIs for `net8.0`.

## Key Rules

1. **Read `.csproj` first**; don't assume target version.
2. **Prefer newer APIs** available for the detected version.
3. **`Xamarin.*` don't exist in MAUI** — won't compile.
4. **Avoid `Compatibility.*`** — Xamarin.Forms layout logic, migration aid only.
5. **`Device` class deprecated** — split into services (see table).
6. **Use `*Async` in .NET 10+** — animation/dialog methods renamed.
7. **Check package versions** — CommunityToolkit/MauiReactor break between majors.

---

## Deprecated APIs — .NET MAUI 10

### Controls

| ❌ Deprecated | ✅ Replacement | Why |
|---------------|----------------|-----|
| `ListView` | `CollectionView` | Deprecated in .NET 10 with all cell types |
| `TableView` | `CollectionView` or custom layout | Deprecated in .NET 10 |
| `Frame` | `Border` | Legacy; Border supports StrokeShape |
| `Compatibility.RelativeLayout` | `Grid` | Migration-only |
| `Compatibility.StackLayout` | `VerticalStackLayout`/`HorizontalStackLayout` | Xamarin layout logic |

### Gestures

| ❌ Deprecated | ✅ Replacement |
|---------------|----------------|
| `ClickGestureRecognizer` | `TapGestureRecognizer` |
| `Accelerator` | `KeyboardAccelerator` |

### Navigation

| ❌ Deprecated | ✅ Replacement | Why |
|---------------|----------------|-----|
| `Page.IsBusy` | `ActivityIndicator` | Obsolete .NET 10 |
| `DisplayAlert()` | `DisplayAlertAsync()` | Async rename |
| `DisplayActionSheet()` | `DisplayActionSheetAsync()` | Same |
| `MessagingCenter` | `WeakReferenceMessenger` (CommunityToolkit.Mvvm) | Internal .NET 10; leaked subscriptions |

### Animation (*Async renames in .NET 10)

| ❌ Old | ✅ New |
|--------|--------|
| `FadeTo()`, `RotateTo()`, `ScaleTo()`, `TranslateTo()` | `FadeToAsync()`, `RotateToAsync()`, `ScaleToAsync()`, `TranslateToAsync()` |
| `RelRotateTo()`, `RelScaleTo()`, `LayoutTo()` | `RelRotateToAsync()`, `RelScaleToAsync()`, `LayoutToAsync()` |

### Device APIs (class split into focused services)

| ❌ Deprecated | ✅ Replacement |
|---------------|----------------|
| `Device.RuntimePlatform` | `DeviceInfo.Platform` |
| `Device.BeginInvokeOnMainThread()` | `MainThread.BeginInvokeOnMainThread()` |
| `Device.OpenUri()` | `Launcher.OpenAsync()` |
| `Device.StartTimer()` | `Dispatcher.StartTimer()` or `PeriodicTimer` |
| `DependencyService` | Constructor injection via `builder.Services` |

### Other

| ❌ Deprecated | ✅ Replacement |
|---------------|----------------|
| `Color.FromHex()` | `Color.FromArgb()` |
| `Page.UseSafeArea` / `Layout.IgnoreSafeArea` | `SafeAreaEdges` property (.NET 10) |
| `AutomationProperties.Name`/`.HelpText` | `SemanticProperties.Description`/`.Hint` |

### NuGet Packages

| ❌ Old | ✅ New |
|--------|--------|
| `Xamarin.Forms` | `Microsoft.Maui.Controls` |
| `Xamarin.Essentials` | Built-in MAUI APIs |
| `Xamarin.CommunityToolkit` | `CommunityToolkit.Maui` |
| `Microsoft.Toolkit.Mvvm` | `CommunityToolkit.Mvvm` |

---

## MauiReactor v3+ (.NET MAUI 9/10)

- **Hot reload**: feature switch in `.csproj`, not v2 `EnableMauiReactorHotReload()`.
- **State**: `State<T>`/`Props<T>`, not `RxComponent`.
- **Navigation**: use MauiReactor nav; avoid mixing Shell `GoToAsync`.

## Blazor Hybrid

- Prefer `BlazorWebView`, not `WebView`.
- JS interop: `IJSRuntime.InvokeAsync<T>()` — sync deadlocks on mobile.
- Safe areas: CSS `env(safe-area-inset-*)`; don't combine with XAML `SafeAreaEdges`.

## Version Detection

| TFM | .NET | MAUI | CommunityToolkit.Maui |
|-----|------|------|-----------------------|
| `net10.0-*` | 10 | 10 | v11+ |
| `net9.0-*` | 9 | 9 | v9-10 |
| `net8.0-*` | 8 (LTS) | 8 | v5-7 |
