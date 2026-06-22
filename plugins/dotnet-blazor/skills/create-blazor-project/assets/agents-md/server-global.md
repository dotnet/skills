# {AppName}

| Setting | Value |
|---------|-------|
| **Interactivity Mode** | Server |
| **Interactivity Scope** | Global |

## Rendering configuration
This project uses global Interactive Server with prerendering.
Created with `dotnet new blazor -int Server -ai`.

All pages are interactive by default via `<Routes @rendermode="InteractiveServer" />` in `App.razor`.

## Adding new components
- Create new `.razor` files in `Components/Pages/` for routable pages or `Components/` for shared components.
- All pages are already interactive. No need to add `@rendermode` to individual components.

## Data access
- Components can inject services directly — EF Core DbContext, file system, server-only APIs. No HTTP API layer needed.

## Environment constraints
- Components run on the server via SignalR.
- `HttpContext` is NOT available in interactive components — it's only available during the initial static prerender.
- Browser APIs are not directly available — use `IJSRuntime` interop.
- Every connected user holds a SignalR circuit on the server.

## Don'ts
- Don't add `@rendermode InteractiveServer` to pages — global interactivity is already configured in `App.razor`.
- Don't add `@rendermode` to Identity/Account pages if auth is configured — they must stay static SSR.
- Don't inject `HttpContext` in interactive components — it's not available during SignalR circuit lifetime.
- Don't use browser APIs (localStorage, DOM) directly — use `IJSRuntime` interop instead.
