# Quickstart: Guided Skill Submission Workspace

## Run

From `src/SkillCatalog`, start the API:

```powershell
dotnet run --project api/SkillCatalog.Api
```

From `src/SkillCatalog/web`:

```powershell
npm ci
npm run dev
```

Open `http://127.0.0.1:5173/` and choose **Create skill**.

## P1 verification

1. Select an existing plugin and unique kebab-case name.
2. Complete purpose, boundaries, inputs, workflow, validation, owners, motivation, and one positive scenario.
3. Validate, preview, and download.
4. Confirm the ZIP contains SKILL.md, eval.yaml, CODEOWNERS additions, and contribution summary at documented paths.

## Failure verification

- Existing name blocks packaging.
- Absolute or traversing paths produce errors.
- Insecure links, pipe-to-shell, secret-like values, and unknown domains produce safety findings.
- Skill-name leakage in a scenario produces an overfitting warning.
- No positive scenario blocks packaging.
- Editing a validated draft marks validation stale.

## Automated checks

```powershell
dotnet test api/SkillCatalog.Api.Tests/SkillCatalog.Api.Tests.csproj
dotnet test api/SkillCatalog.Api.ContractTests/SkillCatalog.Api.ContractTests.csproj
cd web
npm test
npm run build
npm run test:e2e
```

Tests cover rule codes, canonical rendering, size/archive boundaries, non-persistence, endpoint shapes, draft recovery, accessibility, mobile/desktop completion, unsafe inputs, and existing catalog regression. Telemetry must never contain draft text, prompts, resources, or generated files.

## Handoff

The ZIP's contribution summary instructs users to merge approved CODEOWNERS entries, run static validation and repeated skill evaluations, then open the issue and PR required by repository policy. This feature performs no GitHub write.