---
name: maui-dependency-injection
description: >
  Configure or diagnose dependency injection in .NET MAUI — MauiProgram
  registration, lifetimes, constructor injection, Shell activation and startup
  handler timing. Use when service construction, registration or lifetime is
  at issue, not a wrong binding path on an already-correct BindingContext.
  Answer supplied-code advisory questions directly; inspect
  sources only for edits or unresolved version/project facts.
  USE FOR: "dependency injection", "DI setup", "AddSingleton", "AddTransient",
  "AddScoped", "service registration", "constructor injection", "IServiceProvider",
  "MauiProgram DI", "register services", "same
  ViewModel after switching tabs", "retained DbContext", "singleton captures transient".
  DO NOT USE FOR: ASP.NET Core request DI, binding/property-name defects after
  successful injection (use maui-data-binding),
  Shell route/query design without a DI issue (use maui-shell-navigation), unit-test mocking frameworks (use standard xUnit
  and NSubstitute patterns).
license: MIT
---

# Dependency Injection in .NET MAUI

.NET MAUI uses the same `Microsoft.Extensions.DependencyInjection` container as ASP.NET Core. All service registration happens in `MauiProgram.CreateMauiApp()` on `builder.Services`. The container is built once at startup and is immutable thereafter.

## Advisory Fast Path

When the supplied code and lifecycle establish the cause, give the diagnosis and
one minimal implementation from the rules below. Do not browse the repository,
read reference files, or build an app just to restate these verified behaviors.
Use tools when editing code, checking an unresolved project/version fact, or when
the user requests verification. If an optional reference read fails, do not chase
paths for facts already covered here; disclose any uncertainty that matters.

## When Not to Use

- XAML binding/property-path defects with an already-correct ViewModel and BindingContext, data-binding syntax or compiled bindings — use **maui-data-binding**, not a DI investigation
- Shell route/query design without a DI issue — use the **maui-shell-navigation** skill
- ASP.NET Core request-scoped DI — ordinary MAUI's missing scope boundary does not apply
- Mocking frameworks or test runners — use standard .NET testing tools (xUnit, NUnit, MSTest) and mocking libraries (NSubstitute, Moq)

## Rules That Change the Answer

| Situation | Do this | Why |
|---|---|---|
| Registering a Page or ViewModel | Prefer `AddTransient` for independently created detail pages | Transient means fresh **per resolution**, not per tab selection. Shell can cache a root page. A Singleton is defensible for intentionally shared root state; do not attach the same Page instance to multiple parents/windows |
| Registering shared/expensive state | `AddSingleton` | One instance app-wide (settings, DB connection, `HttpClient` handler) |
| Shared cache also needs HTTP | Keep cache ownership separate from short-lived client creation | Do not register the same intended service as both Singleton and a typed HTTP client; see the named-client example in the API reference |
| Tempted to use `AddScoped` | Decide who creates, resolves from, and disposes the scope | Ordinary non-Blazor MAUI has **no automatic DI scope per window or navigation**. Scoped instances resolved from the root are shared until root disposal (or resolution throws when scope validation is enabled) |
| App already owns explicit per-window scopes | Preserve the graph; audit root/template/route resolution and teardown | Shell uses its MAUI context's provider, not an arbitrary new scope. Avoid root/static resolvers; cancel and await outstanding work before disposing the window scope |
| Navigating to a detail page | Register its dependencies and `Routing.RegisterRoute`; optionally register the page to control lifetime | With a service provider, Shell's type route factory uses `ActivatorUtilities.GetServiceOrCreateInstance`: an unregistered page can still receive registered constructor dependencies |
| Typed Shell `ContentTemplate` | Keep constructor injection; check the MAUI version and available context | MAUI 10's `ShellContent` looks up the page in DI, then falls back to `ActivatorUtilities.CreateInstance`. It does **not** universally bypass DI. Root content is cached; refresh on activation rather than expecting transient registration to recreate it |
| Singleton captures a transient `DbContext` | Use `IDbContextFactory<TContext>` for EF-only work, or an owned operation scope for a complete dependency graph | Constructor injection resolves once. A root-resolving `Func<T>` is not a scope: it retains disposable transients and shares scoped collaborators. `AddDbContextFactory` also registers the context type; change the consumer too |
| Intentional single-window Singleton root Page | Leave it alone; state the ownership assumption briefly | If another window/parent later needs that Page, create separate Page instances; sharing a ViewModel is a separate state decision |
| Platform-specific implementation | `#if` per platform **with every platform covered** | A missing platform branch leaves the service unregistered and throws at resolution time |

**Do not** introduce DI into a project that isn't using it, swap a working service
lifetime, or add an interface purely for symmetry — only when the user asked or it
fixes a real defect.

**Answer narrowly, but completely.** For a `DbContext` lifetime defect, explain
root-scope behavior and show a suitable registration/ownership change. Choose
`AddDbContextFactory` for context-only operations, or `CreateAsyncScope` and
resolve the complete worker graph from its provider when collaborators need
that boundary. Await the operation before disposing its context/scope; do not
return a scoped object or start fire-and-forget work that escapes it.
Transient alone does not ensure
operation-level freshness if a long-lived ViewModel retains the context, nor is
a `DbContext` safe for concurrent operations. With scope validation enabled,
root resolution of scoped services throws instead of silently reusing them.
Do not prescribe all alternatives for an unrelated registration question.
Load only the relevant section of [the API reference](references/dependency-injection-api.md)
when an ownership implementation or version-specific investigation needs it.

## Workflow

1. Discover the supplied graph, construction path, MAUI version and declared platforms; do not require a project for an advisory question.
2. Match lifetimes to ownership — shared services can be Singleton, independently created detail graphs usually Transient. Preserve intentional shared root state.
3. Register required constructor services and ViewModels in `MauiProgram.CreateMauiApp()` on `builder.Services`; register Pages when controlling their lifetime.
4. For pushed detail pages, register Shell routes in `AppShell.xaml.cs`. For root tabs/flyout content, retain the working `ContentTemplate`; do not convert it to a pushed route just to enable DI.
5. Wire each Page to its ViewModel via constructor injection, assigning the ViewModel as `BindingContext`.
6. Add platform-specific registrations with `#if` directives, ensuring every target platform is covered or has a fallback.
7. Supply usable code for a setup request: containing class/method, each requested Page constructor, and route registration/navigation when Shell details are involved. Preserve unrelated fonts/resources. For a no-op review give the verdict and one relevant ownership caveat, not a new scaffold.
8. Validate the actual construction path, dependency availability, and ownership. Run existing targeted tests/builds when changing code; report exactly what ran and any platform limitations. An advisory answer does not require a workload install or app build.

## Registration Pattern in MauiProgram.cs

```csharp
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<IDataService, DataService>();
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<DetailViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<DetailPage>();
        return builder.Build();
    }
}
```

Include only the services the requested graph needs. For typed/named HTTP clients,
`AddHttpClient` requires `Microsoft.Extensions.Http`; do not add it to unrelated setup.

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

### Confusing Template Creation Paths

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

### XAML Resource Parsing vs. DI Timing

The container already exists when DI constructs `App`, but `Application.Current`
and element handlers/MAUI contexts may not be ready during `InitializeComponent()`.
Do not resolve through those globals while parsing `App.xaml`. Inject dependencies;
when useful, defer creating the root Shell to `CreateWindow()`:

Register `AppShell`, resolve it from the injected provider in `CreateWindow()`,
and return `new Window(shell)`, or inject the Shell directly when its constructor
does not depend on unavailable app globals. Choose one straightforward path;
prefer an ordinary constructor for a XAML partial class, not speculative
overload chains or redundant constructor variants:

```csharp
// MauiProgram.CreateMauiApp(), before builder.Build()
builder.Services.AddTransient<AppShell>();
```

```csharp
using Microsoft.Extensions.DependencyInjection;

public partial class App : Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services)
    {
        this.services = services;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new Window(services.GetRequiredService<AppShell>());
}
```

Remove global/handler lookup from the XAML resource factory itself; fixing only
`CreateWindow` will not fix an earlier lookup. Window creation is not a guarantee
that a handler exists; genuinely handler-dependent work needs its attachment
lifecycle rather than a different DI container.

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
