# Quickstart: Skill Catalog UI

## Prerequisites

- .NET 10 SDK (the product pins 10.0.302 in `src/SkillCatalog/global.json`)
- Node.js 22 LTS or newer
- npm 10 or newer
- A Git checkout containing `plugins/`

## Configure

The API discovers the nearest checkout by default. Override settings when hosting elsewhere:

```powershell
$env:SkillCatalog__RepositoryRoot = $PWD.Path
$env:SkillCatalog__SourceBaseUrl = "https://github.com/JonC613/skills/tree/main"
```

## Run

```powershell
Set-Location src/SkillCatalog
dotnet restore SkillCatalog.slnx
dotnet run --project api/SkillCatalog.Api
```

The development profile listens on `http://localhost:5102`; OpenAPI is at `/openapi/v1.json` and health is at `/health`.

```powershell
Set-Location src/SkillCatalog/web
npm ci
npm run dev
```

Open `http://localhost:5173`.

## Verify

```powershell
Set-Location src/SkillCatalog
dotnet test api/SkillCatalog.Api.Tests/SkillCatalog.Api.Tests.csproj
dotnet test api/SkillCatalog.Api.ContractTests/SkillCatalog.Api.ContractTests.csproj
Set-Location web
npm run lint
npm test
npm run build
npm run test:e2e
```

Verified on 2026-07-26: 11 API unit/performance tests, 12 contract/security tests, 5 component tests, and 18 desktop/mobile Playwright journeys passed. The production build completed successfully.

### Windows path note

Vite 8/Rolldown misparses a `#` in a Windows checkout path during development and tests. The production build still completes from the original path. For local browser and component tests, temporarily map the checkout to a path without `#`, for example:

```powershell
subst S: "C:\Users\x\Documents\C#SkillsRepoFork"
Set-Location S:\src\SkillCatalog\web
npm test
npm run test:e2e
```

Vitest currently emits a non-failing Fluent UI/Tabster `NodeFilter` teardown warning under jsdom; all assertions pass, and the Playwright axe suites run without serious or critical violations.

## Manual acceptance

1. Search and filter for a skill.
2. Open its shareable detail URL and inspect sanitized instructions and resources.
3. Navigate and download with only the keyboard.
4. Confirm the ZIP contains only the selected skill plus `skill-package.json`.
5. Verify layouts at 320px, 768px, and 1440px without horizontal scrolling.
6. Configure an invalid repository root and verify startup fails closed.
