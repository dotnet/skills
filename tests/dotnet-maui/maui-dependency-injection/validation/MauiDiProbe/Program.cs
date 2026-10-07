using Microsoft.Extensions.DependencyInjection;
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
