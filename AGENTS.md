# Repository Instructions

This repository contains skill plugins under `plugins/`. Each subdirectory in `plugins/` is an independent plugin (e.g., `plugins/dotnet-msbuild`, `plugins/dotnet`).

## Build

When you modify skills, run the agentic-workflows build script to validate and regenerate compiled artifacts.

```powershell
pwsh agentic-workflows/<plugin>/build.ps1
```

This validates skill frontmatter and recompiles knowledge lock files. Always commit the regenerated lock files together with your changes.

## Skill-Validator

Don't care much about backwards-compatibility for this tool. Consumers understand that the shape is constantly changing.

The skill-validator is a shipping tool — its NuGet package and `.tar.gz` archives are built from `eng/skill-validator/src/`. Content referenced at runtime or bundled with the tool (docs, README, etc.) must live under `src/` so it is included in the published output. Do not add references from `src/` to files outside of it, except for explicitly linked packaging assets (such as the repo-root `LICENSE` file) referenced by the project file.

When modifying the evaluation pipeline (`evaluation.yml`), results JSON schema (`Models.cs`), or the skill-validator evaluation logic, review and update `eng/skill-validator/src/docs/InvestigatingResults.md` to keep the failure investigation guidance, schema documentation, and example scripts in sync.
