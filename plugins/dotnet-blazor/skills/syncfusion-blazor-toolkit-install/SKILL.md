---
license: MIT
name: syncfusion-blazor-toolkit-install
description: >
  Install, upgrade, or troubleshoot setup of the open-source Syncfusion Blazor
  Toolkit (v2.0.1+) in a web-hosted Blazor app: package identity, service
  registration, SfThemeRoot/CSS-isolation theming, host files, namespaces,
  split Web App registration, and interactive render-mode boundaries.

  USE ONLY WHEN: the user asks to install, migrate, register, configure hosting
  or theming, or diagnose a setup failure for Syncfusion.Blazor.Toolkit in
  legacy Blazor Server, standalone WebAssembly, or a Blazor Web App. Also use
  for accidental commercial Syncfusion.Blazor installs or license prompts when
  the user intended the open-source Toolkit.
  DO NOT USE WHEN: the Toolkit is already installed and the question is about
  component APIs or behavior such as grid columns, data binding, events, or
  styling a specific component (use author-component); the user asks to create
  a Blazor project (use create-blazor-project); the app is .NET MAUI Blazor
  Hybrid; or asks only about commercial Syncfusion components.
---

# Install Syncfusion Blazor Toolkit

> **Continue after loading.** Loading this skill is the start of the task, not the end. In the same turn, do the work the user asked for (write the files or give the concrete answer), then finish with a final message that contains the actual code or steps, not just a summary of files changed. Read a reference file only if the question needs it.

## Core Rules

1. **Verify the target framework first.** `Syncfusion.Blazor.Toolkit` 2.0.1 targets `net8.0`, `net9.0`, and `net10.0`. Always check the project's `<TargetFramework>` before installation. On `net6.0`, `net7.0`, or older frameworks, NuGet restore fails with an incompatibility such as `NU1202` because the package has no compatible assets for those TFMs. Do not expect a later `CS0246` as the primary signal—fix the framework mismatch first. Do not proceed with Toolkit installation on unsupported frameworks. Recommend upgrading the application to .NET 8 or later first (for example with the `dotnet-upgrade` plugin), then continue with the installation steps. If the `<TargetFramework>` is unknown, read it before doing anything else. Never edit the TFM just to make the package install.
2. Use the exact package ID `Syncfusion.Blazor.Toolkit`. Require version 2.0.1 or newer and prefer the latest 2.x stable release. Read the existing `PackageReference` first; upgrade 2.0.0 before removing `.Data` imports because Data types moved into the root namespace only in 2.0.1. Version 2.0.x is **not source-compatible** with 1.0.2.
3. In every `Program.cs` that calls `AddSyncfusionBlazorToolkit()`, add `using Syncfusion.Blazor.Toolkit;` so the extension method is in scope. Call it before `builder.Build()`.
4. **Namespace consolidation (2.0 / 2.0.1 breaking change).** All public types—including components (`SfButton`, `SfTextBox`, `SfChart`, calendars, dialogs, spinner) **and** data-binding types (`DataAdaptor<T>`, `DataManager`, `Query`, `SfDataManager`, adaptors, etc.)—live under the single root namespace `Syncfusion.Blazor.Toolkit`. Domain-specific namespaces (`Syncfusion.Blazor.Toolkit.Buttons`, `.Calendars`, `.Charts`, `.Inputs`, `.Popups`, `.Spinner`, **`.Data`**, etc.) were **removed** (components in 2.0.0; Data in 2.0.1). Add only:
   ```razor
   @using Syncfusion.Blazor.Toolkit
   ```
   to `_Imports.razor` (and the equivalent `using` in C# files). That single import is enough for every public component type. A missing namespace produces Razor warning `RZ10012` and renders the tag as an unknown HTML element. `_Imports.razor` does not affect `Program.cs`.

**There is no `Syncfusion.Blazor.Toolkit.Data` exception in 2.0.1.** Data-binding types such as `DataAdaptor<T>`, `DataManager`, and `Query` live in the root `Syncfusion.Blazor.Toolkit` namespace; use the single `@using Syncfusion.Blazor.Toolkit` import and root-qualified names when needed.
5. **Theming (2.0 — no host theme stylesheet).** Do **not** link `fluent.min.css`, `highcontrast.min.css`, or per-component stylesheets under `_content/Syncfusion.Blazor.Toolkit/styles/`. Component styles use Blazor CSS isolation; the shared theme layer (`:root` tokens, base/utility styles, icon font, keyframes, dark/high-contrast/forced-colors) is emitted at render time by `SfThemeRoot`. Every consumer-facing root component renders `<SfThemeRoot />` as its previous sibling automatically—no extra consumer configuration is required when Toolkit components are present. Place an explicit `<SfThemeRoot />` only when theme emission is needed without a Toolkit component in the tree. Keep the template's own `app.css` / `{Project}.styles.css` links; do not replace the host file.
6. Use `@rendermode` only in a Blazor Web App. Legacy Blazor Server and standalone WebAssembly are already interactive and do not use render modes. In a Web App, **`-int Server`, `-int WebAssembly` and `-int Auto` are per-page** by default: `<Routes />` has **no** directive unless you passed `-ai`. Only `-ai` (`--all-interactive`) is global (mode on `Routes` / `HeadOutlet`). Any Toolkit component that uses events, bindings, dialogs, calendar navigation, or button clicks must sit inside an **interactive boundary**: (a) the page's `@rendermode`, (b) an interactive **wrapper component** on an otherwise static SSR page, or (c) the inherited `Routes` mode in a global (`-ai`) app. The page itself does **not** have to be interactive—Blazor allows interactive children on a static page.
7. Toolkit components need `AddSyncfusionBlazorToolkit()` on **every host that renders them, including static SSR**. Without it the page returns HTTP 500 with `There is no registered service of type 'Syncfusion.Blazor.Toolkit.SyncfusionBlazorToolkitService'`. In a split Blazor Web App, register on the server and in `.Client`: prerendering runs `.Client` components on the server, so client-only registration still returns 500.
8. In a split Blazor Web App, the `.Client` project is not a standalone WASM app. Do not call `RootComponents.Add<App>("#app")` there. Keep the `.AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` call the template puts on `MapRazorComponents<App>()`; the server needs it to see `.Client` pages. It is unrelated to Toolkit, so never add, remove, or move it.
9. Do not add a commercial `Syncfusion.Blazor*` package to satisfy a Toolkit request. If the app already uses commercial components, leave those packages and `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(...)` in place.
10. Toolkit JavaScript is embedded in the NuGet package; do not add commercial script tags.
11. Keep this skill install-focused; use the references for details and troubleshooting.
12. **CSS isolation (2.0).** Component styles are scoped. App-level overrides that previously targeted unscoped toolkit class names (for example `.e-btn`) may need the `::deep` combinator or adjustment for isolation attributes. Review custom CSS after upgrading from 1.x.

## Don'ts

- Don't install the Toolkit on a .NET 6 or .NET 7 project, or when the `<TargetFramework>` has not been checked. Recommend upgrading to .NET 8 or later first.
- Don't treat `Syncfusion.Blazor` or component-specific commercial packages as Toolkit dependencies.
- Don't add external script tags or commercial Syncfusion license scripts; Toolkit JS is bundled with the package.
- Don't leave an interactive Toolkit control outside any interactive boundary (page `@rendermode`, interactive wrapper, or inherited `Routes` mode). A static SSR page is fine if a child wrapper owns the control and its handlers.
- Don't use this skill as a component API reference; consult the official Syncfusion Blazor Toolkit demos or documentation for API details.
- Don't remove `.Data` imports from an app that is still pinned to 2.0.0. Upgrade to 2.0.1 or newer first, then replace all domain-specific imports with `@using Syncfusion.Blazor.Toolkit`.
- Don't link `fluent.min.css`, `highcontrast.min.css`, or other `_content/Syncfusion.Blazor.Toolkit/styles/*.css` files in the host; 2.0 theming uses `SfThemeRoot` and CSS isolation.
- Don't replace or retype `Program.cs`, `App.razor`, `_Host.cshtml`, or `wwwroot/index.html`. Make only the additions shown below and leave every other template line (`UseExceptionHandler`, `UseHsts`, `MapStaticAssets`, `HeadOutlet`, `Routes`, render-mode directives, script tags) untouched.

## Quick Decision Table

Pick the **single** row that matches the app the user is on. The "Program.cs" columns say where `AddSyncfusionBlazorToolkit()` goes; the template's own endpoint calls stay as generated.

| Scenario | Register in server `Program.cs` | Register in `.Client/Program.cs` | Host file (keep template CSS only) | Where interactive Toolkit components go |
| --- | --- | --- | --- | --- |
| Legacy Blazor Server (`_Host.cshtml`, net8.0+ only for Toolkit) | Yes | n/a | `_Host.cshtml` | Anywhere; already interactive, no `@rendermode` |
| Standalone Blazor WebAssembly | n/a | Yes, in the standalone `Program.cs`; it keeps its own `RootComponents.Add<App>("#app")` | `wwwroot/index.html` | Anywhere; already interactive, no `@rendermode` |
| Web App `-int Server` (**per-page**, not global) | Yes | n/a (no `.Client` project) | `Components/App.razor` | Interactive boundary: page `@rendermode InteractiveServer` **or** interactive wrapper; pure static pages are display-only |
| Web App `-int Server -ai` (**global**) | Yes | n/a | `Components/App.razor` | Anywhere; inherits `InteractiveServer` from `Routes`. Do not add a different mode |
| Web App `-int WebAssembly` (**per-page**, not global) | Yes | Yes; **no** `RootComponents.Add` | `Components/App.razor` | Interactive boundary in `.Client` (page **or** wrapper with `@rendermode InteractiveWebAssembly`); server-project pages prerender then fail in the browser |
| Web App `-int WebAssembly -ai` (**global**) | Yes | Yes | `Components/App.razor` | In `.Client` only (inherits from `Routes`); server-project pages prerender then fail in the browser |
| Web App `-int Auto` (**per-page**, not global) | Yes | Yes; **no** `RootComponents.Add` | `Components/App.razor` | Interactive boundary in `.Client` (page **or** wrapper with `@rendermode InteractiveAuto`); server-project pages prerender then fail in the browser. First visit server, later cached WASM; a running component does not switch |
| Web App `-int Auto -ai` (**global**) | Yes | Yes | `Components/App.razor` | In `.Client` only (inherits from `Routes`); server-project pages prerender then fail in the browser |
| Web App `-int None` / static SSR page | Yes, if any Toolkit markup renders | n/a | `Components/App.razor` | Display-only Toolkit markup only; **never** components that use events, bindings, dialogs, or clicks |
| .NET MAUI Blazor Hybrid | **Out of scope** (see below) | n/a | n/a | n/a |

Verified with `dotnet new blazor`: without `-ai`, `<Routes />` carries no directive and only the sample `Counter.razor` has its own `@rendermode`. With `-ai`, `HeadOutlet` and `Routes` carry `@rendermode="Interactive..."`. See [Render modes explained](./references/render-modes.md).

## Minimal Setup

### Package

```bash
dotnet add package Syncfusion.Blazor.Toolkit
```

Prefer the latest 2.x version (2.0.1 or newer). In a split Blazor Web App, install into the `.Client` project — the reference flows transitively to Server — by targeting the project file directly. From the solution directory:
```bash
dotnet add MyApp.Client/MyApp.Client.csproj package Syncfusion.Blazor.Toolkit
```
Replace `MyApp` with the app's name (the generated project is named `<AppName>.Client`, never literally `.Client`, so the SDK can't resolve it as a directory). Installing only into Server leaves `.Client` without the package, so `@using Syncfusion.Blazor.Toolkit` and `AddSyncfusionBlazorToolkit()` there will fail to compile.

### Program.cs: make only these additions

Every template (`-int Server`, `-int WebAssembly`, `-int Auto`) already generates its own `AddRazorComponents()`, `Map*` and `AddAdditionalAssemblies(...)` calls. **Do not retype or replace them.** Add **only** `using Syncfusion.Blazor.Toolkit;` and `builder.Services.AddSyncfusionBlazorToolkit();` (plus the same registration on `.Client` when the template has a Client project). Pasting a full sample `Program.cs` from docs is a common failure: it can drop `using <App>.Components;` (`CS0246` on `App`), remove `UseExceptionHandler` / `UseHsts` / `UseHttpsRedirection`, drop static-asset mapping (`MapStaticAssets` on .NET 9+, `UseStaticFiles` on .NET 8), remove `.AddAdditionalAssemblies(...)` on WebAssembly/Auto hosts (Client routes stop resolving), or add Interactive Server services to a WebAssembly-only template.

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

Add the single root namespace. In a split Web App add it to the `_Imports.razor` of **every project that renders Toolkit components** (server `Components/_Imports.razor` and `.Client/_Imports.razor`).

```razor
@using Syncfusion.Blazor.Toolkit
```

Do **not** add domain-specific usings such as `@using Syncfusion.Blazor.Toolkit.Buttons` or `@using Syncfusion.Blazor.Toolkit.Data` — those namespaces were removed (components in 2.0.0; Data in 2.0.1). Types such as `SfButton`, `DataManager`, and `Query` resolve from the root `@using Syncfusion.Blazor.Toolkit` alone.

### Host file and theming

**Do not** add a Toolkit theme stylesheet link. In 2.0, styling comes from:

1. **CSS isolation** — component scoped styles from the package.
2. **`SfThemeRoot`** — shared theme layer emitted automatically when a Toolkit component is in the tree (or place an explicit `<SfThemeRoot />` if you need theme tokens with no Toolkit component present).

Ensure the host has an active `<link>` to `{Project}.styles.css` so Toolkit's isolated component styles load. Empty standalone WebAssembly templates leave this link commented out, so uncomment it or add it if missing. `SfThemeRoot` does not replace the application's scoped CSS bundle.

Keep the template-generated host file intact: `HeadOutlet`, `Routes` and its render-mode directive, `app.css` / `{Project}.styles.css`, and the framework script tag. When upgrading from 1.x, **remove** any `<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" ...>` (or `highcontrast.min.css` / per-component `.min.css`) entries. See [Theme and host files](./references/theme-and-host-files.md).

## Migrating from Toolkit 1.x or 2.0.0 to 2.0.1+

1. Update the package to **2.0.1 or newer** (2.0.0 moved component namespaces; 2.0.1 also moved Data).
2. Replace **all** domain-specific usings — `.Buttons`, `.Calendars`, `.Charts`, `.Inputs`, `.Popups`, `.Spinner`, **`.Data`**, and similar — with the single `@using Syncfusion.Blazor.Toolkit`. Remove any remaining `@using Syncfusion.Blazor.Toolkit.Data`.
3. Update fully qualified type names that used child namespaces (`.Buttons`, `.Calendars`, **`.Data`**, etc.) to the root namespace (e.g. `Syncfusion.Blazor.Toolkit.DataManager`, `Syncfusion.Blazor.Toolkit.Query`, `Syncfusion.Blazor.Toolkit.DataAdaptor<T>`).
4. **Remove** any 1.x Toolkit theme `<link>` tags (`fluent.min.css`, `highcontrast.min.css`, per-component stylesheets). Component styles are CSS-isolated; the shared theme is delivered by `SfThemeRoot`.
5. Review custom CSS overrides that targeted unscoped toolkit class names; isolation may require `::deep` or scope adjustments.
6. If subclasses of `DataAdaptor<T>` exist, add `where T : notnull`.
7. Collection return types that were `List<T>` may now be `IList<T>`; avoid casting to `List<T>`.

## Hybrid / MAUI boundary

This skill does not cover .NET MAUI Blazor Hybrid. When a user asks, explain the boundary instead of applying Web App steps. Verified against the `maui-blazor` template:

- There is no `App.razor`, `_Host.cshtml`, `HeadOutlet`, or render mode. The UI runs in a `BlazorWebView` declared in `MainPage.xaml`.
- The host page is `wwwroot/index.html`, with a `<div id="app">` root wired through `<RootComponent Selector="#app" ComponentType="{x:Type components:Routes}" />`. It loads `_framework/blazor.webview.js`.
- Services are registered in `MauiProgram.cs`, after `builder.Services.AddMauiBlazorWebView();`.
- The Toolkit's `net10.0` assets resolve for the platform TFMs `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst` and `net10.0-windows10.0.19041.0` (verified by restore against 2.0.0). `net9.0-android` and `net8.0-android` fail on the SDK itself (`NETSDK1147`, MAUI workload not installed), so Toolkit support there was not testable. Even so, this skill does not configure Hybrid apps: point the user to Hybrid-specific documentation. The same TFM check applies: a MAUI project on an unsupported .NET version needs an upgrade first.

## Common Mistakes

| Mistake | Symptom | Fix |
| --- | --- | --- |
| Toolkit installed on .NET 6, .NET 7, or older | NuGet restore fails (`NU1202` / no compatible assets for the TFM) | Upgrade the app to .NET 8 or later first. Never change the TFM just to make the package install |
| `AddSyncfusionBlazorToolkit()` missing on a host that renders Toolkit | HTTP 500: `no registered service of type ...SyncfusionBlazorToolkitService` | Register on every rendering host, including the server for `.Client` components and for static SSR |
| Adding a 1.x Toolkit theme `<link>` (`fluent.min.css` / `highcontrast.min.css`) | Unnecessary; may confuse troubleshooting | Remove it; rely on `SfThemeRoot` + CSS isolation |
| Host file replaced wholesale while adjusting theming | `HeadOutlet`, `Routes`, or the render-mode directive is lost; events stop firing | Restore the template-generated file; do not add Toolkit theme links |
| App `{Project}.styles.css` / `app.css` link removed | App or scoped styles missing | Keep the template stylesheet links |
| `Program.cs` retyped from a trimmed sample | `CS0246` on `App`, and `UseExceptionHandler`, `UseHsts`, `MapStaticAssets` and `app.Run()` are lost | Restore the template file, then add only the `using` and the registration line |
| Template's `AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` deleted from `MapRazorComponents<App>()` | Routable pages in `.Client` return 404 | Restore the call exactly as the template generated it |
| Commercial package added for a Toolkit-only app | License prompt, missing Toolkit components | Replace that package with `Syncfusion.Blazor.Toolkit`. If other pages still use commercial components, keep those packages and `SyncfusionLicenseProvider.RegisterLicense(...)` |
| Domain-specific `@using` still present after upgrading to 2.0.1+ (including `.Data`) | `CS0234` or unresolved type; components/data types not recognized | Replace with `@using Syncfusion.Blazor.Toolkit` only |
| Namespace missing after upgrading to 2.0.1+ | Razor warning `RZ10012` or `CS0234`; `<SfButton>` / data types unresolved | Add `@using Syncfusion.Blazor.Toolkit` to `_Imports.razor` of the project that renders it |
| Custom CSS from 1.x no longer applies | Styles ignored after upgrade | Account for CSS isolation; use `::deep` for child selectors when needed |
| Added an external Syncfusion CDN script tag | JavaScript conflicts or duplicate library errors | Remove the CDN script; Toolkit JS ships in the NuGet package |
| Assumed `-int Server` is global, so an interactive Toolkit component has no interactive boundary | Clicks, bindings, dialogs and calendar navigation do nothing | Check `Routes`. In a per-page app put the template `@rendermode` on the page **or** on a wrapper that hosts the control; in a `-ai` app inherit `Routes` |
| `@rendermode` added to standalone WASM or legacy Server | No effect, and it can look like a fix | Remove it; those apps are already interactive |
| Child `@rendermode` differs from the parent interactive mode | Runtime error; a child cannot switch interactive modes | Match the inherited mode, or move the component to a page with the intended mode |
| Auto or WebAssembly endpoint removed from a split Web App | Client components never load, or load only after a server round-trip | Keep the endpoints the template generated |
| `InteractiveWebAssembly` or `InteractiveAuto` page created in the **server** project | HTTP 200 and the component shows from the prerender, then the browser console reports `Root component type '...' could not be found in the assembly '<ServerApp>'` and nothing is interactive | Move the page and its components into the `.Client` project |
| `RootComponents.Add<App>("#app")` added to `.Client` | The Client host has no `#app` element | Remove it. That call belongs only in standalone WebAssembly |

## Top Failure Symptoms

- **Unstyled components**: missing package/registration/root `@using`; no Toolkit component (and no explicit `<SfThemeRoot />`) in the tree; custom overrides need isolation adjustment; or residual 1.x expectations about `fluent.min.css`. See [Theme and host files](./references/theme-and-host-files.md).
- **HTTP 500 mentioning `SyncfusionBlazorToolkitService`**: `AddSyncfusionBlazorToolkit()` is missing on a host that renders Toolkit. See [Troubleshooting guide](./references/troubleshooting.md).
- **Clicks or inputs do nothing**: check for an inherited `Routes` mode first; if none, ensure an interactive boundary (page `@rendermode` or interactive wrapper). See [Render modes explained](./references/render-modes.md).
- **Only one half of a split app works**: register Toolkit on both hosts. See [Split Blazor Web App registration](./references/split-webapp-registration.md).
- **License prompt after installing `Syncfusion.Blazor`**: that is the commercial package. See [Package identity](./references/package-identity.md) before removing anything the app still uses.
- **Unresolved types after upgrading from 1.x or 2.0.0 to 2.0.1+**: domain namespaces (including `.Data` as of 2.0.1) were removed; use only `@using Syncfusion.Blazor.Toolkit`.

## Next Steps

After installation, use the component demos or component-specific guidance for API details; this skill only covers setup and configuration.

## References

- [Package identity](./references/package-identity.md)
- [Split Blazor Web App registration](./references/split-webapp-registration.md)
- [Theme and host files](./references/theme-and-host-files.md)
- [Render modes explained](./references/render-modes.md)
- [Troubleshooting guide](./references/troubleshooting.md)
