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
license: MIT
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
2. **Persist as state changes** — use `Preferences` for small values or file serialization for larger state. Debounce frequent edits if needed; lifecycle callbacks are supplemental flush points, not the only durable save.
3. **Load on cold start** before showing the draft. `Resumed` does not fire after process death or on first launch. Guard initialization so normal foreground entry does not overwrite newer in-memory edits.
4. **Flush opportunistically in `OnStopped` / `OnDestroying`** — neither event is guaranteed before process termination. A back action can bypass `Stopped`; adding `Destroying` still does not guarantee a final save.
5. **Keep handlers fast** — the OS can suspend or terminate the app; do not depend on a fixed time allowance or on an `async void` handler finishing.

```csharp
bool _draftLoaded;

protected override void OnActivated()
{
    base.OnActivated();
    if (_draftLoaded)
        return;
    _viewModel.DraftText = Preferences.Get("draft_text", string.Empty);
    _viewModel.ScrollY = Preferences.Get("scroll_y", 0.0);
    _draftLoaded = true;
}

// Call from the draft-change path too, not only from lifecycle callbacks.
void SaveDraft() => Preferences.Set("draft_text", _viewModel.DraftText);

protected override void OnStopped()
{
    base.OnStopped();
    SaveDraft();
    Preferences.Set("scroll_y", _viewModel.ScrollY);
}

protected override void OnResumed()
{
    base.OnResumed();
    // Resume surviving work; do not replace live edits with an older snapshot.
}

protected override void OnDestroying()
{
    base.OnDestroying();
    SaveDraft(); // Best-effort flush, not a guaranteed termination notification.
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

| Window Event | UIKit Callback | `AddiOS` builder method |
|---|---|---|
| Created | `WillFinishLaunching` / `SceneWillConnect` | `.WillFinishLaunching()` / `.SceneWillConnect()` |
| Activated | `DidBecomeActive` | `.OnActivated()` |
| Deactivated | `WillResignActive` | `.OnResignActivation()` |
| Stopped | `DidEnterBackground` | `.DidEnterBackground()` |
| Resumed | `WillEnterForeground` | `.WillEnterForeground()` |
| Destroying | `WillTerminate` | `.WillTerminate()` |

> ⚠️ The UIKit selector names and the `AddiOS` builder method names differ for
> activation. There is **no** `.DidBecomeActive()` or `.WillResignActive()` builder
> method — use `.OnActivated()` and `.OnResignActivation()` or the code will not compile.

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
        .OnActivated(app => Debug.WriteLine("iOS OnActivated"))
        .OnResignActivation(app => Debug.WriteLine("iOS OnResignActivation"))
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

3. **No guaranteed final callback.** Back navigation can bypass `Stopped`, and process death can bypass both `Stopped` and `Destroying`. Save critical state during ordinary changes and reload it on cold start; lifecycle saves only supplement that policy.

4. **Multi-window ownership is not service isolation.** Each window owns its document identity, edit state and subscriptions. A shared persistence service or event publisher is valid when operations identify the document and each window removes only its own exact handler/delegate. Do not demand separate stores/services or ban shared events. Make saves dirty-aware/idempotent rather than blindly writing twice when both `Stopped` and `Destroying` occur.

5. **Lifecycle events confer no background-execution entitlement.** An awaited upload does not gain a guaranteed execution window by starting in `Stopped`. Do not apply a universal Android ANR deadline to asynchronous work or promise that Windows will simply keep running. Persist pending intent/checkpoints and retry idempotently. If execution while suspended is actually required, verify the target platform's supported transfer/job facility and its constraints; scheduling one still does not guarantee completion after force-stop, network failure or OS policy changes.

6. **Do not use legacy Xamarin.Forms lifecycle methods.** `Application.OnStart()`, `Application.OnSleep()`, and `Application.OnResume()` exist for backward compatibility but bypass Window-level events. In .NET MAUI, prefer `Window` lifecycle events (`OnActivated`, `OnStopped`, `OnResumed`, etc.) for correct multi-window behavior.
