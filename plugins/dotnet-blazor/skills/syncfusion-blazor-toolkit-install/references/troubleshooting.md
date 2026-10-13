# Troubleshooting Toolkit Installation

> **Framework support.** `Syncfusion.Blazor.Toolkit` 2.0.0 targets `net8.0`, `net9.0`, and `net10.0`. Always check the project's `<TargetFramework>` before installation. On `net6.0`, `net7.0`, or older frameworks, NuGet restore fails with an incompatibility such as `NU1202` because the package has no compatible assets for those TFMs. Do not expect a later `CS0246` as the primary signal—fix the framework mismatch first. Do not proceed with Toolkit installation on unsupported frameworks. Recommend upgrading the application to .NET 8 or later first, then continue with the installation steps.

## Problem: Components unstyled

**Symptoms**:
- Components appear in the page but have no colors, borders, or styling
- Or only some visual tokens are missing

**Root cause (2.0)** — theming does **not** depend on linking `fluent.min.css`. Check in order:
1. Package `Syncfusion.Blazor.Toolkit` 2.0+ is referenced and restored.
2. `AddSyncfusionBlazorToolkit()` is registered on every host that renders Toolkit components.
3. `@using Syncfusion.Blazor.Toolkit` is in `_Imports.razor` of the project that owns the markup.
4. At least one Toolkit component is in the render tree (so `SfThemeRoot` emits the shared layer), **or** an explicit `<SfThemeRoot />` is present.
5. The template still links the app's own styles (`app.css`, `{Project}.styles.css`); those were not deleted while editing the host.
6. Custom CSS written for 1.x unscoped class names may need `::deep` or isolation-aware selectors.

**1.x leftovers**: If the host still has
```html
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```
remove it on a 2.0 upgrade; it is not part of the required setup. A 404 on that path after removal is expected and is not a failure.

**Fix**: Install/register/import as above; ensure a Toolkit component or `<SfThemeRoot />` is rendered; keep template CSS links; adjust custom overrides for isolation if needed.

---

## Problem: Services not configured

**Symptoms**:
- The page returns HTTP 500 (also for static SSR pages that render a Toolkit component)
- The log shows: `InvalidOperationException: Cannot provide a value for property 'SyncfusionService' on type 'Syncfusion.Blazor.Toolkit.SfButton'. There is no registered service of type 'Syncfusion.Blazor.Toolkit.SyncfusionBlazorToolkitService'.`

**Root cause**: `AddSyncfusionBlazorToolkit()` is not called on a host that renders a Toolkit component.

**Diagnosis**:
1. Open `Program.cs` (or the Server/Client `Program.cs` in split Web Apps)
2. Look for:
   ```csharp
   builder.Services.AddSyncfusionBlazorToolkit();
   ```
3. If missing, add it before `builder.Build()`.

**Fix**: add only these two lines to the existing `Program.cs`; leave every other template line as generated.
```csharp
using Syncfusion.Blazor.Toolkit;                       // top of the file

builder.Services.AddSyncfusionBlazorToolkit();         // before builder.Build()
```

---

## Problem: Click events or form inputs don't work

**Symptoms**:
- Toolkit component is visible and styled correctly
- Clicking buttons or entering text in forms has no effect
- No errors in the browser console

**Root cause**: In a Blazor Web App the component is on a static SSR page. `dotnet new blazor -int Server`, `-int WebAssembly` and `-int Auto` are **per-page**: `<Routes />` has no `@rendermode`, so a page is static unless it declares its own. Only `-ai` (`--all-interactive`) makes the whole app interactive. A less common cause is an interactive child trying to switch to a different mode than the one it inherits.

**Diagnosis**:
1. Identify the app. Legacy Blazor Server and standalone WebAssembly are already interactive; do not add `@rendermode`.
2. In a Blazor Web App, open `Components/App.razor` and check `Routes`. If `Routes` carries `@rendermode="..."` (a `-ai` app), every page inherits it. A child cannot switch to a different interactive mode.
3. If `<Routes />` has no directive (a per-page app) and the page has no `@rendermode`, the page is static SSR. That is the cause.
4. If a mode is already in effect and clicks still do nothing, check the circuit, the browser console, and failed `_content` script requests before editing the page.

**Fix** (per-page app, page has no interactive mode): add exactly one directive matching the template (`InteractiveServer` for `-int Server`, `InteractiveWebAssembly` for `-int WebAssembly`, `InteractiveAuto` for `-int Auto`) to the page that hosts the component:
```razor
@page "/mypage"
@* Add one mode that matches the app's configured interactivity *@
@rendermode InteractiveServer
@using Syncfusion.Blazor.Toolkit

<SfButton OnClick="OnClick">Click me</SfButton>

@code {
    private void OnClick()
    {
        Console.WriteLine("Clicked!");
    }
}
```

Or, for a per-page Blazor Web App created with `dotnet new blazor -int WebAssembly` (the page lives in `.Client`):
```razor
@page "/mypage"
@rendermode InteractiveWebAssembly
@using Syncfusion.Blazor.Toolkit

<SfButton OnClick="OnClick">Click me</SfButton>

@code {
    private void OnClick()
    {
        Console.WriteLine("Clicked!");
    }
}
```

---

## Problem: `using Syncfusion.Blazor.Toolkit;` fails in `Program.cs`

**Symptoms**:
- `CS0246: The type or namespace name 'Syncfusion' could not be found` on the `using` line in a `.cs` file

**Root cause** (check in this order):
1. The package is not referenced in **that project's** `.csproj`. In a split Web App both the server and `.Client` projects need the `PackageReference`.
2. The project targets `net6.0`, `net7.0`, or older. NuGet restore fails (for example `NU1202`) because no compatible Toolkit assets exist for that TFM. Upgrade to .NET 8 or later first, then restore again.

**Diagnosis**:
1. Check the `.csproj` file for:
   ```xml
   <PackageReference Include="Syncfusion.Blazor.Toolkit" Version="2.0.0" />
   ```
2. Check `<TargetFramework>` is `net8.0`, `net9.0`, or `net10.0`.

**Fix**:
1. Install the package in each project that calls the registration:
   ```bash
   dotnet add package Syncfusion.Blazor.Toolkit
   ```
2. A root `@using Syncfusion.Blazor.Toolkit` in `_Imports.razor` is required for component markup and does not affect `.cs` files by itself.

---

## Problem: Component type not found (`SfButton` and similar)

**Symptoms**:
- Build **succeeds** with warning `RZ10012: Found markup element with unexpected name 'SfButton'. If this is intended to be a component, add a @using directive for its namespace.`
- At runtime the tag renders as an unknown HTML element: no styling, and `OnClick` does nothing
- There is **no** `CS0246` for a missing component namespace in a `.razor` file

**Root cause (2.0)**: All public component types live in the single root namespace `Syncfusion.Blazor.Toolkit`. Domain-specific namespaces from 1.x (`Syncfusion.Blazor.Toolkit.Buttons`, `.Calendars`, `.Charts`, `.Inputs`, `.Popups`, `.Spinner`, etc.) were removed. The root namespace holds components, enums, and the registration extension class.

**Fix**:
1. Add the single root namespace to `_Imports.razor`:
   ```razor
   @using Syncfusion.Blazor.Toolkit
   ```
2. Remove any leftover 1.x domain-specific `@using` directives; they no longer resolve.
3. The root namespace **must** be added to the project that owns the component. In a split Web App, add `@using Syncfusion.Blazor.Toolkit` to **both** the Server and `.Client` `_Imports.razor` files when both render Toolkit components.

---

## Problem: Unresolved types after upgrading from Toolkit 1.x to 2.0

**Symptoms**:
- `CS0234: The type or namespace name 'Buttons' does not exist in the namespace 'Syncfusion.Blazor.Toolkit'`
- Or similar for `Calendars`, `Charts`, `Inputs`, `Popups`, `Spinner`

**Root cause**: Namespace consolidation in 2.0 removed all domain-specific namespaces.

**Fix**: Replace every `@using Syncfusion.Blazor.Toolkit.<Domain>` and every fully qualified type that used a child namespace with the single root `Syncfusion.Blazor.Toolkit`.

---

## Problem: Routable pages in `.Client` return 404 after editing `Program.cs`

**Symptoms**:
- Pages defined in the `.Client` project no longer route
- It started after `Program.cs` was edited or replaced

**Root cause**: The template's `.AddAdditionalAssemblies(typeof(<App>.Client._Imports).Assembly)` call on `MapRazorComponents<App>()` was removed. This is unrelated to Toolkit registration; it lets the router discover `.Client` pages.

**Fix**: Restore the call on `MapRazorComponents<App>()`, exactly as the template generated it. Do not move it onto `AddRazorComponents()`.

---

## Problem: Split Web App: components work in Server but not in Client

**Symptoms**:
- Toolkit components work fine in Server-side pages
- Same components in Client pages fail or don't render

**Root cause**: `AddSyncfusionBlazorToolkit()` is missing in one of the two hosts. With client-only registration, `.Client` pages also fail on first load (HTTP 500), because prerendering runs them on the server.

**Diagnosis**:
1. Check both `Program.cs` files (Server and Client)
2. Look for `AddSyncfusionBlazorToolkit()` in each
3. If only one has it, the other is missing registration

**Fix**:
- Add `using Syncfusion.Blazor.Toolkit;` and `builder.Services.AddSyncfusionBlazorToolkit();` to `.Client/Program.cs`. Do not add `RootComponents.Add<App>("#app")`.
- Also register Toolkit in the server `Program.cs` while prerendering is enabled. Keep the generated `AddInteractiveWebAssemblyComponents()` and `AddInteractiveWebAssemblyRenderMode()` calls.
- Leave the template's `AddAdditionalAssemblies(...)` call on `MapRazorComponents<App>()` untouched.

Also ensure both projects have the NuGet package reference in their `.csproj` files and both have `@using Syncfusion.Blazor.Toolkit` in their `_Imports.razor` files.

---

## Problem: Wrong package installed (commercial Syncfusion instead of Toolkit)

**Symptoms**:
- You see `Syncfusion.Blazor` or `Syncfusion.Blazor.Buttons` in the `.csproj` instead of `Syncfusion.Blazor.Toolkit`
- Code references license keys or methods like `AddSyncfusionLicense()`
- Components work but you're on a commercial license

**Root cause**: The commercial package was installed for a request that asked for the open-source Toolkit. Confirm that before deleting anything.

**Diagnosis**:
1. Check the `.csproj` and search the app for `Syncfusion.Blazor` component usage.
2. License registration, when present, uses `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(...)`. There is no `AddSyncfusionLicense()` API.

**Fix**:
1. If Toolkit is replacing every commercial component, remove the unused commercial package and its `RegisterLicense` call, then add `Syncfusion.Blazor.Toolkit`.
2. If any page still uses a commercial component, leave that package and its license registration alone. Add `Syncfusion.Blazor.Toolkit` beside it.
3. Do not invent a license key for the Toolkit package. Toolkit does not require one.

---

## Problem: JavaScript errors in browser console

**Symptoms**:
- Browser DevTools Console shows JavaScript errors related to Toolkit or Syncfusion
- Components may not respond or may crash
- Errors mention "script", "undefined", or Syncfusion library functions

**Root cause**: Toolkit JavaScript is not loading correctly, or there's a conflict with browser scripts.

**Diagnosis**:
1. Open browser DevTools (F12) and check the Console tab
2. Look for errors that mention Syncfusion, Toolkit, or JS loading
3. Check the Network tab to verify JavaScript files are loading (look for `_content/` files)
4. If 404 errors appear, the package may not be installed correctly
5. If "undefined" errors appear, services may not be registered or JS is loading before services

**Fix**:
1. Ensure `AddSyncfusionBlazorToolkit()` is called in `Program.cs` before any components render
2. Verify the package is installed: `dotnet list package` and check if `Syncfusion.Blazor.Toolkit` is listed
3. Clear browser cache (Ctrl+Shift+Delete) and reload
4. Rebuild and republish: `dotnet build` and `dotnet publish`
5. Check that no other scripts are conflicting (disable extensions, try incognito mode)

---

## Problem: Script fails to load during component initialization

**Symptoms**:
- Console shows errors like "Uncaught ReferenceError" or "undefined" when component renders
- Components appear briefly then disappear or show errors
- Toolkit JavaScript functions are not available

**Root cause**: Toolkit script files are not loading correctly, or services are not registered before components attempt to use them.

**Diagnosis**:
1. Open browser DevTools (F12) Network tab
2. Look for files in `_content/Syncfusion.Blazor.Toolkit/` directory
3. Check if any show 404 status (not found)
4. Verify `AddSyncfusionBlazorToolkit()` is registered **before** `builder.Build()` in `Program.cs`
5. Check if any page/component is rendering before services are initialized

**Fix**:
1. Ensure service registration is early in `Program.cs`:
   ```csharp
   var builder = WebApplication.CreateBuilder(args);
   builder.Services.AddRazorComponents()
       .AddInteractiveServerComponents();
   builder.Services.AddSyncfusionBlazorToolkit(); // <-- Must be before Build()
   var app = builder.Build();
   ```
2. Rebuild and clear cache:
   ```bash
   dotnet clean
   dotnet build
   ```
3. Clear browser cache (Ctrl+Shift+Delete) and reload (Ctrl+F5)
4. For split Web App, ensure **both** Server and Client `Program.cs` files have the registration
5. Verify the package is installed: `dotnet list package` and check if `Syncfusion.Blazor.Toolkit` is listed

---

## General Checklist

Before troubleshooting further, verify:

1. ✓ `<TargetFramework>` in the `.csproj` is `net8.0`, `net9.0`, or `net10.0`. If it is older, recommend upgrading first.
2. ✓ Package name is `Syncfusion.Blazor.Toolkit` (not a commercial `Syncfusion.Blazor.*` package), version 2.0.0 or newer preferred
3. ✓ `using Syncfusion.Blazor.Toolkit;` and `AddSyncfusionBlazorToolkit()` are in `Program.cs` (in both Server and Client projects of a split Web App)
4. ✓ `@using Syncfusion.Blazor.Toolkit` is in `_Imports.razor` (in both projects of a split Web App). Do **not** use domain-specific namespaces from Toolkit 1.x.
5. ✓ The template's `AddAdditionalAssemblies(...)` and `AddInteractive*` calls are unchanged; only the Toolkit `using` and `AddSyncfusionBlazorToolkit()` were added.
6. ✓ No Toolkit theme `<link>` is required; 1.x `fluent.min.css` / `highcontrast.min.css` links may be removed. Host still has template `app.css` / `{Project}.styles.css` links. `SfThemeRoot` runs via Toolkit components (or an explicit tag).
7. ✓ Blazor Web App interactivity is inherited from `Routes` or declared once. Do not add `@rendermode` to legacy Server or standalone WebAssembly
8. ✓ Commercial packages and `SyncfusionLicenseProvider.RegisterLicense(...)` remain only where commercial components are still used
9. ✓ Build completes without errors: `dotnet build`
10. ✓ Browser console shows no unexpected 404 or JavaScript errors (a 404 on a removed 1.x theme path is fine)
11. ✓ After a 1.x → 2.0 upgrade, custom CSS overrides account for CSS isolation if needed
