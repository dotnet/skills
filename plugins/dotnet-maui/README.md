# .NET MAUI skills

This plugin provides task-specific guidance, not a blanket default installation
set. All three plugin manifests discover skills through `./skills/`.

| Skill | Intended task |
|---|---|
| [dotnet-maui-doctor](skills/dotnet-maui-doctor/SKILL.md) | Diagnose the requested host/target toolchain while preserving repository SDK and workload policy. |
| [maui-app-lifecycle](skills/maui-app-lifecycle/SKILL.md) | Handle application/window transitions and preserve state. |
| [maui-safe-area](skills/maui-safe-area/SKILL.md) | Apply version-appropriate screen and keyboard inset policies. |
| [maui-data-binding](skills/maui-data-binding/SKILL.md) | Repair binding contexts, notifications, compiled bindings and diagnostics. |
| [maui-collectionview](skills/maui-collectionview/SKILL.md) | Build bound, appropriately sized and virtualized item views. |
| [maui-shell-navigation](skills/maui-shell-navigation/SKILL.md) | Design Shell hierarchy, routes and navigation state. |
| [maui-dependency-injection](skills/maui-dependency-injection/SKILL.md) | Resolve page/service graphs with correct lifetime and explicit scope ownership. |
| [maui-theming](skills/maui-theming/SKILL.md) | Apply theme-aware resources without losing unrelated dictionaries. |

## Maintenance and adoption

Review ownership is recorded in [CODEOWNERS](../../.github/CODEOWNERS). This
records review routing; it does not establish an individual's support commitment.
Issues use the `area-maui` label. A default distribution set also needs confirmed,
engaged maintainers.

Before promoting a skill to a project default, require fresh isolated and plugin
routing evidence on at least one GPT-family and one Claude-family executor.
Keep their results separate. Check quality against each executor's own baseline
and measure cost/time with comparable accounting; fewer bytes or raw token totals
alone do not prove cost neutrality. Follow the repository's
[quality bar](../../CONTRIBUTING.md#quality-bar), including preference significance
and dormancy requirements.

No project-default set is currently supported by the measured quality and cost
evidence. In [evaluation 37652729093](https://github.com/dotnet/skills/actions/runs/37652729093),
Shell navigation and theming passed preference gates on both model families, but
every suite's matched plugin-context SDK execution counters increased: 24.1% to
86.7% for Claude Sonnet 5 and 7.7% to 44.0% for GPT-5.6 Luna. These counters include
dormancy tasks and exclude judges; they are not invoices or a task-weighted
cost-neutrality proof. Aggregate completion passes also do not guarantee every
generated sample is correct. Keep guidance task-specific and on-demand pending
fresh evidence; no default installer policy is changed by this documentation.

When models or supported SDKs change, repeat the comparison before retaining a
default. Classify fixture, harness, power, routing and content failures separately.
An underpowered result is not evidence to retire a skill. Conversely, keep neither
a default nor a support claim solely because an older dashboard run passed.

## Local validation

Run from the repository root with a current Python and PyYAML available:

```bash
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- check --plugin plugins/dotnet-maui
python eng/eval-quality/check_eval_quality.py
VALLY_TELEMETRY_OPTOUT=1 node eng/evaluation-tools/vally.mjs lint plugins/dotnet-maui \
  --eval-spec tests/dotnet-maui --known-domains eng/known-domains.txt \
  --allowed-external-deps eng/allowed-external-deps.txt
```

New fixture files must be tracked in Git for the quality gate. Use its documented
`--base-ref` option to check changed suites against the intended base revision.
The pinned Vally dependencies live in `eng/evaluation-tools/`.

Each repaired suite includes golden evidence and deterministic replay helpers.
Doctor's test README documents its offline replay. DI includes executable probes
against real MAUI Controls packages. The six UI/lifecycle suites share
`tests/dotnet-maui/maui-collectionview/replay_goldens.py`; `--production` replays
their references through Vally with paid prompt graders removed.

These checks validate contracts, fixture behavior and reference answers, not fresh
model improvement or native UI/OS delivery. Run normal cross-family evaluations
separately before claiming default readiness or cost neutrality.
