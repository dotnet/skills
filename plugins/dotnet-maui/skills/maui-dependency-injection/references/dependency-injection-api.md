# Dependency Injection API Reference

## Service Registration in MauiProgram.cs

Register services on `builder.Services` inside `CreateMauiApp()`:

```csharp
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>();

    // Services
    builder.Services.AddSingleton<IDataService, DataService>();
    builder.Services.AddTransient<IApiClient, ApiClient>();

    // ViewModels
    builder.Services.AddTransient<MainViewModel>();
    builder.Services.AddTransient<DetailViewModel>();

    // Pages
    builder.Services.AddTransient<MainPage>();
    builder.Services.AddTransient<DetailPage>();

    return builder.Build();
}
```

## Lifetime Reference

| Lifetime | Use When | Examples |
|-----------|----------|----------|
| `AddSingleton<T>` | Shared state, expensive to create, or app-wide config | Database connection, settings service, HttpClient factory |
| `AddTransient<T>` | Stateless, lightweight, or per-request usage | ViewModels, pages, API call wrappers |
| `AddScoped<T>` | Per explicitly owned scope; ordinary MAUI creates no automatic window/navigation DI scope | Scoped unit-of-work in manually created scopes |

Root-provider resolution shares a scoped instance until the root is disposed,
unless `ValidateScopes` is enabled, in which case it throws. Neither `AddScoped`
nor creating a new MAUI context provides an operation boundary on its own.

## Constructor Injection

Inject dependencies through the constructor. The DI container resolves them automatically
when the type is itself resolved from the container:

```csharp
public class MainViewModel
{
    private readonly IDataService _dataService;
    private readonly IApiClient _apiClient;

    public MainViewModel(IDataService dataService, IApiClient apiClient)
    {
        _dataService = dataService;
        _apiClient = apiClient;
    }
}
```

## ViewModel → Page Pattern

Register both the ViewModel and the Page. Inject the ViewModel into the Page constructor
and assign it as `BindingContext`:

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

## Automatic Resolution via Shell Navigation

Shell type routes use `ActivatorUtilities.GetServiceOrCreateInstance` when a
service provider is available. Registered pages respect their DI lifetime;
unregistered pages can still receive registered constructor dependencies. Register
the page explicitly when you need to control its lifetime:

```csharp
// In MauiProgram.cs
builder.Services.AddTransient<DetailPage>();
builder.Services.AddTransient<DetailViewModel>();

// Route registration (AppShell.xaml.cs or startup)
Routing.RegisterRoute(nameof(DetailPage), typeof(DetailPage));

// Navigation — DI resolves DetailPage and its DetailViewModel
await Shell.Current.GoToAsync(nameof(DetailPage));
```

## Explicit Resolution

When constructor injection is not available, resolve services explicitly:

```csharp
// From any Element with a Handler
var service = this.Handler.MauiContext.Services.GetService<IDataService>();
```

### IServiceProvider Injection

Inject `IServiceProvider` when you need to resolve services dynamically:

```csharp
public class MyService
{
    private readonly IServiceProvider _serviceProvider;

    public MyService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void DoWork()
    {
        var api = _serviceProvider.GetRequiredService<IApiClient>();
    }
}
```

## Platform-Specific Service Registration

Use preprocessor directives to register platform-specific implementations:

```csharp
// In MauiProgram.cs
#if ANDROID
builder.Services.AddSingleton<INotificationService, AndroidNotificationService>();
#elif IOS || MACCATALYST
builder.Services.AddSingleton<INotificationService, AppleNotificationService>();
#elif WINDOWS
builder.Services.AddSingleton<INotificationService, WindowsNotificationService>();
#else
builder.Services.AddSingleton<INotificationService, UnsupportedNotificationService>();
#endif
```

Choose a no-op only when ignoring notifications is acceptable; otherwise make
unsupported-platform behavior explicit. A thrown exception from
`GetRequiredService` is different from `GetService` returning null.

## Interface-First Pattern for Testability

Define interfaces for services that need substitution, not mechanically for every type:

```csharp
public interface IDataService
{
    Task<List<Item>> GetItemsAsync();
}

public class DataService : IDataService
{
    public async Task<List<Item>> GetItemsAsync() { /* ... */ }
}

// Register the interface → implementation mapping
builder.Services.AddSingleton<IDataService, DataService>();
```

In tests, substitute a mock without touching production code:

```csharp
var services = new ServiceCollection();
services.AddSingleton<IDataService, FakeDataService>();
```

## Shell construction paths and version evidence

| Path | Behavior with a usable MAUI service provider |
|---|---|
| `ShellContent.ContentTemplate = new DataTemplate(typeof(DetailPage))`, including the typed XAML shorthand | `ShellContent` tries `GetService(template.Type)`, then `ActivatorUtilities.CreateInstance(services, template.Type)`. Constructor dependencies are injected even without a Page registration |
| `Routing.RegisterRoute(name, typeof(DetailPage))` | Type route factory uses `ActivatorUtilities.GetServiceOrCreateInstance`. Dependency registrations are required, Page registration is optional |
| `new DataTemplate(() => ...)` | The caller's factory owns creation. Shell cannot inject dependencies into an object already manually created by that factory |
| Direct `DataTemplate.CreateContent()` outside Shell | Does not get ShellContent's service-provider-aware template setup |
| `ShellContent.Content = existingPage` | Uses the supplied instance, not a new DI resolution |

For typed Shell content, the parent must have a reachable MAUI context. Early,
unattached construction is not the normal initialized Shell path. If creation
fails, inspect the actual version, handler/context, template type vs factory,
constructor signature, and missing dependency registrations before recommending
a change. Missing required constructor services fail resolution; they do not
silently become null. Do not replace constructor injection with a parameterless
service locator as a blanket fix.

ShellContent retains its `ContentCache`, so repeated selection of the same root
tab can reuse the page and its ViewModel even if both were registered transient.
Transient means a new instance when resolved, not on every navigation event.
MAUI can clear the cache when content disconnects/unloads; avoid promising it is
kept forever. Refresh data on an appropriate activation event or explicitly reset
state if that is the user's requirement.

An intentional Singleton root Page for one window is valid. Its registration
is app-wide, not per-window: don't resolve that same visual instance for another
window or parent. Use separate Page instances if that requirement changes.
A shared ViewModel can remain Singleton only if sharing its state is intentional.

### Evidence and reproduction

The repository-only reproduction commands below run against the **real Microsoft.Maui.Controls
packages 10.0.0 and 10.0.51**, with a platform-neutral `net10.0` console probe:

```bash
dotnet run --project tests/dotnet-maui/maui-dependency-injection/validation/MauiDiProbe/MauiDiProbe.csproj --verbosity quiet
dotnet run --project tests/dotnet-maui/maui-dependency-injection/validation/MauiDiProbe/MauiDiProbe.csproj -p:MauiVersion=10.0.51 --verbosity quiet
```

The probe executes real `IShellContentController.GetOrCreateContent`,
`Routing.GetOrCreateContent`, `DataTemplate`, `MauiContext`, and DI code. A minimal
`IElementHandler` supplies the context without native UI; it does not implement
Shell, templates, or DI behavior. It checks registered/unregistered page
activation, dependency failures, factory/direct-template/existing-content creation, repeated content caching,
root-context sharing, explicit scope disposal, and scope validation failures.
It also probes real EF Core 10 `AddDbContextFactory` registrations, context
identity, captive dependency validation, and caller-owned factory disposal;
it checks root-resolved disposable transient retention, scoped route/template
provider selection, and awaiting worker completion before async scope disposal.
It distinguishes MAUI's static `Microsoft.Maui.Storage.Preferences` API from
a concrete application settings service registered as Singleton.
It does not connect to a database or test database queries.
This is **not** a device/UI-navigation or window-lifecycle test. The lack of an
automatic per-window DI scope is grounded in the official scope contract below,
not inferred from simulating windows.

Source cross-checks (2026-10-07):

- [MAUI 10.0.0 ShellContent.cs](https://github.com/dotnet/maui/blob/10.0.0/src/Controls/src/Core/Shell/ShellContent.cs)
- [MAUI 10.0.0 Routing.cs](https://github.com/dotnet/maui/blob/10.0.0/src/Controls/src/Core/Routing.cs)
- [MAUI 9.0.0 ShellContent.cs](https://github.com/dotnet/maui/blob/9.0.0/src/Controls/src/Core/Shell/ShellContent.cs)
  has the same typed-template activation sequence (historical source check, not
  runtime certification for unsupported MAUI 9).
- [Development ShellContent.cs](https://github.com/dotnet/maui/blob/main/src/Controls/src/Core/Shell/ShellContent.cs)
  also uses the sequence at inspection time; `main` is not a supported-version
  guarantee. Recheck the installed version when troubleshooting.
- [Official MAUI 10 DI lifetime contract](https://learn.microsoft.com/dotnet/maui/fundamentals/dependency-injection?view=net-maui-10.0#dependency-lifetime):
  ordinary non-Blazor MAUI has no natural scope boundary. BlazorWebView services
  have separate host semantics; do not generalize this guidance to them.

## Short-lived database work and explicit scope ownership

Choose the operation boundary, not just a registration label:

- **Factory:** `AddDbContextFactory<MyDbContext>(options => ...)`; inject
  `IDbContextFactory<MyDbContext>`, create one context per unit of work, await it,
  then dispose it. Each concurrent operation needs its own context.
- **Explicit scope:** retain scoped registrations, inject `IServiceScopeFactory`,
  and resolve the *entire operation graph* from the owned scope. For async
  disposables use `CreateAsyncScope()` and `await using`.
- **Transient context:** can fit a short-lived owner that disposes it, but a
  Singleton or cached root ViewModel capturing a transient context still retains
  that same context. Changing `AddScoped` to `AddTransient` alone may not fix it.

A Singleton `Func<T>` that closes over the root provider only repeats root
resolution. The root tracks disposable transient objects until root teardown,
even when callers dispose them early, and scoped collaborators remain
root-scoped (or resolution throws with scope validation). Prefer an EF context
factory when only the context needs isolation; use an owned operation scope
when the worker has a wider scoped/disposable graph. These are different
ownership contracts, not interchangeable factory spellings.

**Registration is not a consumer rewrite.** With the default Singleton factory,
`AddDbContextFactory<TContext>` also registers `TContext` as Scoped for convenient
direct resolution. It does not remove existing registrations or make direct
context injection impossible. Change long-lived consumers to accept the factory,
not `TContext`. Factory-created contexts are not tracked for disposal by the
service provider; their caller owns them. Preserve existing database options and
provider configuration when changing registration.

```csharp
public sealed class UploadService(IDbContextFactory<MyDbContext> factory)
{
    public async Task UploadAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        // Perform this operation's database work using only this context.
        await db.SaveChangesAsync(cancellationToken);
    }
}
```

See the [official EF Core factory registration contract](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dependencyinjection.entityframeworkservicecollectionextensions.adddbcontextfactory?view=efcore-10.0).

```csharp
public sealed class SyncService(IServiceScopeFactory scopeFactory)
{
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<SyncWorker>();
        await worker.RunAsync(cancellationToken);
    }
}
```

Do not return a scoped dependency or start unawaited work that outlives this
scope.

### Auditing an existing per-window scope

Keep a working window-owned scope. Check the actual graph end-to-end:

- Resolve the window's Shell/root Page and ViewModels from `scope.ServiceProvider`.
  A root `IServiceProvider` injected into a Singleton, static/global resolver, or
  cached factory can bypass it even when the window itself owns a scope.
- Typed root templates and registered type routes use the provider reachable
  through their MAUI context. Creating a scope does not attach it to Shell.
  Check that navigation actually reaches the window's provider, or use an
  explicit window-aware page factory/navigation owner that resolves from it.
  A global route factory must not capture one window's scope for other windows.
- Resolve the session twice within one window and assert identity; across
  window scopes assert different instances. Include root content and pushed
  pages, not just `CreateWindow`.
- At the app's real window teardown boundary, stop new work, cancel/await
  outstanding operations and async cleanup, then dispose the owned scope.
  Merely starting asynchronous cleanup from a closing event and immediately
  disposing the scope races the work. Don't let another window retain that
  session; async disposables require awaited scope disposal.

## Startup resolution

When DI constructs `App(IServiceProvider services)`, the provider has already
been built. What may be unavailable in `InitializeComponent()` is
`Application.Current`, a handler, or a handler's MAUI context. Use the injected
provider rather than these globals. An injected Shell or a root Shell resolved
from that provider in `CreateWindow()` are valid alternatives; deferring every
service access until window creation is not a universal requirement.
