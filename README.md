# .NET Agent Skills

[![Dashboard](https://github.com/dotnet/skills/actions/workflows/pages/pages-build-deployment/badge.svg)](https://dotnet.github.io/skills/)

This repository contains the .NET team's curated set of core skills and custom agents for coding agents. For information about the Agent Skills standard, see [agentskills.io](https://agentskills.io).

[**📊 Dashboard**](https://dotnet.github.io/skills/) - Accuracy and efficiency scoring trends for contained plugins (https://dotnet.github.io/skills/)

## What's Included

| Plugin | Description |
|--------|-------------|
| [dotnet](plugins/dotnet/) | Collection of core .NET skills for handling common .NET coding tasks. |
| [dotnet-data](plugins/dotnet-data/) | Skills for .NET data access and Entity Framework related tasks. |
| [dotnet-diag](plugins/dotnet-diag/) | Skills for .NET performance investigations, debugging, and incident analysis. |
| [dotnet-msbuild](plugins/dotnet-msbuild/) | Comprehensive MSBuild and .NET build skills: failure diagnosis, performance optimization, code quality, and modernization. |
| [dotnet-nuget](plugins/dotnet-nuget/) | NuGet and .NET package management: dependency management and modernization. |
| [dotnet-upgrade](plugins/dotnet-upgrade/) | Skills for migrating and upgrading .NET projects across framework versions, language features, and compatibility targets. |
| [dotnet-maui](plugins/dotnet-maui/) | Skills for .NET MAUI development: environment setup, diagnostics, and troubleshooting. |
| [dotnet-ai](plugins/dotnet-ai/) | AI and ML skills for .NET: technology selection, LLM integration, agentic workflows, RAG pipelines, MCP, and classic ML with ML.NET. |
| [dotnet-template-engine](plugins/dotnet-template-engine/) | .NET Template Engine skills: template discovery, project scaffolding, and template authoring. |
| [dotnet-test](plugins/dotnet-test/) | Skills for running, generating, analyzing, and improving .NET tests: test execution, filtering, platform detection, coverage, testability, and MSTest workflows. |
| [dotnet-test-migration](plugins/dotnet-test-migration/) | Skills and an orchestrator agent for migrating .NET test frameworks and platforms: MSTest and xUnit version upgrades, xUnit-to-MSTest conversion, and VSTest to Microsoft.Testing.Platform. |
| [dotnet-aspnetcore](plugins/dotnet-aspnetcore/) | ASP.NET Core web development skills including middleware, endpoints, real-time communication, and API patterns. |
| [dotnet-blazor](plugins/dotnet-blazor/) | Skills for Blazor development: component authoring, interactivity, and web application patterns. |
| [dotnet11](plugins/dotnet11/) | Skills for new .NET 11 APIs and language features. |

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

### VS Code / VS Code Insiders (Preview)

> [!IMPORTANT]  
> VS Code plugin support is a preview feature and subject to change. You may need to enable it first.

```jsonc
// settings.json
{
  "chat.plugins.enabled": true,
  "chat.plugins.marketplaces": ["dotnet/skills"]
}
```

Once configured, type `/plugins` in Copilot Chat or use the `@agentPlugins` filter in Extensions to browse and install plugins from the marketplace.

### Cursor

This repository is a [Cursor plugin marketplace](https://cursor.com/docs/plugins). You can discover and install published plugins directly in Cursor:

1. Open the marketplace panel in Cursor
2. Search for `.NET` or browse [cursor.com/marketplace](https://cursor.com/marketplace)
3. Install the desired plugins

For local development or unpublished changes, import plugins from a local checkout:

1. Copy or symlink your local checkout to `~/.cursor/plugins/local/dotnet-agent-skills`
2. Restart Cursor or run **Developer: Reload Window**

### Codex CLI

Skills in this repository follow the [agentskills.io](https://agentskills.io) open standard
and are compatible with [OpenAI Codex](https://developers.openai.com/codex/skills).

#### Plugin marketplace (recommended)

Codex CLI v0.121.0 and later supports a [plugin marketplace](https://developers.openai.com/codex/plugins).
This repository ships a Codex-native marketplace manifest at `.agents/plugins/marketplace.json`,
so you can register `dotnet/skills` as a marketplace and install plugins from it directly.

1. Add the marketplace:
   ```bash
   codex plugin marketplace add dotnet/skills
   ```
2. Launch Codex and open the plugin browser:
   ```
   /plugins
   ```
3. Browse the `dotnet-agent-skills` tab and install the desired plugins.
4. Update plugins on demand:
   ```bash
   codex plugin marketplace upgrade dotnet-agent-skills
   ```

#### Individual skills

You can also install individual skills using the `skill-installer` CLI with the GitHub URL:

```bash
$ skill-installer install https://github.com/dotnet/skills/tree/main/plugins/<plugin>/skills/<skill-name>
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and how to add a new plugin.

## License

See [LICENSE](LICENSE) for details.
