# Skill Catalog

A repository-backed catalog for discovering, inspecting, and downloading the skills in this fork. The application uses an ASP.NET Core 10 Minimal API and a React 19 + Fluent UI frontend. Repository files remain the source of truth; there is no catalog database.

## Run locally

Prerequisites: .NET SDK 10.0.302+ and Node.js 22+.

```powershell
cd src/SkillCatalog
$env:DOTNET_CLI_HOME="$env:TEMP\skill-catalog-dotnet-home"
dotnet run --project api/SkillCatalog.Api
```

In a second terminal:

```powershell
cd src/SkillCatalog/web
npm ci
npm run dev
```

Open `http://localhost:5173`. Vite proxies `/api` to the API. If the API port changes, update `web/vite.config.ts`.

## Configuration

`SkillCatalog` in `api/SkillCatalog.Api/appsettings.json` controls the repository root, source URL, maximum preview size, and maximum file size permitted in an archive. At startup the API locates the nearest Git repository containing `plugins/`, parses each `SKILL.md`, and atomically exposes the resulting in-memory snapshot.

## Security boundaries

All preview and archive paths are canonicalized and must remain inside their skill directory. Reparse-point files and files above configured limits are excluded. Markdown is rendered as inert, sanitized content; repository content is never executed. Downloads contain only regular files from one skill plus a generated `skill-package.json` manifest.

## Verification

```powershell
cd src/SkillCatalog
dotnet test SkillCatalog.slnx
cd web
npm run build
npm test
npm audit --audit-level=high
```

OpenAPI is available at `/openapi/v1.json`; health is available at `/health`.

## Troubleshooting

- If SDK selection uses the repository's preview SDK, run commands from `src/SkillCatalog`, which has its own `global.json`.
- If Vite reports the `#` in the parent directory, the production build still succeeds; use a path without `#` if a future plugin cannot handle it.
- If the UI cannot reach the API, align the API launch port and the Vite proxy target.
