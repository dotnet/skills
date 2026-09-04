# Visual manual — slides (authoring deferred)

This folder is reserved for the **visual manual / slide deck** for the .NET Skills
repository. Content authoring is deferred to a later pass (owner: repo maintainer).

## Goal of the deck

A short, human-friendly companion to the written docs that explains:

1. **What this repo is** — the .NET team's curated Agent Skills & custom agents
   (see `README.md` and <https://dotnet.github.io/skills/>).
2. **The plugin map** — one slide per plugin family
   (dotnet, dotnet-ai, dotnet-msbuild, dotnet-aspnetcore, dotnet11, ...).
3. **Local dev constraint** — why the .NET 11 preview only builds on glibc hosts
   (Termux/Android is Bionic) and the supported paths (Codespaces, Docker,
   WSL2, glibc VM). Source of truth: `docs/LOCAL-DEVELOPMENT.md`.
4. **Lightweight telemetry add-on** — the `lightweight-telemetry` skill
   (`plugins/dotnet11/skills/lightweight-telemetry/`): what it measures and how
   to run the sample.
5. **Green CI** — the `skill-validator.yml` build/test matrix and the dashboard
   at <https://dotnet.github.io/skills/>.

## Suggested format

- 8–12 slides.
- Diagrams: dark theme, architecture/flow style (see repo `docs/design/`).
- Keep code snippets minimal; link to the skill files for full source.

## Status

- [ ] Outline approved
- [ ] Slides drafted
- [ ] Reviewed against `docs/LOCAL-DEVELOPMENT.md` and the telemetry skill
- [ ] Published / linked from README
