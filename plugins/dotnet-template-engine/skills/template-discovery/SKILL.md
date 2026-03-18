---
name: template-discovery
description: >
  Helps find, inspect, and compare .NET project templates.
  Resolves natural-language project descriptions to ranked template matches
  with pre-filled parameters.
  USE FOR: finding the right dotnet new template for a task, comparing templates side by
  side, inspecting template parameters and constraints, understanding what a template
  produces before creating a project, resolving intent like "web API with auth" to
  concrete template + parameters.
  DO NOT USE FOR: actually creating projects (use template-instantiation), authoring
  custom templates (use template-authoring), MSBuild or build issues (use dotnet-msbuild
  plugin), NuGet package management unrelated to template packages.
---

# Template Discovery

This skill helps an agent find, inspect, and select the right `dotnet new` template for a given task using `dotnet new` CLI commands for search, listing, and parameter inspection.

## When to Use

- User asks "What templates are available for X?"
- User describes a project in natural language ("I need a web API with authentication")
- User wants to compare templates or understand parameters before creating a project
- User needs to know what a template produces (files, structure) before committing

## When Not to Use

- User wants to create a project — route to `template-instantiation` skill
- User wants to author or validate a custom template — route to `template-authoring` skill
- User is troubleshooting build issues — route to `dotnet-msbuild` plugin

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| User intent or keywords | Yes | Natural-language description or keywords (e.g., "web API", "console app", "MAUI") |
| Language preference | No | C#, F#, or VB — defaults to C# |
| Framework preference | No | Target framework (e.g., net10.0, net9.0) |

## Workflow

### Step 1: Resolve intent to template candidates

Map the user's natural-language description to template short names using these common keyword mappings:

| User Intent | Template | Suggested Parameters |
|-------------|----------|---------------------|
| web API, REST API | `webapi` | `--auth Individual --use-controllers` if auth requested |
| web app, website | `webapp` | |
| Blazor, interactive web | `blazor` | |
| console app, CLI tool | `console` | |
| class library, shared code | `classlib` | |
| worker service, background job | `worker` | |
| gRPC service | `grpc` | |
| MAUI app, mobile app | `maui` | |
| test project, unit tests | `xunit`, `mstest`, or `nunit` | |

### Step 2: Search for templates

Use `dotnet new search` to find templates by keyword across both locally installed templates and NuGet.org:

```bash
dotnet new search blazor
```

Use `dotnet new list` to show only installed templates, with optional filters:

```bash
dotnet new list --language C# --type project
dotnet new list web
```

### Step 3: Inspect template details

Use `dotnet new <template> --help` to get full parameter details for a specific template — parameter names, types, defaults, and allowed values:

```bash
dotnet new webapi --help
```

### Step 4: Preview output

Use `dotnet new <template> --dry-run` to show what files and directories a template would create without writing anything to disk:

```bash
dotnet new webapi --name MyApi --auth Individual --dry-run
```

### Step 5: Present findings

Summarize the best template match with:
- Template name and short description
- Key parameters and recommended values
- What the user should expect (files created, project structure)
- Any constraints or prerequisites

## Validation

- [ ] At least one template match was found for the user's intent
- [ ] Template parameters are explained with types and defaults
- [ ] User understands what the template produces before proceeding to creation

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Not searching NuGet for templates | If `dotnet new list` shows no matches, use `dotnet new search <keyword>` to find installable templates on NuGet.org. |
| Not checking template constraints | Some templates require specific SDKs or workloads. Use `dotnet new <template> --help` to surface constraints before recommending. |
| Recommending a template without previewing output | Always use `dotnet new <template> --dry-run` to confirm the template produces what the user expects. |

## More Info

- [dotnet new templates](https://learn.microsoft.com/dotnet/core/tools/dotnet-new-sdk-templates) — built-in template reference
- [Template Engine Wiki](https://github.com/dotnet/templating/wiki) — template engine internals
