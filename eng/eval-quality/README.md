# Eval quality gate

`check_eval_quality.py` blocks structural defects that can corrupt an eval
result. Most were first found only after an eval mysteriously lost to its own
baseline or won every trial and still failed.

Run it from the repository root:

```bash
python eng/eval-quality/check_eval_quality.py          # what CI runs
python eng/eval-quality/check_eval_quality.py --strict # also fail on warnings
python eng/eval-quality/selftest_eval_quality.py       # prove the gate still fires
```

## Failing checks

All eleven are **structural** — they inspect file existence, git state, declared
numbers, or YAML shape/keys. None of them interprets prose, so they cannot fire
spuriously on a well-written eval.

### 1. Referenced fixture missing on disk

A stimulus points at a fixture path that does not exist. The scenario fails at
setup, which reads as a skill failure.

### 2. Referenced fixture not tracked by git

The fixture exists locally but is not in the index, so it will not exist on the
CI runner.

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

A grader whose `config` is absent, null, or missing its required key
(`pattern`, `substring`, `command`, `path`) parses as valid YAML and **enforces
nothing**. The scenario looks like it has one more assertion than it really has.

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

### 7. Dormancy guard that also sets `reject_skills`

A dormancy guard is a stimulus with `expect_activation: false`: an off-target
request where the skill should stay dormant rather than hijack the task.

Adding `constraints.reject_skills: ["*"]` forces the skilled arm to run
skill-free — which makes it **identical to the baseline arm**. The head-to-head
score is then pure judge noise. Across four evals using this pattern the same
guard scored −0.4, +0.4, +0.4 and 0, and twice cost a skill its pass.

The repo convention is `expect_activation: false` **alone** (see
`agent.test-quality-auditor`, `agent.test-migration`,
`system-text-json-net11`), so the skill is actually loaded and the guard
measures the real property.

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

### 10. A spec declaring both `config:` and `defaults:`

`config` is a deprecated alias for `defaults` in vally 0.9. The loader folds one
into the other and throws when a spec carries both:

```text
eval spec: cannot specify both 'config' and 'defaults'
```

Some evals still open with a `config:` block. Adding a separate `defaults:`
block for any modern setting, including `runs`, breaks those specs.

What makes it worth a gate is how it fails. `vally` rejects the spec, but the
evaluate job still exits 0 with no verdicts, and the PR comment reports:

> ❌ Evaluation ran but produced no results. … This is usually a **transient
> infrastructure failure** … not a problem with your skill. … re-post
> `/evaluate` to try again.

So the one actionable signal points away from the cause, and the suggested fix
re-runs a spec that can never load. Replace `config:` with one `defaults:` block
that carries all settings.

### 11. Duplicate stimulus names

Vally pairs baseline and treatment trajectories by `(stimulus name, trial
index)`. Two stimuli with the same name therefore create ambiguous comparison
slots even when their prompts differ. The authoring gate requires every
stimulus name in one eval to be unique; the runtime adapter also rejects missing
or duplicate comparison slot identities.

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

### Statistical power

The evals that are still below the five-distinct-stimulus floor of failing
check 8. The warning lists distinct stimuli, runs per stimulus, and total paired
runs separately. Their verdicts are ⚠️ underpowered rather than a pass or a
failure, so adding independent stimuli is the highest-value eval work available.
See check 8 for why the floor sits at five.

### Evals parked at the floor

Evals at 5–7 distinct stimuli, where a pass still requires a loss-free record
and enough non-tie votes to clear the floor. These are eligible for a verdict,
so they are not underpowered. At five stimuli, one tie removes the possibility
of a pass. Add stimuli unless the current cases are near-certain discriminators.

### Orphaned fixtures

A fixture directory that is committed but that no stimulus references. Usually
means a scenario was planned and dropped, so the coverage it was built for is
being paid for in repo size but never exercised. Wiring these up is the cheapest
way to raise an eval's independent task count, because the fixture already exists —
`migrate-nullable-references` sits at 3 scenarios with three unreferenced
fixtures beside it.

### Skill eval coverage

A skill that ships with `SKILL.md` but has no `tests/<plugin>/<skill>/eval.yaml`
carries zero evidence of impact.

**Reference skills are reported separately.** A skill whose frontmatter sets
`disable-model-invocation: true` is dropped from the Copilot CLI's
`<available_skills>` menu, so the model cannot reach it from a user prompt — a
consumer skill or agent loads it by name. The experiment's `skilled` variant
loads exactly one skill (`plugins/${eval.grandparent}/skills/${eval.parent}`),
so any direct eval for one of these would run an arm the model can never invoke:
treatment equals control by construction and the head-to-head score is judge
noise. Answer-content graders cannot create a difference between identical
arms. That is the same defect failing check 7 exists to prevent, and adding
such an eval would make the number worse, not better.

The honest coverage for these is **dependency-level**: they are exercised
through the evals of the skills that load them (for example `run-tests` and
`mtp-hot-reload` load `platform-detection` and `filter-syntax`, the polyglot
analysis skills load `test-analysis-extensions`, and `code-testing-agent` loads
`code-testing-extensions`), and in the plugin arm, where the whole plugin is
loaded. Closing this properly needs harness support for declaring a dependency
in the skilled variant, not a per-skill eval file.

**A reference skill that has a direct eval is reported too, and more loudly.**
The same argument cuts both ways: if the skilled arm cannot reach the skill, an
eval sitting beside it does not measure the skill — it measures the judge
comparing baseline to baseline and then labels the result a pass or a fail. That
is worse than no eval, because no eval is visibly zero evidence whereas a
fabricated verdict is counted in the plugin's pass rate. Remove the direct eval
and preserve its scenarios through reachable consumer outcomes instead.

The current `dotnet-test` reference skills — `code-testing-extensions`,
`filter-syntax`, and `test-analysis-extensions` — therefore have no direct eval.
Their consumer coverage is documented in `plugins/dotnet-test/README.md`;
`filter-syntax`, for example, is covered through filtered-command scenarios in
`tests/dotnet-test/run-tests/eval.yaml`, where the consumer can load the
reference and produce a measurable outcome. Do not add a direct eval for a
reference-only skill until the harness supports declaring skilled-arm
dependencies.

### Dormancy guard without an anti-hijack rubric item

Once `reject_skills` is removed the skill loads, so the judge scores the guard
against its rubric. If that rubric only says "wrote tests", the judge has
nothing to grade the real property with and falls back to comparing **output
volume** between two near-identical runs — which is exactly how a passing skill
regressed to a −40% loss on its own guard.

Add an explicit criterion, e.g. *"Did not derail into a mutation analysis of
code the user never asked about"*, plus one instructing the judge not to reward
raw test count.

This check is a warning rather than an error because detecting it requires
phrase matching over free text and will always have false positives — a gate
that blocks a PR spuriously is a gate the team switches off.
