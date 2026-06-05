# {AppName}

| Setting | Value |
|---------|-------|
| **Interactivity Mode** | WebAssembly |
| **Interactivity Scope** | Global |

## Rendering configuration
This project uses global Interactive WebAssembly with prerendering.
Created with `dotnet new blazor -int WebAssembly -ai`.

All pages are interactive by default via `<Routes @rendermode="InteractiveWebAssembly" />` in `App.razor`. Components run entirely in the browser after the initial prerender.

## Project structure
- **{AppName}** (server): Hosts the Blazor app, serves static files, API endpoints.
- **{AppName}.Client** (WebAssembly): All interactive UI components. Runs entirely in the browser.

## Adding new components
- All interactive components MUST go in the `.Client` project, not the server project.
- New pages go in `{AppName}.Client/Pages/`.
- All pages are already interactive. No need to add `@rendermode` to individual components.

## Data access
Interactive components cannot access the database directly. Use this pattern:
1. Define an interface in the `.Client` project (e.g., `IDataService`).
2. In the `.Client` project, implement it using `HttpClient` to call server APIs.
3. In the server project, implement it using direct data access (EF Core DbContext, etc.).
4. Register the client implementation in the client `Program.cs` and the server implementation in the server `Program.cs`.
5. Expose server data through minimal API endpoints (e.g., `app.MapGet(...)`) that the client implementation calls.
6. If the page requires authorization, apply the same auth policy to both the Blazor page (`@attribute [Authorize]`) and the minimal API endpoint (`.RequireAuthorization()`).

## Service registration
- Client-side services go in `{AppName}.Client/Program.cs`.
- Server-side services go in `{AppName}/Program.cs`.

## Environment constraints
- Components run in the browser via WebAssembly. No `HttpContext`, no server file system.
- All data access goes through `HttpClient` calls to server API endpoints.
- The .NET runtime (~10 MB) is downloaded to the browser on first visit and cached.

## Don'ts
- Don't put interactive components in the server project — they work during prerender but fail after WebAssembly handoff.
- Don't inject `DbContext` or server-only services in `.Client` project components — use HTTP APIs instead.
- Don't add `@rendermode InteractiveWebAssembly` to pages — global interactivity is already configured in `App.razor`.
- Don't add `@rendermode` to Identity/Account pages if auth is configured — they must stay static SSR.
