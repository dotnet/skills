---
name: template-validation
description: >
  Validates custom dotnet new templates for correctness before publishing.
  Catches missing fields, parameter bugs, shortName conflicts, constraint issues,
  and common authoring mistakes that cause templates to fail silently.
  USE FOR: checking template.json files for errors before publishing or testing,
  diagnosing why a template doesn't appear after installation, reviewing template
  parameter definitions for type mismatches and missing defaults, finding shortName
  conflicts with dotnet CLI commands, validating post-action and constraint configuration.
  DO NOT USE FOR: finding or using existing templates (use template-discovery),
  creating projects from templates (use template-instantiation), creating templates
  from existing projects (use template-authoring).
license: MIT
---

# Template Validation

This skill helps validate custom `dotnet new` templates for correctness before publishing. It encodes the validation rules that catch common authoring mistakes — issues that cause templates to silently fail, produce broken projects, or not appear in `dotnet new list`.

## When to Use

- User asks to check or validate a template.json file
- User reports "my template doesn't show up after installing"
- User wants to review a template before packaging and publishing to NuGet
- User encounters unexpected behavior from a custom template

## When Not to Use

- User wants to find or use existing templates — route to `template-discovery`
- User wants to create a project — route to `template-instantiation`
- User wants to create a template from an existing project — route to `template-authoring`

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| template.json path | Yes | Path to the template.json file or the template directory containing `.template.config/template.json` |

## Validation Rules

When reviewing a template.json, check ALL of the following categories systematically. Report every finding as an error, warning, or suggestion.

> **Parse gate — stop on syntax errors.** Parse JSON before applying any semantic rule. If
> parsing fails, report the parser's line and column, show only the smallest concrete syntax
> correction, and stop. Do not invent required-field, symbol, post-action, or discoverability
> findings from a document that did not parse. Re-parse after the correction before making any
> semantic claim.
>
> The malformed-JSON final response has exactly two parts: the one-line parse verdict and a
> corrected snippet showing the exact edit. Do not append semantic recommendations, optional
> metadata, or a full replacement manifest.

### 1. Required Fields

| Field | Severity | Rule |
|-------|----------|------|
| `identity` | ERROR | Must be present and non-empty |
| `name` | ERROR | Must be present and non-empty |
| `shortName` | ERROR | Must be present and non-empty |
| `sourceName` | WARNING | Without it, `--name` won't customize the generated project name |
| `author` | WARNING | Improves template discoverability |
| `description` | SUGGESTION | Helps users understand what the template creates |
| `classifications` | SUGGESTION | Improves search and categorization (e.g., `["Web", "API"]`) |
| `defaultName` | SUGGESTION | Provides a fallback project name when `--name` is not specified |

### 2. Identity Format

- ERROR if identity contains spaces — use dots or dashes (e.g., `MyCompany.WebApi.CSharp`)
- WARNING if identity has no namespace separator (`.` or `-`) — use reverse-DNS format

### 3. ShortName Conflicts

A shortName that matches a `dotnet new` subcommand conflicts, because `dotnet new <name>` is then parsed as that subcommand instead of instantiating the template. Read the reserved set for the installed SDK from the `Commands:` section of `dotnet new --help` — that is the authoritative source and avoids this rule going stale.

As of current SDKs the subcommands include (illustrative only — version-dependent, do not hardcode this list; the live `dotnet new --help` output is canonical): `install`, `uninstall`, `update`, `list`, `search`, `details`, `create`. Note that top-level `dotnet` verbs like `build`, `run`, `test`, and `publish` do NOT conflict — `dotnet new test` does not collide with `dotnet test`.

- ERROR if shortName matches any subcommand reported by `dotnet new --help` (case-insensitive)
- WARNING if shortName is only 1 character — too short for discoverability
- Note: shortName can be a string or an array of strings; check all values

### 4. Symbol Validation

For each symbol in the `symbols` object:

- ERROR if a symbol is missing the `type` field
- For `type: "parameter"`:
  - WARNING if no `datatype` specified (defaults to `string`)
  - SUGGESTION if no `description` (improves `--help` output)
  - If `datatype: "choice"`:
    - ERROR if no `choices` defined
    - ERROR if `choices` is empty
    - ERROR if `defaultValue` is not in the choices list
    - WARNING if optional (not `isRequired`) and no `defaultValue` — users get unexpected behavior
  - If `datatype: "bool"`:
    - ERROR if `defaultValue` is not a valid boolean
  - If `datatype: "int"`:
    - ERROR if `defaultValue` is not a valid integer
  - Valid datatypes: `string`, `bool`, `choice`, `int`, `float`, `hex`, `text`
  - ERROR if datatype is not in the valid list
- For `type: "computed"`:
  - ERROR if missing `value` expression
- For `type: "generated"`:
  - ERROR if missing `generator` field
  - Valid generators: `casing`, `coalesce`, `constant`, `port`, `guid`, `now`, `random`, `regex`, `regexMatch`, `switch`, `join`

Custom parameter help is template-specific: it appears under
`dotnet new <shortName> --help`, not the global `dotnet new --help`. Correct that premise
when necessary, then explain which invalid symbol definitions prevent the parameters from
appearing reliably.

A valid choice parameter uses a non-empty choices object, for example:

```json
"Color": {
  "type": "parameter",
  "datatype": "choice",
  "defaultValue": "Blue",
  "choices": {
    "Blue": { "displayName": "Blue" },
    "Green": { "displayName": "Green" }
  }
}
```

**Parameter prefix collisions**: WARNING if any parameter name is a prefix of another parameter name (e.g., `Auth` and `AuthMode`) — this creates ambiguous parsing in expression contexts.

### 5. Sources Validation

For source modifier conditions:
- WARNING if a condition string doesn't contain parentheses around symbol names — expected format is `(symbolName)`, not bare `symbolName`

### 6. Post-Action Validation

For each post-action:
- ERROR if missing `actionId`
- WARNING if missing `description` — this text is shown to users when the action requires manual steps
- SUGGESTION if missing `manualInstructions` — these are shown when the action can't run automatically (e.g., in an IDE)

### 7. Constraint Validation

For each constraint:
- ERROR if missing `type` field
- WARNING if missing `args` — most constraint types require arguments
- For `type: "host"`, missing `args` is an ERROR. `args` is a required array; each entry needs `hostname`. Supported
  built-in identifiers include `dotnetcli`, `vs`, `vs-mac`, `ide`, and
  `dotnetcli-preview`. An optional `version` uses NuGet version/range syntax such as
  `[10.0.100,)`. The engine matches argument keys case-insensitively, so the documented
  `hostName` spelling is also valid. Reject unrelated fields such as `pattern` and `value`.
- For `type: "sdk-version"`, `args` is a version string or array using the same syntax.

### 8. Tags Validation

- SUGGESTION if no `language` tag — adding `tags.language` (e.g., `"C#"`) improves filtering in `dotnet new list --language`
- SUGGESTION if no `type` tag — adding `tags.type` (e.g., `"project"` or `"item"`) improves categorization

## Workflow

### Step 1: Locate the template.json

The file can be at:
- Direct path: `path/to/template.json`
- In a template directory: `path/to/.template.config/template.json`
- In a `.template.config` directory: `path/.template.config/template.json`

### Step 2: Parse and validate

Read the JSON. If it's malformed, report the JSON parse error with line and column.
If an absolute-path read fails, retry the user-supplied relative path from the working
directory before concluding the file is unavailable.

Only after parsing succeeds, run all 8 validation categories above. Collect errors, warnings,
and suggestions separately. Verify schema-sensitive claims against the installed SDK or the
current template-engine schema; do not infer a runtime failure from a field name alone.

### Step 3: Report results

**Lead with a one-line verdict**, then a single findings table. This decisive shape is required — do not scatter findings across prose paragraphs.

Verdict header (pick one):
- `❌ Not ready — N error(s), M warning(s)` — has errors
- `⚠️ Publishable but N warning(s)` — no errors, has warnings
- `✅ Ready to publish — 0 errors, 0 warnings` — no errors or warnings (optional suggestions may still apply)

Then one table, ordered errors → warnings → suggestions:

| Severity | Location (JSON path or `line:col`) | Issue | Fix |
|----------|------------------------------------|-------|-----|
| ERROR | `shortName` | `"list"` conflicts with a `dotnet new` subcommand | Rename to a distinctive value, e.g. `"my-list"` |
| ERROR | `symbols.maxRetries.defaultValue` | `"abc"` is not a valid `int` | Set a numeric default, e.g. `"3"` |
| WARNING | `sourceName` | Missing replacement token | Set it to the source project name |

**Every ERROR and WARNING MUST include a concrete fix** — the corrected value, JSON snippet, or a specific edit instruction (e.g. "remove the trailing comma"), not just a restatement of the problem. A finding without an actionable fix is incomplete. This is the single biggest thing that separates a useful validation from a generic lint.

Close with the total: "N error(s), M warning(s), K suggestion(s)."

For malformed JSON the output is intentionally smaller and has exactly two parts:

`❌ Not ready — JSON parse error at line N, column M: <message>.`

```json
<smallest corrected fragment showing the exact edit>
```

Do not append a findings table or semantic totals until the corrected file parses.

## Common Pitfalls

| Pitfall | Impact |
|---------|--------|
| ShortName = "list" or "search" | Template can never be created — conflicts with a `dotnet new` subcommand |
| Missing `sourceName` | `--name MyProject` doesn't rename anything in the generated files |
| Choice parameter without `defaultValue` | Confusing user experience on optional choice params |
| Invalid `datatype` value | Template engine ignores the symbol, causing silent failures |
| Computed symbol without `value` | Template engine throws at instantiation time |
| Parameter prefix collision (`Auth` vs `AuthMode`) | Ambiguous expression evaluation |
| Source condition without parentheses | Condition may not evaluate correctly |
| Continuing semantic validation after JSON parsing failed | Findings are speculative. Report the exact parse fix and stop. |
| Host constraint uses scalar `args`, the invalid `dotnet-cli` host ID, or unrelated fields | Use `args: [{ "hostname": "dotnetcli", "version": "[10.0.100,)" }]`; `hostName` is also accepted case-insensitively. |

## More Info

- [template.json reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json) — full schema
- [Available Symbol Generators](https://github.com/dotnet/templating/wiki/Available-Symbols-Generators) — generator types
- [Post-action registry](https://github.com/dotnet/templating/wiki/Post-Action-Registry) — action IDs
- [Constraints](https://github.com/dotnet/templating/wiki/Constraints) — constraint types
