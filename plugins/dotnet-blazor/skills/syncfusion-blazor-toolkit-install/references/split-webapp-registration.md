# Split Blazor Web App Service Registration

A generated `.Client` project is not a standalone WebAssembly app. The server owns `App.razor`, and the host markup has no `#app` element. Never call `RootComponents.Add<App>("#app")` in `.Client/Program.cs`.

Default prerendering renders Interactive WebAssembly and Interactive Auto components on the server first. If the server does not have Toolkit services, that first render fails even when `.Client` is registered correctly. Disable prerendering only when the user explicitly asks; otherwise register Toolkit on both hosts.

`dotnet new blazor -int Auto` and `dotnet new blazor -int WebAssembly` already add interactive server and WebAssembly endpoints on the server. Keep those endpoints. A server-only Interactive Server app should not gain WebAssembly endpoints just to host Toolkit.

## Scenario 1: Interactive Server only

The server does not host Client components, so leave WebAssembly endpoints off. Do not add a `.Client` registration for components that project never renders, and do not add `RootComponents.Add<App>("#app")`.

**Server/Program.cs**:
```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSyncfusionBlazorToolkit();

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

## Scenario 2: Client components use Toolkit

This is not "Client only" while prerendering is on. Keep the generated WebAssembly endpoints, and register Toolkit on the server as well.

**Server/Program.cs**:
```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddSyncfusionBlazorToolkit();

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();
```

**.Client/Program.cs**:
```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSyncfusionBlazorToolkit();

await builder.Build().RunAsync();
```

## Scenario 3: Interactive Auto

Use the same server and client registration as Scenario 2. Interactive Auto needs both, because the first visit renders on the server and later cached visits render in WebAssembly. Do not add `RootComponents.Add<App>("#app")` to `.Client`.

## Common Mistake: Registering only one host

Calling `AddSyncfusionBlazorToolkit()` only in the server `Program.cs` does not register it for `.Client`. The reverse is also wrong while prerendering is on.

**Symptom**: Server-hosted components work, but Client components fail on first load or after the WASM runtime takes over.

**Fix**: Register Toolkit in every host that renders it, keep the template's WebAssembly endpoints, and never add `RootComponents.Add<App>("#app")` to `.Client`.

## Theme CSS in a Split Web App

Link the stylesheet in `Components/App.razor` so both Server and Client components can use it. Fluent is the default; `highcontrast.min.css` is also valid.

```razor
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```
