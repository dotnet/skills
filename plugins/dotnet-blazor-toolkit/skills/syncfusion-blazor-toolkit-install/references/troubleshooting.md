# Troubleshooting Toolkit Installation

## Problem: Theme CSS not loading (404) or components unstyled

**Symptoms**:
- Components appear in the page but have no colors, borders, or styling
- DevTools Network tab shows the CSS file returns 404 (not found)
- Or the file returns 200 but components still appear unstyled

**Root cause**: Theme CSS link is missing, using the wrong path, wrong filename, wrong theme name, or linked in the wrong host file.

**Diagnosis**:
1. Check the correct host file for your project type:
   - Blazor Web App / modern Blazor Server: `App.razor`
   - Legacy Blazor Server: `_Host.cshtml`
   - Blazor WebAssembly: `wwwroot/index.html`
2. Look for the CSS link in the `<head>` section:
   ```html
   <link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
   ```
3. If found, verify:
   - Path is **exactly** `_content/Syncfusion.Blazor.Toolkit/styles/` (not `themes/`)
   - Filename is **exactly** `fluent.min.css` (Toolkit only supports Fluent; no Bootstrap, Tailwind, or Material)
   - No typos; `.min` extension is required
4. If missing, add it to the `<head>` section
5. Open browser DevTools (F12) Network tab and verify the CSS file loads with status 200 (not 404)

**Common mistakes**:
- Wrong path: `_content/Syncfusion.Blazor.Toolkit/themes/fluent.min.css` (should be `styles/`)
- Wrong theme name: `bootstrap5.min.css`, `tailwind.min.css`, `material.min.css` (must be `fluent.min.css`)
- Missing `.min` extension: `fluent.css` (should be `.min.css`)
- Linked in wrong host file: use `App.razor` for Blazor Web App/Server or `wwwroot/index.html` for WebAssembly
- Linked in a component file instead of the host file (always place it in the host file's `<head>` section for best results)

**Fix**:
```html
<!-- In the <head> section of the host file (App.razor for Web App, index.html for WASM, _Host.cshtml for legacy Server) -->
<link href="_content/Syncfusion.Blazor.Toolkit/styles/fluent.min.css" rel="stylesheet" />
```

---

## Problem: Services not configured

**Symptoms**:
- Runtime error when component tries to use Toolkit services
- Error message mentions missing or null service

**Root cause**: `AddSyncfusionBlazorToolkit()` is not called in `Program.cs`.

**Diagnosis**:
1. Open `Program.cs` (or the Server/Client `Program.cs` in split Web Apps)
2. Look for:
   ```csharp
   builder.Services.AddSyncfusionBlazorToolkit();
   ```
3. If missing, add it before `builder.Build()`.

**Fix**:
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();
builder.Services.AddSyncfusionBlazorToolkit();  // <-- Add this

var app = builder.Build();
```

---

## Problem: Click events or form inputs don't work

**Symptoms**:
- Toolkit component is visible and styled correctly
- Clicking buttons or entering text in forms has no effect
- No errors in the browser console

**Root cause**: Component is rendered in static SSR mode; interactive render mode is not applied.

**Diagnosis**:
1. Check the component or page definition:
   - Look for `@rendermode InteractiveServer` or `@rendermode InteractiveWebAssembly`
   - If missing, the component is static
2. Confirm the interaction type (Server or WebAssembly)

**Fix**:
```razor
@page "/mypage"
@* Add this line *@
@rendermode InteractiveServer

<SfButton @onclick="OnClick">Click me</SfButton>

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

<SfButton @onclick="OnClick">Click me</SfButton>

@code {
    private void OnClick()
    {
        Console.WriteLine("Clicked!");
    }
}
```

---

## Problem: `Syncfusion.Blazor.Toolkit` namespace not found

**Symptoms**:
- Compilation error: "The type or namespace name 'Syncfusion' could not be found"
- IntelliSense doesn't show Toolkit types

**Root cause**: The NuGet package is not installed, or namespaces are not imported.

**Diagnosis**:
1. Check the `.csproj` file for:
   ```xml
   <PackageReference Include="Syncfusion.Blazor.Toolkit" Version="..." />
   ```
2. If missing, install the package.
3. Check `_Imports.razor` (or component file) for:
   ```razor
   @using Syncfusion.Blazor.Toolkit
   ```
4. If missing, add the namespace import.

**Fix**:
1. Install the package:
   ```bash
   dotnet add package Syncfusion.Blazor.Toolkit
   ```
2. Add to `_Imports.razor`:
   ```razor
   @using Syncfusion.Blazor.Toolkit
   ```

---

## Problem: Split Web App: components work in Server but not in Client

**Symptoms**:
- Toolkit components work fine in Server-side pages
- Same components in Client pages fail or don't render

**Root cause**: `AddSyncfusionBlazorToolkit()` is registered in Server `Program.cs` but not in Client `Program.cs`.

**Diagnosis**:
1. Check both `Program.cs` files (Server and Client)
2. Look for `AddSyncfusionBlazorToolkit()` in each
3. If only one has it, the other is missing registration

**Fix**:
- **Server/Program.cs**:
  ```csharp
  builder.Services.AddSyncfusionBlazorToolkit();
  ```
- **Client/Program.cs**:
  ```csharp
  builder.Services.AddSyncfusionBlazorToolkit();
  ```

Also ensure both projects have the NuGet package reference in their `.csproj` files and both have namespace imports in their `_Imports.razor` files.

---

## Problem: Wrong package installed (commercial Syncfusion instead of Toolkit)

**Symptoms**:
- You see `Syncfusion.Blazor` or `Syncfusion.Blazor.Buttons` in the `.csproj` instead of `Syncfusion.Blazor.Toolkit`
- Code references license keys or methods like `AddSyncfusionLicense()`
- Components work but you're on a commercial license

**Root cause**: The commercial Syncfusion.Blazor package was installed instead of the open-source Toolkit.

**Diagnosis**:
1. Check the `.csproj` file:
   ```xml
   <!-- Wrong -->
   <PackageReference Include="Syncfusion.Blazor" Version="..." />
   
   <!-- Correct -->
   <PackageReference Include="Syncfusion.Blazor.Toolkit" Version="..." />
   ```
2. Check `Program.cs` for license-key setup.

**Fix**:
1. Uninstall the commercial package:
   ```bash
   dotnet remove package Syncfusion.Blazor
   ```
2. Install the Toolkit:
   ```bash
   dotnet add package Syncfusion.Blazor.Toolkit
   ```
3. Remove any license-key registration code.
4. Add `AddSyncfusionBlazorToolkit()` instead.

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

1. ✓ Package name is `Syncfusion.Blazor.Toolkit` (not commercial packages)
2. ✓ `AddSyncfusionBlazorToolkit()` is in `Program.cs` (both in split Web Apps)
3. ✓ `@using Syncfusion.Blazor.Toolkit` is in `_Imports.razor` (both in split Web Apps)
4. ✓ Theme CSS link is `fluent.min.css` in the correct host file (`App.razor` for Blazor Server/Web App or `wwwroot/index.html` for Blazor WebAssembly)
5. ✓ Theme filename is exactly `fluent.min.css` (not other theme names)
6. ✓ Interactive components have `@rendermode InteractiveServer`, `@rendermode InteractiveWebAssembly`, or `@rendermode InteractiveAuto`
7. ✓ No license-key registration code is present
8. ✓ Build completes without errors: `dotnet build`
9. ✓ Browser console shows no 404 or JavaScript errors
