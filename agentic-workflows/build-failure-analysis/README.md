# Build Failure Analysis

Installs the .NET SDK Build Failure Analysis agentic workflow. When the
`dotnet-sdk-public-ci` GitHub check fails, the workflow downloads the binary logs already
published by the Azure Pipelines build, analyzes them with `binlog-mcp`, and posts an
advisory summary and inline suggestions on the pull request. It does not rebuild or
execute pull request code.

## Install

From the root of the consuming repository:

```powershell
gh aw add-wizard dotnet/skills/agentic-workflows/build-failure-analysis@main
```

For non-interactive installation:

```powershell
gh aw add dotnet/skills/agentic-workflows/build-failure-analysis@main
```

Pin production installations to a release tag or commit SHA instead of `main`.

The package installs:

- `.github/workflows/build-failure-analysis.md`
- `.github/workflows/build-failure-analysis-fetch.md`
- `.github/workflows/build-failure-analysis-shared.md`
- `.github/workflows/build-failure-analysis-pat-pool.md`
- `.github/agents/build-failure-analyst.agent.md`
- the generated `.github/workflows/build-failure-analysis.lock.yml`

## Repository-specific assumptions

This initial package intentionally preserves the SDK workflow's configuration. A
consumer must either match or customize these assumptions after installation:

- Azure DevOps organization/project: `dnceng-public/public`
- Pipeline and GitHub check name: `dotnet-sdk-public-ci`
- Azure Pipelines definition ID: `101`
- Build artifacts named `<Leg>_Logs_Attempt<N>` containing `*.binlog` files
- A protected `copilot-pat-pool` environment with at least one
  `COPILOT_PAT_0` through `COPILOT_PAT_9` environment secret

The source grants `copilot-requests: write`, but currently retains the .NET team's PAT
pool override. Consumers without that setup must replace the PAT-pool import, environment,
and `engine.env.COPILOT_GITHUB_TOKEN` override with their supported Copilot authentication
configuration before enabling the workflow.

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
