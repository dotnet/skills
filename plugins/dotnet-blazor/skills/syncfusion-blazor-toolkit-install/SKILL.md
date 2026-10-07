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

1. Use the exact package ID `Syncfusion.Blazor.Toolkit`.
2. In every `Program.cs` that calls `AddSyncfusionBlazorToolkit()`, add `using Syncfusion.Blazor.Toolkit;` so the extension method is in scope. Call it before `builder.Build()`.
3. Import `@using Syncfusion.Blazor.Toolkit` in `_Imports.razor`; add component namespaces such as `@using Syncfusion.Blazor.Toolkit.Buttons` only when a component needs them. `_Imports.razor` does not affect `Program.cs`.
4. Link `_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css` (or `highcontrast.min.css`) in the app host file. Fluent is the default.
5. Use `@rendermode` only in a Blazor Web App. Legacy Blazor Server and standalone WebAssembly are already interactive and do not use render modes.
6. In a split Blazor Web App, the `.Client` project is not a standalone WASM app. Do not call `RootComponents.Add<App>("#app")` there. Register Toolkit on every host that renders Toolkit components, including the server when prerendering is left on.
7. Do not add a commercial `Syncfusion.Blazor*` package to satisfy a Toolkit request. If the app already uses commercial components, leave those packages and `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(...)` in place.
8. Toolkit JavaScript is embedded in the NuGet package; do not add commercial script tags.
9. Keep this skill install-focused; use the references for details and troubleshooting.

## Don'ts

- Don't treat `Syncfusion.Blazor` or component-specific commercial packages as Toolkit dependencies.
- Don't add external script tags or commercial Syncfusion license scripts; Toolkit JS is bundled with the package.
- Don't rely on static SSR when the component must respond to clicks, binding, or dynamic updates.
- Don't use this skill as a component API reference; consult the official Syncfusion Blazor Toolkit demos or documentation for API details.

## Quick Decision Table

| Scenario | Package | Program.cs | Host file for CSS | Interactivity |
| --- | --- | --- | --- | --- |
| Blazor Server (legacy `_Host.cshtml`) | `Syncfusion.Blazor.Toolkit` | `using Syncfusion.Blazor.Toolkit;` then `builder.Services.AddSyncfusionBlazorToolkit()` | `_Host.cshtml` | Already interactive (no `@rendermode`) |
| Blazor WebAssembly (standalone) | `Syncfusion.Blazor.Toolkit` | `using Syncfusion.Blazor.Toolkit;` then `builder.Services.AddSyncfusionBlazorToolkit()` | `wwwroot/index.html` | Already interactive (no `@rendermode`; not static SSR) |
| Blazor Web App, global Interactive Server | `Syncfusion.Blazor.Toolkit` | Server `Program.cs` only, with the using above | `Components/App.razor` | Inherited from `Routes`; do not add a different child mode |
| Blazor Web App, per-page or per-component | `Syncfusion.Blazor.Toolkit` | Every host that renders the component, including the server while prerendering is on | `Components/App.razor` | One interactive mode: `InteractiveServer`, `InteractiveWebAssembly`, or `InteractiveAuto` |
| Blazor Web App (split Server/Client) | `Syncfusion.Blazor.Toolkit` | Server and `.Client` when both render Toolkit. No `RootComponents.Add` in `.Client` | `Components/App.razor` | Same mode as the parent page; a child cannot switch interactive modes |
| Static SSR only (Blazor Web App) | `Syncfusion.Blazor.Toolkit` | Server `Program.cs` if any Toolkit markup is rendered | `Components/App.razor` | None; read-only only. Standalone WASM is not this row |

## Minimal Setup

### Package

```bash
dotnet add package Syncfusion.Blazor.Toolkit
```

### Program.cs

Match the render modes from the Quick Decision Table above. Do not add unused render modes.

**Blazor Web App server `Program.cs` (Auto, WebAssembly, or split)**:

Keep the interactive services the template already generated. Register Toolkit here even when the component lives in `.Client`, because default prerendering runs it on the server.

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

**Blazor Web App server `Program.cs` (Interactive Server only)**:

Do not add WebAssembly services unless the project already references `Microsoft.AspNetCore.Components.WebAssembly.Server`.

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

**Blazor Web App `.Client/Program.cs`**:

This project is not a standalone WASM app. The server owns `App.razor`, and the host page has no `#app` element, so do not register a root component here.

```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSyncfusionBlazorToolkit();

await builder.Build().RunAsync();
```

**Standalone Blazor WebAssembly `Program.cs`**:

```csharp
using Syncfusion.Blazor.Toolkit;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSyncfusionBlazorToolkit();

await builder.Build().RunAsync();
```

### _Imports.razor

```razor
@using Syncfusion.Blazor.Toolkit

@* Optional: add component-specific namespaces only when using those components *@
@* @using Syncfusion.Blazor.Toolkit.Buttons *@
@* @using Syncfusion.Blazor.Toolkit.Calendars *@
```

`_Imports.razor` does not put the namespace in scope for `Program.cs`.

### Host file CSS

```html
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```

Pick the host file from the Quick Decision Table: `_Host.cshtml` for legacy Blazor Server, `Components/App.razor` for a Blazor Web App, or `wwwroot/index.html` for standalone WebAssembly. Use `highcontrast.min.css` only when the user asks for high contrast.

## Common Mistakes

| Mistake | Symptom | Fix |
| --- | --- | --- |
| CSS path uses `themes/` or omits `.min` | Components are unstyled (404 in browser DevTools) | Use `_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css` |
| CSS linked in a component file instead of the host file | Styling doesn't apply consistently | Move the `<link>` to the host file `<head>` from the Quick Decision Table |
| Commercial package added for a Toolkit-only app | License prompt, missing Toolkit components | Replace that package with `Syncfusion.Blazor.Toolkit`. If other pages still use commercial components, keep those packages and `SyncfusionLicenseProvider.RegisterLicense(...)` |
| Added an external Syncfusion CDN script tag | JavaScript conflicts or duplicate library errors | Remove the CDN script; Toolkit JS ships in the NuGet package |
| `@rendermode` missing, and no interactive mode is inherited | Clicks and binding do nothing on a Blazor Web App page | Apply one interactive mode, or inherit the mode already on `Routes` |
| `@rendermode` added to standalone WASM or legacy Server | No effect, and it can look like a fix | Remove it; those apps are already interactive |
| Child `@rendermode` differs from the parent interactive mode | Runtime error; a child cannot switch interactive modes | Match the inherited mode, or move the component to a page with the intended mode |
| `RootComponents.Add<App>("#app")` added to `.Client` | The Client host has no `#app` element | Remove it. That call belongs only in standalone WebAssembly |

## Top Failure Symptoms

- **Unstyled components**: the stylesheet link is missing, wrong, or placed in the wrong host file. See [Theme and host files](./references/theme-and-host-files.md).
- **Services not configured**: `AddSyncfusionBlazorToolkit()` or its `using Syncfusion.Blazor.Toolkit;` is missing from a `Program.cs` that renders Toolkit. See [Troubleshooting guide](./references/troubleshooting.md).
- **Clicks or inputs do nothing**: check for an inherited render mode before editing the page. If none exists, the page is static SSR. See [Render modes explained](./references/render-modes.md).
- **Only one half of a split app works**: register Toolkit on every host that renders it, including the server while prerendering is on. See [Split Blazor Web App registration](./references/split-webapp-registration.md).
- **License prompt after installing `Syncfusion.Blazor`**: that is the commercial package. See [Package identity and splits](./references/package-identity.md) before removing anything the app still uses.

## Next Steps

After installation, use the component demos or component-specific guidance for API details; this skill only covers setup and configuration.

## References

- [Package identity and splits](./references/package-identity.md)
- [Split Blazor Web App registration](./references/split-webapp-registration.md)
- [Theme and host files](./references/theme-and-host-files.md)
- [Render modes explained](./references/render-modes.md)
- [Troubleshooting guide](./references/troubleshooting.md)
