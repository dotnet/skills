# Investigating Evaluation Results (Vally)

This guide is for AI agents (and humans) investigating non-passing, invalid, or warning-bearing skill evaluation results produced by the **Vally** harness via `eng/vally-adapter/adapt.mjs`. It documents the `results.json` schema, how to reach the raw Vally output, common result patterns, and recommended fixes.

For the end-to-end architecture, decision policy, metric definitions, and
historical examples, start with the
[Skill evaluation infrastructure overview](./README.md).

Evaluations run through Vally (`@microsoft/vally-cli`): every skill's `tests/<plugin>/<skill>/eval.yaml` is run in up to three variants — **baseline** (no skills), **skilled** (only the skill under test), and **plugin** (the whole plugin loaded). The workflow records the exact expected-eval manifest before execution. The adapter then runs `vally compare` (a debiased, position-swapped head-to-head judgment of skilled vs baseline) and writes one `results.json` per expected skill, including an explicit invalid result when required evidence is missing.

> Note: the linter (`skill-validator check`) is a **separate** workflow (`skill-check.yml`) and is unrelated to these eval results.

## Using this guide with an AI agent

When an evaluation has a non-pass or warning, the PR comment includes a ready-to-use prompt. Copy it to your AI agent. The agent downloads the artifacts, reads this guide, analyzes the `results.json` files, and suggests fixes.

## Quick start

1. **Download the results artifacts:** `gh run download <run-id> --repo dotnet/skills --pattern "vally-results-*" --dir ./eval-results`
2. **Skim the run's step summary** (the "Full Results" link) for the complete metrics and scenario tables.
3. **Read `adapter-summary.json` and each `results.json`** (`eval-results/vally-results-*/<plugin>/<skill>/results.json`). The summary proves expected-versus-produced accounting; each skill file gives the compare state and evidence.
4. **Identify the result pattern** using the categories below and fix in priority order: invalid accounting or judge evidence → timeouts → activation → underpowered design → quality/preference.
5. **Apply the fix**, push it, and evaluate that exact commit. Submit a PR review containing `/evaluate` (recommended), or comment `/evaluate <new-head-sha>` in the PR conversation.

> The `--pattern "vally-results-*"` flag matters — without it, `gh` also tries to download non-zip artifacts and exits non-zero.

## The PR comment

`eng/vally-adapter/consolidate.mjs` renders the comment and the fuller step summary. The PR comment starts with:

- the number of unique skills, execution models, and model/skill results;
- the exact evaluated commit and judge model;
- expected / observed / written result accounting, with missing, unexpected,
  invalid, recovered, and unresolved counts; and
- an explicit notice that the objective completion regression gate is not
  enabled.

Its compact table has these columns:

| Column | Meaning |
|--------|---------|
| `Skill` | Skill under test |
| `Model` | Model used for the baseline and skilled agent runs. This prevents duplicate skill rows from being ambiguous |
| `Verdict` | ✅ Improved / ➖ Not proven improved / 📉 Preference loss (report only) / ⚠️ Invalid or underpowered / 🔻 Objective regression when that future gate is enabled |
| `Gate evidence` | `n` distinct-stimulus votes, stimulus W/T/L, `d` discordant votes, exact one-sided `p`, and net win. A pass needs `p ≤ 0.05` and net win ≥20% |
| `Overfit` | Overfitting-judge severity — ✅ Low, 🟡 Moderate, 🔴 High, — none — with its score |
| `Warnings` | Activation gaps, timeouts, recovered judge slots, and unresolved comparison errors |
| `Next action` | A cause-specific repair step. It does not recommend more repeated runs as a power fix |

A collapsible **How to read this report** block follows the table. The PR
comment includes details only for non-passing, invalid, or warning-bearing
results. Each block says why the result did not pass, gives the next repair
action, names weak or warning scenarios, includes one clearly labeled
illustrative judge excerpt when available, and separates repeated-run
reliability from stimulus-vote gate evidence.

`--format full` (the workflow summary) keeps every result and adds `Δ Pref`,
isolated/plugin quality, and baseline quality. These are triage metrics. They
are not the gate. The `p` value applies to one model/skill result; the renderer
does not apply a matrix-wide multiple-comparison correction.

### Reading the evaluation dashboard

The Skills Evaluation Dashboard preserves the same distinction. Its **Latest
Verdict Evidence** table shows the latest retained result per executor model:
the authoritative distinct-stimulus W/T/L vote, discordant count, exact
one-sided sign-test p-value, and net win. The table also separates expected
dormancy (`expect_activation: false`) and non-model-invocable reference skills
from missing or unexpected activation, and exposes compact paired-judge
excerpts plus source links when the result contains them.
Plugin-arm activation is labeled as aggregate plugin activity because the
current adapter does not identify which loaded plugin skill emitted that event;
only the isolated arm proves activation of the target skill.

The 0–10 **Quality Score Triage** summary and trend charts remain useful for
spotting changes in absolute grader scores. They do not decide pass/fail. Older
dashboard history predates the additive `verdictEvidence` payload, so the UI
labels authoritative evidence unavailable for those runs instead of deriving a
verdict from score averages.

## Understanding `results.json`

Each file has a top-level object:

| Field | Description |
|-------|-------------|
| `schemaVersion` | Adapter schema version. Version 2 adds explicit states; version 3 makes stimulus votes authoritative and separates repeated-run evidence |
| `evalFile` / `expectedEval` | Normalized eval path and whether it was in the pre-run manifest |
| `model` | Model used for agent runs |
| `judgeModel` | Model used by `vally compare` |
| `timestamp` | When results were written (UTC) |
| `verdicts[]` | Per-skill results (one entry, since the adapter writes one file per skill) |

### Verdict structure

A verdict carries **both** the head-to-head preference and absolute per-role data. `state` is authoritative. Boolean fields remain for compatibility with older consumers.

| Field | Description |
|-------|-------------|
| `skillName` / `skillPath` | The skill under test |
| `state` | One of `VALID_PASS`, `VALID_REGRESSION`, `VALID_NO_CHANGE`, or `INVALID_INCONCLUSIVE` |
| `stateReason` | Machine-readable `{ code, phase }`. Use this field for automation; do not parse `reason` |
| `passed` | **The gate.** `true` only when `conclusive`, at least 5 distinct stimuli were counted, `signTest.pValue <= 0.05`, and `netWin >= 0.20` |
| `netWin` | `(wins − losses) / stimulus votes` — the effect size the gate reads. Magnitude-free, so an identical stimulus W/T/L record always yields an identical verdict |
| `practicalSignificance` | `{ netWin, minimum, passed }`. The absolute directional effect must reach 20%; this blocks sparse records such as `5W/95T/0L` |
| `signTest` | `{ wins, ties, losses, discordant, direction, pValue, alpha }` — exact one-sided binomial tail over discordant stimulus votes. **This is what decides.** Ties cannot support a win, so they hold `discordant` down |
| `regressed` / `preferenceRegressed` | Compatibility and explicit fields for a credible LLM preference loss. In the current schema version 3 this maps to `VALID_NO_CHANGE`, not `VALID_REGRESSION`, because ordinal LLM preference is not objective completion evidence. Renderers apply the same report-only meaning to legacy records that have `regressed: true` but no `state` |
| `conclusive` | `false` when the comparison did not complete: errored runs, unmatched trajectories, or a summary that disagrees with its own `stimuli[].trials` |
| `underpowered` | `true` only when a completed, `conclusive: true` comparison counted fewer than `minCredibleStimuli` distinct stimuli, so no record could have reached `p <= 0.05`. Rendered ⚠️ — never a pass, never a regression. This is separate from the `conclusive: false` error path |
| `minCredibleStimuli` | The distinct-stimulus floor in force (5). See `eng/eval-quality/README.md` for why |
| `minCredibleTrials` | Compatibility alias for `minCredibleStimuli` |
| `meanScore` | Vally's magnitude-weighted mean preference (`much-better` ±1.0, `slightly-better` ±0.4), −1..1. **Triage only — not the gate**; weighting the statistic by magnitude is what made verdicts flip in dotnet/skills#952 |
| `confidenceInterval` | `{ low, high, level: 0.95 }` — the 95% CI on `meanScore`, reported alongside it |
| `winRate`, `wins`, `ties`, `losses` | Authoritative stimulus-vote tally |
| `stimulusVoteCount` | Number of distinct stimuli that supplied a vote |
| `trialCount` | Compatibility alias for `stimulusVoteCount`; it no longer means pooled runs in schema version 3 |
| `erroredCount` | Raw comparison-judge runs that errored. Any unresolved error makes the verdict inconclusive |
| `comparisonTrialEvidence` | Pooled paired-run W/T/L, marked `gateEligible: false`; use it for reliability, not task breadth |
| `comparisonAttempts` | Retry telemetry. Successful first-attempt slots are frozen; only errored slots can be filled by attempt 2 |
| `errors[]` / `recoveredErrors[]` | Structured unresolved and recovered comparison failures, with phase, code, stimulus, trial, and attempt provenance |
| `scenarioEvidence` | One effective vote per stimulus after repeated runs are collapsed. Authoritative (`gateEligible: true`) |
| `completionTransitions` | Baseline/treatment aggregate pass transitions. Report-only because Vally aggregate pass can include LLM grading |
| `reason` | Human-readable summary of the above |
| `scenarios[]` | Per-scenario detail (below) |

### Scenario structure

Each scenario merges the compare preference for that stimulus with the absolute per-role runs.

| Field | Description |
|-------|-------------|
| `scenarioName` | The stimulus name from the eval spec |
| `meanScore` / `trials[]` | Compare preference for this stimulus and its per-trial `{ winner, magnitude, score, evidence, errored }` |
| `expectActivation` | Whether the target should activate; `false` marks an expected-dormancy stimulus |
| `timedOut` | Whether the skilled run hit its timeout |
| `skillActivationIsolated.activated` | Did the skill activate in the skilled (isolated) run? |
| `skillActivationPlugin.activated` | Whether any skill activity was observed in the whole-plugin run; the current adapter does not retain the emitting skill identity (present only when a plugin variant ran) |
| `baseline` | `{ judgeResult: { overallScore }, metrics }` — the skill-free control (`overallScore` is 0–5) |
| `skilledIsolated` | Same shape, for the isolated skilled run |
| `skilledPlugin` | Same shape, for the whole-plugin run (may be absent) |

`metrics` on each role: `{ wallTimeMs, tokenEstimate, inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens }`.

### Adapter summary

`adapter-summary.json` is the result-set accounting record. It contains
`expectedEvalCount`, `observedEvalCount`, `writtenResultCount`, `missingEvals`,
`unexpectedEvals`, `invalidEvals`, and `measurementInvalidEvals`.
`measurementInvalidEvals` is the fail-closed subset: missing baseline or skilled
records, unresolved judge or pairing failures, malformed reports, and other
adapter failures. It excludes only the explicit `underpowered` eval-design
state. The workflow requires this list to be empty and also checks that the
number of primary result files equals the exact pre-run manifest count. A
missing or invalid measurement cannot disappear while unrelated results make
the job look complete.

## Reaching the raw Vally output

The adapter's `results.json` is a summary. The uploaded artifact also contains the full Vally run under `artifacts/TestResults/vally/<entry>/`:

- `_experiment/<timestamp>/<variant>/results.jsonl` — one `trial-result` record per stimulus per variant, each with the full `trajectory` (`endReason`, `metrics.tokenUsage`, `metrics.skillActivationCount`, `toolCallCount`) and `gradeResult.score` (0–1).
- `_experiment/<timestamp>/executor-session-logs/**/{metadata.json,events.jsonl}` — the per-session event stream (prompts, tool calls, agent output). `metadata.json` carries `variant`, `stimulusName`, `evalName`/`evalFilePath`, `model`, and `status`. This is what powers the AGENTVIZ replay link in the PR comment.

To see exactly what the agent did for a failing scenario, open its `events.jsonl` (match on `variant` + `stimulusName` in the sibling `metadata.json`).

## Result patterns and fixes

Work top-down; earlier categories often cause later ones.

### 1. Errored or missing trials (`state == "INVALID_INCONCLUSIVE"`)
The agent crashed, the model was unavailable, evidence was missing, or the comparison judge failed. Check `stateReason`, `errors[]`, `adapter-summary.json`, and the variant's `results.jsonl`/session logs. These are invalid measurements, not skill regressions. If a required variant produced no records, the adapter writes an explicit invalid result with `missing_baseline_records` or `missing_skilled_records`.

If Vally writes a JSON record that cannot satisfy the comparison schema, the
adapter emits `comparison_report_invalid` for that eval and continues the batch.
This preserves exact result accounting without treating malformed evidence as a
quality result.

For comparison-judge failures, inspect `errors[].code`. Known codes include
`judge_session_idle_timeout`, `judge_organization_disabled`,
`judge_rate_limited`, and `judge_service_error`. The adapter makes one targeted retry. It keeps every
successful first-attempt judgment fixed and replaces only errored slots. A
recovered transient appears in `recoveredErrors[]`; an unresolved failure stays
in `errors[]` and makes the state invalid.

### 2. Timeouts (`scenario.timedOut == true`, `trajectory.endReason == "agent_timeout"`)
The agent didn't finish within the eval's `config.timeout`. Either the task is too large for the budget or the skill sent the agent down a slow path. Fixes: raise `config.timeout` in `eval.yaml` if the task legitimately needs more time, or tighten the skill so it converges faster.

### 3. Skill didn't activate (`skillActivationIsolated.activated == false`)
The skill was available but the agent never invoked it, so "skilled" ≈ "baseline" and no improvement is possible. Fixes: sharpen the skill's `description`/trigger phrasing in `SKILL.md` so the model recognizes when to use it, and make sure the eval prompt actually describes a task the skill targets.

### 4. Underpowered eval (`underpowered == true`)
Not a skill problem — an eval problem. This is `INVALID_INCONCLUSIVE` with `stateReason.code == "underpowered"`. The gate gives each distinct stimulus one vote. Repeated runs collapse by majority direction and remain available as reliability evidence. The exact one-sided sign test cannot reach `p ≤ 0.05` on fewer than five discordant stimulus votes (`0.5⁴ = 0.0625`), so below `minCredibleStimuli` (5) **no possible record passes**, however good the skill is.

Do not "fix" the skill or raise `defaults.runs` in response to this. Add independent, discriminating stimuli. Vally defines stimuli as test cases and uses runs for pass rate, pass@k, pass^k, and flakiness. Its scoring guide recommends 3 runs for CI and 5–10 for nightly reliability measurement, but does not prescribe a distinct-stimulus count or sign-test alpha. `eng/eval-quality/check_eval_quality.py` fails any new eval below the five-stimulus floor and tracks grandfathered debt in `eng/eval-quality/underpowered-allowlist.txt`.

Clearing the floor is necessary, not sufficient. The sign test conditions on **discordant** (non-tie) stimulus votes, so an eval at exactly 5 stimuli only passes on a flawless 5W/0T/0L sweep. One tie leaves 4 discordant votes. Check `signTest.discordant`, not raw run volume, when a record with more wins than losses still fails.

### 5. No credible or practical net win
The judge didn't consistently prefer the skilled run over baseline.
- **`netWin <= 0`** — at least as many losses as wins. Either the skill isn't helping for these scenarios, or the baseline model is already strong here. If `preferenceRegressed` is `true`, the LLM judge credibly preferred baseline. This is report-only preference evidence, not an objective completion regression.
- **`netWin > 0` but `signTest.pValue > 0.05`** — a real but inconsistent signal: the skill wins some stimuli and ties or loses others. Ties hold the discordant vote count down. Add broader stimuli and make the skill help consistently.
- **`signTest.pValue <= 0.05` but `practicalSignificance.passed == false`** — the direction is statistically credible but too sparse to matter across tested tasks. For example, 100 distinct stimuli with `5W/95T/0L` have `p=0.03125` but only a 5% net win. Add discriminating stimuli or improve the skill.
- Do **not** read `meanScore` here. It is magnitude-weighted and reported for triage only; a verdict never turns on it (see `eng/eval-quality/README.md`, "Why the gate scores direction, not magnitude").
- Inspect `scenarios[].trials[].evidence` for the judge's reasoning on losses/ties, and compare the skilled vs baseline `events.jsonl` to see what the skill changed (or failed to change).

### 6. Completion-transition telemetry

`completionTransitions` counts aggregate `baselinePassed` and
`treatmentPassed` transitions from Vally compare. It is not a hard gate:
Vally's aggregate pass can include LLM grader output, so it is not an objective
task-completion primitive. Do not infer an objective regression from
`completionTransitions.baselineOnly`.

The required objective primitive is tri-state per
`(eval, stimulus, trialIndex, arm)`: `true` only when all explicitly marked,
allowlisted deterministic completion graders pass; `false` when one explicitly
fails and none is missing or errored; otherwise `unknown`. It must use raw
per-grader details tied to explicit unique declarations in the parsed eval spec,
never aggregate pass, weights, thresholds, LLM graders, or human graders. One baseline-only
transition is only a candidate. `VALID_REGRESSION` additionally requires
conclusive paired confirmation at `p <= 0.05`, at least a 20% objective net
loss, and correction across multiple tested completion scenarios.

Official Vally `GraderResult` records expose broad `kind` taxonomy, not the
eval spec's grader `type`; `kind: "code"` does not prove deterministic
task-completion semantics. Evals also do not yet declare which graders are
completion invariants, and compare JSONL exposes only aggregate booleans.
Therefore the state remains reserved and the aggregate transition remains
report-only.

### Comparison slot identity

Comparison trials use `(stimulusName, trialIndex)` as the retry slot identity.
Repeated compare calls over the same persisted inputs must produce the same
indices. The adapter rejects missing or duplicate identities instead of pairing
trials by array position. Treat the index as scoped to one persisted experiment,
not as a durable ID across regenerated runs.

If compare writes a structured report but exits nonzero, the adapter still
reads the report so it can classify and retry errored slots. A nonzero exit with
no report remains an invocation failure.

### 7. Quality looks fine but the skill still fails the gate
The gate is a **preference** comparison, not an absolute score. A high `skilledIsolated.judgeResult.overallScore` that isn't clearly better than `baseline.judgeResult.overallScore` will not pass. Focus on the *delta* over baseline, not the absolute number.

## Re-running

Push the fix, then bind the new run to its exact commit:

1. **Recommended:** open **Files changed → Review changes**, enter `/evaluate`,
   and submit the review. GitHub supplies the reviewed commit ID.
2. **PR conversation:** comment `/evaluate <new-head-sha>`. A bare conversation
   comment does not run an evaluation because `issue_comment` has no trusted
   commit identity.

For a transient retry without a code change, use the exact SHA printed in the
result comment. The workflow regenerates the verdicts and updates the PR
comment.
