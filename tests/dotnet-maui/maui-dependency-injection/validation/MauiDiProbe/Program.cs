using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

ProbeTemplate(registerPage: true);
ProbeTemplate(registerPage: false);
ProbeRoute(registerPage: true);
ProbeRoute(registerPage: false);
ProbeMissingDependency();
ProbeFactoryTemplate();
ProbeDirectTemplate();
ProbeExistingContent();
ProbeLifetimes();
ProbeMainGraphLifetimes();
ProbePreferencesTypes();
await ProbeDbContextFactory();
ProbeScopedShellProviders();
ProbeRootResolvingFactory();
await ProbeAsyncOperationScope();
Console.WriteLine("PASS: real MAUI Controls template, route, cache, failure and DI lifetime probes");

static ServiceProvider Services(bool registerPage)
{
    var services = new ServiceCollection().AddSingleton<DataService>();
    if (registerPage)
        services.AddTransient<InjectedPage>();
    return services.BuildServiceProvider();
}

static ShellContent Content(IServiceProvider services, DataTemplate template)
{
    var section = new ShellSection();
    var handler = new ContextHandler(new MauiContext(services));
    section.Handler = handler;
    var content = new ShellContent { ContentTemplate = template };
    section.Items.Add(content);
    return content;
}

static void ProbeTemplate(bool registerPage)
{
    using var services = Services(registerPage);
    var content = Content(services, new DataTemplate(typeof(InjectedPage)));
    var controller = (IShellContentController)content;
    var page = (InjectedPage)controller.GetOrCreateContent();
    Require(ReferenceEquals(page.Data, services.GetRequiredService<DataService>()),
        $"typed template injection (registered={registerPage})");
    Require(ReferenceEquals(page, controller.GetOrCreateContent()), "ShellContent cache");
}

static void ProbeRoute(bool registerPage)
{
    using var services = Services(registerPage);
    var route = $"probe-{registerPage}";
    Routing.RegisterRoute(route, typeof(InjectedPage));
    try
    {
        var page = (InjectedPage)Routing.GetOrCreateContent(route, services);
        Require(ReferenceEquals(page.Data, services.GetRequiredService<DataService>()),
            $"route injection (registered={registerPage})");
        Require(!ReferenceEquals(page, Routing.GetOrCreateContent(route, services)),
            "transient route resolution");
    }
    finally
    {
        Routing.UnRegisterRoute(route);
    }
}

static void ProbeMissingDependency()
{
    using var services = new ServiceCollection().BuildServiceProvider();
    var content = Content(services, new DataTemplate(typeof(InjectedPage)));
    RequireThrows<InvalidOperationException>(
        () => ((IShellContentController)content).GetOrCreateContent(),
        "missing typed-template dependency");
    const string route = "probe-missing";
    Routing.RegisterRoute(route, typeof(InjectedPage));
    try
    {
        RequireThrows<InvalidOperationException>(
            () => Routing.GetOrCreateContent(route, services), "missing route dependency");
    }
    finally
    {
        Routing.UnRegisterRoute(route);
    }
}

static void ProbeFactoryTemplate()
{
    using var services = Services(registerPage: false);
    var supplied = new InjectedPage(services.GetRequiredService<DataService>());
    var calls = 0;
    var content = Content(services, new DataTemplate(() => { calls++; return supplied; }));
    Require(ReferenceEquals(supplied, ((IShellContentController)content).GetOrCreateContent()),
        "factory controls page creation");
    ((IShellContentController)content).GetOrCreateContent();
    Require(calls == 1, "factory result cached");
}

static void ProbeDirectTemplate()
{
    RequireThrows<MissingMethodException>(
        () => new DataTemplate(typeof(InjectedPage)).CreateContent(),
        "direct typed template has no Shell DI activation");
}

static void ProbeExistingContent()
{
    var page = new InjectedPage(new DataService());
    var content = new ShellContent { Content = page };
    Require(ReferenceEquals(page, ((IShellContentController)content).GetOrCreateContent()),
        "existing content is not re-resolved");
}

static void ProbeLifetimes()
{
    using var services = new ServiceCollection()
        .AddScoped<DisposableWork>()
        .BuildServiceProvider();
    var rootWork = services.GetRequiredService<DisposableWork>();
    var firstContext = new MauiContext(services);
    var secondContext = new MauiContext(services);
    Require(ReferenceEquals(rootWork, firstContext.Services.GetRequiredService<DisposableWork>()),
        "context one uses supplied root");
    Require(ReferenceEquals(rootWork, secondContext.Services.GetRequiredService<DisposableWork>()),
        "context two does not create DI scope");
    DisposableWork scopedWork;
    using (var scope = services.CreateScope())
    {
        scopedWork = scope.ServiceProvider.GetRequiredService<DisposableWork>();
        Require(!ReferenceEquals(rootWork, scopedWork), "explicit scope separates instances");
        Require(ReferenceEquals(scopedWork,
            scope.ServiceProvider.GetRequiredService<DisposableWork>()), "same scope shares instance");
    }
    Require(scopedWork.Disposed && !rootWork.Disposed, "explicit scope owns disposal");
    using var validating = new ServiceCollection().AddScoped<DisposableWork>()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    RequireThrows<InvalidOperationException>(
        () => validating.GetRequiredService<DisposableWork>(), "validated root resolution");
}

static async Task ProbeDbContextFactory()
{
    var registrations = new ServiceCollection();
    registrations.AddDbContextFactory<ProbeContext>();
    Require(registrations.Single(s => s.ServiceType == typeof(ProbeContext)).Lifetime
        == ServiceLifetime.Scoped, "factory also registers context as scoped");
    ProbeContext first;
    ProbeContext second;
    using (var services = registrations.BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true }))
    {
        RequireThrows<InvalidOperationException>(
            () => services.GetRequiredService<ProbeContext>(), "factory direct root context validation");
        var factory = services.GetRequiredService<IDbContextFactory<ProbeContext>>();
        first = await factory.CreateDbContextAsync();
        second = await factory.CreateDbContextAsync();
        Require(!ReferenceEquals(first, second), "factory creates independent operation contexts");
        ProbeContext direct;
        using (var scope = services.CreateScope())
        {
            direct = scope.ServiceProvider.GetRequiredService<ProbeContext>();
            Require(ReferenceEquals(direct, scope.ServiceProvider.GetRequiredService<ProbeContext>()),
                "direct context resolution remains scoped");
        }
        Require(direct.Disposed, "scope owns direct context disposal");
    }
    Require(!first.Disposed && !second.Disposed, "provider does not own factory context disposal");
    await first.DisposeAsync();
    await second.DisposeAsync();
    Require(first.Disposed && second.Disposed, "caller disposes factory contexts");

    var captive = new ServiceCollection();
    captive.AddDbContextFactory<ProbeContext>();
    captive.AddSingleton<CapturingWorker>();
    using var validating = captive.BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true });
    RequireThrows<InvalidOperationException>(
        () => validating.GetRequiredService<CapturingWorker>(), "factory does not repair direct singleton capture");

    var existing = new ServiceCollection().AddTransient<ProbeContext>();
    existing.AddDbContextFactory<ProbeContext>();
    Require(existing.Single(s => s.ServiceType == typeof(ProbeContext)).Lifetime
        == ServiceLifetime.Transient, "factory preserves existing context registration");
    existing.AddSingleton<CapturingWorker>();
    using var root = existing.BuildServiceProvider();
    var worker = root.GetRequiredService<CapturingWorker>();
    Require(ReferenceEquals(worker.Context,
        root.GetRequiredService<CapturingWorker>().Context),
        "singleton keeps constructor-injected transient across worker resolutions");
    Require(!ReferenceEquals(worker.Context, root.GetRequiredService<ProbeContext>()),
        "fresh direct transient resolution does not replace singleton's context");
}

static void ProbeMainGraphLifetimes()
{
    foreach (var lifetime in new[] { ServiceLifetime.Singleton, ServiceLifetime.Transient })
    {
        var registrations = new ServiceCollection().AddSingleton<DataService>()
            .AddTransient<DetailGraph>();
        registrations.Add(new ServiceDescriptor(typeof(MainGraph), typeof(MainGraph), lifetime));
        using var root = registrations.BuildServiceProvider();
        var main = root.GetRequiredService<MainGraph>();
        var first = root.GetRequiredService<DetailGraph>();
        var second = root.GetRequiredService<DetailGraph>();
        Require(ReferenceEquals(main, root.GetRequiredService<MainGraph>())
            == (lifetime == ServiceLifetime.Singleton), $"main graph lifetime {lifetime}");
        Require(ReferenceEquals(main.Data, first.Data) && ReferenceEquals(first.Data, second.Data),
            $"shared data service with {lifetime} main graph");
        first.EditText = "first draft";
        Require(!ReferenceEquals(first, second) && second.EditText.Length == 0,
            $"independent detail edit state with {lifetime} main graph");
    }
}

static void ProbePreferencesTypes()
{
    var frameworkType = typeof(Microsoft.Maui.Storage.Preferences);
    Require(frameworkType.IsAbstract && frameworkType.IsSealed,
        "framework Preferences is static, not an app service class");
    using var services = new ServiceCollection().AddSingleton<AppPreferences>()
        .BuildServiceProvider();
    Require(ReferenceEquals(services.GetRequiredService<AppPreferences>(),
        services.GetRequiredService<AppPreferences>()), "concrete AppPreferences singleton is valid");
}

static void ProbeScopedShellProviders()
{
    using var root = new ServiceCollection().AddScoped<DisposableWork>()
        .BuildServiceProvider();
    using var windowScope = root.CreateScope();
    using var otherScope = root.CreateScope();
    var expected = windowScope.ServiceProvider.GetRequiredService<DisposableWork>();
    Require(!ReferenceEquals(expected, otherScope.ServiceProvider.GetRequiredService<DisposableWork>()),
        "separate owned scopes isolate session");
    var rootTemplate = Content(root, new DataTemplate(typeof(ScopedPage)));
    var scopedTemplate = Content(windowScope.ServiceProvider, new DataTemplate(typeof(ScopedPage)));
    Require(!ReferenceEquals(expected,
        ((ScopedPage)((IShellContentController)rootTemplate).GetOrCreateContent()).Work),
        "root template provider bypasses existing scope");
    Require(ReferenceEquals(expected,
        ((ScopedPage)((IShellContentController)scopedTemplate).GetOrCreateContent()).Work),
        "scoped template provider supplies scoped session");
    const string route = "probe-scoped-route";
    Routing.RegisterRoute(route, typeof(ScopedPage));
    try
    {
        Require(!ReferenceEquals(expected,
            ((ScopedPage)Routing.GetOrCreateContent(route, root)).Work),
            "root route provider bypasses existing scope");
        Require(ReferenceEquals(expected,
            ((ScopedPage)Routing.GetOrCreateContent(route, windowScope.ServiceProvider)).Work),
            "scoped route provider supplies scoped session");
    }
    finally
    {
        Routing.UnRegisterRoute(route);
    }
}

static void ProbeRootResolvingFactory()
{
    var registrations = new ServiceCollection().AddTransient<ProbeContext>();
    registrations.AddDbContextFactory<ProbeContext>();
    registrations.AddSingleton<Func<ProbeContext>>(sp => () => sp.GetRequiredService<ProbeContext>());
    var root = registrations.BuildServiceProvider();
    ProbeContext context;
    try
    {
        context = root.GetRequiredService<Func<ProbeContext>>()();
        context.Dispose();
        Require(context.DisposeCount == 1, "caller disposes root-factory context");
    }
    finally
    {
        root.Dispose();
    }
    Require(context.DisposeCount == 2, "root retains disposable transient until teardown");

    using var validating = new ServiceCollection().AddScoped<DisposableWork>()
        .AddTransient<ScopedPage>()
        .AddSingleton<Func<ScopedPage>>(sp => () => sp.GetRequiredService<ScopedPage>())
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    RequireThrows<InvalidOperationException>(
        () => validating.GetRequiredService<Func<ScopedPage>>()(),
        "root factory does not isolate scoped collaborators");
}

static async Task ProbeAsyncOperationScope()
{
    var registrations = new ServiceCollection().AddScoped<OperationWorker>();
    registrations.AddDbContextFactory<ProbeContext>();
    using var root = registrations.BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true });
    OperationWorker worker;
    await using (var operation = root.CreateAsyncScope())
    {
        worker = operation.ServiceProvider.GetRequiredService<OperationWorker>();
        Require(!worker.Context.Disposed, "operation context alive before work");
        await worker.RunAsync();
        Require(worker.Completed && !worker.Context.Disposed,
            "awaited worker finishes before scope teardown");
    }
    Require(worker.Context.Disposed, "async operation scope owns graph disposal");
}

static void Require(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}");
}

static void RequireThrows<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Console.WriteLine($"PASS: {name} throws {typeof(T).Name}"); return; }
    throw new InvalidOperationException($"FAIL: {name} did not throw {typeof(T).Name}");
}

public sealed class DataService;
public sealed class MainGraph(DataService data)
{
    public DataService Data { get; } = data;
}
public sealed class DetailGraph(DataService data)
{
    public DataService Data { get; } = data;
    public string EditText { get; set; } = "";
}
public sealed class AppPreferences;
public sealed class CapturingWorker(ProbeContext context)
{
    public ProbeContext Context { get; } = context;
}
public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    public bool Disposed { get; private set; }
    public int DisposeCount { get; private set; }
    public override void Dispose()
    {
        Disposed = true;
        DisposeCount++;
        base.Dispose();
    }
    public override ValueTask DisposeAsync()
    {
        Disposed = true;
        DisposeCount++;
        return base.DisposeAsync();
    }
}
public sealed class OperationWorker(ProbeContext context)
{
    public ProbeContext Context { get; } = context;
    public bool Completed { get; private set; }
    public async Task RunAsync()
    {
        await Task.Yield();
        if (Context.Disposed)
            throw new InvalidOperationException("Operation context disposed before completion");
        Completed = true;
    }
}
public sealed class ScopedPage(DisposableWork work) : ContentPage
{
    public DisposableWork Work { get; } = work;
}
public sealed class InjectedPage(DataService data) : ContentPage
{
    public DataService Data { get; } = data;
}
public sealed class DisposableWork : IDisposable
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

// Supplies the real MAUI context without creating native UI or replacing Controls behavior.
sealed class ContextHandler(IMauiContext context) : IElementHandler
{
    public IMauiContext MauiContext { get; private set; } = context;
    public IElement? VirtualView { get; private set; }
    public object PlatformView { get; } = new();
    public void SetMauiContext(IMauiContext value) => MauiContext = value;
    public void SetVirtualView(IElement view) => VirtualView = view;
    public void UpdateValue(string property) { }
    public void Invoke(string command, object? args = null) { }
    public void DisconnectHandler() => VirtualView = null;
}
