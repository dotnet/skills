---
license: MIT
name: use-igniteui-blazor
description: >
  Add, configure, or review Ignite UI component support in Blazor applications.
  USE FOR: installing IgniteUI.Blazor.Lite or IgniteUI.Blazor.GridLite,
  registering AddIgniteUIBlazor() in Blazor Server, WASM, Hybrid, or split
  Blazor Web App projects, adding @using IgniteUI.Blazor.Controls, wiring the
  theme stylesheet and app.bundle.js assets, picking the right host page and
  framework script, locating the GridLite stylesheet path, explaining
  single-project vs split Server/Client Web App setup differences, and checking
  where an interactive render mode is needed for Ignite UI components to work.
  DO NOT USE FOR: general Blazor component authoring without Ignite UI, choosing
  app architecture or render mode from scratch (see create-blazor-project),
  JavaScript interop (see use-js-interop), authentication (see configure-auth),
  prerendering (see support-prerendering), or layout/component design questions that need
  no Ignite UI setup.
---

# Application Setup & Component Registration

## 1. NuGet package

```bash
dotnet add package IgniteUI.Blazor.Lite       # OSS core UI components (MIT)
dotnet add package IgniteUI.Blazor.GridLite   # OSS lightweight grid (MIT)
```

## 2. `IgniteUI.Blazor.Lite` Service Registration

Usually in `Program.cs`:

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

`IgniteUI.Blazor.GridLite` ships its own stylesheet from its own asset root, but should be used only if you are using the GridLite component exclusively. If you are using other Ignite UI components, use the main theme stylesheet above.

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
