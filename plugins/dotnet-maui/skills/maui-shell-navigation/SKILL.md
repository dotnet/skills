---
name: maui-shell-navigation
description: >-
  Guide for implementing Shell-based navigation in .NET MAUI apps. Covers AppShell
  setup, visual hierarchy (FlyoutItem, TabBar, Tab, ShellContent), URI-based navigation
  with GoToAsync, route registration, query parameters, back navigation, flyout and
  tab configuration, navigation events, and navigation guards.
  Use when: setting up Shell navigation, adding tabs or flyout menus, navigating between
  pages with GoToAsync, passing parameters between pages, registering routes, customizing
  back button behavior, or guarding navigation with confirmation dialogs.
  Do not use for: deep linking from external URLs (see .NET MAUI deep linking
  documentation), data binding on pages (use maui-data-binding), dependency injection
  setup (use maui-dependency-injection), or NavigationPage-only apps that don't use Shell.
license: MIT
---

# .NET MAUI Shell Navigation

Implement page navigation in .NET MAUI apps using Shell. Shell provides URI-based navigation, a flyout menu, tab bars, and a four-level visual hierarchy — all configured declaratively in XAML.

## When to Use

- Setting up top-level app navigation with tabs or a flyout menu
- Navigating between pages programmatically with `GoToAsync`
- Passing data between pages via query parameters or object parameters
- Registering detail-page routes for push navigation
- Guarding navigation with confirmation dialogs (e.g., unsaved changes)
- Customizing back button behavior per page

## When Not to Use

- Deep linking from external URLs or app links — see [.NET MAUI deep linking docs](https://learn.microsoft.com/dotnet/maui/fundamentals/app-links)
- Data binding on navigation target pages — use `maui-data-binding`
- Dependency injection for pages and view models — use `maui-dependency-injection`
- Apps using `NavigationPage` without Shell (different navigation API)

## Inputs

- A .NET MAUI project with `AppShell.xaml` as the root shell
- Pages (`ContentPage`) to navigate between
- Route names for detail pages not in the visual hierarchy

## Rules That Change the Answer

These are the Shell-specific decisions that are easy to get wrong. Apply them
whenever they are relevant to what the user asked.

| Situation | Do this | Not this |
|---|---|---|
| Declaring pages in `AppShell.xaml` | With `xmlns:views="clr-namespace:MyApp.Views"` declared: `<ShellContent ContentTemplate="{DataTemplate views:MyPage}" />` — the page is created on first navigation | `<ShellContent><views:MyPage /></ShellContent>`, which constructs **every** page at startup |
| Navigating to a page not in the visual hierarchy | `Routing.RegisterRoute("details", typeof(DetailsPage))` first | Calling `GoToAsync("details")` unregistered — it throws at runtime |
| Receiving navigation parameters | Prefer `IQueryAttributable` on the **ViewModel**; Page is also valid when it owns the state | Reflection-based `[QueryProperty]` in a full-trim / NativeAOT build |
| Passing a whole object | `ShellNavigationQueryParameters` | Serialising the object into the query string |
| Any `GoToAsync` call | `await` it | Fire-and-forget — exceptions are swallowed and navigation races |
| Confirming before back navigation | `ShellNavigatingEventArgs.GetDeferral()` … `deferral.Complete()` | Blocking synchronously on the dialog task |
| Detecting back navigation | Check `e.Source == ShellNavigationSource.Pop` | Assuming every navigation is a back action |

**Do not** propose `NavigationPage` / `PushAsync` solutions for a Shell app, and do
not restructure a working `AppShell` hierarchy unless the user asked.

For supplied-code checks, leave a passing source set unchanged and report the
validation limits explicitly: package/object-model checks do not execute native
navigation, XAML rendering or device handlers.

If the user requests an absolute URI, give the actual `GoToAsync("//…")` value
in the final answer, derived from the named hierarchy. A slash-separated
breadcrumb without the leading `//` is not an absolute Shell URI, even if the
source repair and checks pass.

**Answer narrowly, but completely.** Supply one working approach with the pieces
the request needs: a registration and navigation call for a new detail route,
or the sender and receiver when passing data. Compare alternatives only when the
user needs to choose between them.

For a how-to question without app files, show usable app-level code. The loaded
guide and its references are not the user's `AppShell`; do not edit them as a
substitute for answering the question.

## Shell Visual Hierarchy

Shell uses a four-level hierarchy. Each level wraps the one below it:

```
Shell
 ├── FlyoutItem / TabBar          (top-level grouping)
 │    ├── Tab                     (bottom-tab grouping)
 │    │    ├── ShellContent        (page slot → ContentPage)
 │    │    └── ShellContent        (multiple = top tabs)
 │    └── Tab
 └── FlyoutItem / TabBar
```

- **FlyoutItem** — appears in the flyout menu; contains `Tab` children
- **TabBar** — bottom tab bar with no flyout entry
- **Tab** — groups `ShellContent`; multiple children produce top tabs
- **ShellContent** — each points to a `ContentPage`

### Implicit Conversion

You can omit intermediate wrappers. Shell auto-wraps:

| You write                    | Shell creates                         |
|------------------------------|---------------------------------------|
| `ShellContent` only          | `FlyoutItem > Tab > ShellContent`     |
| `Tab` only                   | `FlyoutItem > Tab`                    |
| `ShellContent` in `TabBar`   | `TabBar > Tab > ShellContent`         |

Explicit and implicit wrappers are both valid. A single explicit `Tab` around a
single `ShellContent` does not by itself force a visible tab bar. In MAUI 10,
the controller normally hides the bar for one section unless visibility is
overridden; Windows also considers nested contents. `FlyoutDisplayOptions`
controls flyout presentation, not tab-bar visibility. Do not remove a working
wrapper based only on a claimed extra native UI level.

## Workflow: Set Up AppShell

1. Define `AppShell.xaml` inheriting from `Shell`
2. Add `FlyoutItem` or `TabBar` elements for top-level navigation
3. Add `Tab` elements for bottom tabs; nest multiple `ShellContent` for top tabs
4. **Always use `ContentTemplate`** with `DataTemplate` so pages load on demand
5. **Give every `ShellContent` an explicit `Route`** (see below)
6. Register detail-page routes in the `AppShell` constructor

For a stable full absolute URI, explicitly route **every level referenced by the
URI**, including `FlyoutItem`/`Tab`. A title does not assign that route. Return
the leading `//`, not only a slash-separated component path, when asked for an
absolute Shell route. Check that the exact segments in `GoToAsync` exist in the
markup you just supplied; preserve supplied fixture route names.

Match the requested visible levels before adding wrappers. For one flyout entry
with two **top** subtabs, use one `FlyoutItem`, one `Tab`, and two sibling
`ShellContent` elements titled for the subtabs. Leave that grouping `Tab` untitled;
do not repeat the flyout title as another visible navigation label. For two **bottom** tabs, use two
sibling `Tab` elements, each containing its page. Do not add a visible intermediate
tab named after the flyout section unless the user requests that extra level.

> **Set `Route=` on every `ShellContent`.** If you omit it, MAUI auto-generates a
> name from a shared counter — `Routing.cs` produces `D_FAULT_{TypeName}{n}`. A real
> shell with three unnamed `ShellContent` elements yields routes like
> `D_FAULT_ShellContent2` and `D_FAULT_ShellContent5`: the numbers are not
> sequential, they depend on how many Shell elements were constructed first, and they
> shift when you reorder or add pages. You cannot write a stable absolute route
> (`//dashboard`) or deep link against that. An explicit `Route="dashboard"` is stable
> forever.

```xml
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:views="clr-namespace:MyApp.Views"
       x:Class="MyApp.AppShell"
       FlyoutBehavior="Flyout">

    <FlyoutItem Title="Animals" Route="animals" Icon="animals.png">
        <Tab Title="Cats" Route="cats">
            <ShellContent Title="Domestic" Route="domesticcats"
                          ContentTemplate="{DataTemplate views:DomesticCatsPage}" />
            <ShellContent Title="Wild" Route="wildcats"
                          ContentTemplate="{DataTemplate views:WildCatsPage}" />
        </Tab>
        <Tab Title="Dogs" Route="dogtab" Icon="dogs.png">
            <ShellContent Route="dogs" ContentTemplate="{DataTemplate views:DogsPage}" />
        </Tab>
    </FlyoutItem>

    <TabBar Route="main">
        <ShellContent Title="Home" Icon="home.png" Route="home"
                      ContentTemplate="{DataTemplate views:HomePage}" />
        <ShellContent Title="Settings" Icon="settings.png" Route="settings"
                      ContentTemplate="{DataTemplate views:SettingsPage}" />
    </TabBar>
</Shell>
```

```csharp
// AppShell.xaml.cs
public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("animaldetails", typeof(AnimalDetailsPage));
        Routing.RegisterRoute("editanimal", typeof(EditAnimalPage));
    }
}
```

## Workflow: Navigate with GoToAsync

All programmatic navigation uses `Shell.Current.GoToAsync`. Always `await` the call.

### Route Prefixes

| Prefix | Meaning                                     |
|--------|---------------------------------------------|
| `//`   | Absolute route from Shell root              |
| (none) | Relative; pushes onto the current nav stack |
| `..`   | Go back one level                           |
| `../`  | Go back then navigate forward               |

### Navigation Examples

```csharp
// 1. Absolute — switch to a specific hierarchy location
await Shell.Current.GoToAsync("//animals/cats/domesticcats");

// 2. Relative — push a registered detail page
await Shell.Current.GoToAsync("animaldetails");

// 3. With query string parameters
await Shell.Current.GoToAsync($"animaldetails?id={animal.Id}");

// 4. Go back one page
await Shell.Current.GoToAsync("..");

// 5. Go back two pages
await Shell.Current.GoToAsync("../..");

// 6. Go back one page, then push a different page
await Shell.Current.GoToAsync("../editanimal");
```

## Workflow: Pass Data Between Pages

### Option 1: IQueryAttributable (Preferred)

Implement on ViewModels to receive all parameters in one call:

```csharp
public class AnimalDetailsViewModel : ObservableObject, IQueryAttributable
{
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id))
            AnimalId = id.ToString();
    }
}
```

### Option 2: QueryProperty Attribute

Apply on the **ViewModel** class (or the page, if it genuinely owns the state).
Prefer `IQueryAttributable` on the ViewModel — it keeps navigation state with the
`BindingContext` and handles multiple parameters in one call:

`QueryPropertyAttribute` is reflection-based and is **not** trim-safe for full
trimming or NativeAOT. Use `IQueryAttributable` for those deployments; do not claim
the attribute gets a generated reflection-free setter.

```csharp
[QueryProperty(nameof(AnimalId), "id")]
public partial class AnimalDetailsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _animalId = string.Empty;
}
```

Shell applies query attributes *after* the page constructor sets `BindingContext`,
so the property must raise change notification — a plain auto-property leaves the
binding stuck on its initial value.

### Option 3: Complex Objects via ShellNavigationQueryParameters

Pass objects without serializing to strings:

```csharp
var parameters = new ShellNavigationQueryParameters
{
    { "animal", selectedAnimal }
};
await Shell.Current.GoToAsync("animaldetails", parameters);
```

Receive via `IQueryAttributable`:

```csharp
public void ApplyQueryAttributes(IDictionary<string, object> query)
{
    Animal = query["animal"] as Animal;
}
```

`ShellNavigationQueryParameters` is a single-use transfer: Shell clears it after
navigation. In contrast, an ordinary `IDictionary<string, object>` can retain
values for the lifetime of the destination page and resend them on back
navigation; clear consumed values when that retention is unwanted.

Clearing either dictionary does **not** release references you saved elsewhere.
If the object must not remain on the destination's back-stack entry, copy only
the needed scalar values (with notifying setters), or retain an ID and reload;
do not assign the entire object to a long-lived ViewModel field. Avoid clearing
all query keys when another receiver still needs them.

## Workflow: Guard Navigation

Use `GetDeferral()` in `OnNavigating` for async checks (e.g., "save unsaved changes?"):

```csharp
// In AppShell.xaml.cs
protected override async void OnNavigating(ShellNavigatingEventArgs args)
{
    base.OnNavigating(args);
    if (_checkingNavigation)
    {
        if (args.CanCancel)
            args.Cancel();
        return;
    }
    if (!hasUnsavedChanges || args.Source != ShellNavigationSource.Pop || !args.CanCancel)
        return;
    var deferral = args.GetDeferral();
    _checkingNavigation = true;
    try
    {
        if (!await ShowConfirmationDialog())
            args.Cancel();
    }
    catch (Exception ex)
    {
        args.Cancel();
        System.Diagnostics.Debug.WriteLine(ex);
    }
    finally
    {
        deferral.Complete();
        _checkingNavigation = false;
    }
}
```

Declare `_checkingNavigation` as an instance `bool`. Always complete the deferral
and clear the guard on success, cancellation and dialog failure. `OnNavigating`
is `async void`: log/report a caught error and cancel safely rather than rethrowing
an unhandled exception. Route initiating calls through the same in-flight policy;
a second `GoToAsync` can be rejected by Shell before another callback is raised.

For a pending-deferral problem, show the initiating method as well as the
handler. Call this on the same `AppShell` instance that owns the guard:

```csharp
public async Task<bool> TryNavigateAsync(string route)
{
    if (_checkingNavigation)
        return false;
    try
    {
        await GoToAsync(route);
        return true;
    }
    catch (InvalidOperationException ex)
    {
        System.Diagnostics.Debug.WriteLine(ex);
        return false;
    }
}
```

The caller awaits the result; `false` means the request was skipped or rejected.
`true` only means `GoToAsync` completed: a canceled transition need not throw,
so it does not prove the route changed. Do not claim a pending second request
always reaches `OnNavigating`.

## Tab Configuration

### Bottom Tabs

Multiple `ShellContent` (or `Tab`) children inside a `TabBar` or `FlyoutItem` produce bottom tabs.

### Top Tabs

Multiple `ShellContent` children inside a single `Tab` produce top tabs:

```xml
<Tab Title="Photos">
    <ShellContent Title="Recent"    ContentTemplate="{DataTemplate views:RecentPage}" />
    <ShellContent Title="Favorites" ContentTemplate="{DataTemplate views:FavoritesPage}" />
</Tab>
```

### Tab Bar Appearance

| Attached Property              | Type    | Purpose                        |
|--------------------------------|---------|--------------------------------|
| `Shell.TabBarBackgroundColor`  | `Color` | Tab bar background             |
| `Shell.TabBarForegroundColor`  | `Color` | Selected icon color            |
| `Shell.TabBarTitleColor`       | `Color` | Selected tab title color       |
| `Shell.TabBarUnselectedColor`  | `Color` | Unselected tab icon/title      |
| `Shell.TabBarIsVisible`        | `bool`  | Show/hide the tab bar          |

```xml
<!-- Hide the tab bar on a specific page -->
<ContentPage Shell.TabBarIsVisible="False" ... />
```

## Flyout Configuration

### FlyoutBehavior

Set on `Shell`: `Disabled`, `Flyout`, or `Locked`.

```xml
<Shell FlyoutBehavior="Flyout"> ... </Shell>
```

### FlyoutDisplayOptions

Controls how children appear in the flyout:

- `AsSingleItem` (default) — one flyout entry for the group
- `AsMultipleItems` — each child `Tab` gets its own entry

```xml
<FlyoutItem Title="Animals" FlyoutDisplayOptions="AsMultipleItems">
    <Tab Title="Cats" ... />
    <Tab Title="Dogs" ... />
</FlyoutItem>
```

### MenuItem (Non-Navigation Flyout Entries)

```xml
<MenuItem Text="Log Out"
          Command="{Binding LogOutCommand}"
          IconImageSource="logout.png" />
```

## Back Button Behavior

Customize the back button per page:

```xml
<Shell.BackButtonBehavior>
    <BackButtonBehavior Command="{Binding BackCommand}"
                       IconOverride="back_arrow.png"
                       TextOverride="Cancel"
                       IsVisible="True" />
</Shell.BackButtonBehavior>
```

Properties: `Command`, `CommandParameter`, `IconOverride`, `TextOverride`, `IsVisible`, `IsEnabled`.

## Inspecting Navigation State

```csharp
// Current URI location
string location = Shell.Current.CurrentState.Location.ToString();

// Current page
Page page = Shell.Current.CurrentPage;

// Navigation stack of the current tab
IReadOnlyList<Page> stack = Shell.Current.Navigation.NavigationStack;
```

## Navigation Events

Override in `AppShell`:

```csharp
protected override void OnNavigated(ShellNavigatedEventArgs args)
{
    base.OnNavigated(args);
    // args.Current, args.Previous, args.Source
}
```

`ShellNavigationSource` values: `Push`, `Pop`, `PopToRoot`, `Insert`, `Remove`, `ShellItemChanged`, `ShellSectionChanged`, `ShellContentChanged`, `Unknown`.

## Common Pitfalls

- **Eager page creation**: Using `Content` directly instead of `ContentTemplate` with `DataTemplate` creates all pages at Shell init, hurting startup time. Always use `ContentTemplate`.
- **Duplicate route names**: `Routing.RegisterRoute` throws `ArgumentException` if a route name matches an existing route or a visual hierarchy route. Every route must be unique across the app.
- **Relative-route diagnosis**: Register pushed detail routes before navigating.
  MAUI 10 does not support ordinary relative pushes to visual Shell elements;
  select those destinations with absolute `//` routes. Relative global routes and
  `..` back navigation use the current location. Inspect the actual exception:
  missing registration is not the only cause — destination construction can fail too.
- **Fire-and-forget GoToAsync**: Not awaiting `GoToAsync` causes race conditions and silent failures. Always `await` the call.
- **Wrong absolute route path**: Absolute routes select the visual hierarchy; global/detail routes registered with `Routing.RegisterRoute` are pushed relatively. A non-existent route can throw `ArgumentException`, and `//globalRoute` is unsupported on MAUI 10. Do not describe either as a guaranteed silent no-op.
  If navigation completes without a visible change, check whether that destination
  is already selected or its data stayed unchanged. A unique absolute leaf can
  resolve, but prefer named full paths for stable deep links.
- **Manipulating Tab.Stack directly**: The navigation stack is read-only. Use `GoToAsync` for all navigation changes.
- **Forgetting `GetDeferral()` for async guards**: Synchronous cancellation in `OnNavigating` works, but async checks require `GetDeferral()` / `deferral.Complete()` to avoid race conditions.

## References

- `references/shell-navigation-api.md` — Full API reference for Shell hierarchy, routes, tabs, flyout, and navigation
- [.NET MAUI Shell Navigation](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/navigation)
- [.NET MAUI Shell Tabs](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/tabs)
- [.NET MAUI Shell Flyout](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/flyout)
- [.NET MAUI Shell Pages](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/pages)
