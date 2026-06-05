---
name: format-fsharp-with-fantomas
description: "Format F# code with Fantomas, the standard F# code formatter, and configure its style via .editorconfig. Use when formatting F# files, enforcing consistent F# style in a repo, wiring up a format check in CI, or resolving disagreements about F# layout. Covers installing/running the Fantomas dotnet tool, formatting and check modes, and key .editorconfig (fsharp_*) settings. Do not use for code logic/idiom changes (use writing-idiomatic-fsharp) - Fantomas only changes layout."
license: MIT
---

# Formatting F# with Fantomas

## Purpose

Fantomas is the de facto F# code formatter. It applies the F# style guide automatically and
ends layout debates - one consistent style, enforced by a tool, configurable through
`.editorconfig`.

## When to Use

- Formatting one or more F# files to a consistent style
- Enforcing F# formatting across a repository
- Adding a format check to CI
- Settling layout disagreements objectively

## When Not to Use

- Changing logic, idiom, or structure - Fantomas only reformats; use
  `writing-idiomatic-fsharp` for idiom

## Install and run

Fantomas is a .NET tool. Use a local tool manifest so the version is pinned per repo.

```bash
dotnet new tool-manifest        # once per repo, if no .config/dotnet-tools.json
dotnet tool install fantomas    # adds it to the manifest
```

Format in place:

```bash
dotnet fantomas src/            # format a directory recursively
dotnet fantomas File.fs         # format a single file
```

Check mode (no writes; non-zero exit if any file would change) - ideal for CI:

```bash
dotnet fantomas --check src/
```

## Configure style via .editorconfig

Fantomas reads `fsharp_*` settings from `.editorconfig`. Place a `[*.fs]` (and `[*.fsx]`)
section at the repo root.

```ini
[*.{fs,fsx,fsi}]
indent_size = 4
max_line_length = 120
fsharp_space_before_uppercase_invocation = false
fsharp_multiline_bracket_style = aligned
fsharp_keep_max_number_of_blank_lines = 1
```

Common settings:

| Setting | Effect |
|---------|--------|
| `max_line_length` | Wrap threshold (default 120) |
| `fsharp_multiline_bracket_style` | `cramped` / `aligned` / `stroustrup` layout for records & lists |
| `fsharp_keep_max_number_of_blank_lines` | Collapse runs of blank lines |
| `fsharp_space_before_uppercase_invocation` | Space before `(` on PascalCase calls |

Keep configuration minimal; the defaults already follow the style guide.

## Workflow

1. Ensure a tool manifest exists and Fantomas is installed (`dotnet tool restore` if it is
   already in the manifest).
2. Run `dotnet fantomas <path>` to format.
3. Add (or adjust) a `[*.{fs,fsx,fsi}]` section in `.editorconfig` only for deliberate
   deviations from defaults.
4. Add `dotnet fantomas --check` to CI to keep the tree formatted.
5. Confirm the project still builds after formatting (`dotnet build`).

## Validation

- [ ] Fantomas is pinned in the tool manifest (`.config/dotnet-tools.json`)
- [ ] `dotnet fantomas <path>` formats without error
- [ ] `dotnet fantomas --check` passes on the formatted tree
- [ ] Any custom style lives in `.editorconfig`, not ad hoc
- [ ] The project still builds after formatting

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Globally installed Fantomas with a drifting version | Use a pinned local tool manifest |
| Hand-formatting to fight the formatter | Configure `.editorconfig`, then let Fantomas win |
| Expecting Fantomas to fix idiom | It only reformats; use `writing-idiomatic-fsharp` |
| `--check` not in CI | Add it so unformatted code is caught on PRs |

## More info

- F# style guide: https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/
- Fantomas docs: https://fsprojects.github.io/fantomas-docs/
- Fantomas repo: https://github.com/fsprojects/fantomas
