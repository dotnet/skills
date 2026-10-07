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
  DO NOT USE FOR: ASP.NET Core request DI, data binding (use maui-data-binding),
  Shell route/query design without a DI issue (use maui-shell-navigation), unit-test mocking frameworks (use standard xUnit
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
- Shell route/query design without a DI issue — use the **maui-shell-navigation** skill
- ASP.NET Core request-scoped DI — ordinary MAUI's missing scope boundary does not apply
- Mocking frameworks or test runners — use standard .NET testing tools (xUnit, NUnit, MSTest) and mocking libraries (NSubstitute, Moq)

## Inputs

- A .NET MAUI project with a `MauiProgram.cs` file
- Knowledge of which services, ViewModels, and Pages need registration
- Target platforms (Android, iOS, Mac Catalyst, Windows) for conditional registrations

## Rules That Change the Answer

| Situation | Do this | Why |
|---|---|---|
| Registering a Page or ViewModel | Prefer `AddTransient` for independently created detail pages | Transient means fresh **per resolution**, not per tab selection. Shell can cache a root page. A Singleton is defensible for intentionally shared root state; do not attach the same Page instance to multiple parents/windows |
| Registering shared/expensive state | `AddSingleton` | One instance app-wide (settings, DB connection, `HttpClient` handler) |
| Tempted to use `AddScoped` | Decide who creates, resolves from, and disposes the scope | Ordinary non-Blazor MAUI has **no automatic DI scope per window or navigation**. Scoped instances resolved from the root are shared until root disposal (or resolution throws when scope validation is enabled) |
| Navigating to a detail page | Register its dependencies and `Routing.RegisterRoute`; optionally register the page to control lifetime | With a service provider, Shell's type route factory uses `ActivatorUtilities.GetServiceOrCreateInstance`: an unregistered page can still receive registered constructor dependencies |
| Typed Shell `ContentTemplate` | Keep constructor injection; check the MAUI version and available context | MAUI 10's `ShellContent` looks up the page in DI, then falls back to `ActivatorUtilities.CreateInstance`. It does **not** universally bypass DI. Root content is cached |
| Platform-specific implementation | `#if` per platform **with every platform covered** | A missing platform branch leaves the service unregistered and throws at resolution time |

**Do not** introduce DI into a project that isn't using it, swap a working service
lifetime, or add an interface purely for symmetry — only when the user asked or it
fixes a real defect.

**Answer narrowly, but completely.** For a `DbContext` lifetime defect, explain
root-scope behavior and show a suitable registration/ownership change. Compare
short-lived contexts via `AddDbContextFactory`, a deliberately created/disposed
scope, and transient contexts when relevant. Transient alone does not ensure
operation-level freshness if a long-lived ViewModel retains the context, nor is
a `DbContext` safe for concurrent operations. Do not prescribe all alternatives
for an unrelated registration question. Load [the API reference](references/dependency-injection-api.md)
for explicit ownership examples, startup timing, or version-specific Shell behavior.

```csharp
// Explicit scope when you genuinely need unit-of-work semantics
using var scope = scopeFactory.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
```

## Workflow

1. Identify all services, ViewModels, and Pages that need to participate in dependency injection.
2. Match lifetimes to ownership — shared services can be Singleton, independently created detail graphs usually Transient. Preserve intentional shared root state.
3. Register all types in `MauiProgram.CreateMauiApp()` on `builder.Services`, grouping by category (services, HTTP, ViewModels, Pages).
4. For pushed detail pages, register Shell routes in `AppShell.xaml.cs`. For root tabs/flyout content, retain the working `ContentTemplate`; do not convert it to a pushed route just to enable DI.
5. Wire each Page to its ViewModel via constructor injection, assigning the ViewModel as `BindingContext`.
6. Add platform-specific registrations with `#if` directives, ensuring every target platform is covered or has a fallback.
7. Validate the actual construction path, dependency availability, and ownership. Run existing targeted tests/builds when changing code; report exactly what ran and any platform limitations. An advisory answer does not require a workload install or app build.

---

## Lifetime Selection

| Lifetime | When to Use | Typical Types |
|---|---|---|
| `AddSingleton<T>()` | Shared state, expensive to create, app-wide config | `HttpClient` factory, settings service, database connection |
| `AddTransient<T>()` | Needs a new instance when resolved | Detail Pages, ViewModels, per-call API wrappers |
| `AddScoped<T>()` | An explicitly created and disposed `IServiceScope` | Scoped unit-of-work (rare in ordinary MAUI) |

**Key rule:** Register Pages and ViewModels as **Transient** by default. Register shared services as **Singleton**.

> **No implicit scope ownership:** a new MAUI window/context does not give your
> scoped services a new DI scope. If you implement per-window scopes yourself,
> resolve that window's graph from its scope and dispose it on window teardown.
> Merely creating a scope does not make Shell automatically use its provider.

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

    // ViewModels — Transient for fresh state per resolution
    builder.Services.AddTransient<MainViewModel>();
    builder.Services.AddTransient<DetailViewModel>();

    // Pages — Transient when a fresh page is resolved
    builder.Services.AddTransient<MainPage>();
    builder.Services.AddTransient<DetailPage>();

    return builder.Build();
}
```

---

## Constructor Injection and Page Wiring

Register the ViewModel and its dependencies; register the Page when controlling
its lifetime. Inject the ViewModel into the Page and assign it as `BindingContext`:

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

With Shell's normal service-provider-backed type route factory, a registered Page
is resolved from DI; an unregistered Page is constructed with registered
dependencies. Registering the Page explicitly controls its lifetime:

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
Don't try to inject them through the constructor — `IQueryAttributable` can receive
navigation values independently of constructor services. For route/query design,
use **maui-shell-navigation** rather than redesigning registrations.

Shell applies query attributes to the page **and** its `BindingContext`, so the
ViewModel receives them without any wiring in the page.

---

## Platform-Specific Registration

Cover every declared target, including Mac Catalyst, using platform conditionals
or an equivalent selection mechanism. Choose an explicit unsupported-target
policy: no-op only if losing the feature is acceptable, otherwise fail clearly.
See [registration examples](references/dependency-injection-api.md#platform-specific-service-registration).

---

## Explicit Resolution (Last Resort)

Prefer constructor injection. An injected `IServiceProvider` can support dynamic
page factories. A handler's services are available only after its context exists;
avoid service lookup from unattached elements. Use `GetRequiredService` for a
required service so a missing registration fails clearly. See
[explicit resolution](references/dependency-injection-api.md#explicit-resolution).

---

## Interface-First Pattern for Testability

Define interfaces where services need substitution in tests, not mechanically for
every class. Resolve the production/test graph through the appropriate provider;
manual `new` graphs will bypass registered test doubles. See
[substitution example](references/dependency-injection-api.md#interface-first-pattern-for-testability).

---

## Common Pitfalls

### 1. Singleton ViewModels Cause Stale Data

```csharp
// Shared ViewModel state across independently created detail pages
builder.Services.AddSingleton<DetailViewModel>();

// New ViewModel on each resolution (not each selection of cached root content)
builder.Services.AddTransient<DetailViewModel>();
```

### 2. Confusing Template Creation Paths

For MAUI 10, `<ShellContent ContentTemplate="{DataTemplate views:DetailPage}">`
uses a typed template. `ShellContent` obtains services from its parent MAUI
context, tries `GetService(template.Type)`, and otherwise uses
`ActivatorUtilities.CreateInstance` with those services. A Page registration is
optional for activation; its required constructor dependencies must be available.
Missing required dependencies cause resolution to fail, not silently become null.

This is different from directly calling `DataTemplate.CreateContent()` outside
Shell, embedding an already-created Page, or supplying a `DataTemplate(Func<object>)`
whose factory controls creation. Check the actual version, template shape, and
context before diagnosing a failing path. Do not add a parameterless
service-locator constructor as a blanket fix. See [verified paths and caching](references/dependency-injection-api.md#shell-construction-paths-and-version-evidence).

### 3. XAML Resource Parsing vs. DI Timing

The container already exists when DI constructs `App`, but `Application.Current`
and element handlers/MAUI contexts may not be ready during `InitializeComponent()`.
Do not resolve through those globals while parsing `App.xaml`. Inject dependencies;
when useful, defer creating the root Shell to `CreateWindow()`:

Register `AppShell`, resolve it from the injected provider in `CreateWindow()`,
and return `new Window(shell)`, or inject the Shell directly when its constructor
does not depend on unavailable app globals. See [startup resolution](references/dependency-injection-api.md#startup-resolution).

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

See the rule table above: `AddScoped` does not create a window/navigation scope.
Root resolution shares one instance unless scope validation rejects it. Explicit
scopes/factories need a defined owner and disposal boundary.

---

## Checklist

- [ ] Required dependencies are registered in `MauiProgram.cs`; Page registrations control lifetime where needed
- [ ] Lifetimes match actual resolution and caching, not an assumed navigation scope
- [ ] Constructor injection used everywhere possible; service locator only as last resort
- [ ] Interfaces defined for services that need test substitution
- [ ] Platform-specific `#if` registrations cover all target platforms or include a fallback
- [ ] Startup code uses injected services, not globals/handlers before they are ready
- [ ] Explicit scopes are resolved through and disposed by their owner

## References

- [Dependency injection in .NET MAUI](https://learn.microsoft.com/dotnet/maui/fundamentals/dependency-injection)
- [.NET dependency injection fundamentals](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)
