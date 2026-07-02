# `dotnet-tools.json` manifest

The skill scaffolds a **local** tool manifest. Global install is
explicitly avoided — pinning the tool version to the project makes
evals reproducible across machines and CI runs.

## Why local

- Reproducible: every clone gets the exact same `aieval` version.
- CI-friendly: `dotnet tool restore` in the workflow is one line.
- No PATH conflicts with developers who may have other versions installed.

## Generation (do not hand-write the version)

Generate the manifest instead of authoring a pinned version literal:

```pwsh
cd <App>.Evals.Tests
dotnet new tool-manifest                                          # if none exists
dotnet tool install microsoft.extensions.ai.evaluation.console    # latest; provides `aieval`
```

`dotnet tool install` (no `--version`) records the **current** tool version into
the manifest and defaults to `rollForward: false`, so you still get a pinned,
reproducible entry — but NuGet chose the number, not the skill. The resulting
manifest looks like this (version shown is an illustrative snapshot):

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

`dotnet new tool-manifest` places the file per the SDK default — conventionally
`<App>.Evals.Tests/.config/dotnet-tools.json`, though newer SDKs may write a
repo-root-style `dotnet-tools.json` in the current directory. Either way, run
`dotnet tool restore` / `dotnet tool run` **from the `<App>.Evals.Tests/`
directory** so discovery finds the manifest. Running them from the repo root
would walk *up* and could miss a manifest that lives in a child directory, so the
CI `Restore tools` step, the `aieval report` safety-net step, and the runtime
`AssemblyCleanup` report call all use the test-project directory as their working
directory. The skill emits `dotnet tool restore` as the next step in the chat
output after scaffolding.

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
