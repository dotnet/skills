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

All four are **structural** — they inspect file existence, git state, or YAML
keys. None of them interprets prose, so they cannot fire spuriously on a
well-written eval.

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

### 4. Dormancy guard that also sets `reject_skills`

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

## Warnings (reported, never failing)

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
| 2 | 3.04 |
| 3 | 1.84 |
| 4 | 1.39 |
| 6 | 1.00 |
| 8 | 0.82 |

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
