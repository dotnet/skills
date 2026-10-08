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
The isolated safe-area counters also increased 26.2% for Sonnet and 7.8% for
Luna. The absence of a cost-neutral default is therefore not inferred solely
from the larger plugin menu. These are the same historical run's SDK counters,
not a fresh or statistically established cost-neutrality measurement.

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

### Twelfth changed-suite comparison

Doctor, binding and DI were measured at `e5f40050`, using the repository
generator, all eight concrete plugin skill directories, five workers and
unchanged declared trial/time budgets. Both families completed 111 trials,
37 per role, with matched SDK shutdown accounting and no trial errors or
unmatched comparisons. This is local evidence, not an official full-eight
certification.

| Skill | Sonnet 5 W/T/L | GPT-5.6 Luna W/T/L |
|---|---|---|
| Doctor | 9/1/1, pass (p=0.0107) | 7/4/0, activation-blocked (p=0.0078) |
| Binding | 3/7/0, tie-limited (p=0.125) | 7/2/1, pass (p=0.0352) |
| DI | 10/2/0, pass (p=0.0010) | 6/4/2, unproven (p=0.1445) |

Luna invoked Doctor on the runtime-crash dormancy task, although its final
answer correctly redirected to application debugging without toolchain repair.
The isolated dormancy contract therefore fails; the plugin arm stayed dormant.
The other three dormancy tasks passed in both contexts. Binding's working-input
task missed positive activation in both families/roles; Sonnet also missed the
supplied repair task. None of these suites passes all gates on both families.

Aggregate failures per baseline/isolated/plugin were Sonnet 2/0/0 and Luna
2/1/0; failed-leaf trial counts were 8/3/1 and 4/1/0 respectively. Luna's
isolated Windows check failed a phrase regex despite identifying the matching
Windows Kits path. That wording defect is separate from its generated
PowerShell variable interpolation error; neither historical result is rescored.
All native views and patches succeeded. One Luna plugin ripgrep call failed
to create its output file; its underlying cause remains unclassified.

| Skill | Sonnet isolated/plugin SDK counter change | Luna isolated/plugin SDK counter change |
|---|---|---|
| Doctor | -6.1% / +11.3% | +26.4% / +35.3% |
| Binding | +23.2% / +45.8% | +13.0% / +18.6% |
| DI | +41.2% / +66.9% | +1.8% / +22.5% |

These ratios use matched role totals, including dormancy and excluding judges.
They are not paired confidence intervals, invoices or wall-clock comparisons.

The subsequent exact-policy compiler replay confirmed a content defect:
XC0045 is a missing-member warning on the tested MAUI 10 compiler, and promoting
only XC0022/XC0025 still permits that build. The binding core/golden now includes
XC0045; replay accepts correct markup and rejects removing that promotion.
The earlier blanket warnings-as-errors probe did not establish the selective
policy's behavior. Doctor's phrase grader now also accepts the matching SDK
path, with wrong-version path rejection.

The HTTP replay confirms that the .NET 10 factory default on a
SocketsHttpHandler-supported host sets its connection lifetime from
HandlerLifetime, whereas a custom SocketsHttpHandler does not inherit that
setting. Retained clients are not categorically invalid; verify the target
version/platform/handler before claiming DNS safety. The reference now explains
that distinction. These later fixes do not rewrite the twelfth votes or prove
fresh agent improvement.

The standalone harness [#1280](https://github.com/dotnet/skills/pull/1280)
is ready for engineering review, with its existing engineering CODEOWNERS
requested, but remains unmerged. The physical-device heap-capture proposal
[#1274](https://github.com/dotnet/skills/pull/1274) is also ready for review;
neither review status establishes trusted-base integration, hardware acceptance
or default readiness.

### Thirteenth corrected-content comparison

Only binding and DI were remeasured at `28595929`: these contain the new
selective-diagnostic and HTTP-reference corrections. Doctor's source/routing
was not rerun for a better dormancy outcome. Generated configurations again
registered all eight concrete skill directories, with five workers and unchanged
declared counts/timeouts. Both families completed 75 trials, 25 per role, with
complete shutdown accounting and zero errored or unmatched comparisons.

| Skill | Sonnet 5 W/T/L (p) | GPT-5.6 Luna W/T/L (p) |
|---|---|---|
| Binding | 5/3/2, unproven (0.2266) | 6/3/1, unproven (0.0625) |
| DI | 6/4/2, unproven (0.1445) | 4/6/2, unproven (0.3438) |

All three dormancy tasks passed in both contexts. Positive routing still misses:
Sonnet binding healthy/repair did not activate in either role, and its template
case missed plugin activation; Luna's healthy case missed plugin activation.
No wins from dormant boundary tasks are counted as preference improvement.

Sonnet's baseline/isolated/plugin aggregate failures were 3/0/0, with
failed-leaf trial counts 5/3/1. Luna's counts were 1/0/0 and 3/0/0. Both
treatment families now demonstrate correct XC0045 promotion on the compiled-page
task, but this observed task success is not proof of sole causation or an overall
preference pass. Sonnet still generated a two-child ContentPage despite the
source's correct one-root rule/example, omitted native-validation limitations
on an unactivated healthy task, and captured a typed HTTP client with an
unverified universal pooling assurance. Luna's HTTP answer uses the valid
per-operation factory design; its method sketch is not executable implementation
proof. Namespace-detail and architecture preferences also contributed losses.

The inspected HTTP and explicit-source tasks activated the target skill, but
neither family/role read an additional reference on those tasks. Do not attribute
their answers to consultation of the new HTTP reference. Source correctness,
reference consultation, generated correctness and aggregate grades are distinct.
All observed native reads/edits succeeded. Three Luna baseline web fetches
returned 404; treatment tools had no observed errors.

| Skill | Sonnet isolated/plugin SDK counter change | Luna isolated/plugin SDK counter change |
|---|---|---|
| Binding | -9.7% / +2.5% | +17.6% / +23.5% |
| DI | +42.0% / +69.1% | +0.7% / +21.1% |

The same matched-total, dormancy-included, judge-excluded limitations apply.
Do not compare absolute counters across runs as if task/model execution were
identical, pool families, rescore historical votes or repeat an unchanged matrix
to obtain a pass. These results support engineering review of the correctness
repairs, not a quality/cost-neutral default certification.

### Post-thirteenth critical grading contracts

Three advisory/no-op contracts now use Vally's supported binary prompt scoring
and explicit correctness conditions. The explicit-source case rejects wrong
binding types and invalid complete pages while accepting minimal fragments.
The preservation case requires the requested final validation limitation, not
an implicit inference from a passing managed check. The HTTP case rejects
combined-lifetime and unconditional handler-pooling assurances while accepting
separate cache ownership and verified connection recycling, including supported
.NET 9+ defaults. The new instructions also enter pairwise judging; historical
votes are not rescored or transferred to this eval revision.

Production oracle replay accepted ten golden/equivalent answers and rejected
seven semantic mutations separately with GPT-5.6 Terra and Claude Haiku 4.5.
These are seventeen variants of three existing contracts per judge, not
seventeen independent stimulus votes or fresh executor/plugin comparisons.
The deterministic graders still pass for each semantic negative; the prompt
grader is the rejection mechanism. Binary LLM scoring remains probabilistic,
not deterministic correctness enforcement.

Initial replay exposed a prose-only preservation reference without tool
evidence and a judge accepting omitted disclosure. Its curated ATIF reference
now includes inspection and the managed execution; the helper reruns each
recorded command against the actual materialized fixture and compares its
output, rather than trusting hand-authored observations. An overly detailed
draft grading instruction was also rejected: concise limitations remain valid,
without requiring an inventory of assertions or platform tests.

No skill guidance, stimulus count, agent budget or installer behavior changed.
These oracle checks do not establish new activation, preference, cost neutrality
or native/device acceptance. The subsequent executor matrix is reported below
with its newly discovered grading confound.

### Fourteenth: staged-input grading confound

Seven suites ran at `eb90dead`, with all eight concrete skill directories,
five workers and unchanged declared budgets. Sonnet 5 and GPT-5.6 Luna each
completed 240 trials: 80 per baseline/isolated/plugin role. All comparisons
matched without trial errors; all 480 SDK shutdowns matched their trial
identities. This establishes execution/accounting completeness, not valid scope
grading or readiness.

The expanded inventory grader incorrectly treated supplied skill/reference files
as unauthorized additions. Vally copies these directories into the workspace
before setup and agent execution. All twelve UI repair/no-op cases failed that
grader in both treatment roles, for both families. This is a grader defect,
not evidence that those agents modified supplied skills or failed the repair.
The six UI suites' raw preference records remain historical diagnostics, not
certification; no votes or grades are retroactively rewritten.

| Skill | Sonnet raw W/T/L (p) | Luna raw W/T/L (p) | Sonnet / Luna plugin SDK counter change |
|---|---|---|---|
| Lifecycle | 5/2/3 (.363281) | 5/3/2 (.226563) | +6.9% / +10.3% |
| CollectionView | 6/2/2 (.144531) | 7/3/0 (.007813) | +77.3% / +45.9% |
| Binding | 6/4/0 (.015625) | 7/1/2 (.089844) | +49.1% / +18.2% |
| DI | 7/3/2 (.089844) | 6/4/2 (.144531) | +72.4% / +16.4% |
| Safe area | 9/1/0 (.001953) | 7/1/2 (.089844) | +37.2% / -17.5% |
| Shell | 6/2/2 (.144531) | 7/2/1 (.035156) | +69.9% / +32.3% |
| Theming | 6/3/1 (.0625) | 6/4/0 (.015625) | +67.7% / +36.6% |

DI does not use the faulty inventory guard; its preference remains unproven in
both families. No project-default conclusion follows from the raw UI pass
labels. Cost columns are ratios of matched role totals, including dormancy and
excluding judges, not paired confidence intervals, invoices or wall-clock
claims. They describe these executions, not a cost-neutral default set.

The prospective correction uses Vally's built-in initial-to-final workspace
diff, captured after supplied inputs/setup and before the agent. Only repair
`Program.cs` and root generated `bin`/`obj` changes are permitted; no-op allows
only generated changes. Known protected fixture hashes remain independently
checked. Supplied skill/reference directories are not excluded: modifying or
deleting their files fails the same diff guard. This revision has deterministic
replay coverage, not a new executor matrix or transferred historical votes.

### Adoption decision

Keep the eight skills task-specific and opt-in. The corrected SDK, compiler and
runtime decisions have executable evidence, but current cross-family preference,
positive routing and cost requirements do not establish a project-default set.
Do not remove a useful skill solely because a tie-limited comparison is unproven;
equally, do not retain a default solely because an older comparison passed.
This proposal changes no installer behavior. Maintainers still need to confirm
support ownership and the integration basis; the standalone harness needs
trusted-base integration, and native/device acceptance remains separate.

### Engineering review corrections

The six UI repair/no-op scope graders now protect all non-generated file paths
using a trusted pre-agent snapshot, not selected source extensions or a mutable
workspace manifest. Nested `bin`/`obj` names, scratch notes, hidden files and
symlinks do not bypass the contract. The no-op prompts explicitly preserve
non-generated workspace files. Replay stages the complete plugin before
capturing the baseline, accepts unchanged inputs, and rejects additions,
protected-input changes, and changes/deletions of every staged skill file.
Generated paths with spaces, quotes and newlines remain accepted; equivalent
out-of-scope paths and generated-to-unprotected renames are rejected.

Production oracle checks cover both executable cases in baseline, isolated and
full-plugin environments. Scope mutations are applied in the golden patch
after setup, not introduced as supplied inputs. The scope guard must fail while
every other deterministic grader still passes. Oracle uses the supplied golden
patch as its diff; the separate snapshot replay exercises actual cumulative Git
diffs. Full replay passes 66 references and 12 golden workspaces, rejects 918
preservation mutations, and accepts 36 staged-environment goldens while rejecting
96 scope negatives. A final five-worker curated oracle diagnostic separately
accepts 36 cases and rejects 36 script-addition mutations, with declared budgets
retained. Neither replay executes an agent or uses a paid judge; the concurrent
diagnostic is not a fresh cross-family preference measurement.

Doctor's healthy/discovery contracts independently protect their inputs with
the same trusted pre-agent diff mechanism. The in-workspace state checker is
diagnostic, not the preservation authority: recapturing its manifest or
rewriting the checker cannot erase the external baseline. Discovery permits
only the requested resolver/output and root Python cache changes. Replay
accepts both cases in all three staged roles and rejects 18 changed-and-recaptured,
deleted-and-recaptured, and checker-bypass mutations even though the old
in-workspace checks accept them. Its 12 deterministic goldens, six behavior
mutations and eight wording contrasts pass. Discovery's variant now changes
the JDK range as well as its recommendation; a resolver hardcoding the primary
range is rejected rather than passing both fixtures.

The harness integration test now uses different log and workspace roots. The
unwrapped provider rejects the workspace file. The historical test correction
at `787dc85b` required the workspace overlay to read it successfully; that unsafe
overlay has since been removed. The current provider installed through real SDK
session setup now uses the native rooted helper on supported hosts, while
preserving log operations and factory/configuration contracts. The standalone
harness PR's `e4cd05f0` remains the historical fail-closed safety correction;
the native restoration is a later parent integration, not that child payload.
Trusted-base integration remains pending. The earlier README/PR
measurement inconsistency is resolved by pinning
executor evidence to `28595929` and reporting later grading checks separately.

### Native rooted filesystem provider and fail-closed unsupported hosts

The harness owner confirmed an intermediate-parent symlink swap after path
canonicalization can make the adapter read or overwrite an outside owned
fixture. Canonical checks are not an atomic or adversarial confinement boundary.
The unsafe canonicalize/reopen overlay is removed. A pinned Go1.27.1 `os.Root`
helper now implements all ten workspace operations on Linux/macOS amd64/arm64.
Node captures a directory descriptor before agent execution; each helper inherits
that capability instead of reopening mutable workspace paths. Actual SDK
read/stat/write works. Metadata comes from Node `fstat` on an already-rooted
descriptor, including accurate platform birthtime; it requires a readable target.
Parent-swap and entire-root replacement tests leave outside sentinels unchanged.

This is a platform-native descriptor-relative implementation, not a JavaScript
recheck or assumed OS sandbox. Original relative/log-root operations and other
provider types retain their behavior. Cross-root rename and root removal reject;
capabilities close on normal/forced shutdown. Missing-helper and unsupported-host
operations remain fail-closed with `ERR_EVALUATION_WORKSPACE_ISOLATION_REQUIRED`.
No trust flag or unsafe opt-in exists.

Scope remains explicit: trusted initial setup is required. Privileged mount
changes, devices, hard-link inode sharing, original log operations, shell tools
and grader-host I/O are not isolated by this provider. Absolute symlinks reject;
contained relative symlinks work. No whole-host sandbox is claimed.

SDK 1.0.11 converts all `exists` errors to `false`; rooted-policy errors
retain the actionable message with SDK code `UNKNOWN`, while missing files retain
`ENOENT`. Shell fallback is not a
native-I/O repair. CI stages and builds the helper from the trusted revision,
then smoke-tests the launcher before token selection. The default local runner
also builds the pinned helper and uses the guarded launcher. Native tests do not
transfer historical MAUI matrices or certify trusted integration, model outcomes,
hardware behavior or default adoption.

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
The compiler probe reads the binding core's selective warning list rather than
enabling blanket warnings-as-errors: an unpromoted missing member builds,
removing XC0045 reproduces that false success, and the shipping policy rejects
it. DI's HTTP probe distinguishes default connection recycling from custom
primary-handler policy without making real network requests.
The Shell core's actual guard/caller example is compiled and its pending-request
skip path exercised. Real-package probes also check back-button property
ownership and reject sibling-theme `var` conditionals and undeclared dictionary
`RemoveWhere` calls. Native `MainThread` dispatch is not exercised.

The opt-in semantic replay below **uses paid LLM judges**, requires authenticated
Vally access and executes no agent. An optional exact stimulus-name argument
selects one of its three contracts. It uses the production oracle CLI and also
replays curated tool observations against the real fixture:

```bash
VALLY_TELEMETRY_OPTOUT=1 node tests/dotnet-maui/replay_correctness.mjs gpt-5.6-terra
VALLY_TELEMETRY_OPTOUT=1 node tests/dotnet-maui/replay_correctness.mjs claude-haiku-4.5
```

These checks validate contracts, fixture behavior and reference answers, not fresh
model improvement or native UI/OS delivery. Run normal cross-family evaluations
separately before claiming default readiness or cost neutrality.
