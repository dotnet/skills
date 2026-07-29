# Eval quality gate

`check_eval_quality.py` blocks defect classes that have each already cost a real
evaluation result on this repo. Every one of them was invisible to the existing
checks: the eval specs parsed, `skill-validator` passed, and the damage only
showed up as a skill mysteriously losing to its own baseline — or as a skill
winning every trial and failing anyway.

Run it from the repository root:

```bash
python eng/eval-quality/check_eval_quality.py          # what CI runs
python eng/eval-quality/check_eval_quality.py --strict # also fail on warnings
python eng/eval-quality/selftest_eval_quality.py       # prove the gate still fires
```

## Failing checks

All eight are **structural** — they inspect file existence, git state, declared
numbers, or YAML keys. None of them interprets prose, so they cannot fire
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

### 8. Fewer than 5 trials behind a verdict

Trials, not scenarios, are what the pass gate is computed over. `vally compare`
produces one head-to-head trial per stimulus per run, so

```
trials = scenarios × defaults.runs
```

and the gate is an exact one-sided **sign test**: more wins than losses, at
`p ≤ 0.05`.

That test cannot reach 5% on fewer than five discordant (non-tie) trials —
`0.5⁴ = 0.0625` is already above alpha, `0.5⁵ = 0.031` is not — and discordant
trials can never exceed counted trials. So **below five trials no possible
record passes**, however good the skill is: the eval cannot answer the question
it exists to answer. Five is not a chosen constant; it is where the test becomes
attainable.

| trials | best possible record | its `p` |
| ---: | --- | ---: |
| 1–4 | a clean sweep | ≥ 0.0625 — cannot pass |
| 5 | 5W/0T/0L | 0.031 |
| 8 | 5W/3T/0L | 0.031 |

This is an *eligibility* floor — the minimum evidence a verdict may rest on —
not a guarantee of adequate power for a realistic effect, which needs
considerably more. Below it, `eng/vally-adapter/adapt.mjs` marks the verdict
`underpowered` and the PR comment shows ⚠️: never a pass, never a regression.
This check makes that state un-shippable for *new* evals.

Raising an eval over the floor by adding scenarios is strictly better than
raising `runs`: five repeats of one scenario satisfy the arithmetic but provide
no cross-scenario evidence, so the skill is still only measured on one task.
Use `runs` where a scenario is genuinely expensive to add:

```yaml
defaults:
  runs: 3
```

`dotnet-skills.experiment.yaml` deliberately does not set `runs` in its
`overrides:` block. Precedence there is *CLI flags > experiment overrides > eval
defaults* and the merge is a plain spread, so an experiment-level `runs` does
not *default* anything — it overwrites every eval's own value and makes
per-eval trial counts impossible to express.

**Grandfathering.** `underpowered-allowlist.txt` carries the evals that predate
the floor. It is a debt ledger and it is shrink-only in the mechanical sense:
the gate errors on an entry that is stale, duplicated, or no longer needed, and
`--base-ref` (which CI passes on every pull request) rejects entries that are
*new* relative to the base branch. Without that second half, a PR could add a
below-floor eval and exempt it in the same change — the defect the floor exists
to prevent, relocated one file over. Renames are read from git, so moving a
grandfathered eval is not treated as growth. `agent.*` evals are exempt
outright: the experiment's `evals:` glob excludes them, so no verdict is ever
computed and the floor has nothing to protect.

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

The evals that are still below the five-trial floor of failing check 8, listed
from `underpowered-allowlist.txt` with their current
`scenarios × runs`. Their verdicts are reported as ⚠️ underpowered rather than
as a pass or a failure, so raising them is the highest-value eval work
available. See check 8 for how, and for why the floor sits at five.

### Orphaned fixtures

A fixture directory that is committed but that no stimulus references. Usually
means a scenario was planned and dropped, so the coverage it was built for is
being paid for in repo size but never exercised. Wiring these up is the cheapest
way to raise an eval's trial count, because the fixture already exists —
`migrate-nullable-references` sits at 3 scenarios with three unreferenced
fixtures beside it.

### Skills with no eval

A skill that ships with `SKILL.md` but has no `tests/<plugin>/<skill>/eval.yaml`
carries zero evidence of impact.

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
