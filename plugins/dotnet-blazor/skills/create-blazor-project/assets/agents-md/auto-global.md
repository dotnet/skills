# {AppName}

| Setting | Value |
|---------|-------|
| **Interactivity Mode** | Auto |
| **Interactivity Scope** | Global |

## Rendering configuration
This project uses global Interactive Auto with prerendering.
Created with `dotnet new blazor -int Auto -ai`.

On a user's first visit, components render via Interactive Server (SignalR). On subsequent visits the cached WebAssembly runtime takes over and interactions run entirely in the browser.

## Project structure
- **{AppName}** (server): Hosts the Blazor app, serves static files, API endpoints.
- **{AppName}.Client** (WebAssembly): All interactive UI components. Run on server first, then browser.

## Adding new components
- Interactive components MUST go in the `.Client` project, not the server project.
- New pages go in `{AppName}.Client/Pages/`.
- All pages are already interactive (global mode). No need to add `@rendermode` to individual components.
- Server-only static components (e.g., error pages) belong in the server `Components/` folder.

## Data access
Interactive components cannot access the database directly. Use this pattern:
1. Define an interface in the `.Client` project (e.g., `IDataService`).
2. In the `.Client` project, implement it using `HttpClient` to call server APIs.
3. In the server project, implement it using direct data access (EF Core DbContext, etc.).
4. Register the client implementation in the client `Program.cs` and the server implementation in the server `Program.cs`.
5. Expose server data through minimal API endpoints (e.g., `app.MapGet(...)`) that the client implementation calls.
6. If the page requires authorization, apply the same auth policy to both the Blazor page (`@attribute [Authorize]`) and the minimal API endpoint (`.RequireAuthorization()`).

## Service registration
- Both server and client `Program.cs` must register matching services for any DI used by interactive components.
- Server-only services (EF Core, Identity) stay in the server `Program.cs` only.

## Environment constraints
- Code must work in both server and browser execution environments.
- Do not use `HttpContext` or browser-only JS APIs without `RendererInfo` guards.
- The .NET runtime (~10 MB) is downloaded to the browser on first visit and cached — subsequent visits use WebAssembly.

## Don'ts
- Don't put interactive components in the server project — they work during prerender but fail after WebAssembly handoff.
- Don't inject `DbContext` or server-only services in `.Client` project components — use HTTP APIs instead.
- Don't assume execution environment — the same component runs on Server first, then WebAssembly later. Test both.
- Don't add `@rendermode InteractiveAuto` to pages — global interactivity is already configured in `App.razor`.
- Don't add `@rendermode` to Identity/Account pages if auth is configured — they must stay static SSR.
