# .NET Agent Skills

[![Dashboard](https://github.com/dotnet/skills/actions/workflows/pages/pages-build-deployment/badge.svg)](https://refactored-sniffle-qm9o678.pages.github.io/)

This repositorycontains the .NET team's curated set of core skills and custom agents for coding agents. For information about the Agent Skills standard, see [agentskills.io](http://agentskills.io).

## What's Included

| Component | Description |
|-----------|-------------|
| [dotnet](src/dotnet/) | Collection of core .NET skills for handling common .NET coding tasks. |
| [dotnet-msbuild](src/dotnet-msbuild/) | Comprehensive MSBuild and .NET build skills: failure diagnosis, performance optimization, code quality, and modernization. |

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

### 📦 Distribution Templates

Some components include ready-to-use templates (agent instructions, prompt files) that can be copied directly into your repository without installing a plugin or extension:

1. Browse the component's **Distribution Templates** section in its README
2. Copy agent instructions to your repo root as `AGENTS.md`
3. Copy prompt files to `.github/prompts/`

### ⚡ Agentic Workflows

Some components include [GitHub Agentic Workflow](https://github.com/github/gh-aw) templates for CI/CD automation:

1. Install the `gh aw` CLI extension
2. Copy the desired workflow `.md` files and the `shared/` directory to your repository's `.github/workflows/`
3. Compile and commit:
   ```
   gh aw compile
   ```
4. Commit both the `.md` and generated `.lock.yml` files

### 🧩 Copilot Extension

Some components include a deployable [Copilot Extension](https://docs.github.com/copilot/building-copilot-extensions) for GitHub.com, VS Code, and Visual Studio:

1. Find the extension in the [GitHub Marketplace](https://github.com/marketplace) or your organization's Copilot Extensions
2. Install the GitHub App on your organization or personal account
3. Use `@<extension-name>` in any Copilot Chat surface to interact with it

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and how to add a new component.

## License

See [LICENSE](LICENSE) for details.
