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

The following short names conflict with dotnet CLI commands and will cause problems:

`new`, `build`, `run`, `test`, `publish`, `restore`, `clean`, `pack`, `add`, `remove`, `list`, `nuget`, `tool`, `sln`, `help`

- ERROR if shortName matches any reserved name (case-insensitive)
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

Read the JSON. If it's malformed, report the JSON parse error with line number.

Run all 8 validation categories above. Collect errors, warnings, and suggestions separately.

### Step 3: Report results

Present findings organized by severity:
1. **Errors** (must fix) — template will not work correctly
2. **Warnings** (should fix) — template may cause confusion or limited functionality
3. **Suggestions** (nice to have) — improvements for discoverability and user experience

Include the total: "X error(s), Y warning(s), Z suggestion(s)"

## Common Pitfalls

| Pitfall | Impact |
|---------|--------|
| ShortName = "test" or "build" | Template can never be created — conflicts with CLI |
| Missing `sourceName` | `--name MyProject` doesn't rename anything in the generated files |
| Choice parameter without `defaultValue` | Confusing user experience on optional choice params |
| Invalid `datatype` value | Template engine ignores the symbol, causing silent failures |
| Computed symbol without `value` | Template engine throws at instantiation time |
| Parameter prefix collision (`Auth` vs `AuthMode`) | Ambiguous expression evaluation |
| Source condition without parentheses | Condition may not evaluate correctly |

## More Info

- [template.json reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json) — full schema
- [Available Symbol Generators](https://github.com/dotnet/templating/wiki/Available-Symbols-Generators) — generator types
- [Post-action registry](https://github.com/dotnet/templating/wiki/Post-Action-Registry) — action IDs
- [Constraints](https://github.com/dotnet/templating/wiki/Constraints) — constraint types
