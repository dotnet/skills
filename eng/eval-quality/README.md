# Eval quality gate

`check_eval_quality.py` blocks defect classes that have each already cost a real
evaluation result on this repo. Every one of them was invisible to the existing
checks: the eval specs parsed, `skill-validator` passed, and the damage only
showed up as a skill mysteriously losing to its own baseline.

Run it from the repository root:

```bash
python eng/eval-quality/check_eval_quality.py          # what CI runs
python eng/eval-quality/check_eval_quality.py --strict # also fail on warnings
python eng/eval-quality/selftest_eval_quality.py       # prove the gate still fires
```

## Failing checks

All seven are **structural** — they inspect file existence, git state, declared
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

## Warnings (reported; failing only under `--strict`)

CI runs the gate without `--strict`, so these are informational there. Passing
`--strict` returns exit code 1 when any warning is present.

### Statistical power

`dotnet-skills.experiment.yaml` sets `runs: 1`, so `n` is the scenario count and
one judge call decides each scenario. The pass gate is `mean > 0 ∧ ci_low > 0`,
i.e.

```
sqrt(n) × (mean / sd) > t(n-1)
```

| n | required mean/sd |
| ---: | ---: |
| 1 | undefined — a single trial decides |
| 2 | 8.98 |
| 3 | 2.48 |
| 4 | 1.59 |
| 6 | 1.05 |
| 8 | 0.84 |

These are `t(n-1)/sqrt(n)`. An earlier revision of this table read the critical
value at `t(n)` instead, understating every row — n=3 appeared to need 1.84 when
it really needs 2.48, and n=2 appeared to need 3.04 against a true 8.98. That
made a thin eval look one good trial away from credible when it was not. The
worked example below is the check: `coverage-analysis` scoring 0.4/1.0/0.4 has
mean/sd = 1.73, which reads as a near miss against the old 1.84 but is 30% short
of the real 2.48 — a difference that changes the remedy from "re-run it" to
"raise n or runs".

Consequences seen in practice: `coverage-analysis` **won 100% of its trials in
four consecutive runs and failed all four**; `migrate-static-to-wrapper` missed
by 0.4 of a percentage point at 4W/1T/0L. Neither is a content problem.

Roughly half of the repo's skill evals sit at n ≤ 3. Raising `runs` is the
durable fix (`runs: 3` turns 3 scenarios into 9 trials and drops the required
ratio to 0.77) at a proportional increase in CI cost — a maintainer decision,
which is why this is a warning and not a failure.

### Orphaned fixtures

A fixture directory that is committed but that no stimulus references. Usually
means a scenario was planned and dropped, so the coverage it was built for is
being paid for in repo size but never exercised. Wiring these up is the cheapest
way to raise `n`.

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
