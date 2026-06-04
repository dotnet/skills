# {AppName}

| Setting | Value |
|---------|-------|
| **Interactivity Mode** | Server |
| **Interactivity Scope** | Per-page |

## Rendering configuration
This project uses per-page Interactive Server with prerendering.
Created with `dotnet new blazor -int Server`.

Pages are static SSR by default. Only components that explicitly add `@rendermode InteractiveServer` become interactive.

## Adding new components
- Create new `.razor` files in `Components/Pages/` for routable pages or `Components/` for shared components.
- New pages are static SSR by default. Only add `@rendermode InteractiveServer` to components that need client-side behavior (live search, real-time updates, complex form interactions).
- Static pages can use standard HTML forms with `[SupplyParameterFromForm]` — no interactivity needed.

## Data access
- Components can inject services directly — EF Core DbContext, file system, server-only APIs. No HTTP API layer needed.

## Environment constraints
- Interactive components run on the server via SignalR. `HttpContext` is available in static components but NOT in interactive components during the SignalR circuit lifetime.
- Static pages can access `HttpContext` via `[CascadingParameter]`.
- Browser APIs are not directly available — use `IJSRuntime` interop in interactive components.

## Don'ts
- Don't add `@rendermode InteractiveServer` to every page — keep read-only content static for performance and lower server memory.
- Don't add `@rendermode` to Identity/Account pages if auth is configured — they must stay static SSR.
- Don't inject `HttpContext` in interactive components — it's not available during SignalR circuit lifetime.
- Don't set `@rendermode` on `<Routes>` in `App.razor` — that makes it global. Per-page mode means individual components opt in.
