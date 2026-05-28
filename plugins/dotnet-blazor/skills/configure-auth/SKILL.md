---
license: MIT
name: configure-auth
description: >
  Add authentication and authorization to a Blazor Web App, accounting for the app's render mode.
  USE WHEN the user needs [Authorize] on pages, AuthorizeView, role or policy-based access,
  login/logout Identity pages, or AuthenticationStateProvider.
  Also USE WHEN auth state is null after WebAssembly loads, SignInManager throws in an interactive
  component, <NotAuthorized> content never renders in static SSR, or HttpContext.User is null in
  an interactive component.
  DO NOT USE for general component authoring (see author-component), for prerendering concerns
  unrelated to auth (see support-prerendering), or for managing non-auth cascading state
  (see coordinate-components).
---

# Configure Auth

## Step 1 — Read AGENTS.md

Read `AGENTS.md` at the workspace root for the project's interactivity mode and scope before making changes.

## Step 2 — Register auth services in Program.cs

```csharp
// Program.cs (server project)
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
```

For ASP.NET Core Identity add the Identity services:

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
.AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
```

## Step 3 — Wire App.razor for auth and render mode

The `App.razor` component must use `AuthorizeRouteView` and conditionally apply the render mode so that pages excluded from interactive routing render statically.

```razor
<!DOCTYPE html>
<html>
<head>
    <HeadOutlet @rendermode="RenderModeForPage" />
</head>
<body>
    <Routes @rendermode="RenderModeForPage" />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>

@code {
    [CascadingParameter]
    public HttpContext HttpContext { get; set; } = default!;

    private IComponentRenderMode? RenderModeForPage =>
        HttpContext.AcceptsInteractiveRouting()
            ? InteractiveServer   // replace with the app's render mode
            : null;
}
```

In `Routes.razor` (or wherever the router lives), use `AuthorizeRouteView`:

```razor
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData"
                            DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                @if (context.User.Identity?.IsAuthenticated != true)
                {
                    <RedirectToLogin />
                }
                else
                {
                    <p>You are not authorized to access this resource.</p>
                }
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

## Step 4 — Protect pages and components

### [Authorize] attribute on pages

```razor
@page "/admin"
@attribute [Authorize]
```

With roles or policies:

```razor
@attribute [Authorize(Roles = "Admin")]
@attribute [Authorize(Policy = "RequireManager")]
```

### AuthorizeView for conditional UI

```razor
<AuthorizeView>
    <Authorized>Welcome, @context.User.Identity?.Name!</Authorized>
    <NotAuthorized><a href="Account/Login">Log in</a></NotAuthorized>
</AuthorizeView>
```

Role/policy variants:

```razor
<AuthorizeView Roles="Admin,Manager">
    <Authorized>Admin content here</Authorized>
</AuthorizeView>
```

### Access auth state in code

```csharp
[CascadingParameter]
private Task<AuthenticationState>? AuthState { get; set; }

protected override async Task OnInitializedAsync()
{
    if (AuthState is not null)
    {
        var state = await AuthState;
        var isAdmin = state.User.IsInRole("Admin");
    }
}
```

## Step 5 — Identity pages must stay static SSR

`SignInManager` and `UserManager` use `HttpContext` internally and **throw in interactive components**. Identity pages (login, register, manage) must render as static SSR.

In a **globally interactive** app, mark every Identity page:

```razor
@page "/Account/Login"
@attribute [ExcludeFromInteractiveRouting]
```

This forces a full-page navigation (exits the interactive circuit) so the page renders through the static SSR pipeline with a real `HttpContext`.

`App.razor` must use `AcceptsInteractiveRouting()` (Step 3) to return `null` for these pages — otherwise the framework still tries to render them interactively.

In a **per-page** app, Identity pages are static by default (no `@rendermode` directive), so `[ExcludeFromInteractiveRouting]` is not needed.

## Step 6 — Auth state in WebAssembly / Auto mode

WebAssembly components run in the browser and have no `HttpContext`. Auth state must be serialized from the server during prerendering and deserialized on the client.

**Server `Program.cs`:**

```csharp
builder.Services.AddAuthenticationStateSerialization();
```

**Client `.Client/Program.cs`:**

```csharp
builder.Services.AddAuthenticationStateDeserialization();
```

Without these calls, `Task<AuthenticationState>` resolves to an anonymous user after WebAssembly takes over from prerendering.

`AddAuthenticationStateSerialization` accepts options to include role and claim data:

```csharp
builder.Services.AddAuthenticationStateSerialization(options =>
    options.SerializeAllClaims = true);
```

## Render Mode × Auth Matrix

| Render mode | HttpContext.User | SignInManager | Auth state source | Key requirement |
|---|---|---|---|---|
| Static SSR | Available | Works | Server pipeline | Use middleware for redirects, `<NotAuthorized>` does NOT render |
| Server (interactive) | NOT available | Throws | `CascadingAuthenticationState` | Use `[Authorize]` + `AuthorizeView`, not `HttpContext` |
| WebAssembly | NOT available | Throws | Serialized from server | `AddAuthenticationStateSerialization` / `Deserialization` |
| Auto | NOT available after WASM | Throws | Serialized from server | Same as WebAssembly; register in **both** Program.cs files |

## Common Mistakes

| Mistake | Symptom | Fix |
|---------|---------|-----|
| Using `HttpContext.User` in interactive component | Null or stale claims | Use `[CascadingParameter] Task<AuthenticationState>` |
| `SignInManager` in interactive component | `InvalidOperationException` | Move to static SSR page with `[ExcludeFromInteractiveRouting]` |
| Missing `AddAuthenticationStateSerialization` | Anonymous user after WASM loads | Add to server Program.cs; add `Deserialization` to client Program.cs |
| `<NotAuthorized>` in static SSR layout | Content never shown | Static SSR uses middleware pipeline; redirect via `LoginPath` or `RedirectToLogin` component |
| Global interactivity without `AcceptsInteractiveRouting` | Identity pages crash | Add `AcceptsInteractiveRouting()` check in App.razor (Step 3) |
| Missing `AddCascadingAuthenticationState()` | `Task<AuthenticationState>` is null | Register in Program.cs (Step 2) |
