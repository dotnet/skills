# Split Blazor Web App Service Registration

> **Framework support.** `Syncfusion.Blazor.Toolkit` 1.0.2 targets `net8.0`, `net9.0`, and `net10.0`. Always check the `<TargetFramework>` of **both** projects before installation. On `net6.0`, `net7.0`, or older frameworks, the package may restore successfully, but Toolkit assemblies are not available to the application, so the build later fails with errors such as `CS0246: The type or namespace name 'Syncfusion' could not be found`. Do not proceed with Toolkit installation on unsupported frameworks. Recommend upgrading the application to .NET 8 or later first, then continue with the installation steps.

A generated `.Client` project is not a standalone WebAssembly app. The server owns `App.razor`, and the host markup has no `#app` element. Never call `RootComponents.Add<App>("#app")` in `.Client/Program.cs`.

## Both hosts must register Toolkit

Default prerendering runs WebAssembly and Auto components on the server first. Verified: with `-int WebAssembly`, registering Toolkit **only** in `.Client` makes a `.Client` page return HTTP 500; adding the server registration returns 200.

```text
System.InvalidOperationException: Cannot provide a value for property 'SyncfusionService' on type
'Syncfusion.Blazor.Toolkit.Buttons.SfButton'. There is no registered service of type
'Syncfusion.Blazor.Toolkit.SyncfusionBlazorToolkitService'.
```

The error is the same for any host that renders a Toolkit component without the registration, including static SSR pages. Disable prerendering only if the user asks; otherwise register on both hosts.

## Make only the additions; keep the template

Each template generates its own endpoint calls. **Do not retype `Program.cs`.** A trimmed copy drops `using <App>.Components;` (`CS0246` on `App`) and loses `UseExceptionHandler`, `UseHsts`, `UseHttpsRedirection`, the static-asset mapping (`MapStaticAssets` on .NET 9+, `UseStaticFiles` on .NET 8) and `app.Run()`.

| Template | Already generated on the server (leave alone) |
| --- | --- |
| `-int Server` (no `.Client` project) | `AddRazorComponents().AddInteractiveServerComponents()`; `MapRazorComponents<App>().AddInteractiveServerRenderMode()` |
| `-int WebAssembly` | `AddRazorComponents().AddInteractiveWebAssemblyComponents()`; `MapRazorComponents<App>().AddInteractiveWebAssemblyRenderMode().AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` |
| `-int Auto` | `AddRazorComponents().AddInteractiveServerComponents().AddInteractiveWebAssemblyComponents()`; `MapRazorComponents<App>().AddInteractiveServerRenderMode().AddInteractiveWebAssemblyRenderMode().AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` |

`AddAdditionalAssemblies(...)` sits on `MapRazorComponents<App>()`, not on `AddRazorComponents()`, and it is not a Toolkit setting. Never add, remove, or move it for Toolkit. The three setups are different templates; do not mix them. Adding `AddInteractiveServerComponents()` to a WebAssembly template, or removing it from an Auto template, changes the app's render-mode support and is not needed for Toolkit.

## Server `Program.cs` (all three templates)

Two additions:

```csharp
using Syncfusion.Blazor.Toolkit;                       // add at the top

// ...template code unchanged...

builder.Services.AddSyncfusionBlazorToolkit();         // add before builder.Build()

var app = builder.Build();
```

## `.Client/Program.cs` (`-int WebAssembly` and `-int Auto` only)

The template already has `using Microsoft.AspNetCore.Components.WebAssembly.Hosting;`. Two additions:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;   // already generated
using Syncfusion.Blazor.Toolkit;                              // add

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSyncfusionBlazorToolkit();                // add

await builder.Build().RunAsync();
```

## Interactive Auto behavior

The first visit runs on the server; later visits use the cached WebAssembly bundle. A running component does not switch runtimes. Both hosts therefore need the registration.

## Common Mistake: Registering only one host

**Symptom**: HTTP 500 with the `SyncfusionBlazorToolkitService` error above, either on first load (server missing) or when a `.Client` component runs in the browser (client missing).

**Fix**: Register on every host that renders it, keep the template's endpoints, and never add `RootComponents.Add<App>("#app")` to `.Client`.

## Component namespaces in both projects

Add the child namespace (for example `@using Syncfusion.Blazor.Toolkit.Buttons`) to the `_Imports.razor` of every project that renders that component. A missing namespace only warns (`RZ10012`) and renders an unstyled unknown element.

## Theme CSS in a Split Web App

Add the stylesheet link to the existing `<head>` in `Components/App.razor`; do not replace the file. Both Server and Client components use it. Fluent is the default; `highcontrast.min.css` is also valid.

```html
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```
