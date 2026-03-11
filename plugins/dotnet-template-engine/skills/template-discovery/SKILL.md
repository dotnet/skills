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

This skill helps an agent find, inspect, and select the right `dotnet new` template for a given task. It uses template tools for search, inspection, and intent resolution — providing richer results than raw `dotnet new list` or `dotnet new search` commands.

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

Use `template_from_intent` with the user's natural-language description. This maps 70+ keywords to template + parameter combinations offline (no LLM round-trip needed).

```
template_from_intent("web API with authentication and controllers")
→ webapi + auth=Individual + UseControllers=true
```

If the intent is too vague, fall back to `template_search` with keywords.

### Step 2: Search for additional matches

Use `template_search` to find templates by keyword across both locally installed templates and NuGet.org. Results are ranked with local templates first.

```
template_search("blazor")
→ ranked list of Blazor templates (local + NuGet)
```

Use `template_list` to show only installed templates with optional language, type, or classification filters.

### Step 3: Inspect template details

Use `template_inspect` to get full metadata for a specific template: parameters (names, types, defaults, choices), constraints, post-actions, and classifications.

```
template_inspect("webapi")
→ parameters: Framework, auth, UseControllers, EnableOpenAPI, ...
```

### Step 4: Preview output

Use `template_dry_run` to show what files and directories a template would create without writing anything to disk.

```
template_dry_run("webapi", name="MyApi", parametersJson={"auth": "Individual"})
→ list of files that would be created
```

### Step 5: Suggest parameter values

Use `template_suggest_parameters` to get smart defaults based on cross-parameter relationships.

```
template_suggest_parameters("webapi", parametersJson={"EnableAot": "true"})
→ suggests Framework=net10.0 because "NativeAOT works best with the latest framework"
```

### Step 6: Present findings

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
| Using `dotnet new list` instead of template tools | Template tools provide richer metadata, NuGet search, and intent resolution. Always prefer `template_search` and `template_from_intent`. |
| Not checking template constraints | Some templates require specific SDKs or workloads. Use `template_inspect` to surface constraints before recommending. |
| Recommending a template without previewing output | Always use `template_dry_run` to confirm the template produces what the user expects. |

## More Info

- [dotnet new templates](https://learn.microsoft.com/dotnet/core/tools/dotnet-new-sdk-templates) — built-in template reference
- [Template Engine Wiki](https://github.com/dotnet/templating/wiki) — template engine internals
