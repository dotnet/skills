---
name: maui-dependency-injection
description: >
  Guidance for configuring dependency injection in .NET MAUI apps — service
  registration in MauiProgram.cs, lifetime selection (Singleton / Transient / Scoped),
  constructor injection, Shell navigation auto-resolution, platform-specific
  registrations, and testability patterns.
  USE FOR: "dependency injection", "DI setup", "AddSingleton", "AddTransient",
  "AddScoped", "service registration", "constructor injection", "IServiceProvider",
  "MauiProgram DI", "register services", "BindingContext injection".
  DO NOT USE FOR: data binding (use maui-data-binding), Shell route configuration
  (use maui-shell-navigation), unit-test mocking frameworks (use standard xUnit
  and NSubstitute patterns).
license: MIT
---

# Dependency Injection in .NET MAUI

.NET MAUI uses the same `Microsoft.Extensions.DependencyInjection` container as ASP.NET Core. All service registration happens in `MauiProgram.CreateMauiApp()` on `builder.Services`. The container is built once at startup and is immutable thereafter.

## When to Use

- Registering services, ViewModels, and Pages in `MauiProgram.cs`
- Choosing between `AddSingleton`, `AddTransient`, and `AddScoped`
- Wiring constructor injection for Pages and ViewModels
- Leveraging Shell navigation to auto-resolve DI-registered Pages
- Registering platform-specific service implementations with `#if` directives
- Designing interfaces for testable service layers

## When Not to Use

- XAML data-binding syntax or compiled bindings — use the **maui-data-binding** skill
- Shell route registration and query parameters — use the **maui-shell-navigation** skill
- Mocking frameworks or test runners — use standard .NET testing tools (xUnit, NUnit, MSTest) and mocking libraries (NSubstitute, Moq)

## Inputs

- A .NET MAUI project with a `MauiProgram.cs` file
- Knowledge of which services, ViewModels, and Pages need registration
- Target platforms (Android, iOS, Mac Catalyst, Windows) for conditional registrations

## Rules That Change the Answer

| Situation | Do this | Why |
|---|---|---|
| Registering a Page or ViewModel | Prefer `AddTransient` | A fresh instance per navigation avoids stale state, and a Singleton page cannot be re-added to the visual tree after it is removed. Singleton is defensible for a genuinely single-instance page (e.g. a root tab you want to keep warm) |
| Registering shared/expensive state | `AddSingleton` | One instance app-wide (settings, DB connection, `HttpClient` handler) |
| Tempted to use `AddScoped` | Use `AddTransient` (or `AddSingleton` if sharing is intended) | MAUI has **no** built-in request scope like ASP.NET Core's HTTP pipeline. MAUI does create one `IServiceScope` per window, so a Scoped service lives as long as that window — and resolved from the root provider it behaves like a Singleton. Neither gives you per-navigation freshness |
| Navigating to a DI-registered page | Register the page **and** its ViewModel, then `Routing.RegisterRoute` | `Shell.Current.GoToAsync` resolves the page through DI and injects its constructor dependencies |
| Platform-specific implementation | `#if` per platform **with every platform covered** | A missing platform branch leaves the service unregistered and throws at resolution time |

**Do not** introduce DI into a project that isn't using it, swap a working service
lifetime, or add an interface purely for symmetry — only when the user asked or it
fixes a real defect.

**Answer narrowly, but completely.** When you recommend a lifetime change, show the
registration code, and give the realistic alternatives rather than a single verdict —
for a unit-of-work or `DbContext` question that means `AddTransient`, an explicit
`IServiceScopeFactory.CreateScope()`, **and** the factory pattern
(`AddDbContextFactory`), with a note on when each fits. A one-line prescription is
usually a worse answer than a short menu with trade-offs.

```csharp
// Explicit scope when you genuinely need unit-of-work semantics
using var scope = scopeFactory.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
```

## Workflow

1. Identify all services, ViewModels, and Pages that need to participate in dependency injection.
2. Choose the correct lifetime for each type — `AddSingleton` for shared services, `AddTransient` for Pages and ViewModels.
3. Register all types in `MauiProgram.CreateMauiApp()` on `builder.Services`, grouping by category (services, HTTP, ViewModels, Pages).
4. Register Pages as Shell routes in `AppShell.xaml.cs` so Shell navigation auto-resolves the full dependency graph.
5. Wire each Page to its ViewModel via constructor injection, assigning the ViewModel as `BindingContext`.
6. Add platform-specific registrations with `#if` directives, ensuring every target platform is covered or has a fallback.
7. Verify resolution works by running the app and confirming no `null` dependencies or missing-registration exceptions at runtime.

---

## Lifetime Selection

| Lifetime | When to Use | Typical Types |
|---|---|---|
| `AddSingleton<T>()` | Shared state, expensive to create, app-wide config | `HttpClient` factory, settings service, database connection |
| `AddTransient<T>()` | Lightweight, stateless, or needs a fresh instance per use | Pages, ViewModels, per-call API wrappers |
| `AddScoped<T>()` | Per-window lifetime, or a manually created `IServiceScope` | Scoped unit-of-work (rare in MAUI) |

**Key rule:** Register Pages and ViewModels as **Transient** by default. Register shared services as **Singleton**.

> ⚠️ **Avoid `AddScoped` unless you manually manage `IServiceScope`.** MAUI has no built-in request scope like ASP.NET Core. MAUI creates one `IServiceScope` per window, so a Scoped service lives as long as that window; resolved from the root provider it silently behaves as a Singleton. Neither gives per-navigation freshness.

---

## Registration Pattern in MauiProgram.cs

```csharp
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>();

    // Services — Singleton for shared state
    builder.Services.AddSingleton<IDataService, DataService>();
    builder.Services.AddSingleton<ISettingsService, SettingsService>();

    // HTTP — use typed or named clients via IHttpClientFactory
    // Requires NuGet: Microsoft.Extensions.Http
    builder.Services.AddHttpClient<IApiClient, ApiClient>();

    // ViewModels — Transient for fresh state per navigation
    builder.Services.AddTransient<MainViewModel>();
    builder.Services.AddTransient<DetailViewModel>();

    // Pages — Transient so constructor injection fires each time
    builder.Services.AddTransient<MainPage>();
    builder.Services.AddTransient<DetailPage>();

    return builder.Build();
}
```

---

## Constructor Injection

Inject dependencies through constructor parameters. The container resolves them automatically when the type is itself resolved from DI.

```csharp
public class MainViewModel
{
    private readonly IDataService _dataService;

    public MainViewModel(IDataService dataService)
    {
        _dataService = dataService;
    }

    public async Task LoadAsync() => Items = await _dataService.GetItemsAsync();
}
```

### ViewModel → Page Wiring

Register both Page and ViewModel. Inject the ViewModel into the Page and assign it as `BindingContext`:

```csharp
public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
```

---

## Shell Navigation Auto-Resolution

When a Page is registered in DI **and** as a Shell route, Shell resolves it (and its full dependency graph) automatically on navigation:

```csharp
// MauiProgram.cs
builder.Services.AddTransient<DetailPage>();
builder.Services.AddTransient<DetailViewModel>();

// AppShell.xaml.cs
Routing.RegisterRoute(nameof(DetailPage), typeof(DetailPage));

// Navigate — DI resolves DetailPage + DetailViewModel
await Shell.Current.GoToAsync(nameof(DetailPage));
```

### Passing parameters to a DI-resolved ViewModel

DI supplies the ViewModel's *dependencies*; navigation parameters arrive separately.
Don't try to inject them through the constructor — implement `IQueryAttributable`
on the ViewModel so it receives both:

```csharp
public class DetailViewModel : ObservableObject, IQueryAttributable
{
    readonly IDataService _data;   // ← injected by DI

    public DetailViewModel(IDataService data) => _data = data;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // ← supplied by navigation
        if (query.TryGetValue("id", out var id))
            LoadAsync(id.ToString()!);
    }
}

// Navigate with a parameter — the page and its ViewModel still come from DI
await Shell.Current.GoToAsync($"{nameof(DetailPage)}?id={product.Id}");
```

Shell applies query attributes to the page **and** its `BindingContext`, so the
ViewModel receives them without any wiring in the page.

---

## Platform-Specific Registration

Use preprocessor directives to register platform implementations. Always cover every target platform or provide a no-op fallback to avoid runtime `null`.

```csharp
#if ANDROID
builder.Services.AddSingleton<INotificationService, AndroidNotificationService>();
#elif IOS || MACCATALYST
builder.Services.AddSingleton<INotificationService, AppleNotificationService>();
#elif WINDOWS
builder.Services.AddSingleton<INotificationService, WindowsNotificationService>();
#else
builder.Services.AddSingleton<INotificationService, NoOpNotificationService>();
#endif
```

---

## Explicit Resolution (Last Resort)

Prefer constructor injection. Use explicit resolution only where injection is genuinely unavailable (custom handlers, platform callbacks):

```csharp
// From any Element with a Handler
var service = this.Handler.MauiContext.Services.GetService<IDataService>();
```

For dynamic resolution, inject `IServiceProvider`:

```csharp
public class NavigationService(IServiceProvider serviceProvider)
{
    public T ResolvePage<T>() where T : Page
        => serviceProvider.GetRequiredService<T>();
}
```

---

## Interface-First Pattern for Testability

Define interfaces for every service so implementations can be swapped in tests:

```csharp
public interface IDataService
{
    Task<List<Item>> GetItemsAsync();
}

// Production registration
builder.Services.AddSingleton<IDataService, DataService>();

// Test registration — swap without touching production code
var services = new ServiceCollection();
services.AddSingleton<IDataService, FakeDataService>();
```

---

## Common Pitfalls

### 1. Singleton ViewModels Cause Stale Data

```csharp
// ❌ ViewModel keeps stale state across navigations
builder.Services.AddSingleton<DetailViewModel>();

// ✅ Fresh instance each navigation
builder.Services.AddTransient<DetailViewModel>();
```

### 2. ContentTemplate Pages Are Not Created Through DI

Pages declared in Shell XAML via `<ShellContent ContentTemplate="{DataTemplate views:DetailPage}">` are instantiated with `Activator.CreateInstance` (`ElementTemplate.cs`), **not** through the service provider. Constructor injection does not run on that path: if the page's only constructor takes dependencies, you get a `MissingMethodException` — not a silently `null` dependency.

Pages reached through `Routing.RegisterRoute` + `GoToAsync` are different: they go through `ActivatorUtilities.GetServiceOrCreateInstance` (`Routing.cs`), which injects registered dependencies even if the page type itself was never registered, and **throws** if a required dependency cannot be resolved.

```csharp
// Registering the page and its dependencies keeps both paths working
builder.Services.AddTransient<DetailPage>();
builder.Services.AddTransient<DetailViewModel>();
```

If you need DI for a tab/flyout page, give it a parameterless constructor that resolves what it needs, or navigate to it by route instead of embedding it in `ContentTemplate`.

### 3. XAML Resource Parsing vs. DI Timing

XAML resources in `App.xaml` are parsed during `InitializeComponent()` — before the container is fully available. Defer service-dependent work to `CreateWindow()`:

```csharp
public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Safe — container is fully built
        // Requires: builder.Services.AddTransient<AppShell>() in MauiProgram.cs
        var appShell = _services.GetRequiredService<AppShell>();
        return new Window(appShell);
    }
}
```

### 4. Service Locator Anti-Pattern

```csharp
// ❌ Hides dependencies, hard to test
var svc = this.Handler.MauiContext.Services.GetService<IDataService>();

// ✅ Constructor injection — explicit and testable
public class MyViewModel(IDataService dataService) { }
```

### 5. Missing Platform in Conditional Registration

Forgetting a platform in `#if` blocks means `GetService<T>()` returns `null` at runtime on that platform. Always include an `#else` fallback or cover every target.

### 6. AddScoped Without Manual Scope

See the rule table above: `AddScoped` gives you either window lifetime or Singleton behaviour, never per-navigation freshness. Use `AddTransient` or `AddSingleton` unless you explicitly create and manage an `IServiceScope`.

---

## Checklist

- [ ] Every Page and ViewModel that needs injection is registered in `MauiProgram.cs`
- [ ] Pages and ViewModels use `AddTransient`; shared services use `AddSingleton`
- [ ] Constructor injection used everywhere possible; service locator only as last resort
- [ ] Interfaces defined for services that need test substitution
- [ ] Platform-specific `#if` registrations cover all target platforms or include a fallback
- [ ] Service-dependent work deferred to `CreateWindow()`, not run during XAML parse
- [ ] `AddScoped` used only when window lifetime is intended, or alongside a manually created `IServiceScope`

## References

- [Dependency injection in .NET MAUI](https://learn.microsoft.com/dotnet/maui/fundamentals/dependency-injection)
- [.NET dependency injection fundamentals](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)
