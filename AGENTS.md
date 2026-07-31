# Repository Instructions

This repository contains skill plugins under `plugins/`. Each subdirectory in `plugins/` is an independent plugin (e.g., `plugins/dotnet-msbuild`, `plugins/dotnet`).

## Working on skills and evals

Use the repository's own authoring skills under `.agents/skills/` instead of improvising:

- `create-skill` — scaffolding a new skill, and writing a `description` the runtime will route to.
- `create-skill-test` — writing or resizing an `eval.yaml`. Evals use the Vally schema
  (`stimuli:` / `graders:` / `defaults:`); `scenarios:` / `assertions:` no longer load.
- `improve-skill-quality` — an eval regressed, produced no verdict, or the skill did not activate.
  Classify the failure before editing skill content; broken fixtures, underpowered trial counts and
  harness errors routinely masquerade as skill regressions.

Before pushing eval changes, run `python eng/eval-quality/check_eval_quality.py`. It blocks ten
structural defect classes documented in `eng/eval-quality/README.md`, each of which has already cost
a real evaluation result here.

The distilled quality rules — what makes a skill beat its own baseline — live in the "Quality bar"
section of `CONTRIBUTING.md`.

## Skill-Validator

The skill-validator is a shipping tool — its NuGet package and `.tar.gz` archives are built from `eng/skill-validator/src/`. Content referenced at runtime or bundled with the tool (docs, README, etc.) must live under `src/` so it is included in the published output. Do not add references from `src/` to files outside of it, except for explicitly linked packaging assets (such as the repo-root `LICENSE` file) referenced by the project file.

When modifying the evaluation pipeline (`evaluation.yml`), results JSON schema (`Models.cs`), or the skill-validator evaluation logic, review and update `eng/skill-validator/src/docs/InvestigatingResults.md` to keep the failure investigation guidance, schema documentation, and example scripts in sync.
