---
name: template-smart-defaults
description: >
  Applies cross-parameter default rules when creating .NET projects with dotnet new,
  filling gaps consistently without overriding values the user set explicitly.
  USE FOR: choosing sensible defaults for related parameters during project creation,
  resolving cross-parameter interactions (AOT implies a compatible framework, auth implies
  HTTPS, controllers excludes minimal-API flags), explaining why a default was applied.
  DO NOT USE FOR: creating the project itself (use template-instantiation), finding or
  comparing templates (use template-discovery and template-comparison), authoring or
  validating custom templates (use template-authoring and template-validation).
license: MIT
---

# Template Smart Defaults

This skill helps an agent fill in cross-parameter defaults when creating a `dotnet new`
project. The rules below are guidance heuristics that keep related parameters consistent —
they only fill gaps and never override a value the user set explicitly.

## When to Use

- The user asks to create a project but leaves related parameters unspecified
- A parameter the user chose implies a sensible value for another parameter
- You need to explain why a particular default was selected

## When Not to Use

- User wants to actually create the project — route to `template-instantiation`
- User wants to find or compare templates — route to `template-discovery` or `template-comparison`
- User wants to author or validate a custom template — route to `template-authoring` or `template-validation`

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Template short name | Yes | The template the project will be created from (e.g., `webapi`) |
| Parameters already chosen | Yes | The parameter values the user has explicitly set |
| Available choices | Recommended | Parameter names/choices from `dotnet new <template> --help` |

## Workflow

1. Gather the parameters the user has explicitly set.
2. Apply each rule below **only where the corresponding parameter is unset** — never override an explicit user value.
3. Log every applied default with a short rationale so the user can see and override it.
4. Confirm the chosen parameter names and choices against `dotnet new <template> --help` before creating.

> **AOT at create time vs publish time.** `--aot` is a `dotnet new` flag only on the templates that expose it (e.g. `console`, `worker`, `grpc`); it is **not** on `webapi`/`webapp`. There is no `--publish-aot` template flag — publish-time native AOT is enabled with the MSBuild property `PublishAot=true` (via `dotnet publish` or in the `.csproj`), not through `dotnet new`. Apply the framework rule only when the template actually offers `--aot`.

### Rules

| Rule | Default applied | Rationale |
|------|-----------------|-----------|
| `--aot` is set (on templates that support it, e.g. `console`/`worker`/`grpc`) and `--framework` is unset | Set `--framework` to the latest AOT-compatible framework the template offers | Native AOT requires a recent, AOT-capable target framework; using the latest avoids build failures. |
| `--auth` is anything other than `None` | Do NOT pass `--no-https` | Authentication flows (cookies, tokens, redirects) require HTTPS; disabling it breaks auth. |
| `--use-controllers` is set | Do NOT also pass a minimal-API flag | Controllers and minimal APIs are mutually exclusive program models; passing both is contradictory. |
| User set a value explicitly | Leave it unchanged | Smart defaults only fill gaps; explicit user intent always wins. |

## Validation

- [ ] Each applied default was logged and explained to the user
- [ ] No parameter the user set explicitly was overridden
- [ ] Only unset parameters were filled
- [ ] The resulting parameter names/choices were confirmed against `dotnet new <template> --help`

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Treating heuristics as enforcement | These are guidance rules, not validation. Always confirm against `dotnet new <template> --help` choices, since parameter names vary by template. |
| Overriding an explicit user value | Apply a rule only when the target parameter is unset. |
| Assuming a flag name | The exact flag differs per template (`--aot` exists on `console`/`worker`/`grpc` but not `webapi`; controllers use `--use-controllers`) — verify with `--help`. |
| Picking a framework the template doesn't support | Use the latest framework that appears in the template's `--framework` choices, not an arbitrary newest version. |

## More Info

- [dotnet new](https://learn.microsoft.com/dotnet/core/tools/dotnet-new) — CLI reference
- [Native AOT deployment](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) — AOT framework requirements
