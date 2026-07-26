# Quickstart: Skill Catalog UI

## Prerequisites

- .NET 10 SDK
- Node.js 22 LTS or newer
- npm 10 or newer
- Git checkout containing `plugins/`

The application uses `src/SkillCatalog/global.json`; the repository's .NET 11 preview SDK is not required for this feature.

## Configure

```powershell
$env:SkillCatalog__RepositoryRoot = $PWD.Path
$env:SkillCatalog__RepositoryUrl = "https://github.com/JonC613/skills"
```

## Run the API

```powershell
dotnet restore src/SkillCatalog/SkillCatalog.slnx
dotnet run --project src/SkillCatalog/api/SkillCatalog.Api
Invoke-RestMethod http://localhost:5080/api/catalog
```

## Run the Web App

```powershell
Set-Location src/SkillCatalog/web
npm ci
npm run dev
```

## Verify

```powershell
dotnet test src/SkillCatalog/SkillCatalog.slnx
Set-Location src/SkillCatalog/web
npm run lint
npm run test
npm run build
npm run test:e2e
```

## Manual Acceptance

1. Search and filter for a skill.
2. Open its shareable detail URL and inspect resources.
3. Navigate and download with only the keyboard.
4. Confirm the ZIP contains only the selected skill directory.
5. Verify the layout at 320px without horizontal page scrolling.
6. Configure an invalid repository root and verify the unavailable state.
