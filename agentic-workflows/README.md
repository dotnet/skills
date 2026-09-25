# .NET Agentic Workflows

This directory publishes reusable [GitHub Agentic Workflows](https://github.com/github/gh-aw)
that can be installed into other repositories. The workflow sources are kept outside
`.github/workflows/` so they do not run in this repository.

## Available packages

| Package | Description |
| --- | --- |
| [Build Failure Analysis](build-failure-analysis/) | Analyzes failed `dotnet-sdk-public-ci` Azure Pipelines builds from their existing binary logs and posts evidence-backed findings on the pull request. |

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
