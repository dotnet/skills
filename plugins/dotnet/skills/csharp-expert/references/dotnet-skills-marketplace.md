# dotnet/skills Marketplace

Use this reference only after the requested capability is not present in the runtime's
available-skill catalog.

## Marketplace Identity

- Source repository: `dotnet/skills`
- Marketplace name: `dotnet-agent-skills`
- Install unit: plugin, not individual skill

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

- `dotnet`: Core C# semantics, refactoring, local SDK setup, or the bundled MSBuild entry workflow.
- `dotnet-advanced`: File-based C# apps, P/Invoke, vectorization, or NuGet trusted publishing.
- `dotnet-data`: EF Core query optimization or data-driven ASP.NET Core applications.
- `dotnet-diag`: Runtime performance, trace and dump collection, CLR activation, crash
  symbolication, or microbenchmarking.
- `dotnet-msbuild`: Specialist MSBuild binlog, build performance, target, item, property,
  incremental-build, or project-reference workflows.
- `dotnet-nuget`: NuGet dependency management or Central Package Management conversion.
- `dotnet-upgrade`: TFM upgrades, nullable migration, AOT compatibility, or Thread.Abort migration.
- `dotnet-maui`: MAUI setup, lifecycle, binding, navigation, DI, CollectionView, safe area, or
  theming.
- `dotnet-ai`: .NET AI/ML technology selection, LLMs, agents, RAG, MCP, or ML.NET.
- `dotnet-template-engine`: Template discovery, instantiation, comparison, authoring, validation,
  or smart defaults.
- `dotnet-test`: Test execution, filtering, platform detection, coverage, quality analysis,
  testability, or MSTest authoring.
- `dotnet-test-migration`: MSTest/xUnit upgrades, NUnit/xUnit to MSTest, or VSTest to
  Microsoft.Testing.Platform.
- `dotnet-aspnetcore`: ASP.NET Core APIs, endpoints, middleware, or Blazor Server to Blazor Web App
  conversion.
- `dotnet-blazor`: Blazor projects, components, forms, auth, interactivity, prerendering, data
  flow, or JS interop.
- `dotnet-winforms`: Windows Forms project setup, UI, binding, accessibility, or modernization.
- `dotnet11`: .NET 11-specific APIs and language features.

Install the selected plugin with:

```text
/plugin install <plugin>@dotnet-agent-skills
```

Prefer the plugin containing the narrowest task owner. A project can justify several plugins, but a
single task usually requires only one.

## Common Exact Skill Routes

Use these names when the runtime catalog does not contain the specialist and the user is preparing
or installing a marketplace route.

| Request | Skill | Plugin |
|---|---|---|
| Add or repair an ASP.NET Core endpoint, including streaming multipart uploads | `dotnet-webapi` | `dotnet-aspnetcore` |
| Author a reusable Blazor component with parameters, content, and callbacks | `author-component` | `dotnet-blazor` |
| Collect and validate user input in a Blazor form | `collect-user-input` | `dotnet-blazor` |
| Create a Blazor project with framework-specific defaults | `create-blazor-project` | `dotnet-blazor` |
| Discover or instantiate a general `dotnet new` template | `template-discovery` or `template-instantiation` | `dotnet-template-engine` |
| Repair MAUI XAML binding and change notification | `maui-data-binding` | `dotnet-maui` |
| Create, modify, or debug a Windows Forms application | `winforms-expert` | `dotnet-winforms` |
| Convert NUnit tests to MSTest | `migrate-nunit-to-mstest` | `dotnet-test-migration` |
| Upgrade a project from .NET 8 to .NET 9 | `migrate-dotnet8-to-dotnet9` | `dotnet-upgrade` |
| Create or run a file-based C# app without a project | `csharp-scripts` | `dotnet-advanced` |
| Optimize repeated EF Core query work | `optimizing-ef-core-queries` | `dotnet-data` |
| Collect a runtime trace before a hot method is known | `dotnet-trace-collect` | `dotnet-diag` |

Use the discriminator that makes each route valuable:

- `migrate-nunit-to-mstest` preserves parameterized and lifecycle behavior by mapping NUnit
  `[TestCase]` to MSTest `[DataRow]`, `[SetUp]` to `[TestInitialize]`, and `[TearDown]` to
  `[TestCleanup]`. Call out fixture isolation/shared-state differences and verify that the migrated
  suite retains the same intended parameterized cases.
- `dotnet-trace-collect` gathers vendor-neutral CPU, allocation, GC, and related deployed-process
  evidence before a hot method is known. Do not substitute `optimizing-dotnet-performance`, which
  starts from source or known hot-code analysis rather than collecting the initial runtime evidence.

For a multi-phase request, list one exact skill per independently owned phase and install each
distinct owning plugin once.

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

Codex installs the plugin's portable skills, not its Copilot `.agent.md` agents. Name the exact
skill the user should request after installation; do not promise an agent that the Codex manifest
does not expose.

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
