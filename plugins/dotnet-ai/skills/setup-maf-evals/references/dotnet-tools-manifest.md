# `dotnet-tools.json` manifest

The skill scaffolds a **local** tool manifest. Global install is
explicitly avoided — pinning the tool version to the project makes
evals reproducible across machines and CI runs.

## Why local

- Reproducible: every clone gets the exact same `aieval` version.
- CI-friendly: `dotnet tool restore` in the workflow is one line.
- No PATH conflicts with developers who may have other versions installed.

## Template

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "microsoft.extensions.ai.evaluation.console": {
      "version": "10.7.0",
      "commands": ["aieval"],
      "rollForward": false
    }
  }
}
```

Place at `<App>.Evals.Tests/dotnet-tools.json` (not the repo root) so
the manifest follows the project. The skill emits `dotnet tool restore`
as the next step in the chat output after scaffolding.

## Why `rollForward: false`

The output of `aieval report` is consumed by humans visually and by
artifact comparison in CI. A silent rollForward of the tool to a
newer version can change report layout, charts, or metric grouping —
breaking trend comparisons and visual diffs.

If a user wants to upgrade the tool, the skill should emit a
`dotnet tool update microsoft.extensions.ai.evaluation.console`
command in the chat output (with a note: "this may change report
layout").

## Conflict with existing manifest

If `dotnet-tools.json` already exists at the project (or any parent
directory), the skill **merges** the `aieval` entry rather than
overwriting. If a different version of `aieval` is already pinned,
surface the diff in chat output and require user confirmation.
