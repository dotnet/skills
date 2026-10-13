# Troubleshooting Toolkit Installation

> **Framework support.** `Syncfusion.Blazor.Toolkit` 2.0.0 targets `net8.0`, `net9.0`, and `net10.0`. Always check the project's `<TargetFramework>` before installation. On `net6.0`, `net7.0`, or older frameworks, NuGet restore fails with an incompatibility such as `NU1202` because the package has no compatible assets for those TFMs. Do not expect a later `CS0246` as the primary signal—fix the framework mismatch first. Do not proceed with Toolkit installation on unsupported frameworks. Recommend upgrading the application to .NET 8 or later first, then continue with the installation steps.

## Host files by app type

Use the host that matches the app. Do not send every user to `Components/App.razor`.

| App type | Host file (template CSS only; no Toolkit theme link in 2.0) |
| --- | --- |
| Legacy Blazor Server | `_Host.cshtml` (or `_Layout.cshtml` in some older layouts) |
| Standalone Blazor WebAssembly | `wwwroot/index.html` |
| Blazor Web App (Server / WebAssembly / Auto) | `Components/App.razor` |

---

## Problem: Components unstyled

**Symptoms**:
- Components appear in the page but have no colors, borders, or styling
- Or only some visual tokens are missing

**Root cause (2.0)** — theming does **not** depend on linking `fluent.min.css`. Check in order:
1. Package `Syncfusion.Blazor.Toolkit` 2.0+ is referenced and restored.
2. `AddSyncfusionBlazorToolkit()` is registered on every host that renders Toolkit components.
3. `@using Syncfusion.Blazor.Toolkit` is in `_Imports.razor` of the project that owns the markup.
4. At least one Toolkit component is in the render tree (so `SfThemeRoot` emits the shared layer), **or** an explicit `<SfThemeRoot />` is present.
5. The template still links the app's own styles (`app.css`, `{Project}.styles.css`) in the correct host file for this app type (table above); those were not deleted while editing the host.
6. Custom CSS written for 1.x unscoped class names may need `::deep` or isolation-aware selectors.

**1.x leftovers**: If the host still has
```html
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```
remove it on a 2.0 upgrade; it is not part of the required setup. A 404 while the obsolete link is still present confirms the stale path; once the link is removed (and the browser cache is cleared), the browser should no longer request it.

**Fix**: Install/register/import as above; ensure a Toolkit component or `<SfThemeRoot />` is rendered; keep template CSS links in the correct host; adjust custom overrides for isolation if needed.

---

## Problem: Services not configured

**Symptoms**:
- The page returns HTTP 500 (also for static SSR pages that render a Toolkit component)
- The log shows: `InvalidOperationException: Cannot provide a value for property 'SyncfusionService' on type 'Syncfusion.Blazor.Toolkit.SfButton'. There is no registered service of type 'Syncfusion.Blazor.Toolkit.SyncfusionBlazorToolkitService'.`

**Root cause**: `AddSyncfusionBlazorToolkit()` is not called on a host that renders a Toolkit component.

**Diagnosis**:
1. Open every `Program.cs` that hosts Toolkit markup (server and, in a split Web App, `.Client`).
2. Look for:
   ```csharp
   builder.Services.AddSyncfusionBlazorToolkit();
   ```
3. If missing, add it before the host is built.

**Fix**: add only the Toolkit lines; leave the rest of the template alone.

Server / legacy Server / Web App server `Program.cs`:
```csharp
using Syncfusion.Blazor.Toolkit;                       // top of the file

builder.Services.AddSyncfusionBlazorToolkit();         // before builder.Build()
```

Blazor Web App `.Client/Program.cs` (WebAssembly or Auto templates):
```csharp
using Syncfusion.Blazor.Toolkit;                       // top of the file

builder.Services.AddSyncfusionBlazorToolkit();         // before await builder.Build().RunAsync()
```

Do not paste a full server `WebApplication.CreateBuilder` sample into `.Client` — that project uses `WebAssemblyHostBuilder.CreateDefault(args)`.

---

## Problem: Click events or form inputs don't work

**Symptoms**:
- Toolkit component is visible and styled correctly
- Clicking buttons or entering text in forms has no effect
- No errors in the browser console

**Root cause**: In a Blazor Web App the control is outside any **interactive boundary**. `dotnet new blazor -int Server`, `-int WebAssembly` and `-int Auto` are **per-page**: `<Routes />` has no `@rendermode`, so a page is static unless the page or a child wrapper declares a mode. Only `-ai` makes the whole app interactive via `Routes`.

**Diagnosis**:
1. Identify the app. **Legacy Blazor Server** and **standalone WebAssembly** are already interactive — do **not** add `@rendermode`.
2. In a Blazor Web App, open `Components/App.razor` and check `Routes` **first**. If `Routes` carries `@rendermode="..."` (a `-ai` app), every page inherits it. A child cannot switch to a different interactive mode.
3. If `<Routes />` has no directive (per-page) and neither the page nor a wrapper has `@rendermode`, the control is static SSR.
4. If a mode is already in effect and clicks still do nothing, check the circuit, the browser console, and failed `_content` script requests **before** editing the page.

**Fix** (per-page Web App, no interactive boundary yet): either

- add exactly one page directive matching the template, or
- keep the page static and put the same directive on a **wrapper component** that hosts the Toolkit control.

Page-level example (`InteractiveServer` for `-int Server`; use `InteractiveWebAssembly` / `InteractiveAuto` when those templates apply — **Web App only**, not standalone WASM):

```razor
@page "/counter"
@rendermode InteractiveServer
@using Syncfusion.Blazor.Toolkit

<SfButton OnClick="IncrementCount">Increment</SfButton>
```

---

## Problem: Wrong package or license prompts

**Symptoms**:
- License-key prompts, `AddSyncfusionLicense()`, or commercial package references after intending to use the open-source Toolkit

**Root cause**: `Syncfusion.Blazor` (commercial) was installed instead of `Syncfusion.Blazor.Toolkit`.

**Fix**:
1. Confirm the intended package is `Syncfusion.Blazor.Toolkit`.
2. **Before removing** `Syncfusion.Blazor` or `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(...)`, search the solution for commercial component usage. Only remove the commercial package and license registration when the app no longer needs those components.
3. If the app still uses commercial Syncfusion UI elsewhere, leave that package and license registration in place and add Toolkit alongside it only when appropriate.

---

## Problem: Namespace / unknown element after 1.x → 2.0

**Symptoms**:
- `CS0234` on `Syncfusion.Blazor.Toolkit.Buttons` (or other domain namespaces)
- Razor `RZ10012`; tags render as unknown HTML

**Fix**: replace domain usings with the single root import. In 2.0, types such as `SfButton` live under `Syncfusion.Blazor.Toolkit`:

```razor
@using Syncfusion.Blazor.Toolkit
```

---

## Problem: JavaScript / network errors

**Symptoms**:
- Console errors related to Syncfusion scripts
- Components do not respond to clicks

**Diagnosis**:
1. Confirm `AddSyncfusionBlazorToolkit()` runs before the host is built.
2. Confirm an interactive boundary exists in a Blazor Web App (see above).
3. In DevTools Network, filter `_content/Syncfusion.Blazor.Toolkit/` and note 404s on theme paths: a 404 means a host still has (or the browser still caches) an obsolete 1.x stylesheet request—remove the `<link>` and hard-refresh so the request stops.
4. Do **not** add commercial CDN script tags for the Toolkit; JS is embedded in the package.

**Fix (server example only — keep the template's interactive chain)**:
```csharp
using Syncfusion.Blazor.Toolkit;

// ... template AddRazorComponents / MapRazorComponents unchanged ...

builder.Services.AddSyncfusionBlazorToolkit(); // before Build()
```

For split Web Apps, register on **both** Server and Client `Program.cs`. Rebuild, then hard-refresh the browser.

---

## Problem: Unsupported target framework

**Symptoms**:
- Restore fails with `NU1202` or similar when adding `Syncfusion.Blazor.Toolkit` on net6.0 / net7.0

**Fix**: upgrade the app to .NET 8 or later first, then install the Toolkit. Do not change the TFM only to force a restore.

---

## General Checklist

1. ✓ `<TargetFramework>` is `net8.0`, `net9.0`, or `net10.0`. If older, upgrade first.
2. ✓ Package is `Syncfusion.Blazor.Toolkit` (not commercial `Syncfusion.Blazor.*` unless still required), version 2.0.0 or newer preferred.
3. ✓ `using Syncfusion.Blazor.Toolkit;` and `AddSyncfusionBlazorToolkit()` are in every host `Program.cs` that renders Toolkit (server and Client in a split Web App).
4. ✓ `@using Syncfusion.Blazor.Toolkit` is in `_Imports.razor` of projects that render Toolkit. No 1.x domain namespaces.
5. ✓ Template `AddAdditionalAssemblies(...)` and `AddInteractive*` calls are unchanged.
6. ✓ No Toolkit theme `<link>` is required; 1.x `fluent.min.css` / `highcontrast.min.css` links may be removed. Template `app.css` / `{Project}.styles.css` remain in the **correct host file** for the app type.
7. ✓ Blazor Web App interactivity uses an interactive boundary (inherited `Routes`, page `@rendermode`, or wrapper). Do not add `@rendermode` to legacy Server or standalone WebAssembly.
8. ✓ Commercial packages and `RegisterLicense` remain only where commercial components are still used.
9. ✓ `dotnet build` succeeds.
10. ✓ Browser console has no unexpected JS errors; remaining 404s on `styles/fluent.min.css` (or similar) mean a stale `<link>` or cache—remove the link and hard-refresh until the request disappears.
11. ✓ After 1.x → 2.0, custom CSS accounts for isolation if needed.
