---
name: dotnet-format
description: >
  Format C#/.NET source files using the `dotnet format whitespace` subcommand.
  USE FOR: quickly fixing whitespace and indentation in one or more files after
  code generation or editing, batch-formatting changed files before a commit.
  DO NOT USE FOR: enforcing code-style analyzers (SA/IDE rules), fixing
  non-whitespace style issues, or formatting entire large repositories at once.
---

# dotnet format (whitespace)

`dotnet format whitespace` reformats indentation, trailing whitespace, and line endings in C#/VB files without applying analyzer or code-style fixes. When used with `--folder`, it is the fastest `dotnet format` sub-command because in that mode it operates on syntax only and does not need to load the full MSBuild workspace; when run against a project or solution it still loads the workspace. It is also the safest because it never changes semantics.

## When to Use

- Fixing indentation or trailing whitespace in files you just created or edited
- Batch-formatting a set of changed files before committing
- Cleaning up generated code that has inconsistent whitespace

## When Not to Use

- You need code-style fixes (naming, `var` vs explicit type, etc.) — use `dotnet format style` instead
- You need analyzer-driven fixes (e.g., SA1200) — use `dotnet format analyzers` instead

## Workflow

### Step 1: Determine files to format

Identify the file paths that need formatting — typically files that were just created or edited.

### Step 2: Run `dotnet format whitespace`

Use `--folder` mode to format without needing a project/solution file:

```bash
# Format everything under the current directory
dotnet format whitespace --folder .

# Format only specific files
dotnet format whitespace --folder . --include path/to/File1.cs --include path/to/File2.cs
```

Key flags:

Always use `--folder` — without it, the tool loads the full MSBuild workspace which is much slower and unnecessary for whitespace-only formatting.

| Flag | Purpose |
|------|---------|
| `--folder` | Treats the argument as a plain directory — avoids loading the full workspace |
| `--include <path>` | Restricts formatting to the specified file(s); repeat for multiple files. Optional — omit to format all files in the folder |
| `--verify-no-changes` | Exits non-zero if any file would change (useful for CI checks) |

Multiple files example:

```bash
dotnet format whitespace --folder . \
  --include src/Models/User.cs \
  --include src/Services/AuthService.cs \
  --include tests/AuthTests.cs
```

### Step 3: Verify the result

Review the formatted files to confirm only whitespace changed. If you need to verify programmatically:

```bash
dotnet format whitespace --folder . --include path/to/File.cs --verify-no-changes
```

A zero exit code means the file is already correctly formatted.

## Validation

- [ ] `dotnet format whitespace` exits with code 0
- [ ] Only whitespace/indentation changed — no semantic modifications
- [ ] Formatted files still compile successfully

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Running without `--folder` | Loads the full MSBuild workspace, which is slow and unnecessary for whitespace formatting. Always use `--folder .` |
| Formatting the entire repo unintentionally | Pass `--include` with specific file paths when you only want to format a subset |
| `.editorconfig` not found | `dotnet format` inherits `.editorconfig` settings; ensure one exists in a parent directory for consistent results |
