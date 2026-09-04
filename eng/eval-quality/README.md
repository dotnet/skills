# Eval quality gate

`check_eval_quality.py` blocks structural defects that can corrupt an eval
result. Most were first found only after an eval mysteriously lost to its own
baseline or won every trial and still failed.

Run it from the repository root:

```bash
python eng/eval-quality/check_eval_quality.py          # what CI runs
python eng/eval-quality/check_eval_quality.py --strict # also fail on warnings
python eng/eval-quality/check_eval_quality.py --all    # audit every eval suite
python eng/eval-quality/selftest_eval_quality.py       # prove the gate still fires
```

By default, the gate enforces structural checks on eval suites changed since
the previous commit. Pull-request CI passes `--base-ref` explicitly, so every
fixture, reference, or spec changed by the PR is checked as one suite. This is
a shrink-only ratchet: existing defects in unrelated plugins do not block a
focused change, but editing that suite requires it to meet the current rules.
Use `--all` for a repository-wide migration audit.

## Failing checks

The checks are **deterministic**: they inspect file existence, path containment,
git state, declared numbers, YAML shape, or whether a reference satisfies an
explicit assertion. The sections below explain failure modes that are not
already clear from Vally or parser diagnostics.

### 1. Referenced fixture missing on disk

A stimulus points at a fixture path that does not exist. The scenario fails at
setup, which reads as a skill failure.

### 2. Referenced fixture cannot be materialized by git

The fixture exists locally but is not in the index, so it will not exist on the
CI runner.

The same rule rejects empty fixture directories and untracked content reached
through a tracked symlink. Git does not preserve an empty directory, and a
symlink does not cause Git to include its target. The gate follows contained
symlinks when it checks the index and requires each fixture directory to contain
at least one materializable file.

This is the subtle one. `.gitignore` carries `coverage*.xml` (a sensible rule
for Coverlet output), which silently swallowed a committed Cobertura *fixture*.
`git add -A` reported success, the eval passed locally, and three scenarios
would have failed at setup in CI. Verifying against the working tree cannot
catch it — only the git index can.

"In the index" means `git ls-files` alone. An earlier revision also unioned in
`git diff --cached --name-only`, which is worse than redundant: a fixture staged
for removal but left on disk appears there and would be counted back as tracked,
producing a false negative for exactly this bug class. The self-test commits
before mutating so that path is genuinely exercised — without the commit there
is no `HEAD`, `git diff --cached` errors out, and the defect stays hidden.

The check prunes local `bin`, `obj`, and `.vs` directories before it compares a
fixture with the index. Those directories are deterministic build outputs, not
eval inputs. This keeps a build used to validate a fixture from making the next
quality-gate run fail while still rejecting ignored source fixtures such as the
Cobertura report above.

### 3. Cobertura `line-rate` contradicts its own `<lines>`

The `crap-score` skill documents both parse paths:

> Parse the Cobertura XML to find each method's `line-rate` attribute … **If
> `line-rate` is not available at method level, compute it from the `<lines>`
> elements.**

So when the two disagree, the baseline and skilled arms can legitimately read
*different coverage inputs* for the same method and compute different CRAP
scores. The comparison then measures which number the judge happened to treat
as authoritative rather than the skill.

Observed live: a scenario lost −40% with the judge writing *"Response B made a
critical error by manually counting line hits (12/15 = 80%) instead of using
the XML's recorded line-rate of 0.55"*. The fixture was wrong, not the response.

When fixing one of these, the **declared rate is normally the intent** — the
rubrics are written against it (which method is the risk hotspot) — so adjust
the `<lines>` data to match, then re-derive any rubric item that quotes a
coverage percentage, a CRAP score, or a "coverage needed" figure.

### 4. Whole-file Cobertura totals contradict the file `line-rate`

The same split-brain, one level up. A report also carries file-level summary
attributes, and those are a third way to read the same number:

```xml
<coverage line-rate="0.47" lines-covered="35" lines-valid="60">
```

`0.47` agreed with the per-method `<lines>` (22/47 = 0.468); `35/60` is 58.3%.
A skill reading the summary attributes and one recomputing from the payload
therefore disagreed by 11 points on the same fixture. Found in review on
`coverage-analysis/partial-coverage` after check 3 had already been applied —
the method-level check alone could not see it, because every individual method
was self-consistent.

This compares two *declared* values, so it cannot fire on well-formed input.
Fix it by making the totals agree with both the declared rate and the summed
`<lines>` (here, `lines-covered="22" lines-valid="47"`) rather than only with
the rate — that leaves one number for every reader. The same applies to
`branches-covered`/`branches-valid` against `branch-rate`.

### 5. Aggregate `line-rate` contradicts the `<lines>` beneath it

A file, package or class whose declared `line-rate` disagrees with the `<line>`
elements underneath it — the same split-brain as checks 3 and 4, at the level
the prompt usually quotes.

This shipped for a while as a warning because of one fixture:
`coverage-analysis/fixtures/plateau` declared 75% while its `<lines>` implied
47%, and the scenario prompt said *"my coverage is stuck at 75%"*. It could not
be repaired by recomputing — `CalculateGpa` contributes 24 of the 47 lines at 0%
and the rubric requires it to stay the blocker, capping the achievable rate at
23/47 = 48.9% — so the fix reached into the scenario itself. It was resolved by
restating the plateau at 47% (declared rates and totals aligned to 22/47, prompt
reworded); the plateau story depends on one method dominating the shortfall, not
on the specific number. With that fixture repaired there are no offenders left,
so the check now fails instead of warning.

Fix an occurrence the same way: make the declared rate match the payload, and if
a prompt or rubric quotes the old figure, update it in the same change.

### 6. Grader with a missing or empty required config

A grader whose `config` is absent, null, or missing a required key
(`pattern`, `substring`, `command`, `path`, or `value`) parses as valid YAML
and **enforces less than it declares**. File-content graders require both
`path` and `value`; checking only one lets a primary artifact assertion vanish.
The scenario then looks stronger than it is.

The failure mode is an indentation slip, usually from an edit:

```yaml
      - type: output-matches
        config:                    # <- pattern belongs here
      - type: output-matches       # <- and ended up on the next list item
        config:
          pattern: \d+ call sites
```

Observed live on this repo: a grader-regex fix left the original
`- type: output-matches` / `config:` pair behind, producing a fourth grader with
`config: null` that shipped in a pushed commit. Neither YAML parsing nor a
bespoke regex validator caught it — the validator did
`(g.get("config") or {}).get("pattern")` and silently skipped the entry, so the
pattern count was identical before and after the fix. Only review caught it.

### 7. `reject_skills: ["*"]` blocks the target skill

The wildcard applies to the target skill as well as unrelated skills. On an
on-target capability stimulus, it prevents the treatment from using the feature
being evaluated. On a dormancy stimulus, it forces the skilled arm to run
skill-free and makes it identical to baseline.

The head-to-head score is then biased or pure judge noise. Across four dormancy
evals using this pattern the same guard scored −0.4, +0.4, +0.4 and 0, and twice
cost a skill its pass.

For an off-target request, use `expect_activation: false` **alone** (see
`agent.test-quality-auditor`, `agent.test-migration`,
`system-text-json-net11`), so the skill is actually loaded and the guard
measures the real property. Named exclusions for unrelated sibling skills remain
valid; only the wildcard defeats the direct comparison.

### 8. Fewer than 5 distinct stimuli behind a verdict

Vally defines a [stimulus as a test case](https://microsoft.github.io/vally/concepts/how-it-works/).
It defines repeated runs as inputs to pass rate, pass@k, pass^k, and flakiness.
Its [scoring guidance](https://microsoft.github.io/vally/concepts/scoring/)
recommends 3 runs for CI and 5–10 for nightly evaluation. Those runs measure how
reliably the agent handles the same task. They are not independent task samples.

The repository gate therefore collapses repeated runs to one majority-direction
vote per stimulus, then applies an exact one-sided **sign test**: more stimulus
wins than losses at `p ≤ 0.05`. Five stimuli run three times produce 15 paired
runs for reliability analysis, but only five gate votes.

The sign test cannot reach 5% on fewer than five discordant (non-tie) votes:
`0.5⁴ = 0.0625` is above alpha, while `0.5⁵ = 0.03125` is below it. So **below
five distinct stimuli no possible record passes**, however good the skill is.
Five is derived from this repository's predeclared `alpha=0.05`; it is not a
Vally recommendation.

| Stimulus votes | Minimum passing record | Exact `p` |
| ---: | --- | ---: |
| 1–4 | none; even a clean sweep cannot pass | ≥ 0.0625 |
| 5 | 5W/0T/0L | 0.03125 |
| 8 | 5W/3T/0L | 0.03125 |

This is an *eligibility* floor, not adequate power for a realistic effect. Below
it, `eng/vally-adapter/adapt.mjs` reports `underpowered` and the PR comment shows
⚠️: never a pass, never a regression. This check makes that state unshippable
for new evals.

> **Five is fragile.** A pass at exactly five stimuli needs 5W/0T/0L. One tie
> leaves four discordant votes and makes a pass impossible. At six stimuli one
> tie is survivable; at seven, two ties are survivable. Tolerating one loss needs
> eight discordant votes (`7W/1L`, `p=0.03515625`).

Power depends on the effect that the eval must detect. Under an idealized no-tie
model, the exact discordant-vote counts for at least 80% power at one-sided
`alpha=0.05` are:

| True conditional win probability | Discordant votes needed |
|---:|---:|
| 0.60 | 158 |
| 0.65 | 69 |
| 0.70 | 37 |
| 0.75 | 23 |
| 0.80 | 18 |
| 0.90 | 8 |

These are planning values, not universal minimums. Ties require more total
stimuli because they do not enter the test. Eight stimuli are enough for 80%
power only for a near-deterministic 90% conditional win rate. A non-pass is not
proof of no effect.

The table gives **sign-test power**, before the 20% practical floor is applied.
At a true 60% conditional win rate, the floor is exactly at the expected effect:
with 158 votes the sign test has 80.6% power, but the combined gate passes about
52.2% of records and approaches 50% as the sample grows. The gate is designed to
certify effects above its practical threshold, not effects that only equal it.

Repeated runs still matter. Keep Vally's recommended run counts where the cost
allows, and read `comparisonTrialEvidence` plus per-stimulus run W/T/L for
reliability. Do not use those runs to clear the distinct-stimulus floor.

**Grandfathering.** `underpowered-allowlist.txt` carries the evals that predate the floor. It is a
debt ledger and it is shrink-only in the mechanical sense:
the gate errors on an entry that is stale, duplicated, or no longer needed, and
`--base-ref` (which CI passes on every pull request) rejects entries that are
*new* relative to the base branch. Without that second half, a PR could add a
below-floor eval and exempt it in the same change — the defect the floor exists
to prevent, relocated one file over. Renames are read from git, so moving a
grandfathered eval is not treated as growth. `agent.*` evals are exempt
outright: the experiment's `evals:` glob excludes them, so no verdict is ever
computed and the floor has nothing to protect.

### 9. Duplicate key in a mapping

`yaml.safe_load` accepts duplicate keys silently and keeps the **last** one. So
a stray second `prompt:` / `environment:` / `graders:` / `rubric:` block — the
tail an edit left behind when it moved a scenario — lands inside whichever
stimulus follows it and overwrites *that stimulus's own values*, field by field.

The result is the worst shape a defect can take here: the spec parses, the
scenario count is exactly what the author intended, and one scenario is a
byte-identical rerun of another. It runs the wrong prompt against the wrong
fixture, and the discriminator it was added for does not exist.

Observed live in #971. `grade-tests` was raised from 4 to 5 scenarios to clear
the stimulus floor, and the new "production code available" scenario shipped as a
silent clone of the "production code unavailable" one:

```yaml
  - name: Grade C# tests with the production code available
    prompt: |            # <- overwritten
      ...
    constraints:
      reject_tools: [edit, create]
    prompt: |            # <- leftover tail; this is the one that survives
      ...Payments.Tests/PaymentGatewayTests.cs...
```

`yaml.safe_load(...)` returned 5 stimuli with the 5 expected `name:` values, and
`dotnet-production-available/` — a fixture built for the scenario — was never
loaded. Validating a spec by parsing it and counting scenarios, which is what
the PR had done, cannot see this. Only the parser can, so the gate uses a loader
that refuses duplicate keys and reports both line numbers.

Fix it by deleting the stray block. Check it really is stray first: compare it
against the scenario it duplicates before removing it, so a genuinely distinct
scenario that merely lost its `- name:` line is restored rather than dropped.

### 10. Duplicate stimulus names

Vally pairs baseline and treatment trajectories by `(stimulus name, trial
index)`. Two stimuli with the same name therefore create ambiguous comparison
slots even when their prompts differ. The authoring gate requires every
stimulus name in one eval to be unique; the runtime adapter also rejects missing
or duplicate comparison slot identities.

### 11. Stimulus-level timeout

Vally supports `defaults.timeout` for an eval. Its stimulus schema has no
top-level `timeout`, so this shape parses but the runner silently keeps the
suite default:

```yaml
defaults:
  timeout: 6m
stimuli:
  - name: Long-running diagnosis
    timeout: 10m # ignored
```

This can leave a trial failing at six minutes even though the spec appears to
give it ten. Set a truthful suite-level budget in `defaults.timeout` instead.

### 12. Unquoted rubric code token treated as a YAML comment

YAML treats `#` as the start of a comment when whitespace precedes it in a
plain scalar. A rubric such as this parses successfully but enforces only
`Supports`:

```yaml
rubric:
  - Supports #:property customization in generated files
```

The same defect affects C# preprocessor tokens such as `#if`, `#nullable`, and
`#pragma`. The gate uses YAML source marks to inspect only unquoted rubric
scalars and only these known code-token forms. It does not reject ordinary
comments. Quote the whole rubric item when it contains such a token.

### 13. Golden trajectory or patch missing on disk

A stimulus points at a `golden_trajectory.path` or `golden_patch.path` that does
not exist. Vally cannot load the oracle, so the trial cannot prove the reference
behavior.

### 14. Golden trajectory or patch not tracked by git

The reference exists in the local working tree but is absent from the git
index. Local validation can read it, while CI receives an eval that points at a
file that was never checked out. The gate checks both trajectory JSON and patch
files with the same index-only rule used for fixtures. If the reference is a
symlink, both the link and its contained target must be tracked.

### 15. Golden patch does not apply to declared fixture inputs

A patch can remain present and tracked after its fixture changes, but its
preimage no longer exists. The gate materializes each stimulus's declared
`environment.files` mappings in a scratch workspace and runs
`git apply --check`. A stale patch therefore fails before Vally tries to use a
broken oracle. Stimuli whose workspace is created only by commands are not
checked because their preimage cannot be reconstructed statically.

Materialization is fail-closed. Fixture sources, destinations, and reference
paths must be relative, cannot contain `..`, and must resolve within their
declared suite or scratch-workspace root. A fixture symlink that resolves
outside its suite is also rejected. These rules stop an eval from copying or
reading unrelated host files while the gate checks a patch.

### 16. Output grader has a patch but no response trajectory

A golden patch supplies workspace state, not assistant output. If a stimulus
uses an `output-*` grader with only `golden_patch`, its reference has no response
for that grader to inspect. Add a `golden_trajectory` that represents the
expected assistant response, or replace the output assertion with a workspace
grader when the requirement belongs to the produced artifact.

### 18. Capability stimulus lacks result-slice tags

An overall failure is not actionable when it cannot be mapped to the capability,
risk, and customer journey that failed. Every capability stimulus must declare
non-empty `capability`, `risk`, and `journey` tags. Use stable lowercase
kebab-case values so reports can compare the same slice over time.

### 19. Golden trajectory fails an output grader

A reference response is not GREEN when its own deterministic output grader
rejects it. Like Vally, the gate walks ATIF steps in reverse and grades only the
final non-empty agent message, including flattened content parts. Its image
placeholders also match Vally: `data:` payloads are reduced to their media type
and long references are capped at 128 Unicode code points. It checks all
`output-contains`, `output-not-contains`, `output-matches`, and
`output-not-matches` graders. This catches stale references, regex collisions,
and trajectories whose earlier messages mask an invalid final response before
Vally spends model tokens.

Substring checks also honor `case_sensitive` and `negate`. Regex checks use
only Vally's explicit leading `(?i)`, `(?m)`, and `(?s)` flags and honor
`negate`; the gate does not add case or multiline behavior implicitly.

### 20. `dotnet test` checks only the exit code

`dotnet test` can return exit code 0 when test discovery fails and zero tests
run. A `run-command` grader that checks only `expected_exit_code: 0` therefore
accepts a broken test migration.

Add `stdout_contains` or `stdout_matches` that proves the fixture executed its
expected tests. A real mutation check in the xUnit v3 migration suite produced
this result:

| Workspace | Exit code | Tests | Exit-only grader | Output-aware grader |
|---|---:|---:|---:|---:|
| Correct migration | 0 | 2 passed | pass | pass |
| Broken discovery | 0 | 0 | pass | fail |

The gate does not prescribe one runner's summary format. It requires an output
assertion so the eval author must state the expected execution signal.

### 21. Golden trajectory is not valid ATIF

Vally validates a golden trajectory before it resolves the reference. ATIF step
sources can only be `system`, `user`, or `agent`; a standalone
`"source": "tool"` step is invalid. Tool activity belongs on the agent step that
made the call:

```json
{
  "source": "agent",
  "message": "I will inspect the project.",
  "tool_calls": [
    {
      "tool_call_id": "call_1",
      "function_name": "bash",
      "arguments": { "command": "dotnet test" }
    }
  ],
  "observation": {
    "results": [
      { "source_call_id": "call_1", "content": "Tests passed." }
    ]
  }
}
```

The gate mirrors Vally's ATIF validation for required trajectory, agent, step,
content-part, tool-call, observation, metric, and subagent fields. This prevents
a reference from passing local JSON parsing but failing when Vally loads it.

### 22. Golden trajectory claims work it does not represent

A trajectory can describe an expected answer or expected tool events. Vally
permits those events to be fake, so neither a curated nor recorded-looking
observation proves that an edit, build, test, install, or command happened. A
workspace completion claim requires a golden patch, and an execution completion
claim requires a `run-command` grader so the oracle replays the evidence. Use
expected-result voice when neither form of evidence exists. The gate also
rejects a complete rubric item copied into the response.

## Why the gate scores direction, not magnitude

Worth recording, because the check above is only half of what went wrong.

Compare scores each trial on a five-point ordinal scale — `much-better` `+1.0`,
`slightly-better` `+0.4`, `equal` `0`, `slightly-worse` `−0.4`, `much-worse`
`−1.0`. Weighting a confidence interval by those magnitudes makes a Student's-t
interval read the 0.4 → 1.0 step as *variance*, so a skill is punished for
winning more decisively. Four wins and three ties over seven trials:

| trials | mean | ci_low | verdict |
| --- | ---: | ---: | --- |
| every win `slightly-better` | +0.229 | **+0.031** | ✅ |
| one win `much-better` | +0.314 | **−0.021** | ❌ |

Same record, better outcome, reversed verdict. This is the mechanism behind the
A/A instability in #952, where two runs on byte-identical inputs flipped 3 of 11
verdicts. `coverage-analysis` failed five consecutive runs while winning 100% of
its trials, then passed on a sixth with the same 3W/0T/0L record: its scores
were `[+0.4, +0.4, +1.0]` in a failing run and `[+0.4, +0.4, +0.4]` in the
passing one.

`adapt.mjs` therefore reads only each trial's **winner**, never its magnitude.
The verdict is a deterministic function of the win/tie/loss record, so identical
records always produce identical results.

Collapsing to direction is necessary but not sufficient: a t-interval over
win/tie/loss is still not calibrated at these sample sizes. Exhaustively
comparing it to the exact test up to 10 trials, the two disagree on 12 records
and in **every one of them the interval is the permissive one** — it passes
4W/0T/0L, 4W/3T/0L and 6W/0T/1L, all of which are `p = 0.0625`. The exact
binomial tail has no such gap, which is why the gate uses it rather than an
interval.

Vally's magnitude-weighted mean is still reported (as `meanScore`, and as
**Δ Pref** in the PR comment) because it is useful for triage; it just no longer
decides anything.

## Warnings (reported; failing only under `--strict`)

CI runs the gate without `--strict`, so these are informational there. Passing
`--strict` returns exit code 1 when any warning is present.

### Capability stimulus without a proven reference

Vally cannot prove solvability or grader calibration without a golden input.
The gate reports this debt but does not force an author to invent a reference.
An honest missing reference is safer than a narrated GREEN result added only to
improve the qualification score.

### Simple response reference stored outside the eval

A one-step curated response is easier to review as
`golden_trajectory.inline`, beside its prompt and graders. Keep a path-based
ATIF file for a multi-step trace with tool or observation detail, or for a
substantive report over 2,000 characters or 30 lines.

### Evals parked at the floor

Evals at 5–7 distinct stimuli, where a pass still requires a loss-free record
and enough non-tie votes to clear the floor. These are eligible for a verdict,
so they are not underpowered. At five stimuli, one tie removes the possibility
of a pass. Add stimuli unless the current cases are near-certain discriminators.

### Orphaned fixtures

A fixture directory that no stimulus references adds repository weight without
coverage. Remove it, or connect it only when it represents a needed customer
scenario.

### Expected workspace change without replayable state

A fixture-backed stimulus has a golden trajectory but no golden patch, and at
least one file or positive diff grader fails against the materialized starting
fixture. The trajectory can prove response quality, but it cannot prove the
expected workspace change.

The gate uses Vally's file-grader rules to avoid false warnings. It stays quiet
when file assertions already pass on the fixture, the expected diff is empty,
or the only state check is a command grader. Use a small golden patch for a
stable text edit. Keep explicit debt for generated binaries and other outputs
that a text patch cannot represent; do not invent a patch only to silence the
warning.

### Skill eval coverage

A skill that ships with `SKILL.md` but has no `tests/<plugin>/<skill>/eval.yaml`
carries zero evidence of impact.

A reference skill with `disable-model-invocation: true` cannot activate from a
user prompt. Cover it through the consumer skills that load it. A direct eval
would compare two equivalent arms and measure judge noise.

### Dormancy guard without an anti-hijack rubric item

The rubric must grade restraint, not output volume. Add a criterion that says
what the skill must not take over. This stays a warning because free-text
detection can produce false positives.
