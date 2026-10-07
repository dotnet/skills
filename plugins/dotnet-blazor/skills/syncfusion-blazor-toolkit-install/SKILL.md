---
license: MIT
name: syncfusion-blazor-toolkit-install
description: >
  Install and register the open-source Syncfusion Blazor Toolkit package,
  configure AddSyncfusionBlazorToolkit(), link the Fluent or High Contrast
  stylesheet, and apply the interactive render mode that matches the app
  topology (legacy Server, standalone WASM, or a Blazor Web App).

  USE FOR: Toolkit setup, package verification, stylesheet linking, split-app
  registration, render-mode troubleshooting, and diagnosing an accidental
  commercial Syncfusion.Blazor install or license-key prompt when the user
  wanted the open-source Toolkit.
  DO NOT USE FOR: component API details (use author-component), Blazor project
  creation (use create-blazor-project), configuring commercial Syncfusion
  components that the app actually uses, Hybrid/MAUI.
---

# Install Syncfusion Blazor Toolkit

## Core Rules

1. **Verify the target framework first.** `Syncfusion.Blazor.Toolkit` 1.0.2 targets `net8.0`, `net9.0`, and `net10.0`. Always check the project's `<TargetFramework>` before installation. On `net6.0`, `net7.0`, or older frameworks, the package may restore successfully, but Toolkit assemblies are not available to the application, so the build later fails with errors such as `CS0246: The type or namespace name 'Syncfusion' could not be found`. Do not proceed with Toolkit installation on unsupported frameworks. Recommend upgrading the application to .NET 8 or later first (for example with the `dotnet-upgrade` plugin), then continue with the installation steps. If the `<TargetFramework>` is unknown, read it before doing anything else. Never edit the TFM just to make the package install.
2. Use the exact package ID `Syncfusion.Blazor.Toolkit`.
3. In every `Program.cs` that calls `AddSyncfusionBlazorToolkit()`, add `using Syncfusion.Blazor.Toolkit;` so the extension method is in scope. Call it before `builder.Build()`.
4. Component types live in child namespaces, not the root. Add the namespace for each component to `_Imports.razor` (for example `@using Syncfusion.Blazor.Toolkit.Buttons` for `SfButton`). A missing namespace does not fail the build: Razor warns `RZ10012` and renders the tag as an unknown HTML element. The root `@using Syncfusion.Blazor.Toolkit` is optional and only needed to name its enums. `_Imports.razor` does not affect `Program.cs`.
5. Link `_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css` (or `highcontrast.min.css`) in the app host file. Fluent is the default. Add the link to the host file's `<head>`; do not replace the whole host file.
6. Use `@rendermode` only in a Blazor Web App. Legacy Blazor Server and standalone WebAssembly are already interactive and do not use render modes. In a Web App, **`-int Server`, `-int WebAssembly` and `-int Auto` are per-page**: `<Routes />` has no directive and only pages with their own `@rendermode` are interactive. Only `-ai` (`--all-interactive`) is global. Any Toolkit component that uses events, bindings, dialogs, calendar navigation, or button clicks must sit under an interactive mode (a page directive in a per-page app, or the inherited `Routes` mode in a global app). Never leave it on a static page.
7. Toolkit components need `AddSyncfusionBlazorToolkit()` on **every host that renders them, including static SSR**. Without it the page returns HTTP 500 with `There is no registered service of type 'Syncfusion.Blazor.Toolkit.SyncfusionBlazorToolkitService'`. In a split Blazor Web App, register on the server and in `.Client`: prerendering runs `.Client` components on the server, so client-only registration still returns 500.
8. In a split Blazor Web App, the `.Client` project is not a standalone WASM app. Do not call `RootComponents.Add<App>("#app")` there. Keep the `.AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` call the template puts on `MapRazorComponents<App>()`; the server needs it to see `.Client` pages. It is unrelated to Toolkit, so never add, remove, or move it.
9. Do not add a commercial `Syncfusion.Blazor*` package to satisfy a Toolkit request. If the app already uses commercial components, leave those packages and `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(...)` in place.
10. Toolkit JavaScript is embedded in the NuGet package; do not add commercial script tags.
11. Keep this skill install-focused; use the references for details and troubleshooting.

## Don'ts

- Don't install the Toolkit on a .NET 6 or .NET 7 project, or when the `<TargetFramework>` has not been checked. Recommend upgrading to .NET 8 or later first.
- Don't treat `Syncfusion.Blazor` or component-specific commercial packages as Toolkit dependencies.
- Don't add external script tags or commercial Syncfusion license scripts; Toolkit JS is bundled with the package.
- Don't rely on static SSR when the component must respond to clicks, binding, or dynamic updates.
- Don't use this skill as a component API reference; consult the official Syncfusion Blazor Toolkit demos or documentation for API details.
- Don't replace or retype `Program.cs`, `App.razor`, `_Host.cshtml`, or `wwwroot/index.html`. Make only the additions shown below and leave every other template line (`UseExceptionHandler`, `UseHsts`, `MapStaticAssets`, `HeadOutlet`, `Routes`, render-mode directives, script tags) untouched.

## Quick Decision Table

Pick the **single** row that matches the app the user is on. The "Program.cs" columns say where `AddSyncfusionBlazorToolkit()` goes; the template's own endpoint calls stay as generated.

| Scenario | Register in server `Program.cs` | Register in `.Client/Program.cs` | Host file for CSS | Where interactive Toolkit components go |
| --- | --- | --- | --- | --- |
| Legacy Blazor Server (`_Host.cshtml`, net8.0+ only for Toolkit) | Yes | n/a | `_Host.cshtml` | Anywhere; already interactive, no `@rendermode` |
| Standalone Blazor WebAssembly | n/a | Yes, in the standalone `Program.cs`; it keeps its own `RootComponents.Add<App>("#app")` | `wwwroot/index.html` | Anywhere; already interactive, no `@rendermode` |
| Web App `-int Server` (**per-page**) | Yes | n/a (no `.Client` project) | `Components/App.razor` | Pages with `@rendermode InteractiveServer`; pages without it are static SSR |
| Web App `-int Server -ai` (**global**) | Yes | n/a | `Components/App.razor` | Anywhere; inherited from `Routes`. Do not add a different mode |
| Web App `-int WebAssembly` (**per-page**) | Yes | Yes; **no** `RootComponents.Add` | `Components/App.razor` | Pages with `@rendermode InteractiveWebAssembly`, which **must** be in `.Client` (a server-project page prerenders, then fails in the browser) |
| Web App `-int WebAssembly -ai` (**global**) | Yes | Yes | `Components/App.razor` | Anywhere; inherited from `Routes` |
| Web App `-int Auto` (**per-page**) | Yes | Yes; **no** `RootComponents.Add` | `Components/App.razor` | Pages with `@rendermode InteractiveAuto`, which **must** be in `.Client` (a server-project Auto page prerenders, then fails in the browser). First visit server, later cached WebAssembly; a running component does not switch |
| Web App `-int Auto -ai` (**global**) | Yes | Yes | `Components/App.razor` | Anywhere; inherited from `Routes` |
| Web App `-int None` / static SSR page | Yes, if any Toolkit markup renders | n/a | `Components/App.razor` | Display-only Toolkit markup only; **never** components that use events, bindings, dialogs, or clicks |
| .NET MAUI Blazor Hybrid | **Out of scope** (see below) | n/a | n/a | n/a |

Verified with `dotnet new blazor`: without `-ai`, `<Routes />` carries no directive and only the sample `Counter.razor` has its own `@rendermode`. With `-ai`, `HeadOutlet` and `Routes` carry `@rendermode="Interactive..."`. See [Render modes explained](./references/render-modes.md).

## Minimal Setup

### Package

```bash
dotnet add package Syncfusion.Blazor.Toolkit
```

### Program.cs: make only these additions

Every template (`-int Server`, `-int WebAssembly`, `-int Auto`) already generates its own `AddRazorComponents()`, `Map*` and `AddAdditionalAssemblies(...)` calls. **Do not retype or replace them.** Pasting a trimmed `Program.cs` drops `using <App>.Components;` (so `App` stops resolving, `CS0246`) and removes `UseExceptionHandler`, `UseHsts`, `UseHttpsRedirection`, the static-asset mapping (`MapStaticAssets` on .NET 9+, `UseStaticFiles` on .NET 8) and `app.Run()`.

**Server `Program.cs`** (all three Web App templates and legacy Server). Add one `using` at the top and one line before `var app = builder.Build();`:

```csharp
using Syncfusion.Blazor.Toolkit;          // add at the top, with the other usings

// ...the template's builder.Services.AddRazorComponents()... stays exactly as generated...

builder.Services.AddSyncfusionBlazorToolkit();   // add just before builder.Build()

var app = builder.Build();
```

What the template already contains (leave it alone):

| Template | `AddRazorComponents()` chain | `MapRazorComponents<App>()` chain |
| --- | --- | --- |
| `-int Server` | `.AddInteractiveServerComponents()` | `.AddInteractiveServerRenderMode()` |
| `-int WebAssembly` | `.AddInteractiveWebAssemblyComponents()` | `.AddInteractiveWebAssemblyRenderMode()` and `.AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` |
| `-int Auto` | `.AddInteractiveServerComponents()` and `.AddInteractiveWebAssemblyComponents()` | `.AddInteractiveServerRenderMode()`, `.AddInteractiveWebAssemblyRenderMode()` and `.AddAdditionalAssemblies(...)` |

Never mix these shapes. Do not add `AddInteractiveServerComponents()` to a WebAssembly template, or remove it from an Auto template; Toolkit does not need either change.

**Blazor Web App `.Client/Program.cs`** (`-int WebAssembly`, `-int Auto`). The template already has `using Microsoft.AspNetCore.Components.WebAssembly.Hosting;`. Add the Toolkit using and one line before `await builder.Build().RunAsync();`:

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;   // already generated; keep it
using Syncfusion.Blazor.Toolkit;                              // add

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSyncfusionBlazorToolkit();                // add

await builder.Build().RunAsync();
```

This project is not a standalone WASM app. The server owns `App.razor` and the host page has no `#app` element, so do not register a root component here.

**Standalone Blazor WebAssembly `Program.cs`**. The template already contains the usings and both `RootComponents.Add` lines. Add only the Toolkit using and the registration:

```csharp
using Syncfusion.Blazor.Toolkit;                              // add

// ...template: builder.RootComponents.Add<App>("#app"); builder.RootComponents.Add<HeadOutlet>("head::after");...

builder.Services.AddSyncfusionBlazorToolkit();                // add, before Build()
```

### _Imports.razor

Add one `@using` per component family you render. In a split Web App add it to the `_Imports.razor` of **every project that renders the component** (server `Components/_Imports.razor` and `.Client/_Imports.razor`).

```razor
@using Syncfusion.Blazor.Toolkit.Buttons
@* Other families in 1.0.2: Calendars, Charts, Inputs, Popups (SfDialog, SfTooltip), Spinner *@
```

### Host file CSS

Add this single line to the `<head>` of the existing host file. **Do not replace the rest of the file**: keep `HeadOutlet`, `Routes` and its render-mode directive, and the existing script tag (`_framework/blazor.web.js`, `_framework/blazor.webassembly.js`, or `blazor.server.js`) exactly as the template generated them.

```html
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```

Pick the host file from the Quick Decision Table. Use `highcontrast.min.css` only when the user asks for high contrast. `SfNumericTextBox` additionally needs `numerictextbox.min.css`, because its selectors are not in the Fluent or High Contrast bundle. See [Theme and host files](./references/theme-and-host-files.md).

## Hybrid / MAUI boundary

This skill does not cover .NET MAUI Blazor Hybrid. When a user asks, explain the boundary instead of applying Web App steps. Verified against the `maui-blazor` template:

- There is no `App.razor`, `_Host.cshtml`, `HeadOutlet`, or render mode. The UI runs in a `BlazorWebView` declared in `MainPage.xaml`.
- The host page is `wwwroot/index.html`, with a `<div id="app">` root wired through `<RootComponent Selector="#app" ComponentType="{x:Type components:Routes}" />`. It loads `_framework/blazor.webview.js`.
- Services are registered in `MauiProgram.cs`, after `builder.Services.AddMauiBlazorWebView();`.
- The Toolkit's `net10.0` assets resolve for the platform TFMs `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst` and `net10.0-windows10.0.19041.0` (verified by restore against 1.0.2). `net9.0-android` and `net8.0-android` fail on the SDK itself (`NETSDK1147`, MAUI workload not installed), so Toolkit support there was not testable. Even so, this skill does not configure Hybrid apps: point the user to Hybrid-specific documentation. The same TFM check applies: a MAUI project on an unsupported .NET version needs an upgrade first.

## Common Mistakes

| Mistake | Symptom | Fix |
| --- | --- | --- |
| Toolkit installed on .NET 6, .NET 7, or older | The package may restore, but Toolkit assemblies are not available to the application, so the build fails with `CS0246: ... 'Syncfusion' could not be found` | Upgrade the app to .NET 8 or later first. Never change the TFM just to make the package install |
| `AddSyncfusionBlazorToolkit()` missing on a host that renders Toolkit | HTTP 500: `no registered service of type ...SyncfusionBlazorToolkitService` | Register on every rendering host, including the server for `.Client` components and for static SSR |
| CSS path uses `themes/` or omits `.min` | Components are unstyled (404 in browser DevTools) | Use `_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css` |
| CSS linked in a component file instead of the host file | Styling doesn't apply consistently | Move the `<link>` to the host file `<head>` |
| Host file replaced wholesale instead of adding the link | `HeadOutlet`, `Routes`, or the render-mode directive is lost; events stop firing | Restore the template-generated file, then add only the `<link>` line |
| `Program.cs` retyped from a trimmed sample | `CS0246` on `App`, and `UseExceptionHandler`, `UseHsts`, `MapStaticAssets` and `app.Run()` are lost | Restore the template file, then add only the `using` and the registration line |
| Template's `AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` deleted from `MapRazorComponents<App>()` | Routable pages in `.Client` return 404 | Restore the call exactly as the template generated it |
| Commercial package added for a Toolkit-only app | License prompt, missing Toolkit components | Replace that package with `Syncfusion.Blazor.Toolkit`. If other pages still use commercial components, keep those packages and `SyncfusionLicenseProvider.RegisterLicense(...)` |
| Component namespace missing | Razor warning `RZ10012`; `<SfButton>` renders as an unstyled unknown element that ignores events | Add the child namespace, for example `@using Syncfusion.Blazor.Toolkit.Buttons`, to `_Imports.razor` of the project that renders it |
| Added an external Syncfusion CDN script tag | JavaScript conflicts or duplicate library errors | Remove the CDN script; Toolkit JS ships in the NuGet package |
| Assumed `-int Server` is global, so an interactive Toolkit component has no `@rendermode` | Clicks, bindings, dialogs and calendar navigation do nothing on a Blazor Web App page | Check `Routes` in `App.razor`. In a per-page app add one `@rendermode` matching the template to the page; in a `-ai` app inherit the mode already on `Routes` |
| `@rendermode` added to standalone WASM or legacy Server | No effect, and it can look like a fix | Remove it; those apps are already interactive |
| Child `@rendermode` differs from the parent interactive mode | Runtime error; a child cannot switch interactive modes | Match the inherited mode, or move the component to a page with the intended mode |
| Auto or WebAssembly endpoint removed from a split Web App | Client components never load, or load only after a server round-trip | Keep the endpoints the template generated |
| `InteractiveWebAssembly` or `InteractiveAuto` page created in the **server** project | HTTP 200 and the component shows from the prerender, then the browser console reports `Root component type '...' could not be found in the assembly '<ServerApp>'` and nothing is interactive | Move the page and its components into the `.Client` project |
| `RootComponents.Add<App>("#app")` added to `.Client` | The Client host has no `#app` element | Remove it. That call belongs only in standalone WebAssembly |

## Top Failure Symptoms

- **Unstyled components**: the stylesheet link is missing, wrong, or in the wrong host file; or a component namespace is missing (`RZ10012`). See [Theme and host files](./references/theme-and-host-files.md).
- **HTTP 500 mentioning `SyncfusionBlazorToolkitService`**: `AddSyncfusionBlazorToolkit()` is missing on a host that renders Toolkit. See [Troubleshooting guide](./references/troubleshooting.md).
- **Clicks or inputs do nothing**: check for an inherited render mode before editing the page. If none exists, the page is static SSR. See [Render modes explained](./references/render-modes.md).
- **Only one half of a split app works**: register Toolkit on both hosts. See [Split Blazor Web App registration](./references/split-webapp-registration.md).
- **License prompt after installing `Syncfusion.Blazor`**: that is the commercial package. See [Package identity](./references/package-identity.md) before removing anything the app still uses.

## Next Steps

After installation, use the component demos or component-specific guidance for API details; this skill only covers setup and configuration.

## References

- [Package identity](./references/package-identity.md)
- [Split Blazor Web App registration](./references/split-webapp-registration.md)
- [Theme and host files](./references/theme-and-host-files.md)
- [Render modes explained](./references/render-modes.md)
- [Troubleshooting guide](./references/troubleshooting.md)
