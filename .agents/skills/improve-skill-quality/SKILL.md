---
name: improve-skill-quality
description: Diagnoses and fixes skills in the dotnet/skills repository that lose to their own baseline, fail to activate, time out, or return "no credible improvement". Use when an evaluation verdict is a regression or underpowered, when a skill regressed after a change, when /evaluate reports no results, or when deciding whether a weak skill should be strengthened or retired. Do not use for scaffolding a brand-new skill (use create-skill) or a brand-new eval (use create-skill-test).
---

# Improve Skill Quality

Turn a failing or unconvincing evaluation into a targeted fix. The single most common mistake
in this repo is rewriting skill prose in response to a verdict whose real cause was the eval,
the fixtures, or the harness. Classify first, then fix.

## When to Use

- An evaluation verdict is a regression, underpowered, or "no credible improvement".
- A skill wins in the isolated arm but not in the plugin arm, or is reported "not activated".
- `/evaluate` reports "Evaluation ran but produced no results".
- A skill scores well but costs too much (tokens, turns, wall time, plugin menu budget).
- Deciding whether to strengthen or retire a persistently weak skill.

## When Not to Use

- Creating a new skill from scratch — use `create-skill`.
- Creating a new `eval.yaml` from scratch — use `create-skill-test`.
- Changing the harness itself (`eng/skill-validator`, `eng/vally-adapter`, `evaluation*.yml`).

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Verdict evidence | Yes | The `/evaluate` PR comment, or `results.json` from the run artifacts |
| Losing trial transcripts | Yes for content fixes | Baseline vs. skilled output plus the judge's stated reason |
| W/T/L record and trial count | Yes | Distinguishes a real regression from an underpowered eval |
| Activation status per arm | Yes | Isolated and plugin activation are different failures |

## Workflow

### Step 1: Get the evidence before forming a hypothesis

Read [InvestigatingResults.md](../../../eng/vally-adapter/InvestigatingResults.md) for how to
download artifacts and read `results.json`. Extract, per failing stimulus:

- win / tie / loss record and total trials (`trials = stimuli × runs`)
- activation status in the **isolated** and **plugin** arms, separately
- the judge's verbatim reason on each losing trial
- whether any trial errored, timed out, or produced empty output

Do not change skill content until you can quote a losing trial and the judge's reason for it. For the
other cause classes the evidence is different: harness failures are diagnosed from the job log and
the spec, and power problems from the trial record — neither has a losing trial to quote, and
demanding one is what sends people rewriting prose instead.

### Step 2: Classify the failure

Work down this table and stop at the first row that matches. Rows are ordered by how often the
symptom has been misdiagnosed as a skill-content problem — the fixture row is first because a
fixture failure also presents as a setup or reliability failure and gets misfiled as one.

| Symptom | Real cause class | Go to |
|---------|------------------|-------|
| A fixture does not build, is untracked by git, breaks for the wrong reason, or contradicts itself | Fixture | Step 4 |
| No `results.json`, "produced no results", or the spec never loaded | Harness / spec-load | Step 3 |
| Trials errored, timed out, or returned empty output | Reliability | Step 3 |
| Trajectories unmatched, a trial errored, or the summary disagrees — verdict reported inconclusive | Reliability (not power) | Step 3 |
| Positive record (e.g. 16W/8T/1L), comparison conclusive, verdict still not a pass | Statistical power | Step 5 |
| Skilled arm equals baseline arm by construction | Eval design | Step 6 |
| Activated and lost on quality, judge names a concrete defect | Skill content | Step 7 |
| Activated in isolation, not in plugin | Activation / routing | Step 8 |
| Not activated in either arm | Frontmatter description | Step 8 |
| Wins but costs far more than baseline | Scope and cost | Step 7 |

A verdict is only a *measured* result when the comparison was conclusive: `adapt.mjs` requires zero
errored trials, zero unmatched trajectories, and an agreeing summary before it will report a pass or
a regression. Confirm that before reading a record as a power problem.

### Step 3: Rule out harness and reliability causes

See [references/eval-triage.md](references/eval-triage.md) for the full catalogue. The recurring ones:

- A spec declaring both `config:` and `defaults:` is rejected by vally, the job still exits 0, and
  the PR comment blames "transient infrastructure". Merge them into one `defaults:` block.
- An errored trial is not automatically a fixture problem — judge-side auth and `session.idle`
  failures look identical from the verdict and need harness fixes, not SDK pins.
- `expect_tools: [bash]` on an advisory question forces a restore or build and turns an answer into
  a timeout with no quality gain.
- Genuine code-generation stimuli need roughly 360s; a timeout yields empty output, which fails
  every grader and hides the real quality signal.
- Unmatched trajectories, an errored trial, or a summary that disagrees make the comparison
  **inconclusive**: the remaining matched trials are biased, so the record is not a measured null
  and must not be read as a power or content problem.

### Step 4: Verify the fixtures before touching the skill

Run `python eng/eval-quality/check_eval_quality.py` — it blocks ten defect classes that each already
cost a real result here. Then confirm by hand:

- every fixture behaves as its stimulus assumes — a fixture meant to be healthy builds, and one
  meant to be broken fails for the exact reason the stimulus is about and no other;
- every referenced fixture is in the git index (`git ls-files`), not merely on disk — `.gitignore`
  has silently swallowed committed coverage fixtures;
- a fixture never states the same fact in two places that disagree — a Cobertura report whose
  declared `line-rate`, summary totals and `<line>` elements differ is the canonical case — or the
  two arms legitimately read different truths.

### Step 5: Check whether the eval could ever have passed

The gate has two independent bars, and confusing them is the usual misdiagnosis:

1. **Counted trials ≥ 5** (`trials = stimuli × runs`). Below that the verdict is reported
   `underpowered` — never a pass, never a regression.
2. **The sign test must reach p ≤ 0.05 over the *discordant* (non-tie) trials.** Ties are not
   discarded silently; they hold the discordant count down.

| discordant trials | records that pass | p |
|---:|---|---:|
| ≤ 4 | none, however good the skill | ≥ 0.0625 |
| 5–7 | zero losses only (5W/0L) | 0.031 |
| 8 | one loss survivable (7W/1L) | 0.035 |

So at exactly 5 counted trials a single tie is fatal — it leaves 4 discordant. At 6 counted trials
one tie is survivable (5W/1T/0L); at 7, up to two are (5W/2T/0L). A loss is not.

So a positive record with a failing verdict is a power problem, not a content problem. Fix it by
adding **discriminating stimuli** (cross-task evidence) rather than raising `runs` (repetition
only) — except where each stimulus drives an expensive pipeline. Record the reasoning in a comment
above `defaults:`, as `tests/dotnet-test/grade-tests/eval.yaml` does.

### Step 6: Check whether the two arms differ at all

An eval that compares the skill against itself measures judge noise:

- A dormancy guard (`expect_activation: false`) must **not** also set `constraints.reject_skills`.
  That makes the skilled arm skill-free, i.e. identical to baseline. Across four evals the same
  guard scored −0.4, +0.4, +0.4 and 0, twice costing a skill its pass.
- A skill with `disable-model-invocation: true` cannot self-activate, so an eval graded on
  activation compares two identical arms. Cover it through a consumer skill, or grade the answer
  content instead, as `tests/dotnet-test/filter-syntax/eval.yaml` and
  `tests/dotnet-test/platform-detection/eval.yaml` do.
- A grader whose `config` is missing its required key enforces nothing, so the stimulus has one
  fewer assertion than it appears to.

### Step 7: Fix skill content against the losing trial

Only now change the skill. Apply the patterns in
[references/writing-for-baseline-delta.md](references/writing-for-baseline-delta.md); the ones that
most often flip a loss:

- Replace reference prose the model already knows with decisions it would otherwise get wrong.
- Add stop-conditions so a strong skill does not over-apply — but do not over-correct into
  answering more narrowly than the baseline did.
- Scale output structure to input size; a dashboard for an 8-test suite loses to a direct answer.
- Require truthful validation reporting; claiming "Build succeeded" after a failed restore is an
  automatic loss.
- Verify load-bearing API claims by compiling or probing, not by reading source.
- For cost regressions, gate rare or expensive paths behind `references/` reads and size any
  orchestration to the user's scope.

### Step 8: Fix activation

Activation failures are frontmatter and routing failures, not body failures. See
[references/eval-triage.md](references/eval-triage.md). Summary:

| Failure | Fix |
|---------|-----|
| Not activated in any arm | Put the user's own words in `description`: symptoms, error codes, artifact names, quoted requests |
| A sibling skill wins the prompt | Claim the exact ambiguous words in `description`, and add matching exclusions on **both** siblings |
| Model answers with no skill at all | Raise the stakes in the description, de-crowd the plugin menu, verify with the plugin arm |
| Boundary excludes real scenarios | Re-read every "do not use for" clause against every eval prompt and real workflow phase |
| Description at the 1,024-char ceiling | Cut restated body content, not trigger phrases; check the plugin menu budget too |

### Step 9: Re-validate

```bash
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- check --plugin ./plugins/<plugin>
python eng/eval-quality/check_eval_quality.py
./eng/run-skill-evals.sh <plugin> <skill>
```

Then request the official run by submitting a PR review containing `/evaluate` (Files changed →
Review changes), which binds the run to the reviewed commit. Before declaring a regression on the
result, confirm the skill payload actually changed — reruns on byte-identical content have shifted
7W/2T/2L to 4W/5T/2L.

## Validation

- [ ] For a content fix, a losing trial and the judge's stated reason are quoted in the PR description.
- [ ] The failure was classified before any content was edited.
- [ ] `check_eval_quality.py` and `skill-validator check` both pass.
- [ ] Trial count clears the power bar for the observed tie rate, not just the floor of 5.
- [ ] Isolated **and** plugin activation are both reported.
- [ ] The PR body records root cause, fix, and validation so the lesson is reusable.

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Rewriting skill prose in response to an underpowered verdict | Underpowered means too few discordant trials; add discriminating stimuli instead |
| Adding `defaults: runs:` to a spec that already has `config:` | Merge into a single `defaults:` block; vally rejects specs with both |
| Padding `runs` to clear the trial floor | Five repeats of one stimulus measure one task; add stimuli |
| Treating an errored trial as fixture nondeterminism | Read the stderr first; judge-side auth failures need harness fixes |
| Fixing a "wrong" answer that the fixture actually made wrong | Check fixture self-consistency before blaming the response |
| Strengthening a skill nobody uses and nothing passes | Weak eval signal plus thin telemetry is a valid retirement case |
| Landing a fix without re-running | Verify the invoked payload contains the fix; judge noise is real |

## References

- [references/writing-for-baseline-delta.md](references/writing-for-baseline-delta.md) — content patterns that beat the unskilled model
- [references/eval-triage.md](references/eval-triage.md) — symptom, cause and fix catalogue with PR citations
- [eng/eval-quality/README.md](../../../eng/eval-quality/README.md) — the ten structural gate checks and why each exists
- [eng/vally-adapter/InvestigatingResults.md](../../../eng/vally-adapter/InvestigatingResults.md) — downloading artifacts and reading `results.json`. This is the current guide; the similarly-named `eng/skill-validator/src/docs/InvestigatingResults.md` documents the retired `skill-validator evaluate` schema and does not describe today's results.
