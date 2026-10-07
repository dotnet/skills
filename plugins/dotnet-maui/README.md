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
evidence. In [evaluation 37663692784](https://github.com/dotnet/skills/actions/runs/37663692784),
lifecycle and safe-area passed preference gates on both model families. Every
suite's matched plugin-context SDK execution counters increased for Claude
Sonnet 5 (15.3% to 75.2%). GPT-5.6 Luna's safe-area counters decreased 2.0%;
the other suites increased 17.3% to 47.3%. Earlier preference passes and cost
reductions did not consistently persist. These counters include dormancy tasks
and exclude judges; they are not invoices or a task-weighted cost-neutrality
proof. Aggregate completion passes also do not guarantee every generated sample
is correct. Keep guidance task-specific and on-demand pending fresh evidence;
no default installer policy is changed by this documentation.

Targeted local follow-ups are not a newer official full-plugin certification.
Doctor's advice-only SDK routing and missing-error stop passed separately on
Sonnet (8W/3T/0L) and Luna (7W/3T/1L), but plugin execution counters still rose
43.1% and 39.5%. A smaller binding core, inferred child-context example and
complete manual notification example produced 7W/1T/2L and 5W/4T/1L respectively:
positive, still statistically unproven. Its matched isolated/plugin counters
were Sonnet -7.4%/+7.4% and Luna +16.3%/+23.3%. Neither comparison establishes a
cross-family cost-neutral default. In that binding comparison, treatment aggregate
grades passed, but a Sonnet baseline aggregate and individual treatment criteria
failed; it was not an all-arm correctness pass.

The ninth full-eight local comparison measured the revised binding description
and workspace reader at `03ce473c`. Preference gates passed for four Sonnet
suites and three Luna suites; Doctor, DI and safe-area passed on both. The other
results remain unproven, and generated-code/preservation defects remain even in
passing suites. All dormancy contracts passed. Matched plugin counters increased
12.0% to 71.6% for Sonnet. Luna safe-area decreased 3.2%; the other suites increased
8.1% to 48.3%.

Native reads succeeded in all 413 observed calls, but Luna still had 69 native
patch failures across 22 task/arm slots: workspace writes hit the separate
session-log boundary. The subsequent bounded filesystem repair has passed
real SDK create/edit/delete checks, not a fresh full comparison. This distinction
matters for both correctness and cost interpretation; historical records are
not rewritten or attributed to later fixes. Correct phrase-regex failures and
factual judge errors are repaired separately from skill content.

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
It compiles the binding core's actual notifying property, rejects a wrong-name
notification mutation, and compiles converter/resource examples from the
on-demand binding reference. Real-package probes also check back-button property
ownership and reject sibling-theme `var` conditionals and undeclared dictionary
`RemoveWhere` calls. Native `MainThread` dispatch is not exercised.

These checks validate contracts, fixture behavior and reference answers, not fresh
model improvement or native UI/OS delivery. Run normal cross-family evaluations
separately before claiming default readiness or cost neutrality.
