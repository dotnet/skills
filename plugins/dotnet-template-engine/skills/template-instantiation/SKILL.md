---
name: template-instantiation
description: >
  Creates .NET projects from templates with validated parameters, smart defaults,
  Central Package Management adaptation, and latest NuGet version resolution.
  Powered by the DotnetTemplateMCP MCP server.
  USE FOR: creating new dotnet projects, scaffolding solutions with multiple projects,
  installing or uninstalling template packages, creating projects that respect
  Directory.Packages.props (CPM), composing multi-project solutions (API + tests + library),
  getting latest NuGet package versions in newly created projects.
  DO NOT USE FOR: finding or comparing templates (use template-discovery), authoring
  custom templates (use template-authoring), modifying existing projects or adding
  NuGet packages to existing projects.
---

# Template Instantiation

This skill creates .NET projects from templates using the DotnetTemplateMCP MCP server. It provides validated parameter handling, automatic Central Package Management adaptation, latest NuGet version resolution, and multi-template composition — capabilities beyond raw `dotnet new` commands.

## When to Use

- User asks to create a new .NET project, app, or service
- User needs a solution with multiple projects (API + tests + library)
- User wants to create a project that respects existing `Directory.Packages.props`
- User needs to install or manage template packages

## When Not to Use

- User is searching for or comparing templates — route to `template-discovery` skill
- User wants to author a custom template — route to `template-authoring` skill
- User wants to add packages to an existing project — use `dotnet add package` directly

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Template name or intent | Yes | Template short name (e.g., `webapi`) or natural-language description |
| Project name | Yes | Name for the created project |
| Output path | Recommended | Directory where the project should be created |
| Parameters | No | Template-specific parameters (e.g., `Framework`, `auth`, `EnableAot`) |

## Workflow

### Step 1: Resolve template and parameters

If the user provides a natural-language description, use `template_from_intent` first to resolve it to a template and parameters. If they provide a template name, proceed directly.

Use `template_suggest_parameters` to fill in smart defaults for any parameters the user did not specify.

### Step 2: Analyze the workspace

Use `solution_analyze` to understand the existing solution structure:
- Is Central Package Management (CPM) enabled?
- What target frameworks are in use?
- Is there a `global.json` pinning the SDK?

This ensures the new project is consistent with the workspace.

### Step 3: Preview the creation

Use `template_dry_run` to show the user what files would be created. Confirm before proceeding.

```
template_dry_run("webapi", name="MyApi", parametersJson={"Framework": "net10.0"})
```

### Step 4: Create the project

Use `template_instantiate` with all parameters. The MCP server automatically:
- **Validates parameters** and reports errors with "did you mean?" suggestions
- **Auto-resolves from NuGet** if the template is not installed
- **Adapts to CPM** — detects `Directory.Packages.props`, strips versions from `.csproj`, adds `<PackageVersion>` entries
- **Resolves latest NuGet versions** — replaces template-hardcoded package versions with latest stable releases

```
template_instantiate("webapi", name="MyApi", outputPath="./src/MyApi",
  parametersJson={"Framework": "net10.0", "auth": "Individual"})
```

### Step 5: Multi-project composition (optional)

For complex structures, use `template_compose` to create multiple projects in one orchestrated workflow:

```
template_compose(stepsJson=[
  {"templateName": "webapi", "name": "MyApi", "outputPath": "./src/MyApi"},
  {"templateName": "xunit", "name": "MyApi.Tests", "outputPath": "./tests/MyApi.Tests"}
])
```

### Step 6: Template package management

- **Install**: `template_install("Microsoft.DotNet.Web.ProjectTemplates.10.0")`
- **Uninstall**: `template_uninstall("Microsoft.DotNet.Web.ProjectTemplates.10.0")`

Both operations are idempotent. Install supports upgrade detection.

### Step 7: Post-creation verification

1. Verify the project builds: `dotnet build`
2. If added to a solution, verify `dotnet build` at the solution level
3. If CPM was adapted, verify `Directory.Packages.props` has the new entries

## Validation

- [ ] Project was created successfully with the expected files
- [ ] Project builds cleanly with `dotnet build`
- [ ] If CPM is active, `.csproj` has no version attributes and `Directory.Packages.props` has matching entries
- [ ] Package versions in the project are current (not stale template defaults)
- [ ] If multi-project, all projects build and reference each other correctly

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Using `dotnet new` directly instead of `template_instantiate` | `dotnet new` does not adapt to CPM, does not resolve latest NuGet versions, and does not validate parameters with suggestions. Always prefer the MCP tool. |
| Not checking for CPM before creating a project | If `Directory.Packages.props` exists, a raw `dotnet new` creates projects with inline versions that conflict. `template_instantiate` handles this automatically. |
| Creating projects without specifying the framework | Always specify `--framework` when the template supports multiple TFMs to avoid defaulting to an older version. |
| Not adding the project to the solution | After creation, run `dotnet sln add` to include the project in the solution. |

## More Info

- [DotnetTemplateMCP](https://github.com/YuliiaKovalova/dotnet-template-mcp) — MCP server source and documentation
- [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management) — CPM documentation
- [dotnet new](https://learn.microsoft.com/dotnet/core/tools/dotnet-new) — CLI reference
