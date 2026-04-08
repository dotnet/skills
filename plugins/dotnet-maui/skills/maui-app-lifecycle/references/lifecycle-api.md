# .NET MAUI App Lifecycle — API Reference

## App States

A MAUI app moves through four logical states:

| State | Meaning |
|---|---|
| **Not Running** | App process does not exist. |
| **Running** | App is in the foreground and receiving input. |
| **Deactivated** | App is visible but lost focus (e.g. a dialog or split-screen). |
| **Stopped** | App is fully backgrounded; UI is not visible. |

Typical flow: Not Running → Running → Deactivated → Stopped → Running (resumed) or Not Running (terminated).

## Cross-platform Window Events

`Microsoft.Maui.Controls.Window` exposes six lifecycle events:

| Event | When it fires |
|---|---|
| `Created` | Window has been created (native window allocated). |
| `Activated` | Window has been activated and is receiving input. |
| `Deactivated` | Window lost focus but may still be visible. |
| `Stopped` | Window is no longer visible (backgrounded). |
| `Resumed` | Window returns to the foreground after being stopped. |
| `Destroying` | Window is being torn down (native window deallocated). |

## Subscribing to Window Events

### Option A — Override `CreateWindow` in `App`

```csharp
public partial class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Created += (s, e) => Log("Window Created");
        window.Activated += (s, e) => Log("Window Activated");
        window.Deactivated += (s, e) => Log("Window Deactivated");
        window.Stopped += (s, e) => Log("Window Stopped");
        window.Resumed += (s, e) => Log("Window Resumed");
        window.Destroying += (s, e) => Log("Window Destroying");

        return window;
    }
}
```

### Option B — Custom Window subclass with overrides

```csharp
public class AppWindow : Window
{
    public AppWindow() : base() { }
    public AppWindow(Page page) : base(page) { }

    protected override void OnCreated() { /* init work */ }
    protected override void OnActivated() { /* refresh UI */ }
    protected override void OnDeactivated() { /* pause timers */ }
    protected override void OnStopped() { /* save state */ }
    protected override void OnResumed() { /* restore state */ }
    protected override void OnDestroying() { /* cleanup */ }
}
```

Return it from `CreateWindow`:

```csharp
protected override Window CreateWindow(IActivationState? activationState)
{
    return new AppWindow(new AppShell());
}
```

## Platform Lifecycle Event Mapping

### Android

| Window event | Android Activity callback |
|---|---|
| Created | `OnCreate` |
| Activated | `OnResume` |
| Deactivated | `OnPause` |
| Stopped | `OnStop` |
| Resumed | `OnRestart` → `OnStart` → `OnResume` |
| Destroying | `OnDestroy` |

### iOS / Mac Catalyst

| Window event | UIKit callback |
|---|---|
| Created | `WillFinishLaunching` / `SceneWillConnect` |
| Activated | `DidBecomeActive` |
| Deactivated | `WillResignActive` |
| Stopped | `DidEnterBackground` |
| Resumed | `WillEnterForeground` |
| Destroying | `WillTerminate` |

### Windows (WinUI)

| Window event | WinUI callback |
|---|---|
| Created | `OnLaunched` |
| Activated | `Activated` (foreground) |
| Deactivated | `Activated` (background) |
| Stopped | `VisibilityChanged` (false) |
| Resumed | `VisibilityChanged` (true) |
| Destroying | `Closed` |

## Platform-specific Lifecycle Events

Use `ConfigureLifecycleEvents` in `MauiProgram.cs` to hook directly into native callbacks:

```csharp
builder.ConfigureLifecycleEvents(events =>
{
#if ANDROID
    events.AddAndroid(android => android
        .OnCreate((activity, bundle) => Log("Android OnCreate"))
        .OnStart(activity => Log("Android OnStart"))
        .OnResume(activity => Log("Android OnResume"))
        .OnPause(activity => Log("Android OnPause"))
        .OnStop(activity => Log("Android OnStop"))
        .OnDestroy(activity => Log("Android OnDestroy")));
#elif IOS || MACCATALYST
    events.AddiOS(ios => ios
        .WillFinishLaunching((app, options) => { Log("iOS WillFinishLaunching"); return true; })
        .SceneWillConnect((scene, session, options) => Log("iOS SceneWillConnect"))
        .DidBecomeActive(app => Log("iOS DidBecomeActive"))
        .WillResignActive(app => Log("iOS WillResignActive"))
        .DidEnterBackground(app => Log("iOS DidEnterBackground"))
        .WillTerminate(app => Log("iOS WillTerminate")));
#elif WINDOWS
    events.AddWindows(windows => windows
        .OnLaunched((app, args) => Log("Windows OnLaunched"))
        .OnActivated((window, args) => Log("Windows Activated"))
        .OnClosed((window, args) => Log("Windows Closed")));
#endif
});
```

## State Preservation Pattern

Save and restore transient state during backgrounding:

```csharp
protected override void OnStopped()
{
    base.OnStopped();
    Preferences.Set("draft_text", _viewModel.DraftText);
    Preferences.Set("scroll_position", _viewModel.ScrollY);
}

protected override void OnResumed()
{
    base.OnResumed();
    _viewModel.DraftText = Preferences.Get("draft_text", string.Empty);
    _viewModel.ScrollY = Preferences.Get("scroll_position", 0.0);
}
```

For larger state, use `SecureStorage` or file-based serialization instead of `Preferences`.
