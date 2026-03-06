---
name: convert-blazor-server-to-webapp
description: >
  Guides conversion of a pre-.NET 8 Blazor Server app into a .NET 8+ Blazor Web App.
  USE FOR: migrating apps that use AddServerSideBlazor and MapBlazorHub to the
  AddRazorComponents/MapRazorComponents model, converting _Host.cshtml to an App.razor
  root component, replacing blazor.server.js with blazor.web.js, migrating
  CascadingAuthenticationState to a service, adopting new Blazor Web App features
  like enhanced navigation and streaming rendering.
  DO NOT USE FOR: apps that are already Blazor Web Apps (already use AddRazorComponents
  and MapRazorComponents), Blazor WebAssembly or hosted Blazor WebAssembly apps
  (different migration path), apps that should stay on the Blazor Server hosting
  model without converting, or apps still targeting .NET Framework.
---

# Convert Blazor Server App to Blazor Web App

This skill helps an agent convert a pre-.NET 8 Blazor Server app into a .NET 8+ Blazor Web App. The old hosting model uses `AddServerSideBlazor`/`MapBlazorHub` with a `_Host.cshtml` Razor Page as the entry point. The new Blazor Web App model uses `AddRazorComponents`/`MapRazorComponents` with an `App.razor` root component, enabling per-component render modes, enhanced navigation, streaming rendering, and other .NET 8+ features. The converted app uses `InteractiveServer` render mode to preserve existing interactive behavior.

## When to Use

- Migrating a Blazor Server app from .NET 6 or .NET 7 to .NET 8+
- App currently uses `AddServerSideBlazor()` and `MapBlazorHub()` in `Program.cs` (or `Startup.cs`)
- App uses `Pages/_Host.cshtml` (or `_Host.razor`) as the host page with Component Tag Helpers
- Want to adopt new Blazor Web App features while keeping interactive server rendering

## When Not to Use

- **The app already uses `AddRazorComponents` and `MapRazorComponents`.** It is already a Blazor Web App — no conversion is needed. Stop here and tell the user the app is already using the Blazor Web App model.
- Blazor WebAssembly or hosted Blazor WebAssembly app — these have a different migration path
- The app should stay on the legacy Blazor Server hosting model (just update TFM and packages)
- The app targets .NET Framework — it must be migrated to .NET first

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Blazor Server project | Yes | The `.csproj` and source files of the Blazor Server app |
| Target framework | Yes | .NET 8 or later (e.g., `net8.0`, `net9.0`, `net10.0`) |
| `Program.cs` or `Startup.cs` | Yes | The app's service and middleware configuration |
| `_Host.cshtml` location | Recommended | Usually `Pages/_Host.cshtml`; may be `_Host.razor` in some projects |

## Workflow

> **Commit strategy:** Commit after each logical step so the migration is reviewable and bisectable.

### Step 1: Update the project file

Update the `.csproj` file:

1. Change the Target Framework Moniker (TFM) to the target version:
   ```xml
   <TargetFramework>net8.0</TargetFramework>
   ```
2. Update all `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.Extensions.*`, and `System.Net.Http.Json` package references to the matching version.

For non-Blazor project file changes (nullable reference types, implicit usings, HTTP/3 support, etc.), see the [general ASP.NET Core migration guide](https://learn.microsoft.com/aspnet/core/migration/70-to-80).

### Step 2: Create `Routes.razor` from `App.razor`

The old `App.razor` contains the `<Router>` component. This content moves to a new `Routes.razor` file so that `App.razor` can become the root HTML document component.

1. Create a new file `Routes.razor` in the project root.
2. Move the entire content of `App.razor` into `Routes.razor`.
3. If the content is wrapped in `<CascadingAuthenticationState>`, remove that wrapper (it will be replaced by a service in Step 5).
4. Leave `App.razor` empty for the next step.

The resulting `Routes.razor` should look similar to:

```razor
<Router AppAssembly="@typeof(Program).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
        <FocusOnNavigate RouteData="@routeData" Selector="h1" />
    </Found>
    <NotFound>
        <LayoutView Layout="@typeof(MainLayout)">
            <p>Sorry, there's nothing at this address.</p>
        </LayoutView>
    </NotFound>
</Router>
```

If the app uses `<AuthorizeRouteView>` instead of `<RouteView>`, keep it — it works the same way in Blazor Web Apps.

### Step 3: Convert `_Host.cshtml` to `App.razor`

Move the HTML shell from `Pages/_Host.cshtml` into the now-empty `App.razor` and transform it from a Razor Page into a Razor component:

1. **Remove Razor Page directives** — delete `@page "/"`, `@using Microsoft.AspNetCore.Components.Web`, `@namespace`, and `@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers`.

2. **Add component injection** — if using environment-conditional error UI, add:
   ```razor
   @inject IHostEnvironment Env
   ```

3. **Fix the base tag** — replace `<base href="~/" />` with `<base href="/" />`.

4. **Replace HeadOutlet Component Tag Helper** — replace:
   ```html
   <component type="typeof(HeadOutlet)" render-mode="ServerPrerendered" />
   ```
   with:
   ```razor
   <HeadOutlet @rendermode="InteractiveServer" />
   ```

5. **Replace App Component Tag Helper with Routes** — replace:
   ```html
   <component type="typeof(App)" render-mode="ServerPrerendered" />
   ```
   with:
   ```razor
   <Routes @rendermode="InteractiveServer" />
   ```

6. **Replace Environment Tag Helpers** — replace:
   ```html
   <environment include="Staging,Production">
       An error has occurred. This application may no longer respond until reloaded.
   </environment>
   <environment include="Development">
       An unhandled exception has occurred. See browser dev tools for details.
   </environment>
   ```
   with:
   ```razor
   @if (Env.IsDevelopment())
   {
       <text>
           An unhandled exception has occurred. See browser dev tools for details.
       </text>
   }
   else
   {
       <text>
           An error has occurred. This app may no longer respond until reloaded.
       </text>
   }
   ```

7. **Update the Blazor script** — replace:
   ```html
   <script src="_framework/blazor.server.js"></script>
   ```
   with:
   ```html
   <script src="_framework/blazor.web.js"></script>
   ```

8. **Add render mode import** — add to `_Imports.razor`:
   ```razor
   @using static Microsoft.AspNetCore.Components.Web.RenderMode
   ```

9. **Delete `Pages/_Host.cshtml`** (and `Pages/_Host.cshtml.cs` if it exists).

**Prerendering note:** If the original app used `render-mode="Server"` (not `"ServerPrerendered"`), prerendering was disabled. Preserve this by using `new InteractiveServerRenderMode(prerender: false)` instead of `InteractiveServer` for both `HeadOutlet` and `Routes`.

### Step 4: Update `Program.cs`

Make the following changes to `Program.cs` (or `Startup.cs` if the app uses the older hosting pattern):

1. **Replace Blazor Server services** — replace:
   ```csharp
   builder.Services.AddServerSideBlazor();
   ```
   with:
   ```csharp
   builder.Services.AddRazorComponents()
       .AddInteractiveServerComponents();
   ```

   If `AddServerSideBlazor` had options configured (e.g., circuit options, hub options, detailed errors), migrate them to `AddInteractiveServerComponents`:
   ```csharp
   // Old:
   builder.Services.AddServerSideBlazor(options =>
   {
       options.DetailedErrors = true;
       options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
   });

   // New:
   builder.Services.AddRazorComponents()
       .AddInteractiveServerComponents(options =>
       {
           options.DetailedErrors = true;
           options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
       });
   ```

2. **Replace Blazor endpoint mapping** — replace:
   ```csharp
   app.MapBlazorHub();
   ```
   with:
   ```csharp
   app.MapRazorComponents<App>()
       .AddInteractiveServerRenderMode();
   ```

   Ensure there is a `using` statement for the project's root namespace so that `App` resolves to the `App.razor` component.

3. **Remove the fallback route** — delete:
   ```csharp
   app.MapFallbackToPage("/_Host");
   ```

4. **Remove explicit routing middleware** — delete if present:
   ```csharp
   app.UseRouting();
   ```
   Endpoint routing is the default and explicit `UseRouting()` is no longer needed.

5. **Add antiforgery middleware** — add after `UseAuthentication`/`UseAuthorization` if present:
   ```csharp
   app.UseAntiforgery();
   ```
   `AddRazorComponents` registers antiforgery services automatically, but the middleware must be explicitly added to the pipeline. Without it, form POST requests fail with 400 errors.

### Step 5: Migrate `CascadingAuthenticationState` (if present)

If the app used `<CascadingAuthenticationState>` to wrap the router:

1. Remove the `<CascadingAuthenticationState>` component wrapper (already done in Step 2 if following this workflow).
2. Add the cascading authentication state service in `Program.cs`:
   ```csharp
   builder.Services.AddCascadingAuthenticationState();
   ```

The component wrapper approach does not work across render mode boundaries in Blazor Web Apps. The service-based approach provides `Task<AuthenticationState>` as a cascading value to all components regardless of render mode.

### Step 6: Recommended improvements (optional)

These are optional modernization improvements — not required for the conversion to work. If you suggest any of these, state explicitly that they are optional.

- **Replace `UseStaticFiles` with `MapStaticAssets`** (.NET 9+): `app.MapStaticAssets()` provides optimized static file serving with fingerprinting, pre-compression, and content-based ETags. See [MapStaticAssets documentation](https://learn.microsoft.com/aspnet/core/fundamentals/static-files#mapstaticassets).
- **Add `@attribute [StreamRendering]`** to pages with async data loading (`OnInitializedAsync`) for improved perceived performance. The page renders its initial synchronous content immediately and re-renders when async data arrives.
- **Update CSS isolation bundle reference** if the `<link>` tag referenced a `_Host` assembly name; ensure it matches the project's actual assembly name: `<link href="{AssemblyName}.styles.css" rel="stylesheet" />`.
- For other non-Blazor improvements (minimal hosting, HTTP/3, output caching, etc.), see the [general ASP.NET Core migration guide](https://learn.microsoft.com/aspnet/core/migration/70-to-80).

### Step 7: Verify the migration

1. Build the project targeting the new framework. Confirm no compile errors.
2. Search for remaining references to removed APIs:
   - `AddServerSideBlazor`
   - `MapBlazorHub`
   - `MapFallbackToPage`
   - `blazor.server.js`
   - `_Host.cshtml`
3. Run the app and verify:
   - Pages load and render correctly
   - Interactive features work (forms, event handlers, SignalR circuits)
   - Navigation between pages works
   - Authentication and authorization flows work if present
4. Run existing tests.

## Validation

- [ ] No references to `AddServerSideBlazor` remain
- [ ] No references to `MapBlazorHub` remain
- [ ] No references to `MapFallbackToPage("/_Host")` remain
- [ ] No references to `blazor.server.js` remain
- [ ] `Pages/_Host.cshtml` has been deleted
- [ ] `App.razor` serves as the root component with a full HTML document structure
- [ ] `Routes.razor` contains the `<Router>` configuration
- [ ] `Program.cs` uses `AddRazorComponents().AddInteractiveServerComponents()`
- [ ] `Program.cs` uses `MapRazorComponents<App>().AddInteractiveServerRenderMode()`
- [ ] `app.UseAntiforgery()` is present in the middleware pipeline
- [ ] If the app used `<CascadingAuthenticationState>`, it has been replaced with `AddCascadingAuthenticationState()` service registration
- [ ] App builds and runs successfully on the target framework

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Missing `UseAntiforgery()` middleware | `AddRazorComponents` registers antiforgery services, but the middleware must be explicitly added. Place `app.UseAntiforgery()` after `UseAuthentication`/`UseAuthorization`. Without it, form POST requests fail with 400 errors. |
| Forgetting to replace `blazor.server.js` with `blazor.web.js` | The old script does not work with the Blazor Web App model. Replace all references to `_framework/blazor.server.js` with `_framework/blazor.web.js`. |
| Not removing `<CascadingAuthenticationState>` wrapper | The component wrapper does not work across render mode boundaries in Blazor Web Apps. Use `builder.Services.AddCascadingAuthenticationState()` instead. |
| Leaving `app.UseRouting()` in the pipeline | Explicit `UseRouting()` is no longer needed and can interfere with endpoint routing. Remove it unless other middleware specifically requires it. |
| Using `InteractiveServer` when prerendering was disabled | If the original app used `render-mode="Server"` (not `"ServerPrerendered"`), use `new InteractiveServerRenderMode(prerender: false)` to preserve the same behavior. Using `InteractiveServer` enables prerendering which can cause unexpected issues with components that depend on JS interop during initialization. |
| Not migrating `AddServerSideBlazor` circuit options | If circuit options, hub options, or detailed error settings were configured, migrate them to `AddInteractiveServerComponents(options => { ... })`. Otherwise those settings are silently lost. |
| `UseAntiforgery()` placed before authentication middleware | The antiforgery middleware must be placed after `UseAuthentication` and `UseAuthorization`. Placing it before causes antiforgery validation to run before the user identity is established. |
| CSS isolation bundle link has wrong assembly name | If the `<link href="{Name}.styles.css">` tag referenced the old project name, update it to match the current assembly name. |

## More Info

- [Convert a Blazor Server app into a Blazor Web App](https://learn.microsoft.com/aspnet/core/migration/70-to-80#convert-a-blazor-server-app-into-a-blazor-web-app) — the official step-by-step migration guide
- [ASP.NET Core Blazor render modes](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes) — understanding InteractiveServer, InteractiveWebAssembly, and InteractiveAuto
- [Migrate CascadingAuthenticationState to services](https://learn.microsoft.com/aspnet/core/migration/70-to-80#migrate-the-cascadingauthenticationstate-component-to-cascading-authentication-state-services) — replacing the component wrapper with a service
- [MapStaticAssets](https://learn.microsoft.com/aspnet/core/fundamentals/static-files#mapstaticassets) — optimized static file serving in .NET 9+
- [Migrate from ASP.NET Core 7.0 to 8.0](https://learn.microsoft.com/aspnet/core/migration/70-to-80) — general migration guide for all ASP.NET Core changes
- [Stream rendering with Blazor](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes#streaming-rendering) — `@attribute [StreamRendering]` for async data loading
- [Cascading values and render mode boundaries](https://learn.microsoft.com/aspnet/core/blazor/components/cascading-values-and-parameters#cascading-valuesparameters-and-render-mode-boundaries) — why cascading parameters do not cross render mode boundaries
