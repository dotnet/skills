# Deprecated APIs — .NET MAUI 10

Complete table of deprecated, obsolete, and removed APIs organized by category.

## Controls

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `ListView` | `CollectionView` | `ListView`, `EntryCell`, `ImageCell`, `SwitchCell`, `TextCell`, `ViewCell`, and `Cell` are all deprecated in .NET 10 |
| `TableView` | `CollectionView` or custom layout | Deprecated in .NET 10 |
| `Frame` | `Border` | Legacy Xamarin.Forms control; `Border` supports `StrokeShape` for rounded corners |
| `Compatibility.RelativeLayout` | `Grid` | Migration-only; removed from templates in .NET 10 |
| `Compatibility.StackLayout` | `VerticalStackLayout` / `HorizontalStackLayout` | Compatibility wrapper uses Xamarin.Forms layout logic with subtle measurement differences |

## Gestures & Input

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `ClickGestureRecognizer` | `TapGestureRecognizer` | Removed in .NET 10 |
| `Accelerator` | `KeyboardAccelerator` | Removed from `Microsoft.Maui.Controls` in .NET 10 |

## Page & Navigation

| ❌ Deprecated / Removed | ✅ Use Instead | Notes |
|--------------------------|----------------|-------|
| `Page.IsBusy` | `ActivityIndicator` | Obsolete in .NET 10; use an explicit activity indicator |
| `DisplayAlert()` | `DisplayAlertAsync()` | Sync-named versions deprecated for async consistency |
| `DisplayActionSheet()` | `DisplayActionSheetAsync()` | Same pattern |
| `MessagingCenter` | `WeakReferenceMessenger` (CommunityToolkit.Mvvm) | Made internal in .NET 10; was leaking subscriptions without weak references |

## Animation

All animation extension methods were renamed to `*Async` in .NET 10 for consistency with the Task-based async pattern:

| ❌ Deprecated | ✅ Use Instead |
|---------------|----------------|
| `FadeTo()` | `FadeToAsync()` |
| `RotateTo()` | `RotateToAsync()` |
| `ScaleTo()` | `ScaleToAsync()` |
| `TranslateTo()` | `TranslateToAsync()` |
| `RelRotateTo()` | `RelRotateToAsync()` |
| `RelScaleTo()` | `RelScaleToAsync()` |
| `LayoutTo()` | `LayoutToAsync()` |

## Device & Platform APIs

The `Device` class was deprecated because it was a grab-bag of unrelated functionality. Each method now has a dedicated, focused replacement:

| ❌ Deprecated | ✅ Use Instead | Replacement Namespace |
|---------------|----------------|-----------------------|
| `Device.RuntimePlatform` | `DeviceInfo.Platform` | `Microsoft.Maui.Devices` |
| `Device.BeginInvokeOnMainThread()` | `MainThread.BeginInvokeOnMainThread()` | `Microsoft.Maui.ApplicationModel` |
| `Device.InvokeOnMainThreadAsync()` | `MainThread.InvokeOnMainThreadAsync()` | `Microsoft.Maui.ApplicationModel` |
| `Device.OpenUri()` | `Launcher.OpenAsync()` | `Microsoft.Maui.ApplicationModel` |
| `Device.StartTimer()` | `Dispatcher.StartTimer()` or `PeriodicTimer` | `Microsoft.Maui.Dispatching` |
| `DependencyService` | Constructor injection via `IServiceProvider` | Register in `MauiProgram.cs` with `builder.Services` |

## XAML & Markup

| ❌ Deprecated | ✅ Use Instead | Notes |
|---------------|----------------|-------|
| `FontImageExtension` (markup extension) | `FontImageSource` (type) | Use `<FontImageSource>` element syntax |
| `Color.FromHex()` | `Color.FromArgb()` | `FromHex` is obsolete |

## Safe Area & Layout

| ❌ Deprecated | ✅ Use Instead | Notes |
|---------------|----------------|-------|
| `Page.UseSafeArea` (iOS platform-specific) | `SafeAreaEdges` property | New unified API in .NET 10; `ContentPage` defaults to edge-to-edge |
| `Layout.IgnoreSafeArea` | `SafeAreaEdges` property | Single API replaces both old approaches |

## Accessibility

| ❌ Deprecated | ✅ Use Instead | Notes |
|---------------|----------------|-------|
| `AutomationProperties.Name` | `SemanticProperties.Description` | `SemanticProperties` is the MAUI-native accessibility API |
| `AutomationProperties.HelpText` | `SemanticProperties.Hint` | Same |
| iOS `SetAccessibilityHint` / `SetAccessibilityLabel` | `Microsoft.Maui.Platform.UpdateSemantics()` | Compatibility extensions deprecated in .NET 10 |

## NuGet Packages

| ❌ Deprecated Package | ✅ Use Instead | Notes |
|------------------------|----------------|-------|
| `Xamarin.Forms` | `Microsoft.Maui.Controls` | Completely different API surface — won't compile |
| `Xamarin.Essentials` | Built-in MAUI APIs (`Microsoft.Maui.Devices`, `Microsoft.Maui.ApplicationModel`, etc.) | Essentials was merged into the MAUI SDK |
| `Xamarin.CommunityToolkit` | `CommunityToolkit.Maui` | Different namespace and API surface |
| `Microsoft.Toolkit.Mvvm` | `CommunityToolkit.Mvvm` | Package was renamed |
