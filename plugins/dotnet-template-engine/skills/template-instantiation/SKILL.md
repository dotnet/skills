---
name: template-instantiation
description: >
  Creates .NET projects from templates with validated parameters, smart defaults,
  Central Package Management adaptation, and latest NuGet version resolution.
  USE FOR: creating new dotnet projects, scaffolding solutions with multiple projects,
  installing or uninstalling template packages, creating projects that respect
  Directory.Packages.props (CPM), composing multi-project solutions (API + tests + library),
  getting latest NuGet package versions in newly created projects.
  DO NOT USE FOR: finding templates (use template-discovery), producing a detailed
  side-by-side comparison of templates (use template-comparison), authoring custom
  templates (use template-authoring), deciding
  cross-parameter defaults such as which framework to pair with native AOT or whether to
  keep HTTPS when auth is enabled (use template-smart-defaults), modifying existing
  projects or adding NuGet packages to existing projects.
license: MIT
---

# Template Instantiation

This skill creates .NET projects from templates using `dotnet new` CLI commands, with guidance for parameter validation, Central Package Management adaptation, and multi-project composition.

> **Match the workspace, then stop.** The highest-value move is aligning the new project with the repo it lands in: detect **CPM** (`Directory.Packages.props`) and the **target framework** used by neighbouring `.csproj` files, and mirror both. **Treat the discovered target framework as an explicit choice** — pass it as `--framework` so `template-smart-defaults` won't override it; deviate only when it's incompatible with a requested feature (then flag the conflict). Do this in as few steps as possible — a `--dry-run`, the create, and one `dotnet build` to confirm is usually enough. Extra exploratory turns add cost without improving the result.

## When to Use

- User asks to create a new .NET project, app, or service
- User needs a solution with multiple projects (API + tests + library)
- User wants to create a project that respects existing `Directory.Packages.props`
- User needs to install or manage template packages

## When Not to Use

- User is searching for templates — route to `template-discovery` skill; for a detailed side-by-side comparison — route to `template-comparison` skill
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

When a parameter the user chose implies a value for an unset *related* parameter, **invoke the `template-smart-defaults` skill** to resolve the gap before assembling the command line — e.g., native AOT implies a recent AOT-capable target framework, a non-`None` `--auth` choice means HTTPS must stay enabled (don't add `--no-https`), and `--use-controllers` excludes the minimal-API option. Smart defaults only fill gaps; never let them override a value the user set explicitly. The workspace framework discovered in Step 2 counts as such an explicit value — pass it to smart-defaults as the chosen `--framework` so it isn't treated as an unset gap; deviate only if it is incompatible with the requested feature/template (then surface the conflict to the user).

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

After creation, adapt the project to Central Package Management and refresh stale versions:

1. **Detect CPM** — walk up the directory tree from the new project looking for a `Directory.Packages.props`.
2. **Strip inline versions** — if found, for each `<PackageReference Include="X" Version="Y" />` the template generated, remove the `Version` attribute from the `.csproj` (leaving `<PackageReference Include="X" />`).
3. **Centralize the version** — add or merge a `<PackageVersion Include="X" Version="Y" />` entry in `Directory.Packages.props`.
4. **Optionally refresh stale template-default versions** — templates often hardcode old versions. Keep the template's versions by default (safest for reproducibility and controlled upgrades). Only refresh when the user asks, and when you do:
   - Prefer a tooling-driven flow: run `dotnet list package --outdated` and confirm the proposed bumps with the user before changing anything.
   - Constrain upgrades to the same **major** (or major/minor) version unless the user explicitly opts into larger upgrades, since cross-major bumps can introduce breaking changes.
   - When checking the latest **stable** version of a package conceptually, the NuGet V3 flat-container `index.json` endpoint for that package ID lists published versions; never select a prerelease unless requested.
5. **Build** — run `dotnet build` to confirm the centralized/refreshed versions resolve.

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
