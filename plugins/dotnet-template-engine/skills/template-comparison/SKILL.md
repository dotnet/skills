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

**Evidence contract:** a side-by-side table is useful only when every option claim is
grounded in the currently installed templates. Run each `--help` command sequentially,
capture the same requested dimensions for each template, and label an unavailable option
as `Not exposed` rather than guessing or borrowing a flag from another template.

**Decision contract:** optimize the comparison for the user's stated decision, not table
size. Cover every requested dimension, omit unrelated option rows, give a scenario-specific
reason, and include one safe `--dry-run` command for the recommended starting point when it
would make the recommendation actionable.

### Step 1: Inspect each template

Run `dotnet new <template> --help` for each template being compared to collect its
parameters (names, types, defaults, choices) and supported frameworks:

```bash
dotnet new webapi --help
dotnet new webapp --help
```

If a template is not installed, search for its provider and report the missing prerequisite.
Install it only when the user asked you to modify the environment or approved the install.

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

Use one row per requested decision dimension and cite the observed option name in the cell.
Do not fill a requested row with general framework knowledge when it is specifically about
what the template generates or exposes.

When the user asks about **generated dependencies** without allowing project creation,
inspect the installed template package's source `.csproj` files. `--help` and `--dry-run`
do not reveal package references. Do not create temporary projects merely to inspect them,
and do not guess current package IDs or test-platform defaults.

Example shape:

| Aspect | `webapi` | `webapp` |
|--------|----------|----------|
| Auth (`--auth`) | None, Individual, SingleOrg, Windows | None, Individual, SingleOrg, ... |
| AOT (`--aot` flag) | present if `dotnet new webapi --help` lists `--aot` | present if `dotnet new webapp --help` lists `--aot` |
| Controllers (`--use-controllers`) | Yes | n/a |
| Interactivity | n/a | n/a |
| Frameworks | net8.0 / net9.0 / net10.0 | net8.0 / net9.0 / net10.0 |
| Classifications | Web, WebAPI | Web, Razor Pages |

### Step 3: Recommend

End with a decisive **Recommendation** line — never leave the user with just a table. Format:

> **Recommendation: `<template>`** — one sentence tying the choice to the user's stated scenario. (Pick the other if `<condition>`.)

Then link to `template-instantiation` to create it. A comparison that ends without naming a winner (or a clear "it depends on X") is incomplete — that indecision is what makes this skill tie with a plain answer.

### Decision shortcuts for common pairs

Use these only for the recommendation, not as evidence of current parameter support. Still
inspect with `--help` before filling the comparison table:

| Pair | Default pick | Because |
|------|-------------|---------|
| `webapi` vs `webapp` | **`webapi`** for a JSON/REST backend; `webapp` for server-rendered HTML/Razor Pages | webapi ships controllers/minimal APIs + OpenAPI, no UI |
| `blazor` vs `blazorwasm` | **`blazorwasm`** when offline / no server is required; `blazor` (Web App) for flexible server + client interactivity | Standalone WASM runs fully client-side, works offline |
| `worker` vs `console` | **`worker`** for long-lived/queue/background processing | Generic Host: DI, logging, config, graceful shutdown, `IHostedService` lifecycle |
| `mvc` vs `webapp` | **`webapp`** (Razor Pages) for page-focused apps; `mvc` for controller/view separation at scale | Razor Pages is lighter for CRUD-style pages |

These constraints override the shorthand above:

- Choose **`mvc`** when the user explicitly anticipates a large application or shared
  controller logic, even if its first pages are CRUD-focused.
- Choose **`blazor` with Server interactivity** over `webapp` when rich interactive forms
  are central but useful HTML must arrive on the first response. Explain that the initial
  render is server-produced and that interactive components use the Blazor form/component
  model rather than Razor Pages `PageModel`.
- For **offline support**, choose `blazorwasm` and explain the PWA/service-worker requirement,
  cached-after-first-load behavior, and lack of a required live server for execution.
- For a **durable queue processor**, choose `worker` and tie the decision to Generic Host
  lifecycle, dependency injection, configuration, logging, graceful shutdown, and a real
  durable queue rather than an in-memory loop.

## Validation

- [ ] Every template requested was inspected via `dotnet new <template> --help`
- [ ] The comparison covers parameters, feature support, frameworks, and classifications
- [ ] Differences relevant to the user's scenario are called out explicitly
- [ ] A recommendation (or clear trade-off) is provided
- [ ] Unsupported or absent options are labeled instead of guessed
- [ ] The final recommendation is a single decisive `Recommendation:` line

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Comparing uninstalled templates from memory | Install and inspect each template so the comparison reflects the real parameters and choices. |
| Assuming feature parity | Parameter names and feature support vary by template — confirm each with `--help`. |
| Comparing fundamentally different template types | Only compare templates that solve overlapping problems; note when they target different scenarios. |

## More Info

- [dotnet new templates](https://learn.microsoft.com/dotnet/core/tools/dotnet-new-sdk-templates) — built-in template reference
- [dotnet new](https://learn.microsoft.com/dotnet/core/tools/dotnet-new) — CLI reference
