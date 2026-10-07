# Troubleshooting Toolkit Installation

> **Framework support.** `Syncfusion.Blazor.Toolkit` 1.0.2 targets `net8.0`, `net9.0`, and `net10.0`. On an older `<TargetFramework>` (for example `net6.0` or `net7.0`) restore **succeeds** but supplies no assemblies, so the build fails with `CS0246: The type or namespace name 'Syncfusion' could not be found` in `Program.cs`. Recommend upgrading the app to .NET 8 or later first; do not edit the TFM just to make the package install.

## Problem: Theme CSS not loading (404) or components unstyled

**Symptoms**:
- Components appear in the page but have no colors, borders, or styling
- DevTools Network tab shows the CSS file returns 404 (not found)
- Or the file returns 200 but components still appear unstyled

**Root cause**: Theme CSS link is missing, using the wrong path, wrong filename, wrong theme name, or linked in the wrong host file.

**Diagnosis**:
1. Check the correct host file for your project type:
   - Blazor Web App: `Components/App.razor`
   - Legacy Blazor Server: `_Host.cshtml`
   - Blazor WebAssembly: `wwwroot/index.html`
2. Look for the CSS link in the `<head>` section:
   ```html
   <link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
   ```
3. If found, verify:
   - Path is **exactly** `_content/Syncfusion.Blazor.Toolkit/styles/` (not `themes/`)
   - Filename is `fluent.min.css` or `highcontrast.min.css`. Bootstrap, Tailwind, and Material files are not in this package.
   - No typos; `.min` extension is required
4. If missing, add it to the `<head>` section
5. Open browser DevTools (F12) Network tab and verify the CSS file loads with status 200 (not 404)

**Common mistakes**:
- Wrong path: `_content/Syncfusion.Blazor.Toolkit/themes/fluent.min.css` (should be `styles/`)
- Wrong stylesheet name: `bootstrap5.min.css`, `tailwind.min.css`, `material.min.css` (use `fluent.min.css` or `highcontrast.min.css`)
- Missing `.min` extension: `fluent.css` (should be `.min.css`)
- Linked in wrong host file: use `Components/App.razor` for a Blazor Web App, `_Host.cshtml` for legacy Blazor Server, or `wwwroot/index.html` for standalone WebAssembly
- Linked in a component file instead of the host file (always place it in the host file's `<head>` section for best results)

**Fix**:
```html
<!-- In the <head> section of the host file (App.razor for Web App, index.html for WASM, _Host.cshtml for legacy Server) -->
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```

---

## Problem: Services not configured

**Symptoms**:
- The page returns HTTP 500 (also for static SSR pages that render a Toolkit component)
- The log shows: `InvalidOperationException: Cannot provide a value for property 'SyncfusionService' on type 'Syncfusion.Blazor.Toolkit.Buttons.SfButton'. There is no registered service of type 'Syncfusion.Blazor.Toolkit.SyncfusionBlazorToolkitService'.`

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

**Root cause**: The component is static, or an interactive child is trying to switch modes. A missing `@rendermode` does not by itself mean the component is static.

**Diagnosis**:
1. Identify the app. Legacy Blazor Server and standalone WebAssembly are already interactive; do not add `@rendermode`.
2. In a Blazor Web App, open `Components/App.razor` and check `Routes`. If `Routes` already has an interactive mode, the page inherits it. A child cannot switch to a different interactive mode.
3. If neither the page nor `Routes` has an interactive mode, the page is static SSR.
4. If a mode is already present and clicks still do nothing, check the circuit, the browser console, and failed `_content` script requests before editing the page.

**Fix** (only when no interactive mode is inherited):
```razor
@page "/mypage"
@* Add one mode that matches the app's configured interactivity *@
@rendermode InteractiveServer
@using Syncfusion.Blazor.Toolkit.Buttons

<SfButton OnClick="OnClick">Click me</SfButton>

@code {
    private void OnClick()
    {
        Console.WriteLine("Clicked!");
    }
}
```

Or for a Blazor Web App using WebAssembly interactivity (`dotnet new blazor -int WebAssembly`):
```razor
@page "/mypage"
@rendermode InteractiveWebAssembly
@using Syncfusion.Blazor.Toolkit.Buttons

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
2. The project targets `net7.0` or older. Restore succeeds but supplies no assemblies, so this exact error appears. Upgrade to .NET 8 or later.

**Diagnosis**:
1. Check the `.csproj` file for:
   ```xml
   <PackageReference Include="Syncfusion.Blazor.Toolkit" Version="1.0.2" />
   ```
2. Check `<TargetFramework>` is `net8.0`, `net9.0`, or `net10.0`.

**Fix**:
1. Install the package in each project that calls the registration:
   ```bash
   dotnet add package Syncfusion.Blazor.Toolkit
   ```
2. A root `@using Syncfusion.Blazor.Toolkit` in `_Imports.razor` is optional and does not affect `.cs` files.

---

## Problem: Component type not found (`SfButton` and similar)

**Symptoms**:
- Build **succeeds** with warning `RZ10012: Found markup element with unexpected name 'SfButton'. If this is intended to be a component, add a @using directive for its namespace.`
- At runtime the tag renders as an unknown HTML element: no `e-btn` class, no styling, and `OnClick` does nothing
- There is **no** `CS0246` for a missing component namespace in a `.razor` file

**Root cause**: Toolkit **component types do not live in the root `Syncfusion.Blazor.Toolkit` namespace**. Each component family has its own child namespace (`Syncfusion.Blazor.Toolkit.Buttons`, `Syncfusion.Blazor.Toolkit.Calendars`, `Syncfusion.Blazor.Toolkit.Inputs`, etc.). The root namespace holds enums and the registration extension class, not components.

**Fix**:
1. Add the **per-component namespace** to `_Imports.razor`. For example, when using `SfButton` (the root using is optional):
   ```razor
   @using Syncfusion.Blazor.Toolkit.Buttons
   ```
2. Match the component to its namespace (verified against package 1.0.2): `SfButton`, `SfButtonGroup` → `Buttons`; `SfCalendar`, `SfDatePicker`, `SfDateTimePicker`, `SfTimePicker` → `Calendars`; `SfTextBox`, `SfTextArea`, `SfNumericTextBox`, `SfCheckBox`, `SfRadioButton`, `SfSwitch`, `SfUploader` → `Inputs`; `SfChart` → `Charts`; `SfDialog`, `SfTooltip` → `Popups`; `SfSpinner` → `Spinner`. There is no Toolkit grid component. If a type still does not resolve, check the installed version's IntelliSense.
3. The per-component namespace **must** be added to the project that owns the component. In a split Web App, add `@using Syncfusion.Blazor.Toolkit.Buttons` to **both** the Server and `.Client` `_Imports.razor` files when both render `SfButton`.

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

Also ensure both projects have the NuGet package reference in their `.csproj` files and both have namespace imports in their `_Imports.razor` files. Add the per-component namespace (e.g. `@using Syncfusion.Blazor.Toolkit.Buttons` for `SfButton`) to both `_Imports.razor` files when both render the component.

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
2. ✓ Package name is `Syncfusion.Blazor.Toolkit` (not a commercial `Syncfusion.Blazor.*` package)
3. ✓ `using Syncfusion.Blazor.Toolkit;` and `AddSyncfusionBlazorToolkit()` are in `Program.cs` (in both Server and Client projects of a split Web App)
4. ✓ `@using Syncfusion.Blazor.Toolkit` is in `_Imports.razor` (in both projects of a split Web App). Per-component namespaces (e.g. `@using Syncfusion.Blazor.Toolkit.Buttons` for `SfButton`) are added to the `_Imports.razor` of every project that renders the component.
5. ✓ The template's `AddAdditionalAssemblies(...)` and `AddInteractive*` calls are unchanged; only the Toolkit `using` and `AddSyncfusionBlazorToolkit()` were added.
6. ✓ Stylesheet link is `fluent.min.css` or `highcontrast.min.css` in the host file's `<head>` (`Components/App.razor` for a Blazor Web App, `_Host.cshtml` for legacy Blazor Server, `wwwroot/index.html` for standalone WebAssembly). The link is **added** to the existing file — `HeadOutlet`, `Routes`, the render-mode directive, and the framework script tag are preserved.
7. ✓ Path uses `styles/`, not `themes/`, and keeps the `.min` extension
8. ✓ Blazor Web App interactivity is inherited from `Routes` or declared once. Do not add `@rendermode` to legacy Server or standalone WebAssembly
9. ✓ Commercial packages and `SyncfusionLicenseProvider.RegisterLicense(...)` remain only where commercial components are still used
10. ✓ Build completes without errors: `dotnet build`
11. ✓ Browser console shows no 404 or JavaScript errors

