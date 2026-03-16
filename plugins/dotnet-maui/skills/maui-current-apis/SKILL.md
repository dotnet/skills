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
MauiReactor). If a project targets `net8.0`, don't suggest `.NET 10` APIs like
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

## Top Deprecated APIs — Quick Reference

The most common mistakes. Full tables in `references/deprecated-apis-net10.md`.

| ❌ Don't Use | ✅ Use Instead | Why |
|--------------|----------------|-----|
| `Device.RuntimePlatform` | `DeviceInfo.Platform` | `Device` class is deprecated — split into focused services |
| `Device.BeginInvokeOnMainThread()` | `MainThread.BeginInvokeOnMainThread()` | Same reason — use `Microsoft.Maui.ApplicationModel.MainThread` |
| `DependencyService.Get<T>()` | Constructor injection via `builder.Services` | DependencyService is a service locator anti-pattern; DI is testable and explicit |
| `MessagingCenter` | `WeakReferenceMessenger` (CommunityToolkit.Mvvm) | Made internal in .NET 10 — was leaking subscriptions without weak references |
| `DisplayAlert()` / `FadeTo()` | `DisplayAlertAsync()` / `FadeToAsync()` | Sync-named methods deprecated in .NET 10 to match async conventions |
| `ListView` / `TableView` | `CollectionView` / custom layout | Deprecated in .NET 10 — see `maui-coding-guardrails` for details |
| `Frame` | `Border` | Legacy control; `Border` supports `StrokeShape` for rounded corners |
| `Color.FromHex()` | `Color.FromArgb()` | `FromHex` is obsolete |
| `AutomationProperties.Name` | `SemanticProperties.Description` | `SemanticProperties` is the MAUI-native accessibility approach |
| `Xamarin.Forms` / `Xamarin.Essentials` | `Microsoft.Maui.Controls` / built-in APIs | Different API surface — won't compile in MAUI projects |

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

## Full API Tables

For the complete deprecated API tables organized by category (controls, gestures,
navigation, animation, Device APIs, XAML, safe area, accessibility, NuGet packages),
see `references/deprecated-apis-net10.md`.
