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

- [csharp-refactoring](skills/csharp-refactoring/SKILL.md)
- [msbuild](skills/msbuild/SKILL.md)
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
