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
| `AddScoped<T>()` | Per-scope lifetime with manually created `IServiceScope` | Scoped unit-of-work (rare in MAUI) |

**Key rule:** Register Pages and ViewModels as **Transient**. Register shared services as **Singleton**.

> ⚠️ **Avoid `AddScoped` unless you manually manage `IServiceScope`.** MAUI has no built-in request scope like ASP.NET Core. A Scoped registration without an explicit scope silently behaves as a Singleton, leading to subtle bugs.

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

### 2. Unregistered Page Silently Skips Injection

If a Page appears in Shell XAML via `<ShellContent ContentTemplate="...">` but is **not** registered in `builder.Services`, MAUI creates it with the parameterless constructor. Dependencies are silently `null` — no exception is thrown.

```csharp
// ❌ Missing — injection silently skipped
// builder.Services.AddTransient<DetailPage>();

// ✅ Always register pages that need injection
builder.Services.AddTransient<DetailPage>();
builder.Services.AddTransient<DetailViewModel>();
```

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

`AddScoped` in MAUI without creating `IServiceScope` manually gives Singleton behavior silently. Use `AddTransient` or `AddSingleton` instead unless you explicitly manage scopes.

---

## Checklist

- [ ] Every Page and ViewModel that needs injection is registered in `MauiProgram.cs`
- [ ] Pages and ViewModels use `AddTransient`; shared services use `AddSingleton`
- [ ] Constructor injection used everywhere possible; service locator only as last resort
- [ ] Interfaces defined for services that need test substitution
- [ ] Platform-specific `#if` registrations cover all target platforms or include a fallback
- [ ] Service-dependent work deferred to `CreateWindow()`, not run during XAML parse
- [ ] `AddScoped` only used alongside manually created `IServiceScope`

## References

- [Dependency injection in .NET MAUI](https://learn.microsoft.com/dotnet/maui/fundamentals/dependency-injection)
- [.NET dependency injection fundamentals](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)
