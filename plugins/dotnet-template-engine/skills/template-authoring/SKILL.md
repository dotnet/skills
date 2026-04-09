---
name: template-authoring
description: >
  Guides creation and validation of custom dotnet new templates. Generates templates
  from existing projects and validates template.json for authoring issues.
  USE FOR: creating a reusable dotnet new template from an existing project, validating
  template.json files for schema compliance and parameter issues, bootstrapping
  .template.config/template.json with correct identity, shortName, parameters, and
  post-actions, packaging templates as NuGet packages for distribution.
  DO NOT USE FOR: finding or using existing templates (use template-discovery and
  template-instantiation), MSBuild project file issues unrelated to template authoring,
  NuGet package publishing (only template packaging structure).
---

# Template Authoring

This skill helps an agent create and validate custom `dotnet new` templates. It guides bootstrapping templates from existing projects and validates `template.json` files for authoring issues before publishing.

## When to Use

- User wants to create a reusable template from an existing .csproj
- User wants to validate a template.json for correctness
- User is setting up `.template.config/template.json` from scratch
- User wants to package a template for NuGet distribution

## When Not to Use

- User wants to find or use existing templates — route to `template-discovery` or `template-instantiation`
- User has MSBuild issues unrelated to template authoring — route to `dotnet-msbuild` plugin

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Source project path | For creation | Path to the .csproj to use as template source |
| template.json path | For validation | Path to an existing template.json to validate |
| Template name | For creation | Human-readable name for the template |
| Short name | Recommended | Short name for `dotnet new <shortname>` usage |

## Workflow

### Step 1: Bootstrap from existing project

Analyze the source `.csproj` and create a `.template.config/template.json`:

1. Create `.template.config` directory next to the project
2. Generate `template.json` with `identity` (reverse-DNS), `name`, `shortName`, `sourceName` (project name for replacement), `classifications`, and `tags`
3. Preserve from source: SDK type, package references with metadata (PrivateAssets, IncludeAssets), properties (OutputType, TreatWarningsAsErrors), CPM patterns

Minimal example:
```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "MyOrg",
  "classifications": ["Library"],
  "identity": "MyOrg.Templates.MyLib",
  "name": "My Library Template",
  "shortName": "mylib",
  "sourceName": "MyLib",
  "tags": { "language": "C#", "type": "project" }
}
```

### Step 2: Validate template.json

Read and review the `template.json` for common authoring issues:

Validation checks to perform:
- **Required fields** — verify `identity`, `name`, and `shortName` are present
- **Identity format** — use reverse-DNS format (e.g., `MyOrg.Templates.WebApi`)
- **Parameter issues** — check datatypes are valid (`string`, `bool`, `choice`, `int`, `float`), choices have defaults, descriptions are present
- **ShortName conflicts** — avoid names that collide with built-in CLI commands (`build`, `run`, `test`, `publish`). Check with `dotnet new list` to see if the name is already taken
- **Post-action completeness** — verify post-actions have all required configuration
- **Tags** — ensure language, type, and classification tags are set for discoverability

### Step 3: Refine the template

Based on validation results and user requirements:

1. **Add parameters** with appropriate types (string, bool, choice), defaults, and descriptions
2. **Add conditional content** using `#if` preprocessor directives for optional features
3. **Configure post-actions** for solution add, restore, or custom scripts
4. **Set constraints** to restrict which SDKs or workloads the template supports
5. **Add classifications** and tags for discoverability

### Step 4: Test the template locally

```bash
dotnet new install ./path/to/template/root
dotnet new mylib --name TestProject --dry-run
dotnet new mylib --name TestProject --output ./test-output
dotnet build ./test-output/TestProject
```

## Validation

- [ ] `template.json` passes manual validation with zero errors
- [ ] Template identity and shortName are unique and meaningful
- [ ] All parameters have descriptions and appropriate defaults
- [ ] Template can be installed, dry-run, and instantiated successfully
- [ ] Created projects build cleanly with `dotnet build`
- [ ] Conditional content produces correct output for all parameter combinations

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Identity format issues | Use reverse-DNS format (e.g., `MyOrg.Templates.WebApi`). Avoid spaces or special characters. |
| ShortName conflicts with CLI commands | Avoid names like `build`, `run`, `test`, `publish`. Check by running `dotnet new list` to see if the name is already taken. |
| Missing parameter descriptions | Every parameter should have a `description` and `displayName` for discoverability. |
| Not testing all parameter combinations | Use `dotnet new <template> --dry-run` with different parameter values to verify conditional content works correctly. |
| Hardcoded versions in template | Use `sourceName` replacement for project names and consider parameterizing framework versions. |
| Not setting classifications | Add appropriate `classifications` (e.g., `["Web", "API"]`) for template discovery. |

## More Info

- [Custom templates for dotnet new](https://learn.microsoft.com/dotnet/core/tools/custom-templates) — official authoring guide
- [template.json reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json) — full schema reference
- [Template Engine Wiki](https://github.com/dotnet/templating/wiki) — template engine internals
