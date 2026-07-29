# Quickstart: Skill Package Upload and Validation

1. Run the API and web application as documented in `src/SkillCatalog/README.md`.
2. Open `/contribute/skill`.
3. Drop one repository-shaped ZIP or select one `SKILL.md`.
4. Review detected identity, manifest, findings, skill preview, evaluations, and ownership status.
5. If errors exist, fix the source package externally and re-upload it.
6. If valid, download the normalized repository package.

## Automated checks

```powershell
dotnet test api/SkillCatalog.Api.Tests/SkillCatalog.Api.Tests.csproj -c Release
dotnet test api/SkillCatalog.Api.ContractTests/SkillCatalog.Api.ContractTests.csproj -c Release
cd web
npm test
npm run build
npm run test:e2e
```

Security coverage includes ZIP bombs, traversal, duplicate normalized paths, unsupported roots, secret detection, unsafe references, non-execution, redacted telemetry, stale upload state, and server non-persistence.
