# Build Failure Analysis

Installs a configurable .NET Build Failure Analysis agentic workflow. When the
configured Azure Pipelines rollup check fails, the workflow downloads binary logs already
published by that build, analyzes them with `binlog-mcp`, and posts an advisory summary
and inline suggestions on the pull request. It does not rebuild or execute pull request
code.

## Install

From the root of the consuming repository:

```powershell
gh aw add-wizard dotnet/skills/agentic-workflows/build-failure-analysis@main
```

Pin production installations to a release tag or commit SHA instead of `main`.

The interactive installer prompts for these repository variables:

| Variable | Value |
| --- | --- |
| `BUILD_FAILURE_ANALYSIS_CHECK_NAME` | Exact GitHub rollup check name emitted by the Azure Pipelines PR build. |
| `BUILD_FAILURE_ANALYSIS_ADO_ORGANIZATION` | Public Azure DevOps organization name. |
| `BUILD_FAILURE_ANALYSIS_ADO_PROJECT` | Public Azure DevOps project name. |
| `BUILD_FAILURE_ANALYSIS_ADO_DEFINITION_ID` | Numeric Azure Pipelines definition ID. |

This package declares interactive repository configuration, so install it with
`gh aw add-wizard` rather than `gh aw add`.

The package installs:

- `.github/workflows/build-failure-analysis.md`
- `.github/workflows/build-failure-analysis-fetch.md`
- `.github/workflows/build-failure-analysis-shared.md`
- `.github/agents/build-failure-analyst.agent.md`
- the generated `.github/workflows/build-failure-analysis.lock.yml`

## Requirements

- The Azure DevOps project and build artifacts must be publicly readable.
- The configured build must use the GitHub PR merge ref
  (`refs/pull/<number>/merge`) and expose `triggerInfo["pr.sourceSha"]`.
- Build artifacts must contain one or more `*.binlog` files. Artifacts ending in
  `_Logs_Attempt<N>` are deduplicated to the latest attempt per leg; for other naming
  schemes the workflow safely scans every artifact for binlogs.
- GitHub Copilot organization billing for Actions, or another Copilot authentication
  method configured by `gh aw add-wizard`. The workflow uses
  `copilot-requests: write` and does not require the .NET team's PAT-pool infrastructure.

After local customization, run:

```powershell
gh aw compile build-failure-analysis --strict
```

Commit both the installed Markdown source and generated `.lock.yml` in the consuming
repository. Future package updates can be pulled with `gh aw update
build-failure-analysis`; the updater uses a three-way merge to preserve local changes.

## Provenance

This package vendors the workflow, shared imports, and analyst agent from
[`dotnet/sdk`](https://github.com/dotnet/sdk/tree/060de4b26521f6051830bcbe8c355b560df930e0/.github)
at commit `060de4b26521f6051830bcbe8c355b560df930e0`. The workflow was originally
onboarded by [YuliiaKovalova](https://github.com/YuliiaKovalova) in
[`dotnet/sdk@e5c36a9`](https://github.com/dotnet/sdk/commit/e5c36a933e59d0a180cb3b70d3efab26521f20a9).
The shared import files are renamed with a `build-failure-analysis-` prefix when
packaged so `gh aw add` can install them as collision-resistant direct children of
`.github/workflows/`.
