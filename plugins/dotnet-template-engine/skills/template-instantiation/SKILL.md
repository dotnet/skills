---
name: template-instantiation
description: >
  Creates .NET projects from templates with validated parameters, smart defaults,
  Central Package Management adaptation, and latest NuGet version resolution.
  USE FOR: creating new dotnet projects, scaffolding solutions with multiple projects,
  installing or uninstalling template packages, creating projects that respect
  Directory.Packages.props (CPM), composing multi-project solutions (API + tests + library),
  getting latest NuGet package versions in newly created projects.
  DO NOT USE FOR: finding or comparing templates (use template-discovery), authoring
  custom templates (use template-authoring), modifying existing projects or adding
  NuGet packages to existing projects.
license: MIT
---

# Template Instantiation

This skill creates .NET projects from templates using `dotnet new` CLI commands, with guidance for parameter validation, Central Package Management adaptation, and multi-project composition.

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
| Parameters | No | Template-specific parameters (e.g., `--framework`, `--auth`, `--aot`) |

## Workflow

### Step 1: Resolve template and parameters

If the user provides a natural-language description, map it to a template short name (see the keyword table in the `template-discovery` skill). If they provide a template name, proceed directly.

Use `dotnet new <template> --help` to review available parameters, defaults, and types for any parameters the user did not specify.

### Step 2: Analyze the workspace

Check the existing solution structure before creating:
- Is Central Package Management (CPM) enabled? Look for `Directory.Packages.props`
- What target frameworks are in use? Check existing `.csproj` files
- Is there a `global.json` pinning the SDK?

This ensures the new project is consistent with the workspace.

### Step 3: Preview the creation

Use `dotnet new <template> --dry-run` to show the user what files would be created. Confirm before proceeding.

```bash
dotnet new webapi --name MyApi --framework net10.0 --dry-run
```

### Step 4: Create the project

Use `dotnet new` with the template name and all parameters:

```bash
dotnet new webapi --name MyApi --output ./src/MyApi --framework net10.0 --auth Individual
```

#### Common parameter combinations

| Template | Parameters | Example |
|----------|-----------|---------|
| `webapi` | `--auth` (None, Individual, SingleOrg, Windows), `--aot` (native AOT) | `dotnet new webapi -n MyApi --auth Individual --aot` |
| `webapi` | `--use-controllers` (use controllers vs minimal APIs) | `dotnet new webapi -n MyApi --use-controllers` |
| `blazor` | `--interactivity` (None, Server, WebAssembly, Auto), `--auth` | `dotnet new blazor -n MyApp --interactivity Server` |
| `grpc` | `--aot` (native AOT) | `dotnet new grpc -n MyService --aot` |
| `worker` | `--aot` (native AOT) | `dotnet new worker -n MyWorker --aot` |

Note: Use `dotnet new <template> --help` to see all available parameters for any template.

After creation, if the workspace uses CPM:
1. Check `.csproj` for inline `<PackageReference>` versions
2. Move version attributes to `Directory.Packages.props` as `<PackageVersion>` entries
3. Remove `Version` attributes from the `.csproj`

### Step 5: Multi-project composition (optional)

For complex structures, create each project sequentially and wire them together:

```bash
dotnet new webapi --name MyApi --output ./src/MyApi
dotnet new xunit --name MyApi.Tests --output ./tests/MyApi.Tests
dotnet add ./tests/MyApi.Tests reference ./src/MyApi
dotnet sln add ./src/MyApi ./tests/MyApi.Tests
```

### Step 6: Template package management

Install or uninstall template packages:

```bash
dotnet new install Microsoft.DotNet.Web.ProjectTemplates.10.0
dotnet new uninstall Microsoft.DotNet.Web.ProjectTemplates.10.0
```

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
| Not checking for CPM before creating a project | If `Directory.Packages.props` exists, `dotnet new` creates projects with inline versions that conflict. After creation, move versions to `Directory.Packages.props` and remove them from `.csproj`. |
| Creating projects without specifying the framework | Always specify `--framework` when the template supports multiple TFMs to avoid defaulting to an older version. |
| Not adding the project to the solution | After creation, run `dotnet sln add` to include the project in the solution. |
| Not verifying the project builds | Always run `dotnet build` after creation to catch missing dependencies or parameter issues early. |

## More Info

- [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management) — CPM documentation
- [dotnet new](https://learn.microsoft.com/dotnet/core/tools/dotnet-new) — CLI reference
