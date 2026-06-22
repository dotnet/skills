---
name: template-comparison
description: >
  Compares two or more dotnet new templates side by side to help users choose between
  them based on parameters, feature support, frameworks, and classifications.
  USE FOR: deciding between similar templates (webapi vs webapp, blazor vs
  blazorwasm, console vs worker), producing a side-by-side comparison of parameters and
  feature support, understanding how templates differ before creating a project.
  DO NOT USE FOR: creating a project from a template (use template-instantiation),
  authoring or validating custom templates (use template-authoring and template-validation),
  general single-template discovery (use template-discovery).
license: MIT
---

# Template Comparison

This skill helps an agent compare 2+ `dotnet new` templates side by side so the user can
pick the right one. It inspects each template's parameters and feature support and renders
a comparison table.

## When to Use

- User is deciding between similar templates (e.g., `webapi` vs `webapp`, `blazor` vs `blazorwasm`)
- User asks "which template should I use for X?"
- User wants to understand how two or more templates differ before creating a project

## When Not to Use

- User wants to create a project — route to `template-instantiation`
- User wants to author or validate a custom template — route to `template-authoring` or `template-validation`
- User just needs to find or inspect a single template — route to `template-discovery`

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Template short names | Yes | Two or more template short names to compare (e.g., `webapi`, `webapp`) |
| Comparison focus | No | Optional aspect to emphasize (auth, AOT, frameworks, interactivity) |

## Workflow

### Step 1: Inspect each template

Run `dotnet new <template> --help` for each template being compared to collect its
parameters (names, types, defaults, choices) and supported frameworks:

```bash
dotnet new webapi --help
dotnet new webapp --help
```

If a template is not installed, find and install it first (`dotnet new search <keyword>`,
then `dotnet new install <package>`).

> **Run `--help` calls sequentially.** The template engine uses a global mutex, so running
> several `dotnet new <template> --help` commands concurrently can fail with a transient
> "mutex"/"persistence" error and empty output. Inspect templates one at a time; if a call
> fails, retry it once before moving on, and still produce the comparison from whatever
> parameter knowledge you have rather than ending with no answer.

### Step 2: Build the comparison table

Produce a side-by-side table covering:

- **Parameters** — name, type, default, choices
- **Feature support** — auth, AOT, Docker, controllers, interactivity
- **Available frameworks** — e.g., net8.0, net9.0, net10.0
- **Classifications** — categories the template advertises (Web, API, Blazor, etc.)

Example shape:

| Aspect | `webapi` | `webapp` |
|--------|----------|----------|
| Auth (`--auth`) | None, Individual, SingleOrg, Windows | None, Individual, SingleOrg, ... |
| AOT (`--aot` flag) | n/a — native AOT via publish-time `PublishAot` | n/a |
| Controllers (`--use-controllers`) | Yes | n/a |
| Interactivity | n/a | n/a |
| Frameworks | net8.0 / net9.0 / net10.0 | net8.0 / net9.0 / net10.0 |
| Classifications | Web, WebAPI | Web, Razor Pages |

### Step 3: Recommend

Summarize the key differences and recommend a template for the user's stated scenario,
linking to `template-instantiation` to create it.

## Validation

- [ ] Every template requested was inspected via `dotnet new <template> --help`
- [ ] The comparison covers parameters, feature support, frameworks, and classifications
- [ ] Differences relevant to the user's scenario are called out explicitly
- [ ] A recommendation (or clear trade-off) is provided

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Comparing uninstalled templates from memory | Install and inspect each template so the comparison reflects the real parameters and choices. |
| Assuming feature parity | Parameter names and feature support vary by template — confirm each with `--help`. |
| Comparing fundamentally different template types | Only compare templates that solve overlapping problems; note when they target different scenarios. |

## More Info

- [dotnet new templates](https://learn.microsoft.com/dotnet/core/tools/dotnet-new-sdk-templates) — built-in template reference
- [dotnet new](https://learn.microsoft.com/dotnet/core/tools/dotnet-new) — CLI reference
