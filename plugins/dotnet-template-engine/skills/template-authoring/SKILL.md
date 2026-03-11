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

This skill helps an agent create and validate custom `dotnet new` templates. It bootstraps templates from existing projects and validates `template.json` files for authoring issues before publishing.

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

Use `template_create_from_existing` to analyze a `.csproj` and generate a reusable template that preserves the project's exact conventions:

```
template_create_from_existing(projectPath="src/MyLib/MyLib.csproj",
  templateName="My Library Template", shortName="mylib")
```

This generates:
- `.template.config/template.json` with correct identity, parameters, and metadata
- Preserves SDK type, package references with metadata (PrivateAssets, IncludeAssets)
- Preserves properties (OutputType, TreatWarningsAsErrors)
- Detects Central Package Management and shared compiles

### Step 2: Validate template.json

Use `template_validate` to check for authoring issues:

```
template_validate(templateJsonPath="path/to/.template.config/template.json")
```

Validation checks include:
- **Schema compliance** — required fields (identity, name, shortName), identity format
- **Parameter issues** — invalid datatypes, choices without defaults, missing descriptions
- **Prefix collisions** — parameter names that shadow each other (e.g., `auth` vs `authMode`)
- **ShortName conflicts** — names that collide with built-in CLI commands
- **Post-action gaps** — post-actions with incomplete configuration
- **Constraint issues** — constraints with missing or invalid rules
- **Tag recommendations** — missing language, type, or classification tags

The tool returns `{ valid, errors, warnings, suggestions }` JSON.

### Step 3: Refine the template

Based on validation results and user requirements:

1. **Add parameters** with appropriate types (string, bool, choice), defaults, and descriptions
2. **Add conditional content** using `#if` preprocessor directives for optional features
3. **Configure post-actions** for solution add, restore, or custom scripts
4. **Set constraints** to restrict which SDKs or workloads the template supports
5. **Add classifications** and tags for discoverability

### Step 4: Test the template locally

1. Install the template from the local directory:
   ```bash
   dotnet new install ./path/to/template/root
   ```
2. Run a dry-run to verify the output:
   ```
   template_dry_run("mylib", name="TestProject")
   ```
3. Create a test project and verify it builds:
   ```
   template_instantiate("mylib", name="TestProject", outputPath="./test-output")
   ```
4. Verify all parameters produce the expected output

### Step 5: Package for distribution

1. Create a `.nuspec` or use `<PackAsTool>` in a packaging `.csproj`
2. Include the template directory with `.template.config/template.json`
3. Run `dotnet pack` to create the `.nupkg`
4. Test installation from the `.nupkg`:
   ```bash
   dotnet new install ./path/to/package.nupkg
   ```

## Validation

- [ ] `template.json` passes `template_validate` with zero errors
- [ ] Template identity and shortName are unique and meaningful
- [ ] All parameters have descriptions and appropriate defaults
- [ ] Template can be installed, dry-run, and instantiated successfully
- [ ] Created projects build cleanly with `dotnet build`
- [ ] Conditional content produces correct output for all parameter combinations

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Identity format issues | Use reverse-DNS format (e.g., `MyOrg.Templates.WebApi`). Avoid spaces or special characters. |
| ShortName conflicts with CLI commands | Avoid names like `build`, `run`, `test`, `publish`. Use `template_validate` to detect conflicts. |
| Missing parameter descriptions | Every parameter should have a `description` and `displayName` for discoverability. |
| Not testing all parameter combinations | Use `template_dry_run` with different parameter values to verify conditional content works correctly. |
| Hardcoded versions in template | Use `sourceName` replacement for project names and consider parameterizing framework versions. |
| Not setting classifications | Add appropriate `classifications` (e.g., `["Web", "API"]`) for template discovery. |

## More Info

- [Custom templates for dotnet new](https://learn.microsoft.com/dotnet/core/tools/custom-templates) — official authoring guide
- [template.json reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json) — full schema reference
- [Template Engine Wiki](https://github.com/dotnet/templating/wiki) — template engine internals
