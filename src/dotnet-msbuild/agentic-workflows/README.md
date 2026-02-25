# MSBuild Agentic Workflow Templates

These are [GitHub Agentic Workflow](https://github.com/github/gh-aw) templates for MSBuild and .NET build automation. All workflows are triggered by posting a comment on an issue or pull request.

## Available Workflows

| Command | Workflow | Description |
|---------|----------|-------------|
| `/analyze-build-failure` | [build-failure-analysis](build-failure-analysis.md) | Analyzes CI build failures via binlog and posts diagnostic comments with root cause and suggested fixes |
| `/audit-build-perf` | [build-perf-audit](build-perf-audit.md) | Runs a build, analyzes performance bottlenecks, and creates an issue with findings and optimization recommendations |
| `/review-msbuild` | [msbuild-pr-review](msbuild-pr-review.md) | Reviews MSBuild project file changes for anti-patterns, correctness issues, and modernization opportunities |

## Setup

1. Install the `gh aw` CLI extension
2. Copy the desired workflow files to your repository's `.github/workflows/` directory
3. Copy the `shared/` directory as well (workflows import from it)
4. Compile: `gh aw compile`
5. Commit both the `.md` and generated `.lock.yml` files
6. Post a trigger command as a comment on any issue or PR to invoke the workflow

## Customization

- Adjust `safe-outputs` limits as needed
