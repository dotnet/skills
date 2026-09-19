---
license: MIT
name: use-igniteui-blazor
description: >
  Add, configure, or review Ignite UI for Blazor Lite component support in Blazor applications.
  USE FOR: installing IgniteUI.Blazor.Lite or IgniteUI.Blazor.GridLite,
  registering AddIgniteUIBlazor() in Blazor Server, WASM, Hybrid, or split
  Blazor Web App projects, adding @using IgniteUI.Blazor.Controls, wiring the
  theme stylesheet, picking the right host page, locating the GridLite
  stylesheet path, explaining single-project vs split Server/Client Web App
  setup differences, and checking where an interactive render mode is needed
  for Ignite UI components to work.
  DO NOT USE FOR: general Blazor component authoring without Ignite UI, choosing
  app architecture or render mode from scratch (see create-blazor-project),
  JavaScript interop (see use-js-interop), authentication (see configure-auth),
  prerendering (see support-prerendering), or layout/component design questions that need
  no Ignite UI setup.
---

# Application Setup & Component Registration

## 1. NuGet package

Before adding packages, inspect the target projects' target framework and existing package references. The lowest supported target framework version is .NET 8.0. If `IgniteUI.Blazor` or `IgniteUI.Blazor.Trial` is already referenced, keep that package strategy and do not add Lite or GridLite. Only switch package families when the user explicitly asks, replacing conflicting references rather than keeping both.

```bash
dotnet add package IgniteUI.Blazor.Lite       # OSS core UI components (MIT)
dotnet add package IgniteUI.Blazor.GridLite   # OSS lightweight grid (MIT)
```

Use `IgniteUI.Blazor.Lite` for core controls such as `IgbInput`, `IgbCombo` and `IgbDialog`, and `IgniteUI.Blazor.GridLite` for the lightweight grid. Reference both packages only when the app needs both. Do not invent per-component packages such as `IgniteUI.Blazor.Combo`.

Charts, maps, gauges and other premium components are not included in Lite. Check the requested component's package before recommending a reference.

## 2. `IgniteUI.Blazor.Lite` Service Registration

Usually in `Program.cs`:

```csharp
builder.Services.AddIgniteUIBlazor();   // no modules pre-loaded; each loads on first render
```

Pass `typeof(Igb<Name>Module)` values to eagerly pre-load a specific set instead:

```csharp
builder.Services.AddIgniteUIBlazor(
    typeof(IgbInputModule), typeof(IgbComboModule), typeof(IgbDialogModule));
```

Module names always follow `Igb{ComponentName}Module`. Passing modules eagerly loads them during startup, increasing the initial transfer to reduce first-render latency. Components not listed still register their own modules on first render.

For a GridLite-only setup, do not call `AddIgniteUIBlazor()` or add manual Ignite UI script tags. Reference `IgniteUI.Blazor.GridLite`, add the control namespace, and link the GridLite stylesheet shown below.

**Split Blazor Web App:** add each required package to both the Server and Client `.csproj` files. For core controls, use the actual project paths in place of these examples.

```bash
dotnet add Server/Server.csproj package IgniteUI.Blazor.Lite
dotnet add Client/Client.csproj package IgniteUI.Blazor.Lite
```

If GridLite is needed, add `IgniteUI.Blazor.GridLite` to both projects as well. For a GridLite-only app, add only that package and skip the service registrations below.

For Lite, call `AddIgniteUIBlazor()` in **both** the server and client `Program.cs`.

```csharp
// Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddIgniteUIBlazor();

// Client (WebAssemblyHostBuilder)
builder.Services.AddIgniteUIBlazor();
```

For a single-project Interactive Server Blazor Web App, call `AddIgniteUIBlazor()` once in the server `Program.cs`. Do not add a client project or WebAssembly services.

## 3. `_Imports.razor`

```razor
@using IgniteUI.Blazor.Controls
```

Add it to both `_Imports.razor` files in split Blazor Web App solutions.

## 4. Host page — theme stylesheet

Host page is `wwwroot/index.html` (WASM/MAUI), `Pages/_Host.cshtml` (Server), or `Components/App.razor` (Web App).

`IgniteUI.Blazor.Lite` 0.1.1 loads its JavaScript automatically through a Blazor initializer. Keep the existing Blazor framework script and add the theme stylesheet below. Do not add a manual Ignite UI script tag.

```html
<link href="_content/IgniteUI.Blazor/themes/light/bootstrap.css" rel="stylesheet" />
```

The stylesheet is required: without it components render unstyled.

Theme files under `_content/IgniteUI.Blazor/themes/` are `{light|dark}/{bootstrap|material|fluent|indigo}.css` — link exactly one.

.NET 9+ Web App projects can use the fingerprinted asset collection:

```razor
<link rel="stylesheet" href="@Assets["_content/IgniteUI.Blazor/themes/light/bootstrap.css"]" />
```

`IgniteUI.Blazor.GridLite` ships its own stylesheet from its own asset root, but should be used only if you are using the GridLite component exclusively. If you are using other Ignite UI components, do not link (or suggest) the GridLite stylesheet — use the main theme stylesheet above instead.

```html
<link href="_content/IgniteUI.Blazor.GridLite/css/themes/light/bootstrap.css" rel="stylesheet" />
```

## 5. Render mode (Blazor Web App only)

Ignite UI components need an interactive render mode; static SSR renders nothing usable.

```razor
@rendermode InteractiveServer
```

Or globally in `App.razor`: `<Routes @rendermode="InteractiveServer" />`.

Use `InteractiveWebAssembly` or `InteractiveAuto` in place of `InteractiveServer` as needed.
