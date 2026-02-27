# .NET Agent Skills

[![Dashboard](https://github.com/dotnet/skills/actions/workflows/pages/pages-build-deployment/badge.svg)](https://refactored-sniffle-qm9o678.pages.github.io/)

This repository contains the .NET team's curated set of core skills and custom agents for coding agents. For information about the Agent Skills standard, see [agentskills.io](http://agentskills.io).

## What's Included

| Plugin | Description |
|--------|-------------|
| [dotnet](plugins/dotnet/) | Collection of core .NET skills for handling common .NET coding tasks. |
| [dotnet-msbuild](plugins/dotnet-msbuild/) | Comprehensive MSBuild and .NET build skills: failure diagnosis, performance optimization, code quality, and modernization. |

## Installation

### 🚀 Plugins - Copilot CLI / Claude Code

1. Launch Copilot CLI or Claude Code
2. Add the marketplace:
   ```
   /plugin marketplace add dotnet/skills
   ```
3. Install a plugin:
   ```
   /plugin install <plugin>@dotnet-agent-skills
   ```
4. Restart to load the new plugins
5. View available skills:
   ```
   /skills
   ```
6. View available agents:
   ```
   /agents
   ```
7. Update plugin (on demand):
   ```
   /plugin update <plugin>@dotnet-agent-skills
   ```

### ⚡ Agentic Workflows

Some plugins include [GitHub Agentic Workflow](https://github.com/github/gh-aw) templates for CI/CD automation:

1. Install the `gh aw` CLI extension
2. Copy the desired workflow `.md` files and the `shared/` directory to your repository's `.github/workflows/`
3. Compile and commit:
   ```
   gh aw compile
   ```
4. Commit both the `.md` and generated `.lock.yml` files

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and how to add a new plugin.

## License

See [LICENSE](LICENSE) for details.
