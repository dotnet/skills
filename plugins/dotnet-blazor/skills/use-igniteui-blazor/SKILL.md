---
license: MIT
name: use-igniteui-blazor
description: >
  Add, configure, or review Ignite UI component support in Blazor applications.
  USE FOR: installing IgniteUI.Blazor.Lite or IgniteUI.Blazor.GridLite,
  registering AddIgniteUIBlazor() in Blazor Server, WASM, Hybrid, or split Blazor
  Web App projects, adding @using IgniteUI.Blazor.Controls, wiring the required
  theme stylesheet and app.bundle.js assets, and checking where interactive
  render mode is needed for Ignite UI components to appear and function.
  Also USE FOR: explaining the setup differences between single-project and
  split Server/Client Blazor Web Apps, identifying the correct host page for the
  framework script, and locating the GridLite-specific stylesheet path.
  DO NOT USE FOR: general Blazor component authoring that does not involve
  Ignite UI, choosing app architecture or render mode from scratch (see
  create-blazor-project), JavaScript interop (see use-js-interop), authentication
  (see configure-auth), prerendering behavior (see support-prerendering), or
  generic layout/component design questions that do not require Ignite UI setup.
---

# Application Setup & Component Registration

## 1. NuGet package

```bash
dotnet add package IgniteUI.Blazor.Lite       # OSS core UI components (MIT)
dotnet add package IgniteUI.Blazor.GridLite   # OSS lightweight grid (MIT)
```

## 2. `Program.cs`

```csharp
builder.Services.AddIgniteUIBlazor();   // all modules available
```

Pass `typeof(Igb<Name>Module)` values to eagerly pre-load a specific set instead:

```csharp
builder.Services.AddIgniteUIBlazor(
    typeof(IgbInputModule), typeof(IgbComboModule), typeof(IgbDialogModule));
```

Module names always follow `Igb{ComponentName}Module`. In `IgniteUI.Blazor.Lite` a component registers its own module on first render, so the explicit list trims the initial payload rather than gating rendering.

**Blazor Web App:** call `AddIgniteUIBlazor()` in **both** the server and the client `Program.cs`.

```csharp
// Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddIgniteUIBlazor();

// Client (WebAssemblyHostBuilder)
builder.Services.AddIgniteUIBlazor();
```

## 3. `_Imports.razor`

```razor
@using IgniteUI.Blazor.Controls
```

Add it to both `_Imports.razor` files in split Blazor Web App solutions.

## 4. Host page — CSS and script

Host page is `wwwroot/index.html` (WASM/MAUI), `Pages/_Host.cshtml` (Server), or `Components/App.razor` (Web App).

```html
<link href="_content/IgniteUI.Blazor/themes/light/bootstrap.css" rel="stylesheet" />
...
<script src="_content/IgniteUI.Blazor/app.bundle.js"></script>
<script src="_framework/blazor.web.js"></script>   <!-- or blazor.server.js / blazor.webassembly.js / blazor.webview.js -->
```

Both tags are required: without the stylesheet components render unstyled, without `app.bundle.js` they do not render at all. `app.bundle.js` must come **before** the Blazor framework script.

Theme files under `_content/IgniteUI.Blazor/themes/` are `{light|dark}/{bootstrap|material|fluent|indigo}.css` — link exactly one.

.NET 9+ Web App projects can use the fingerprinted asset collection:

```razor
<link rel="stylesheet" href="@Assets["_content/IgniteUI.Blazor/themes/light/bootstrap.css"]" />
```

`IgniteUI.Blazor.GridLite` ships its own stylesheet from its own asset root:

```html
<link href="_content/IgniteUI.Blazor.GridLite/css/themes/light/bootstrap.css" rel="stylesheet" />
```

## 5. Render mode (Blazor Web App only)

Ignite UI components need an interactive render mode; static SSR renders nothing usable.

```razor
@rendermode InteractiveServer   @* or InteractiveWebAssembly / InteractiveAuto *@
```

Or globally in `App.razor`: `<Routes @rendermode="InteractiveAuto" />`.

## Project type reference

| Project type | Builder | Host page | Framework script |
|---|---|---|---|
| Blazor Server | `WebApplication.CreateBuilder` | `Pages/_Host.cshtml` | `blazor.server.js` |
| Blazor WASM | `WebAssemblyHostBuilder` | `wwwroot/index.html` | `blazor.webassembly.js` |
| Blazor Web App | both server + client | `Components/App.razor` | `blazor.web.js` |
| MAUI Blazor Hybrid | `MauiApp.CreateBuilder` | `wwwroot/index.html` | `blazor.webview.js` |
