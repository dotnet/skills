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

1. **Verify the target framework first.** `Syncfusion.Blazor.Toolkit` 1.0.2 targets `net8.0`, `net9.0`, and `net10.0` only. Read `<TargetFramework>` in the app's `.csproj` before installing. On `net6.0`, `net7.0`, or older, NuGet restore still succeeds but supplies no assemblies, so the build later fails with `CS0246` on `Syncfusion`. Do not install; recommend upgrading to .NET 8 or later first (for example with the `dotnet-upgrade` plugin) and resume afterward. Never edit the TFM just to make the package install.
2. Use the exact package ID `Syncfusion.Blazor.Toolkit`.
3. In every `Program.cs` that calls `AddSyncfusionBlazorToolkit()`, add `using Syncfusion.Blazor.Toolkit;` so the extension method is in scope. Call it before `builder.Build()`.
4. Component types live in child namespaces, not the root. Add the namespace for each component to `_Imports.razor` (for example `@using Syncfusion.Blazor.Toolkit.Buttons` for `SfButton`). A missing namespace does not fail the build: Razor warns `RZ10012` and renders the tag as an unknown HTML element. The root `@using Syncfusion.Blazor.Toolkit` is optional and only needed to name its enums. `_Imports.razor` does not affect `Program.cs`.
5. Link `_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css` (or `highcontrast.min.css`) in the app host file. Fluent is the default. Add the link to the host file's `<head>`; do not replace the whole host file.
6. Use `@rendermode` only in a Blazor Web App. Legacy Blazor Server and standalone WebAssembly are already interactive and do not use render modes.
7. Toolkit components need `AddSyncfusionBlazorToolkit()` on **every host that renders them, including static SSR**. Without it the page returns HTTP 500 with `There is no registered service of type 'Syncfusion.Blazor.Toolkit.SyncfusionBlazorToolkitService'`. In a split Blazor Web App, register on the server and in `.Client`: prerendering runs `.Client` components on the server, so client-only registration still returns 500.
8. In a split Blazor Web App, the `.Client` project is not a standalone WASM app. Do not call `RootComponents.Add<App>("#app")` there. Keep the `.AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` call the template puts on `MapRazorComponents<App>()`; the server needs it to see `.Client` pages. It is unrelated to Toolkit, so never add, remove, or move it.
9. Do not add a commercial `Syncfusion.Blazor*` package to satisfy a Toolkit request. If the app already uses commercial components, leave those packages and `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(...)` in place.
10. Toolkit JavaScript is embedded in the NuGet package; do not add commercial script tags.
11. Keep this skill install-focused; use the references for details and troubleshooting.

## Don'ts

- Don't install the Toolkit on a .NET 6 or .NET 7 project. Recommend upgrading first.
- Don't treat `Syncfusion.Blazor` or component-specific commercial packages as Toolkit dependencies.
- Don't add external script tags or commercial Syncfusion license scripts; Toolkit JS is bundled with the package.
- Don't rely on static SSR when the component must respond to clicks, binding, or dynamic updates.
- Don't use this skill as a component API reference; consult the official Syncfusion Blazor Toolkit demos or documentation for API details.
- Don't replace or retype `Program.cs`, `App.razor`, `_Host.cshtml`, or `wwwroot/index.html`. Make only the additions shown below and leave every other template line (`UseExceptionHandler`, `UseHsts`, `MapStaticAssets`, `HeadOutlet`, `Routes`, render-mode directives, script tags) untouched.

## Quick Decision Table

Pick the **single** row that matches the app the user is on. The "Program.cs" columns say where `AddSyncfusionBlazorToolkit()` goes; the template's own endpoint calls stay as generated.

| Scenario | Register in server `Program.cs` | Register in `.Client/Program.cs` | Host file for CSS | Interactivity |
| --- | --- | --- | --- | --- |
| Legacy Blazor Server (`_Host.cshtml`, .NET 8+ only for Toolkit) | Yes | n/a | `_Host.cshtml` | Already interactive (no `@rendermode`) |
| Standalone Blazor WebAssembly | n/a | Yes, in the standalone `Program.cs`; it keeps its own `RootComponents.Add<App>("#app")` | `wwwroot/index.html` | Already interactive (no `@rendermode`; not static SSR) |
| Blazor Web App, `-int Server` | Yes | n/a (no `.Client` project) | `Components/App.razor` | Inherited from `Routes`; do not add a different child mode |
| Blazor Web App, `-int WebAssembly` | Yes | Yes; **no** `RootComponents.Add` | `Components/App.razor` | WebAssembly; prerendered on the server first |
| Blazor Web App, `-int Auto` | Yes | Yes; **no** `RootComponents.Add` | `Components/App.razor` | First visit server, later visits cached WebAssembly; a running component does not switch |
| Blazor Web App, per-page modes | Yes (prerendering runs there) | Yes, if `.Client` renders the component | `Components/App.razor` | One mode per component; a child cannot switch interactive modes |
| Static SSR only (Blazor Web App) | Yes, if any Toolkit markup is rendered | n/a | `Components/App.razor` | None; read-only only. Standalone WASM is not this row |
| .NET MAUI Blazor Hybrid | **Out of scope** (see below) | n/a | n/a | n/a |

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
- The Toolkit's `net10.0` assets resolve for a platform TFM such as `net10.0-windows10.0.19041.0`. Android, iOS and Mac Catalyst were not verified, so point the user to Hybrid-specific documentation rather than promising support.

## Common Mistakes

| Mistake | Symptom | Fix |
| --- | --- | --- |
| Toolkit installed on .NET 6 or .NET 7 | Restore succeeds, then the build fails with `CS0246` on `Syncfusion` (no assemblies are supplied) | Upgrade the app to .NET 8 or later first. Never change the TFM just to make the package install |
| `AddSyncfusionBlazorToolkit()` missing on a host that renders Toolkit | HTTP 500: `no registered service of type ...SyncfusionBlazorToolkitService` | Register on every rendering host, including the server for `.Client` components and for static SSR |
| CSS path uses `themes/` or omits `.min` | Components are unstyled (404 in browser DevTools) | Use `_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css` |
| CSS linked in a component file instead of the host file | Styling doesn't apply consistently | Move the `<link>` to the host file `<head>` |
| Host file replaced wholesale instead of adding the link | `HeadOutlet`, `Routes`, or the render-mode directive is lost; events stop firing | Restore the template-generated file, then add only the `<link>` line |
| `Program.cs` retyped from a trimmed sample | `CS0246` on `App`, and `UseExceptionHandler`, `UseHsts`, `MapStaticAssets` and `app.Run()` are lost | Restore the template file, then add only the `using` and the registration line |
| Template's `AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` deleted from `MapRazorComponents<App>()` | Routable pages in `.Client` return 404 | Restore the call exactly as the template generated it |
| Commercial package added for a Toolkit-only app | License prompt, missing Toolkit components | Replace that package with `Syncfusion.Blazor.Toolkit`. If other pages still use commercial components, keep those packages and `SyncfusionLicenseProvider.RegisterLicense(...)` |
| Component namespace missing | Razor warning `RZ10012`; `<SfButton>` renders as an unstyled unknown element that ignores events | Add the child namespace, for example `@using Syncfusion.Blazor.Toolkit.Buttons`, to `_Imports.razor` of the project that renders it |
| Added an external Syncfusion CDN script tag | JavaScript conflicts or duplicate library errors | Remove the CDN script; Toolkit JS ships in the NuGet package |
| `@rendermode` missing, and no interactive mode is inherited | Clicks and binding do nothing on a Blazor Web App page | Apply one interactive mode, or inherit the mode already on `Routes` |
| `@rendermode` added to standalone WASM or legacy Server | No effect, and it can look like a fix | Remove it; those apps are already interactive |
| Child `@rendermode` differs from the parent interactive mode | Runtime error; a child cannot switch interactive modes | Match the inherited mode, or move the component to a page with the intended mode |
| Auto or WebAssembly endpoint removed from a split Web App | Client components never load, or load only after a server round-trip | Keep the endpoints the template generated |
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
