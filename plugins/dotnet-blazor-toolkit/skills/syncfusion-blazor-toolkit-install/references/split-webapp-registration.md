# Split Blazor Web App Service Registration

When using a split Blazor Web App, register Toolkit services in every project that actually renders Toolkit components.

## Current template note

For `dotnet new blazor -int Auto` and `dotnet new blazor -int WebAssembly`, the generated Server project typically wires interactive server and WebAssembly support in `Program.cs`, while the `.Client` project is the browser-side app. Keep Toolkit registration in whichever project uses Toolkit components.

## Scenario 1: Only Server uses Toolkit

**Server/Program.cs**:
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSyncfusionBlazorToolkit();  // Server-side registration

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

**Client/Program.cs**:
```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");

// No Toolkit registration needed if Client doesn't use Toolkit
await builder.Build().RunAsync();
```

## Scenario 2: Only Client uses Toolkit

**Server/Program.cs**:
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
// No Toolkit registration; Server doesn't use it

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

**Client/Program.cs**:
```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");

builder.Services.AddSyncfusionBlazorToolkit();  // Client-side registration

await builder.Build().RunAsync();
```

## Scenario 3: Both Server and Client use Toolkit (MOST COMMON)

**Server/Program.cs**:
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddSyncfusionBlazorToolkit();  // Register here

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();
```

**Client/Program.cs**:
```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");

builder.Services.AddSyncfusionBlazorToolkit();  // Also register here

await builder.Build().RunAsync();
```

## Scenario 4: Auto render mode uses Toolkit on both sides

If you use `@rendermode InteractiveAuto`, keep Toolkit registration in both Server and Client when both halves render Toolkit components.

## Common Mistake: Forgetting Client Registration

If Toolkit components are used in the Client project but `AddSyncfusionBlazorToolkit()` is only called in Server `Program.cs`, the Client will not have access to Toolkit services and components will fail.

**Symptom**: Components work in Server-hosted components but fail in Client components.

**Solution**: Ensure both projects have the registration call if both use Toolkit.

## Theme CSS in Split Web App

Theme CSS should be linked in the shared `App.razor` host file so both Server and Client components can use it:

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
    <link rel="stylesheet" href="app.css" />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

This ensures the theme is available to both Server and Client components.
