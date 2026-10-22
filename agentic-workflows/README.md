# .NET Agentic Workflows

This directory publishes reusable [GitHub Agentic Workflows](https://github.com/github/gh-aw)
that can be installed into other repositories. The workflow sources are kept outside
`.github/workflows/` so they do not run in this repository.

## Available packages

| Package | Description |
| --- | --- |
| [Build Failure Analysis](build-failure-analysis/) | Analyzes failed .NET Azure Pipelines builds from their existing binary logs and posts evidence-backed findings on the pull request. |
| [MSBuild Quality Review](msbuild-quality-review/) | Reviews changed MSBuild project and infrastructure files and posts one bounded, evidence-backed PR review comment. |
| [Test Failure Analysis](test-failure-analysis/) | Analyzes bounded, normalized test evidence from a repository-owned CI collector and reports supported failures, flakes, hangs, crashes, and duration regressions. |
| [Unskip Closed Tests](unskip-closed-tests/) | Re-enables source-bound .NET tests only after deterministic tracking-state, revision, edit, and structured execution verification. |

## Installation

Install the complete collection:

```powershell
gh aw add-wizard dotnet/skills/agentic-workflows@main
```

Or install an individual package using the command in its README. Pin production
installations to a release tag or commit SHA instead of `main`.

Installed workflows are tracked by `source:` metadata. Pull compatible updates into a
consumer repository with:

```powershell
gh aw update
```

The installer copies each workflow source and its dependencies into the consumer
repository, then generates the executable `.lock.yml` file there. Generated lock files
are therefore not stored in this distribution directory.

## Scenario evaluation

Every package has a Vally-format spec at
`tests/agentic-workflows/<package>/eval.yaml`. The normal `/evaluate` discovery
includes package sources, resources, shared graders, and these scenarios.
Scheduled evaluations include the collection; manual evaluation dispatch with
`plugin: agentic-workflows` selects only these packages.

For a local run:

```powershell
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- evaluate `
  agentic-workflows/msbuild-quality-review/aw.yml `
  --tests-dir tests/agentic-workflows --runs 1 --verdict-warn-only
```

The native SDK lane loads real local imports and installed package resources,
compares them with a no-workflow baseline, and retains a package-agent arm.
Results are published through the normal pipeline with `skillKind: workflow`
and `evaluationLane: workflow-prompt-sdk`.

These are **offline prompt/decision evaluations**: fixture evidence replaces
collectors and external services, and `result.json` contains proposed actions.
They do not execute Actions bootstrap jobs or publish safe outputs. Compilation,
trusted-helper tests, runtime trace graders, and consumer-repository integration
runs cover different contracts and remain separate evidence.
