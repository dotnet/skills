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
  custom templates (use template-authoring), comparing templates side by side in detail
  (use template-comparison), MSBuild or build issues (use dotnet-msbuild
  plugin), NuGet package management unrelated to template packages.
license: MIT
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
- User wants a detailed side-by-side comparison of templates — route to `template-comparison` skill
- User is troubleshooting build issues — route to `dotnet-msbuild` plugin

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| User intent or keywords | Yes | Natural-language description or keywords (e.g., "web API", "console app", "MAUI") |
| Language preference | No | C#, F#, or VB — defaults to C# |
| Framework preference | No | Target framework (e.g., net10.0, net9.0) |

## Workflow

### Step 1: Resolve intent to template candidates

Map the user's natural-language description to template short names and parameters using these mappings.

**Intent → template short name(s):**

| Intent / phrase | Template short name(s) |
|---|---|
| web api, web service, rest api, restful, api, minimal api | `webapi` |
| web app, web application | `webapp`, `blazorserver` |
| mvc | `mvc` |
| razor, razor pages | `webapp` |
| blazor, blazor web app | `blazor` |
| blazor server | `blazorserver` |
| blazor wasm, blazor webassembly | `blazorwasm` |
| grpc | `grpc` |
| signalr | `webapi`, `webapp` |
| console, console app, command line, cli | `console` |
| worker, background service, daemon, windows service | `worker` |
| class library, library, lib, nuget package | `classlib` |
| maui, mobile, cross-platform app, ios, android | `maui` |
| desktop | `maui`, `wpf`, `winforms` |
| wpf | `wpf` |
| winforms, windows forms | `winforms` |
| winui, winui3 | `winui3` |
| test, unit test | `xunit`, `nunit`, `mstest` |
| xunit / nunit / mstest | `xunit` / `nunit` / `mstest` |
| solution | `sln` |
| aspire, .net aspire | `aspire-starter`, `aspire` |
| azure functions, function app, serverless | `func` |
| orleans | `orleans` |
| razor component, web component | `razorcomponent` |
| razor class library | `razorclasslib` |
| gitignore / editorconfig / nuget config / global json | `gitignore` / `editorconfig` / `nugetconfig` / `globaljson` |

**Keyword → parameter:**

| Keyword / phrase | Parameter | Value |
|---|---|---|
| authentication, auth, individual auth, individual accounts | `--auth` | `Individual` |
| windows auth, azure ad, entra | `--auth` | `SingleOrg` |
| no auth, no authentication | `--auth` | `None` |
| controllers, with controllers | `--use-controllers` | (flag) |
| minimal api | (default) | — |
| aot, native aot | `--aot` | (flag) |
| docker, container | `--enable-docker` | (flag) |
| net8 / .net 8 / dotnet 8 | `--framework` | `net8.0` |
| net9 / .net 9 / dotnet 9 | `--framework` | `net9.0` |
| net10 / .net 10 / dotnet 10 | `--framework` | `net10.0` |

These are starting guesses. Always confirm the real parameter names/choices with `dotnet new <template> --help`, because parameter names vary by template (e.g., `--auth` vs `--Authentication`).

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
