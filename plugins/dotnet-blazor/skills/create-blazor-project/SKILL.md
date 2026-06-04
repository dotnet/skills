---
license: MIT
name: create-blazor-project
description: >
  Create a new ASP.NET Core web application or web site using Blazor.
  USE FOR: creating a new Blazor web app, scaffolding a new web project,
  starting a new web site, choosing render modes (Static SSR, Interactive Server,
  Interactive WebAssembly, Auto), running dotnet new blazor with the right options,
  setting up initial project structure.
  DO NOT USE FOR: adding features to existing projects, changing how an existing
  app renders, or component authoring (use author-component).
---

# Create a Blazor Web App

## Before You Start — Gather Requirements

If the user's request doesn't make the following clear, ask before scaffolding:

1. **What does the app do?** List the main screens/features (e.g., "product catalog with search and shopping cart").
2. **What kind of interactivity is needed?** Displaying data and forms? Real-time updates? Offline support? Rich drag-and-drop UI?
3. **Deployment environment?** Internet-facing? Intranet? Mobile users on slow connections?
4. **Authentication needed?** Anonymous? Individual accounts? Organizational (Azure AD)?

## Pick the Right Interactivity Level

Blazor render modes are a progression scale. Start at the simplest level that satisfies the requirements and only move up when there's a concrete reason.

```
Static SSR ──→ SSR + Enhanced Nav ──→ Interactive Server ──→ Interactive WebAssembly
 simplest                                                           most complex
```

### Decision Rules

| If the app needs... | Use | Why |
|---|---|---|
| Display data, simple forms, links between pages | **Static SSR** (`-int None`) | No JS runtime, no circuit, no WebAssembly download. Forms work via HTML POST. Enhanced navigation makes it feel snappy. |
| Everything above + a few components with client-side behavior (live search, real-time updates, complex form wizards) | **Interactive Server, per-page** (`-int Server`) | Only the components that need interactivity opt in with `@rendermode`. The rest stays static. Server-side execution, full .NET access, no API layer needed. |
| Most pages need rich interactivity (dashboards, drag-and-drop, chat) | **Interactive Server, global** (`-int Server -ai`) | Every component is interactive by default. Consistent UX, simpler mental model. Trade-off: every user holds a SignalR circuit on the server. |
| Network latency is a problem, users are on mobile/poor connections, or the app must work offline | **Interactive WebAssembly** (`-int WebAssembly`) | Code runs in the browser. Eliminates round-trip latency but requires a `.Client` project, API layer for data access, and downloads the .NET runtime to the browser on first visit. For offline support, enable PWA: add a service worker and manifest after scaffolding (not included in the template by default). |
| Fast initial load (Server) + low latency after (WebAssembly) | **Interactive Auto** (`-int Auto`) | First visit uses Server; subsequent visits use cached WebAssembly runtime. Most complex setup — see Auto constraints below. Only choose when both Server and WebAssembly constraints apply. |

**Default recommendation:** Start with `-int Server` (per-page). It covers the vast majority of apps. Upgrade to global or WebAssembly only when a specific requirement demands it.

### Auto Mode Constraints

Auto mode means your component code runs on the server first, then in the browser on subsequent visits. This creates real constraints:

- **All interactive components must live in the `.Client` project** — same as WebAssembly.
- **No direct server access** from interactive components — no EF `DbContext`, no file system, no server-only services. All data access must go through HTTP APIs.
- **Both `Program.cs` files must register matching services** — the server and client DI containers must both provide implementations for any service an interactive component injects.
- **Code must not assume its execution environment** — no `HttpContext` access, no browser-only APIs without `RendererInfo` guards.
- **Test in both modes** — a component that works on Server during development may break on WebAssembly in production (second visit). Test both paths.

### Don'ts

- Don't pick WebAssembly "because it's cool" — it adds a `.Client` project, forces API-mediated data access, and downloads ~10MB to the browser on first visit.
- Don't pick Auto unless you can articulate why Server alone and WebAssembly alone are both insufficient.
- Don't pick global interactivity for apps where most pages are read-only content — per-page keeps the static pages fast and reduces server memory.

## Scaffold the Project

### Static SSR Only (display data + simple forms)

```shell
dotnet new blazor -o {AppName} -int None
```

No interactive runtime. Enhanced navigation enabled by default via `blazor.web.js`.

### Interactive Server, Per-Page (recommended default)

```shell
dotnet new blazor -o {AppName} -int Server
```

Pages are static by default. Add `@rendermode InteractiveServer` to components that need interactivity.

### Interactive Server, Global

```shell
dotnet new blazor -o {AppName} -int Server -ai
```

All pages interactive via `<Routes @rendermode="InteractiveServer" />` in `App.razor`.

### Interactive WebAssembly, Per-Page

```shell
dotnet new blazor -o {AppName} -int WebAssembly
```

Creates `{AppName}` (server) and `{AppName}.Client` (WebAssembly) projects. Interactive components must live in `.Client`.

### Interactive WebAssembly, Global

```shell
dotnet new blazor -o {AppName} -int WebAssembly -ai
```

### Interactive Auto, Per-Page

```shell
dotnet new blazor -o {AppName} -int Auto
```

### Interactive Auto, Global

```shell
dotnet new blazor -o {AppName} -int Auto -ai
```

### With Authentication

Append `-au Individual` to any command above:

```shell
dotnet new blazor -o {AppName} -int Server -au Individual
```

`-au Individual` scaffolds ASP.NET Core Identity with SQLite (CLI) or SQL Server (Visual Studio). Identity pages are always static SSR — they do not use interactive render modes.

The `blazor` template only supports `-au Individual`. For organizational auth (Microsoft Entra ID, Azure AD B2C), scaffold with `-au Individual` first, then replace the Identity provider with `Microsoft.Identity.Web` / OIDC middleware and configure the tenant in `appsettings.json`.

## What the Template Creates

### Single project (Static SSR, Server)

```
{AppName}/
├── Components/
│   ├── App.razor              # Root component — sets <HeadOutlet> and <Routes>
│   ├── Routes.razor           # Wraps <Router> with route discovery
│   ├── Layout/
│   │   ├── MainLayout.razor   # App shell with nav, header, footer
│   │   └── MainLayout.razor.css
│   └── Pages/
│       └── Home.razor         # @page "/" — first page
├── Program.cs                 # Service registration and middleware
├── wwwroot/                   # Static files (CSS, images)
└── {AppName}.csproj
```

### Two projects (WebAssembly, Auto)

```
{AppName}/                     # Server project — hosts the app
├── Components/                # Server-only components (static SSR pages, layouts)
│   ├── App.razor
│   ├── Routes.razor
│   └── Layout/
├── Program.cs                 # Server Program.cs
└── {AppName}.Client/          # Client project — WebAssembly components
    ├── Pages/                 # Interactive components go HERE
    ├── Program.cs             # Client Program.cs
    └── _Imports.razor
```

**Rule:** Components using `InteractiveWebAssembly` or `InteractiveAuto` must live in the `.Client` project. They can reference shared code but cannot reference server-only types (EF `DbContext`, server-side services).

## Program.cs Wiring

The template generates the correct `Program.cs` for the chosen mode. Verify these registrations match your intent:

### Static SSR Only

```csharp
// Program.cs
builder.Services.AddRazorComponents();

// ...

app.MapRazorComponents<App>();
```

### Server (per-page or global)

```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ...

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

### WebAssembly (per-page or global)

```csharp
// Server Program.cs
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// ...

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof({AppName}.Client._Imports).Assembly);
```

```csharp
// Client Program.cs
builder.Services.AddAuthorizationCore();
// Register HttpClient, other client-side services
```

## Create Project AGENTS.md

After scaffolding, create an `AGENTS.md` file in the project root (next to the `.csproj`). For two-project setups, put it in the server project root.

Pick the matching template from `assets/agents-md/` based on the chosen mode:

| Mode | Template file |
|------|--------------|
| Static SSR (`-int None`) | `assets/agents-md/ssr-none.md` |
| Server, per-page (`-int Server`) | `assets/agents-md/server-per-page.md` |
| Server, global (`-int Server -ai`) | `assets/agents-md/server-global.md` |
| WebAssembly, per-page (`-int WebAssembly`) | `assets/agents-md/webassembly-per-page.md` |
| WebAssembly, global (`-int WebAssembly -ai`) | `assets/agents-md/webassembly-global.md` |
| Auto, per-page (`-int Auto`) | `assets/agents-md/auto-per-page.md` |
| Auto, global (`-int Auto -ai`) | `assets/agents-md/auto-global.md` |

Copy the template contents into the project's `AGENTS.md` and replace every `{AppName}` with the actual project name. If auth was scaffolded (`-au Individual`), add an `## Authentication` section noting that ASP.NET Core Identity is configured and that Identity pages under `Components/Account/` are always static SSR — do not add `@rendermode` to them.

**After scaffolding the project and creating AGENTS.md, continue implementing the features the user requested.** Remove default template pages (Counter, Weather) and replace them with the actual application pages.

### Auto (per-page or global)

```csharp
// Server Program.cs
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// ...

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof({AppName}.Client._Imports).Assembly);
```

## App.razor — Global vs Per-Page

The difference between global and per-page interactivity is entirely in `App.razor`:

### Per-page (default)

```razor
<!DOCTYPE html>
<html>
<head>
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

No `@rendermode` on `<Routes>` or `<HeadOutlet>`. Individual pages opt in.

### Global

```razor
<!DOCTYPE html>
<html>
<head>
    <HeadOutlet @rendermode="InteractiveServer" />
</head>
<body>
    <Routes @rendermode="InteractiveServer" />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

Replace `InteractiveServer` with `InteractiveWebAssembly` or `InteractiveAuto` as appropriate.

## After Scaffolding

1. **Verify it builds:** `dotnet build`
2. **Run it:** `dotnet run` (in the server project if two-project setup)
3. **Add your first page:** Create a `.razor` file in `Components/Pages/` (server project) or `Pages/` (`.Client` project for WebAssembly components)

## Don'ts

- Don't use `dotnet new blazorwasm` — that creates a standalone WebAssembly SPA without server-side rendering. Use the `blazor` template with `-int WebAssembly` instead.
- Don't manually add `AddInteractiveServerComponents()` to a project created with `-int None` and expect it to work — you also need the `@rendermode` directives and potentially `App.razor` changes. Re-scaffold if the mode needs to change fundamentally.
- Don't put WebAssembly-targeted components in the server project — they'll work during prerender but fail after handoff.
