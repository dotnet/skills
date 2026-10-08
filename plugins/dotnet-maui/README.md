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
session-log boundary.

The tenth full-eight local comparison at `836d5f6c` measured the bounded
filesystem repair and the subsequent content/grading fixes. All 16 comparisons
were conclusive with zero errored or unmatched trials; all nine dormancy cases
per family stayed dormant in both isolated and plugin contexts.

| Skill | Sonnet 5 W/T/L | GPT-5.6 Luna W/T/L |
|---|---|---|
| Doctor | 7/3/1, pass | 6/2/3, unproven |
| Lifecycle | 6/3/1, unproven | 4/6/0, tie-limited |
| CollectionView | 5/4/1, unproven | 8/2/0, pass |
| Binding | 5/3/2, unproven | 6/4/0, pass |
| DI | 6/4/1, unproven | 2/7/2, mixed |
| Safe area | 8/1/1, pass | 9/1/0, pass |
| Shell | 8/2/0, pass | 7/1/2, unproven |
| Theming | 7/2/1, pass | 6/3/1, unproven |

All 24 Luna native patches and all 353 cross-family native reads succeeded.
The preceding root-escape failures are not erased: ninth had 69 patch
failures, tenth had zero. The harness-only repair is proposed separately in
[PR #1280](https://github.com/dotnet/skills/pull/1280); local results do not mean
that it has landed in trusted CI.

All isolated aggregate grades passed, but Sonnet still had five failed-leaf
trials and Luna one. Each plugin arm had one aggregate failure: Sonnet's offline
dependency resolver omitted nested package objects; Luna's new CollectionView
answer omitted runtime context wiring. Other generated defects survived
aggregate grades. Positive activation requirements also remain unmet on several
preservation/repair cases; clean dormancy is not complete routing compliance.

Matched shutdown accounting covers 273 sessions per family without missing,
duplicate or unpaired records. Every suite's plugin counters increased:
13.6% to 81.3% for Sonnet and 4.3% to 43.6% for Luna. Only safe-area passed the
preference gate on both families, and it is still not a cost-neutral default.
DI content was unchanged between ninth and tenth; its vote shift is not evidence
that a new DI content edit caused a regression.

Historical records are not rewritten or attributed to later fixes. Correct
phrase-regex failures, contradictory rubric/golden contracts and factual judge
errors are repaired separately from skill content. Compiler/runtime replay of
the actual Toolkit answer confirms that a zero-parameter `CanExecute` method
works with an integer command and that async execution updates command
availability; a literal string parameter still cannot supply that integer.

When models or supported SDKs change, repeat the comparison before retaining a
default. Classify fixture, harness, power, routing and content failures separately.
An underpowered result is not evidence to retire a skill. Conversely, keep neither
a default nor a support claim solely because an older dashboard run passed.

### Eleventh changed-suite comparison

The four changed suites were measured at `d5d90a06`, with unchanged declared
budgets and five workers. An error in the temporary experiment configuration
registered the plugin parent directory rather than its skill directories:
all 45 plugin tasks per family failed at setup. The valid baseline/isolated
results were retained; only the failed plugin arms were retried using the
repository generator and all eight concrete skill directories. Original failed
artifacts remain preserved. The plugin retry is a separate execution, not a
single-run or newer official full-plugin certification.

| Skill | Sonnet 5 W/T/L | GPT-5.6 Luna W/T/L |
|---|---|---|
| Doctor | 8/0/3, unproven | 5/6/0, pass |
| Binding | 5/5/0, pass | 4/3/3, mixed |
| Shell | 7/1/2, unproven | 8/2/0, pass |
| Theming | 7/2/1, pass | 4/6/0, tie-limited |

All eight isolated comparisons were conclusive with zero errored/unmatched
trials; all four dormancy cases per family stayed dormant in both isolated and
retried plugin contexts. Positive activation requirements still have misses.
None of these four suites passed preference gates on both families.

Luna's plugin aggregates and rubric leaves all passed. Sonnet's plugin had one
aggregate failure and four failed-leaf trials: the activated Doctor deleted
pre-existing validation state during cleanup, despite reading its reference.
The preservation check correctly failed; it was not a nested-package parser
failure. Other missed criteria include truthful inspection and native-validation
limitations. A generated theme answer omitted dictionary class wiring that the
source already supplies. Aggregate passes are not full app or device proof.

Matched shutdown accounting covers 135 sessions per family, 45 per role, with
no missing, duplicate or unpaired records. Retried plugin counters increased
46.8% to 86.0% for Sonnet and 16.2% to 36.4% for Luna versus their preserved
baselines. Separate execution timing and the usual counter limitations remain.
All 15 Luna native patches succeeded without observed root-escape errors;
eight baseline reads targeted missing paths, and native ripgrep output-file
failures remain separately unclassified.

Subsequent repairs make validation-state ownership unconditional during cleanup
and clarify that either manual or generated notification satisfies the binding
task. The original judge wrongly treated a conditional Toolkit criterion as
mandatory. Its terse golden also needed a concrete implementation, not narrated
intent. These contract repairs do not rewrite the recorded votes, and the
eleventh comparison does not measure later content.

## Overlapping proposal integration

The integration basis is [#1273](https://github.com/dotnet/skills/pull/1273),
not a mechanical merge with [#1255](https://github.com/dotnet/skills/pull/1255).
At heads `2544abff` and `846f80a8`, a merge simulation reports 13 content
conflicts: five skill files and all eight eval specs. Keep one coherent
fixture/grader/golden contract per suite; do not combine partially merged evals
or attribute either branch's measurements to an integrated payload.

The useful additional shared-cache/HTTP ownership case is carried forward in
the DI guidance and its executable replay. Existing runtime-verified Shell
activation, explicit scope ownership, lifecycle durability, bindings, restraint
and powered suites remain the integration basis. In particular, do not restore
universal claims that typed Shell templates bypass DI or that ordinary MAUI
automatically creates a DI scope per window. Neither PR is merged or closed
by this decision; maintainers still need to select the final proposal.

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
on-demand binding reference. XamlC/runtime probes verify an `x:Int32` command
parameter retains its integer type and reject a literal-string substitute.
The Shell core's actual guard/caller example is compiled and its pending-request
skip path exercised. Real-package probes also check back-button property
ownership and reject sibling-theme `var` conditionals and undeclared dictionary
`RemoveWhere` calls. Native `MainThread` dispatch is not exercised.

These checks validate contracts, fixture behavior and reference answers, not fresh
model improvement or native UI/OS delivery. Run normal cross-family evaluations
separately before claiming default readiness or cost neutrality.
