---
name: template-smart-defaults
description: >
  Applies cross-parameter default rules when creating .NET projects with dotnet new,
  filling gaps consistently without overriding values the user set explicitly.
  USE FOR: choosing which target framework to pair with native AOT, deciding whether to
  keep HTTPS when authentication is enabled, recognizing that controllers and minimal-API
  flags are mutually exclusive, filling unset related parameters during project creation,
  explaining why a default was applied and ensuring an explicit user value is never
  overridden.
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
2. Apply each rule below **only where the corresponding parameter is unset** — never override a value the user set explicitly.
3. Confirm the chosen parameter names and choices against `dotnet new <template> --help` **at creation time**. For an advice-only request (the user isn't creating yet — e.g. "tell me the parameters/command"), answer from the rules below and note you'd confirm the exact names at creation; don't spend a `--help` call just to advise on well-known parameters.
4. **Emit the two required outputs** (see below) — this is what makes the skill decisive rather than inert.

### Required output

Always produce **both**, in this order:

**A. A "Defaults applied" log** — one row per parameter, covering **both** the explicit values you preserved (`Source = user`) and the gaps you filled by rule (`Source = rule`), so the user can see and override every choice:

| Parameter | Value | Source | Why |
|-----------|-------|--------|-----|
| `--framework` | `net10.0` | rule | Native AOT (from `--aot`) needs the latest AOT-capable TFM |
| `--auth` | `Individual` | user | Explicitly requested — left unchanged |

Use `Source = user` for explicit values (never overridden) and `Source = rule` for gap-fills.

**B. The exact single `dotnet new` command line** you would run — include **only** the flags you are actually passing. Do not list flags you decided *not* to pass (e.g. don't mention `--no-https` when you are keeping HTTPS; don't mention a minimal-API flag when using controllers). Silence on an omitted flag is the correct, decisive signal.

> **AOT at create time vs publish time.** `--aot` is a `dotnet new` flag only on the templates that expose it — always confirm with `dotnet new <template> --help` rather than assuming a given template does or doesn't offer it. There is no `--publish-aot` template flag — publish-time native AOT is enabled with the MSBuild property `PublishAot=true` (via `dotnet publish` or in the `.csproj`), not through `dotnet new`. Apply the framework rule only when the template actually offers `--aot`.

### Rules

| Rule | Default applied | Rationale |
|------|-----------------|-----------|
| `--aot` is set (on any template whose `--help` exposes it) and `--framework` is unset | Set `--framework` to the latest AOT-compatible framework the template offers | Native AOT requires a recent, AOT-capable target framework; using the latest avoids build failures. (A framework already pinned by the workspace or `global.json` counts as set — keep it unless it's incompatible with AOT.) |
| `--auth` is anything other than `None` | Do NOT pass `--no-https` | Authentication flows (cookies, tokens, redirects) require HTTPS; disabling it breaks auth. |
| `--use-controllers` is set | Do NOT also pass a minimal-API flag | Controllers and minimal APIs are mutually exclusive program models; passing both is contradictory. |
| User set a value explicitly | Leave it unchanged | Smart defaults only fill gaps; explicit user intent always wins. |

## Validation

- [ ] A "Defaults applied" log was produced with a Source (user/rule) and rationale per row
- [ ] The exact single `dotnet new` command line was emitted, listing only flags actually passed
- [ ] No parameter the user set explicitly was overridden
- [ ] Only unset parameters were filled
- [ ] Parameter names/choices were confirmed against `dotnet new <template> --help` at creation (for advice-only requests, flagged as to-confirm rather than run eagerly)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Treating heuristics as enforcement | These are guidance rules, not validation. Always confirm against `dotnet new <template> --help` choices, since parameter names vary by template. |
| Overriding an explicit user value | Apply a rule only when the target parameter is unset. |
| Assuming a flag name | The exact flag differs per template — always verify with `--help` (e.g. `--aot` is present only where `--help` lists it; controllers use `--use-controllers`). |
| Picking a framework the template doesn't support | Use the latest framework that appears in the template's `--framework` choices, not an arbitrary newest version. |

## More Info

- [dotnet new](https://learn.microsoft.com/dotnet/core/tools/dotnet-new) — CLI reference
- [Native AOT deployment](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) — AOT framework requirements
