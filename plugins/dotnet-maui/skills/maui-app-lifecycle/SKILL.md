---
name: maui-app-lifecycle
description: >-
  .NET MAUI app lifecycle guidance — the four app states, cross-platform Window
  lifecycle events (Created, Activated, Deactivated, Stopped, Resumed, Destroying),
  platform-specific lifecycle mapping, backgrounding and resume behavior, and
  state-preservation patterns.
  USE FOR: "app lifecycle", "window lifecycle events", "save state on background",
  "resume app", "OnStopped", "OnResumed", "backgrounding", "deactivated event",
  "ConfigureLifecycleEvents", "platform lifecycle hooks".
  DO NOT USE FOR: navigation events (use maui-shell-navigation),
  dependency injection setup (use maui-dependency-injection),
  platform API invocation (use conditional compilation and partial classes).
---

# .NET MAUI App Lifecycle

Handle application state transitions correctly in .NET MAUI. This skill covers the cross-platform Window lifecycle events, their platform-native mappings, and patterns for preserving state across backgrounding and resume cycles.

## When to Use

- Saving or restoring state when the app backgrounds or resumes
- Subscribing to Window lifecycle events (Created, Activated, Deactivated, Stopped, Resumed, Destroying)
- Hooking into platform-native lifecycle callbacks via `ConfigureLifecycleEvents`
- Deciding where to place initialization, teardown, or refresh logic
- Understanding the difference between Deactivated and Stopped

## When Not to Use

- Page-level navigation events — use Shell navigation guidance instead
- Registering services at startup — use dependency injection guidance instead
- Calling platform-specific APIs outside lifecycle context — use platform invoke guidance instead

## Inputs

- The target lifecycle transition (e.g., "save draft when backgrounded", "refresh data on resume")
- Which platforms the developer targets (Android, iOS, Mac Catalyst, Windows)
- Whether the app uses multiple windows (iPad, Mac Catalyst, desktop Windows)

## App States

A .NET MAUI app moves through four states:

| State | Description |
|---|---|
| **Not Running** | Process does not exist |
| **Running** | Foreground, receiving input |
| **Deactivated** | Visible but lost focus (dialog, split-screen, notification shade) |
| **Stopped** | Fully backgrounded, UI not visible |

Typical flow: Not Running → Running → Deactivated → Stopped → Running (resumed) or Not Running (terminated).

## Window Lifecycle Events

`Microsoft.Maui.Controls.Window` exposes six cross-platform events:

| Event | Fires when |
|---|---|
| `Created` | Native window allocated |
| `Activated` | Window receives input focus |
| `Deactivated` | Window loses focus (may still be visible) |
| `Stopped` | Window is no longer visible |
| `Resumed` | Window returns to foreground after Stopped |
| `Destroying` | Native window is being torn down |

### Subscribing via CreateWindow

Override `CreateWindow` in your `App` class and attach event handlers:

```csharp
public partial class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Created += (s, e) => Debug.WriteLine("Created");
        window.Activated += (s, e) => Debug.WriteLine("Activated");
        window.Deactivated += (s, e) => Debug.WriteLine("Deactivated");
        window.Stopped += (s, e) => Debug.WriteLine("Stopped");
        window.Resumed += (s, e) => Debug.WriteLine("Resumed");
        window.Destroying += (s, e) => Debug.WriteLine("Destroying");

        return window;
    }
}
```

### Subscribing via a Custom Window Subclass

Create a `Window` subclass and override the virtual methods:

```csharp
public class AppWindow : Window
{
    public AppWindow(Page page) : base(page) { }

    protected override void OnActivated() { /* refresh UI */ }
    protected override void OnStopped() { /* save state */ }
    protected override void OnResumed() { /* restore state */ }
    protected override void OnDestroying() { /* cleanup */ }
}
```

Return it from `CreateWindow`:

```csharp
protected override Window CreateWindow(IActivationState? activationState)
    => new AppWindow(new AppShell());
```

## Workflow: Save and Restore State on Background

1. **Identify transient state** — draft text, scroll position, form inputs, timer values.
2. **Save in `OnStopped`** — use `Preferences` for small values or file serialization for larger state.
3. **Restore in `OnResumed`** — read back saved values and apply to your view model.
4. **Also save in `OnDestroying`** on Android — the back button can skip `Stopped` entirely.
5. **Keep handlers fast** — complete within 1–2 seconds to avoid ANR on Android or watchdog kills on iOS.

```csharp
protected override void OnStopped()
{
    base.OnStopped();
    Preferences.Set("draft_text", _viewModel.DraftText);
    Preferences.Set("scroll_y", _viewModel.ScrollY);
}

protected override void OnResumed()
{
    base.OnResumed();
    _viewModel.DraftText = Preferences.Get("draft_text", string.Empty);
    _viewModel.ScrollY = Preferences.Get("scroll_y", 0.0);
}

protected override void OnDestroying()
{
    base.OnDestroying();
    // Android back-button can skip Stopped
    Preferences.Set("draft_text", _viewModel.DraftText);
}
```

## Platform Lifecycle Mapping

### Android

| Window Event | Android Callback |
|---|---|
| Created | `OnCreate` |
| Activated | `OnResume` |
| Deactivated | `OnPause` |
| Stopped | `OnStop` |
| Resumed | `OnRestart` → `OnStart` → `OnResume` |
| Destroying | `OnDestroy` |

### iOS / Mac Catalyst

| Window Event | UIKit Callback |
|---|---|
| Created | `WillFinishLaunching` / `SceneWillConnect` |
| Activated | `DidBecomeActive` |
| Deactivated | `WillResignActive` |
| Stopped | `DidEnterBackground` |
| Resumed | `WillEnterForeground` |
| Destroying | `WillTerminate` |

### Windows (WinUI)

| Window Event | WinUI Callback |
|---|---|
| Created | `OnLaunched` |
| Activated | `Activated` (foreground) |
| Deactivated | `Activated` (background) |
| Stopped | `VisibilityChanged` (false) |
| Resumed | `VisibilityChanged` (true) |
| Destroying | `Closed` |

## Hooking Native Lifecycle Directly

Use `ConfigureLifecycleEvents` in `MauiProgram.cs` when you need platform-specific callbacks beyond what Window events provide:

```csharp
builder.ConfigureLifecycleEvents(events =>
{
#if ANDROID
    events.AddAndroid(android => android
        .OnCreate((activity, bundle) => Debug.WriteLine("Android OnCreate"))
        .OnResume(activity => Debug.WriteLine("Android OnResume"))
        .OnPause(activity => Debug.WriteLine("Android OnPause"))
        .OnStop(activity => Debug.WriteLine("Android OnStop"))
        .OnDestroy(activity => Debug.WriteLine("Android OnDestroy")));
#elif IOS || MACCATALYST
    events.AddiOS(ios => ios
        .DidBecomeActive(app => Debug.WriteLine("iOS DidBecomeActive"))
        .WillResignActive(app => Debug.WriteLine("iOS WillResignActive"))
        .DidEnterBackground(app => Debug.WriteLine("iOS DidEnterBackground"))
        .WillEnterForeground(app => Debug.WriteLine("iOS WillEnterForeground")));
#elif WINDOWS
    events.AddWindows(windows => windows
        .OnLaunched((app, args) => Debug.WriteLine("Windows OnLaunched"))
        .OnActivated((window, args) => Debug.WriteLine("Windows Activated"))
        .OnClosed((window, args) => Debug.WriteLine("Windows Closed")));
#endif
});
```

## Common Pitfalls

1. **Resumed does not fire on first launch.** The initial sequence is `Created` → `Activated`. Use `OnActivated` for logic that must run on every foreground entry, not `OnResumed`.

2. **Deactivated ≠ Stopped.** A dialog, split-screen, or notification pull-down triggers `Deactivated` without `Stopped`. Do not perform heavy saves in `OnDeactivated` — the app may never actually background.

3. **Android back button skips Stopped.** On Android, pressing back may call `Destroying` directly without `Stopped`. Place critical save logic in both `OnStopped` and `OnDestroying`.

4. **Multi-window apps fire events independently.** On iPad, Mac Catalyst, and desktop Windows each `Window` instance fires its own lifecycle events. Do not assume a single global lifecycle.

5. **Long-running handlers cause kills.** Android enforces a ~5 second ANR timeout; iOS has limited background execution time. Keep lifecycle handlers synchronous and fast — use `Preferences` for quick saves, not database writes.

6. **Do not use legacy Xamarin.Forms lifecycle methods.** `Application.OnStart()`, `Application.OnSleep()`, and `Application.OnResume()` exist for backward compatibility but bypass Window-level events. In .NET MAUI, prefer `Window` lifecycle events (`OnActivated`, `OnStopped`, `OnResumed`, etc.) for correct multi-window behavior.
