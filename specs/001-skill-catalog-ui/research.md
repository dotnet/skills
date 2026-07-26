# Research: Skill Catalog UI

## .NET Runtime and API Style

**Decision**: Use .NET 10 LTS with ASP.NET Core Minimal APIs and first-party OpenAPI generation.

**Rationale**: .NET 10 is the supported LTS release and Minimal APIs fit the bounded read-only surface. A nested `global.json` isolates the application from the repository's .NET 11 preview SDK pin.

**Alternatives considered**: .NET 11 preview (deployment friction), controllers (unneeded ceremony), and Node API (conflicts with requested C# architecture).

Sources: [Minimal APIs](https://learn.microsoft.com/aspnet/core/tutorials/min-web-api?view=aspnetcore-10.0), [OpenAPI](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0)

## React Build and Design System

**Decision**: Use React 19.2 with TypeScript and Vite, Fluent UI React v9, and Fluent icons.

**Rationale**: React 19.2 is current stable; Fluent UI v9 is Microsoft's current component package; Vite provides a direct SPA build without duplicating the C# server boundary.

**Alternatives considered**: Fluent UI v8, Next.js, and a custom component system.

Sources: [React versions](https://react.dev/versions), [Fluent UI React](https://www.npmjs.com/package/@fluentui/react-components), [Vite](https://vite.dev/guide/)

## Repository Discovery

**Decision**: Index `plugins/<plugin>/skills/<skill>/SKILL.md` at startup into an immutable snapshot. Read manifests and YAML defensively, replacing the snapshot atomically on refresh.

**Rationale**: Files remain authoritative and the catalog is small enough to avoid a database.

**Alternatives considered**: Database ingestion, GitHub API per request, and production filesystem watchers.

## Markdown and Resource Display

**Decision**: Return Markdown as untrusted data, render it with a CommonMark-compatible React renderer, sanitize resulting HTML, disable raw HTML, and preview only allowlisted bounded text resources.

**Alternatives considered**: Server-rendered HTML and raw HTML passthrough.

## Download Packaging

**Decision**: Enumerate regular files under a resolved skill root, reject links/reparse points and escaping paths, then stream a ZIP with normalized relative paths and a generated source manifest.

**Alternatives considered**: Whole-repository GitHub archives and client-side ZIP generation.

## Testing and Quality

**Decision**: Use xUnit on Microsoft.Testing.Platform for API tests; Vitest and React Testing Library for UI behavior; Playwright and axe-core for journeys and accessibility.

**Rationale**: This satisfies deterministic, contract, integration, accessibility, and security gates while keeping failures localizable.
