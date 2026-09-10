# Repository overlays

The `dotnet-test` plugin is piloting repository-specific overlays based on the
portable-core strategy proposed by
[@JeremyKuhne](https://github.com/JeremyKuhne). His
[`agent-skills`](https://github.com/JeremyKuhne/agent-skills) repository and the
[`touki` Roslyn analyzer overlay](https://github.com/JeremyKuhne/touki/blob/main/.agents/skills/roslyn-analyzers/overlay.md)
demonstrate the original convention.

An overlay lets a user-installed or vendored portable skill apply local
conventions without copying those conventions into the shared skill.

## Pilot skills and paths

| Skill | Repository overlay |
|---|---|
| `writing-mstest-tests` | `.agents/skill-overlays/dotnet-test/writing-mstest-tests.md` |
| `scaffold-dotnet-test-project` | `.agents/skill-overlays/dotnet-test/scaffold-dotnet-test-project.md` |
| `run-tests` | `.agents/skill-overlays/dotnet-test/run-tests.md` |

Each path is relative to the repository root being worked on, not the plugin
installation directory. The files are optional. Skills continue with their
portable behavior when no overlay exists.

## Overlay format

Use YAML frontmatter to identify the portable core and its overlay binding
contract:

```markdown
---
core: dotnet-test/run-tests
binding-revision: "1"
mode: extend
---

# Repository test execution

- Run unit tests with `pwsh ./eng/test.ps1 -Suite Unit`.
- Do not replace the repository entry point with a direct `dotnet test`.
```

`mode: extend` is the only mode in the pilot. The skill applies an overlay only
when `core` identifies that exact skill and `binding-revision` matches the
revision in the skill's own metadata. A missing or mismatched field is reported
and the overlay is not applied; the skill continues with its portable guidance.

An overlay may provide paths, names, commands, framework choices, and local
policies. It may narrow portable defaults, but it cannot expand tool
permissions, task scope, filesystem access, network access, or publication
authority.

Precedence is:

1. explicit user instructions;
2. verified project and dependency constraints;
3. the repository overlay;
4. portable skill defaults and examples.

If an overlay contradicts the current repository, the skill reports the
conflict instead of silently selecting either instruction. Increment
`binding-revision` when the skill changes the overlay contract, re-review
matching overlays, and remove local guidance after it is incorporated into the
portable core.

This pilot is instruction composition, not a host-level Markdown merge. The
Agent Skills standard permits custom metadata, but hosts do not currently
provide overlay discovery, pin validation, or conflict resolution.
