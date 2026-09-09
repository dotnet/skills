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
| Receiving navigation parameters | Implement `IQueryAttributable` on the **ViewModel** | Implementing it on the Page, which splits state from the BindingContext |
| Passing a whole object | `ShellNavigationQueryParameters` | Serialising the object into the query string |
| Any `GoToAsync` call | `await` it | Fire-and-forget — exceptions are swallowed and navigation races |
| Confirming before back navigation | `ShellNavigatingEventArgs.GetDeferral()` … `deferral.Complete()` | Blocking synchronously on the dialog task |
| Detecting back navigation | Check `e.Source == ShellNavigationSource.Pop` | Assuming every navigation is a back action |

**Do not** propose `NavigationPage` / `PushAsync` solutions for a Shell app, and do
not restructure a working `AppShell` hierarchy unless the user asked.

**Answer narrowly, but completely.** Staying on topic does not mean being terse. When
you show a navigation change, include the pieces needed to run it: the `AppShell.xaml`
markup *and* the `Routing.RegisterRoute` call, or the `GoToAsync` call *and* the
receiving `IQueryAttributable` / `[QueryProperty]` code. Where two approaches are both
valid (query string vs `ShellNavigationQueryParameters`), show both and say when each
fits — a single snippet the user still has to complete is a worse answer.

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

## Workflow: Set Up AppShell

1. Define `AppShell.xaml` inheriting from `Shell`
2. Add `FlyoutItem` or `TabBar` elements for top-level navigation
3. Add `Tab` elements for bottom tabs; nest multiple `ShellContent` for top tabs
4. **Always use `ContentTemplate`** with `DataTemplate` so pages load on demand
5. **Give every `ShellContent` an explicit `Route`** (see below)
6. Register detail-page routes in the `AppShell` constructor

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

    <FlyoutItem Title="Animals" Icon="animals.png">
        <Tab Title="Cats">
            <ShellContent Title="Domestic" Route="domesticcats"
                          ContentTemplate="{DataTemplate views:DomesticCatsPage}" />
            <ShellContent Title="Wild" Route="wildcats"
                          ContentTemplate="{DataTemplate views:WildCatsPage}" />
        </Tab>
        <Tab Title="Dogs" Icon="dogs.png">
            <ShellContent Route="dogs" ContentTemplate="{DataTemplate views:DogsPage}" />
        </Tab>
    </FlyoutItem>

    <TabBar>
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
await Shell.Current.GoToAsync("//animals/cats/domestic");

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

## Workflow: Guard Navigation

Use `GetDeferral()` in `OnNavigating` for async checks (e.g., "save unsaved changes?"):

```csharp
// In AppShell.xaml.cs
protected override async void OnNavigating(ShellNavigatingEventArgs args)
{
    base.OnNavigating(args);
    if (hasUnsavedChanges && args.Source == ShellNavigationSource.Pop)
    {
        var deferral = args.GetDeferral();
        bool discard = await ShowConfirmationDialog();
        if (!discard)
            args.Cancel();
        deferral.Complete();
    }
}
```

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
- **Relative routes without registration**: You cannot `GoToAsync("somepage")` unless `somepage` was registered with `Routing.RegisterRoute`. Visual hierarchy pages use absolute `//` routes.
- **Fire-and-forget GoToAsync**: Not awaiting `GoToAsync` causes race conditions and silent failures. Always `await` the call.
- **Wrong absolute route path**: Absolute routes must match the full path through the visual hierarchy (`//FlyoutItem/Tab/ShellContent`). Wrong paths produce silent no-ops, not exceptions.
- **Manipulating Tab.Stack directly**: The navigation stack is read-only. Use `GoToAsync` for all navigation changes.
- **Forgetting `GetDeferral()` for async guards**: Synchronous cancellation in `OnNavigating` works, but async checks require `GetDeferral()` / `deferral.Complete()` to avoid race conditions.

## References

- `references/shell-navigation-api.md` — Full API reference for Shell hierarchy, routes, tabs, flyout, and navigation
- [.NET MAUI Shell Navigation](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/navigation)
- [.NET MAUI Shell Tabs](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/tabs)
- [.NET MAUI Shell Flyout](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/flyout)
- [.NET MAUI Shell Pages](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/pages)
