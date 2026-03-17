---
name: maui-current-apis
description: >-
  Prevents deprecated/removed API usage in .NET MAUI 8/9/10, MauiReactor, and
  Blazor Hybrid. USE FOR: generating or reviewing MAUI code, fixing deprecation
  warnings. DO NOT USE FOR: layout patterns (use maui-coding-guardrails),
  environment setup (use dotnet-maui-doctor).
---

# .NET MAUI Current APIs

Prevents generating code with deprecated, obsolete, or removed APIs.

**Before generating MAUI code:** Check `.csproj` for `<TargetFramework>` and
`<PackageReference>` versions. API availability varies by .NET version (8/9/10)
and library versions. Don't suggest .NET 10 APIs for `net8.0` projects.

## Key Rules

1. **Read `.csproj` first.** Don't assume the target version.
2. **Prefer newer APIs.** If two ways exist, the newer one is correct.
3. **`Xamarin.*` namespaces don't exist in MAUI** — won't compile.
4. **Avoid `Compatibility.*` in new projects** — maps to Xamarin.Forms layout logic with subtle measurement differences. Migration aid only.
5. **`Device` class is fully deprecated** — split into focused services (see table).
6. **Use `*Async` names in .NET 10+** — animation/dialog methods renamed.
7. **Check third-party package versions** — CommunityToolkit/MauiReactor break between majors.

---

## Deprecated APIs — .NET MAUI 10

### Controls

| ❌ Deprecated | ✅ Replacement | Why |
|---------------|----------------|-----|
| `ListView` | `CollectionView` | Deprecated with all cell types in .NET 10 |
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
| `Page.IsBusy` | `ActivityIndicator` | Obsolete in .NET 10 |
| `DisplayAlert()` | `DisplayAlertAsync()` | Async rename |
| `DisplayActionSheet()` | `DisplayActionSheetAsync()` | Same |
| `MessagingCenter` | `WeakReferenceMessenger` (CommunityToolkit.Mvvm) | Internal in .NET 10; leaked subscriptions |

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

- **Hot reload**: Feature switch in `.csproj`, not v2's `EnableMauiReactorHotReload()`.
- **State**: `State<T>`/`Props<T>`, not `RxComponent`.
- **Navigation**: Built-in MauiReactor nav, don't mix Shell `GoToAsync`.

## Blazor Hybrid

- Use `BlazorWebView`, not `WebView`.
- JS interop: `IJSRuntime.InvokeAsync<T>()` — sync patterns deadlock on mobile.
- Safe areas: CSS `env(safe-area-inset-*)` — don't combine with XAML SafeAreaEdges.

## Version Detection

| TFM | .NET | MAUI | CommunityToolkit.Maui |
|-----|------|------|-----------------------|
| `net10.0-*` | 10 | 10 | v11+ |
| `net9.0-*` | 9 | 9 | v9-10 |
| `net8.0-*` | 8 (LTS) | 8 | v5-7 |
