# dotnet/skills Marketplace

Use this reference only after the requested capability is not present in the runtime's
available-skill catalog.

## Marketplace Identity

| Item | Value |
|---|---|
| Source repository | `dotnet/skills` |
| Marketplace name | `dotnet-agent-skills` |
| Install unit | Plugin, not individual skill |

## Copilot CLI and Claude Code

```text
/plugin marketplace add dotnet/skills
/plugin install <plugin>@dotnet-agent-skills
```

Restart the host after installation, run `/skills`, and confirm the expected specialist appears
before rerunning the original request.

Update an installed plugin with:

```text
/plugin update <plugin>@dotnet-agent-skills
```

## Plugin Catalog

| Plugin | Install command | Use when the missing capability concerns |
|---|---|---|
| `dotnet` | `/plugin install dotnet@dotnet-agent-skills` | Core C# semantics, refactoring, local SDK setup, or the bundled MSBuild entry workflow |
| `dotnet-advanced` | `/plugin install dotnet-advanced@dotnet-agent-skills` | File-based C# apps, P/Invoke, vectorization, or NuGet trusted publishing |
| `dotnet-data` | `/plugin install dotnet-data@dotnet-agent-skills` | EF Core query optimization or data-driven ASP.NET Core applications |
| `dotnet-diag` | `/plugin install dotnet-diag@dotnet-agent-skills` | Runtime performance, trace collection, dump collection, CLR activation, crash symbolication, or microbenchmarking |
| `dotnet-msbuild` | `/plugin install dotnet-msbuild@dotnet-agent-skills` | Specialist MSBuild binlog, build performance, target, item, property, incremental-build, or project-reference workflows |
| `dotnet-nuget` | `/plugin install dotnet-nuget@dotnet-agent-skills` | NuGet dependency management or Central Package Management conversion |
| `dotnet-upgrade` | `/plugin install dotnet-upgrade@dotnet-agent-skills` | TFM upgrades, nullable migration, AOT compatibility, or Thread.Abort migration |
| `dotnet-maui` | `/plugin install dotnet-maui@dotnet-agent-skills` | MAUI setup, lifecycle, binding, navigation, DI, CollectionView, safe area, or theming |
| `dotnet-ai` | `/plugin install dotnet-ai@dotnet-agent-skills` | .NET AI/ML technology selection, LLMs, agents, RAG, MCP, or ML.NET |
| `dotnet-template-engine` | `/plugin install dotnet-template-engine@dotnet-agent-skills` | Template discovery, instantiation, comparison, authoring, validation, or smart defaults |
| `dotnet-test` | `/plugin install dotnet-test@dotnet-agent-skills` | Test execution, filtering, platform detection, coverage, quality analysis, testability, or MSTest authoring |
| `dotnet-test-migration` | `/plugin install dotnet-test-migration@dotnet-agent-skills` | MSTest/xUnit upgrades, NUnit/xUnit to MSTest, or VSTest to Microsoft.Testing.Platform |
| `dotnet-aspnetcore` | `/plugin install dotnet-aspnetcore@dotnet-agent-skills` | ASP.NET Core APIs, endpoints, middleware, or Blazor Server to Blazor Web App conversion |
| `dotnet-blazor` | `/plugin install dotnet-blazor@dotnet-agent-skills` | Blazor projects, components, forms, auth, interactivity, prerendering, data flow, or JS interop |
| `dotnet-winforms` | `/plugin install dotnet-winforms@dotnet-agent-skills` | Windows Forms project setup, UI, binding, accessibility, or modernization |
| `dotnet11` | `/plugin install dotnet11@dotnet-agent-skills` | .NET 11-specific APIs and language features |

Prefer the plugin containing the narrowest task owner. A project can justify several plugins, but a
single task usually requires only one.

## Codex CLI

Register the marketplace:

```text
codex plugin marketplace add dotnet/skills
```

Launch Codex, open `/plugins`, select the `dotnet-agent-skills` marketplace, and install the chosen
plugin. Update marketplace plugins with:

```text
codex plugin marketplace upgrade dotnet-agent-skills
```

## VS Code

Enable plugin support and register the marketplace in settings:

```jsonc
{
  "chat.plugins.enabled": true,
  "chat.plugins.marketplaces": ["dotnet/skills"]
}
```

Then open `/plugins` in Copilot Chat or use the `@agentPlugins` Extensions filter, install the chosen
plugin, reload the window, and confirm the skill is available.

## Cursor

Open Cursor's marketplace panel, search for the chosen .NET plugin, install it, and reload the
window. Do not substitute a repository checkout unless the user explicitly wants local plugin
development.

## Individual Skill Fallback

When the host supports individual skill installation but not plugins:

```text
skill-installer install https://github.com/dotnet/skills/tree/main/plugins/<plugin>/skills/<skill-name>
```

Use the plugin marketplace when available because it preserves the plugin's complete skill surface
and host integration.
