# dotnet

Core .NET and C# skills for coding agents.

## Includes

- Common .NET development skills
- A C# language server integration for `.cs` files

## LSP

This plugin declares a C# LSP server that is launched through the .NET CLI.
The LSP declaration is available to hosts that support the plugin `lspServers` extension. Codex
plugin installs expose this plugin's skills but do not load that host-specific LSP declaration.

Prerequisites:
- .NET 10 SDK installed
- `dotnet` available on PATH

## Skills

- [csharp-expert](skills/csharp-expert/SKILL.md)
- [csharp-refactoring](skills/csharp-refactoring/SKILL.md)
- [msbuild](skills/msbuild/SKILL.md) — routed MSBuild diagnosis, performance, and authoring guidance
- [setup-local-sdk](skills/setup-local-sdk/SKILL.md)

### MSBuild entry and specialist skills

`msbuild` is the task-routed entry for build failures, build performance, and MSBuild
authoring review. It loads bundled references rather than activating the specialist
skills in the [dotnet-msbuild plugin](../dotnet-msbuild/README.md). The existing
plugin's skills and agents remain supported, unchanged alternative entry points;
installing both plugins does not require running both workflows for one task.

Authoring review includes advisory XML, import, and packed-layout questions without a checkout
or failed build, and preserves the current framework and project system. Framework
upgrades, legacy-project conversion, package-format migration, and C# source
refactoring are outside this entry's scope.

The bundled references and specialist skills share MSBuild owners. Review shared-guidance
updates against both surfaces and track applicable backports with their corresponding
evals rather than maintaining independent guidance. This consolidation leaves specialist
content unchanged; the bundled copy includes explicitly reviewed correctness fixes.
Migrating or retiring the specialist plugin is a separate change.

The [entry eval](../../tests/dotnet/msbuild/eval.yaml) covers the three task lanes
and out-of-scope requests. The normal plugin arm loads only `dotnet`.
The supplemental [coexistence experiment](../../msbuild-coexistence.experiment.yaml)
also exposes all `dotnet-msbuild` skills in its plugin arm, using the same stimuli
and outcomes without prescribing which overlapping skill must win selection.
Point `EXPERIMENT_FILE` at that experiment when running `eng/run-skill-evals.sh dotnet msbuild`.
Inspect its activation traces for duplicate workflows and out-of-scope activation;
the isolated arm's dormancy contract alone does not prove cross-plugin routing.

The entry eval executes the optional/required import matrix, builds a local-feed
consumer of the supplied fixed NuGet package, and checks catalog generation
across edits, input additions/removals, missing outputs, and unchanged builds.
Its golden patches and mutation checks protect behavior rather than particular
XML spellings.
The checker and authoritative source hashes/SDK policy are staged only for
grading, after the agent finishes. The supplied package is checked byte-for-byte
and consumed as-is; it is not replaced with a fresh pack during grading.
The fixtures use a stable 10.0.100 minimum with `latestMajor` roll-forward rather
than the repository's preview authoring SDK. Do not raise this minimum to the
latest patch: cached runners can have an older SDK patch or feature band.
The grader records the SDK actually selected.

Before comparing model results, run the
[installed-reference smoke](../../eng/vally-adapter/InvestigatingResults.md#sdk-reference-file-access)
with the intended executor. It verifies actual `view` results through the logged
Vally/Copilot SDK lane; file existence alone does not prove reference access.

### C# expert marketplace routing

`csharp-expert` routes a .NET request to an installed specialist or identifies the smallest
`dotnet/skills` marketplace plugin that supplies a missing specialist. Its normal eval covers
solution detection, marketplace acquisition, fallback behavior, and dormancy.

The supplemental
[`csharp-expert-coexistence.experiment.yaml`](../../csharp-expert-coexistence.experiment.yaml)
and
[`csharp-expert-coexistence.claude.experiment.yaml`](../../csharp-expert-coexistence.claude.experiment.yaml)
load representative marketplace specialists in the plugin arm for GPT- and Claude-family executors.
Run either without a skill filter:

```bash
EXPERIMENT_FILE=./csharp-expert-coexistence.experiment.yaml ./eng/run-skill-evals.sh
EXPERIMENT_FILE=./csharp-expert-coexistence.claude.experiment.yaml ./eng/run-skill-evals.sh
```

Inspect the plugin-arm activation traces to confirm that installed specialists are invoked without
installation advice. This is trace evidence rather than a shared output grader because the
target-only arm may legitimately identify a missing specialist after completing a safe fallback.
