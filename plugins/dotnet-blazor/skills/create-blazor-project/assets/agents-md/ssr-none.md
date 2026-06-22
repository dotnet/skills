# {AppName}

| Setting | Value |
|---------|-------|
| **Interactivity Mode** | None (Static SSR) |
| **Interactivity Scope** | N/A |

## Rendering configuration
This project uses static server-side rendering with no interactivity.
Created with `dotnet new blazor -int None`.

Enhanced navigation via `blazor.web.js` is enabled by default, making page transitions feel instant without any interactive runtime.

## Adding new components
- Create new `.razor` files in `Components/Pages/` for routable pages or `Components/` for shared components.
- Do NOT add `@rendermode` to any component — this project has no interactive runtime configured.
- Forms use standard HTML POST with `[SupplyParameterFromForm]` for model binding.
- Query string parameters use `[SupplyParameterFromQuery]`.

## Data access
- Components can inject services directly — EF Core DbContext, file system, server-only APIs. No HTTP API layer needed.

## Environment constraints
- No SignalR circuits, no WebAssembly. All rendering happens on the server.
- Forms use HTML POST with `[SupplyParameterFromForm]` and require `<AntiforgeryToken />`.
- `HttpContext` is available via `[CascadingParameter]`.
- Browser APIs (JS interop) are not available.

## Don'ts
- Don't add `@rendermode InteractiveServer` or any interactive render mode — the project has no interactive runtime registered.
- Don't add `AddInteractiveServerComponents()` to `Program.cs` without also updating `App.razor`.
- Don't use `@onclick` or other event handlers — they require an interactive render mode. Use form submissions and links for user actions.
- Don't use `IJSRuntime` — there is no interactive runtime to execute JavaScript calls.
