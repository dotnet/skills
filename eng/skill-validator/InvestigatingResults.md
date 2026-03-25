# Investigating Evaluation Results

This guide is intended primarily for AI agents investigating skill evaluation failures, though humans will find it useful too. It documents the `results.json` schema, common failure patterns, and recommended fixes.

## Using this guide with an AI agent

This document is designed to be read by AI coding agents. When a skill evaluation has failures, the PR comment includes a ready-to-use prompt — just copy and paste it to your AI agent. The agent will download the artifacts, read this guide, analyze the results, and suggest fixes.

If you need to run the investigation manually, follow the [Quick start](#quick-start) below.

## Quick start

1. **Download the results artifact:** `gh run download <run-id> --repo dotnet/skills --dir <path>`
2. **Read `summary.md` first** for a quick overview of which scenarios passed/failed
3. **Read `results.json`** for the full metrics, agent output, assertions, and judge reasoning
4. **Identify the failure pattern** using the categories below — most failures match multiple patterns; fix them in priority order (timeouts first, then activation, then quality/rubric issues)
5. **Apply the fix** and re-run with `/evaluate`

## Finding the artifacts

### Via CLI (recommended for AI agents)

Extract the workflow run ID from the **Full results** link in the PR eval comment (e.g., `https://github.com/dotnet/skills/actions/runs/23520818616` → `23520818616`), then:

```bash
gh run download <run-id> --repo dotnet/skills --dir /tmp/eval-results
```

This downloads all artifacts into subdirectories, each containing `results.json` and `summary.md`.

### Via browser

From the PR comment, click the **Full results** link to open the GitHub Actions workflow run. Then:

1. Click on any job (e.g., `evaluate (mcp-csharp-debug)`)
2. Expand the **Upload results** step
3. Find the `Artifact download URL` in the log output
4. Download and extract

Alternatively, scroll to the bottom of the workflow run summary page and download from the **Artifacts** section.

## Understanding `results.json`

Each file contains a top-level object with:

| Field | Description |
|-------|-------------|
| `model` | Model used for agent runs |
| `judgeModel` | Model used for judging |
| `timestamp` | When the results were written (UTC) |
| `verdicts[]` | Array of per-skill results |

### Verdict structure

Each verdict contains:

| Field | Description |
|-------|-------------|
| `skillName` | Name of the skill being evaluated |
| `passed` | Overall pass/fail |
| `scenarios[]` | Array of per-scenario comparisons |
| `overfittingResult` | Overfitting analysis (if enabled) |

### Scenario structure

Each scenario includes two required runs (baseline + isolated). It may also include an optional plugin run, and their comparison:

| Field | Description |
|-------|-------------|
| `scenarioName` | Human-readable scenario name |
| `baseline` | Run without the skill |
| `skilledIsolated` | Run with only this skill loaded |
| `skilledPlugin` | Optional run with the full plugin loaded (may be null when plugin runs are disabled) |
| `timedOut` | Whether any run hit the timeout |
| `isolatedImprovementScore` | Weighted improvement (isolated vs baseline) |
| `pluginImprovementScore` | Weighted improvement (plugin vs baseline); optional and only computed when a plugin run is present |
| `isolatedBreakdown` | Per-metric contribution to the score (see below) |
| `pluginBreakdown` | Per-metric contribution to the score (see below); optional and only populated when a plugin run is present |
| `pairwiseResult` | Judge's rubric-by-rubric comparison |
| `perRunScores` | Individual run scores (shows variance) |

### Breakdown fields

The `isolatedBreakdown` and `pluginBreakdown` objects show how each metric contributed to the improvement score. Each field is a raw delta (not yet weighted). The final score is computed as a weighted sum:

| Field | Weight | Range | Meaning |
|-------|--------|-------|---------|
| `qualityImprovement` | 0.40 | [-1, 1] | Rubric-based quality delta |
| `overallJudgmentImprovement` | 0.30 | [-1, 1] | Holistic judge assessment delta |
| `taskCompletionImprovement` | 0.15 | {-1, 0, 1} | Did assertions pass? |
| `tokenReduction` | 0.05 | [-1, 1] | Positive = fewer tokens (more efficient) |
| `errorReduction` | 0.05 | [-1, 1] | Positive = fewer errors |
| `toolCallReduction` | 0.025 | [-1, 1] | Positive = fewer tool calls |
| `timeReduction` | 0.025 | [-1, 1] | Positive = faster |

A `tokenReduction` of -1.0 means the skilled run used ≥2× the baseline's tokens. This is common when a skill is loaded (the skill content itself consumes tokens) but is only -0.05 in the final score, so it rarely causes failure on its own.

### Run metrics

Each of `baseline`, `skilledIsolated`, and `skilledPlugin` contains a `metrics` object:

| Field | Description |
|-------|-------------|
| `timedOut` | Whether this run hit the timeout |
| `wallTimeMs` | Total wall-clock time |
| `taskCompleted` | Whether assertions passed |
| `tokenEstimate` | Total tokens used |
| `turnCount` | Number of agent turns |
| `toolCallCount` | Number of tool calls |
| `toolCallBreakdown` | Tool call counts by tool name |
| `errorCount` | Number of errors during the run |
| `assertionResults[]` | Per-assertion pass/fail with messages |
| `agentOutput` | The agent's final text output |

## Common failure patterns

### 1. Timeout with empty output

**Symptoms:**
- `timedOut: true`
- `agentOutput` is empty or just `\n\n`
- All assertions fail
- `toolCallBreakdown` shows `bash` usage

**Cause:** The model spent its entire time budget running shell commands (e.g., `dotnet new`, `dotnet add package`, exploring NuGet contents) and never produced user-facing text.

**Fixes:**
- **Increase `timeout`** in `eval.yaml` — 180s is often not enough for scenarios that involve code generation. Try 360s.
- **Restructure the prompt** to discourage bash exploration (e.g., "Show me the code" rather than "Create a project")
- **Add `reject_tools: ["bash"]`** if the scenario should be answerable without shell commands

### 2. Baseline already bad

**Symptoms:**
- Baseline scores are very low (1.0–2.0/5)
- Skilled scores are also low
- Quality improvement shows 0 or negative

**Cause:** The question is too hard for the model even without the skill. The skill can't fix what the model can't do.

**Fixes:**
- Simplify the scenario prompt
- Verify the baseline is working by examining `baseline.metrics.agentOutput`
- Consider whether the scenario is testing the right thing

### 3. High variance across runs

**Symptoms:**
- `perRunScores` contains both positive and negative values (e.g., `[0.07, -0.85, 0.04]`)
- A spread greater than ~0.3 between min and max scores suggests problematic variance
- Results flip between passing and failing across eval runs
- Isolated and plugin scores disagree

**Cause:** LLM non-determinism. The model takes different strategies on different runs.

**Fixes:**
- **Increase `--runs`** for more statistical stability (5 is the default; consider 7–10 for noisy scenarios)
- **Tighten the prompt** to reduce the space of valid strategies
- **Add `setup.files`** to give the model concrete files to work with rather than letting it scaffold from scratch

### 4. Quality unchanged but weighted score negative

**Symptoms:**
- Footnote says "Quality unchanged but weighted score is -X% due to: judgment, tokens, tool calls"
- The skilled output is roughly as good as baseline

**Cause:** The skill adds token overhead (the skill content itself uses tokens) but doesn't improve quality enough to offset it.

**Fixes:**
- **Improve the skill content** to produce clearly better output for this scenario
- **Reduce skill size** — shorter skills have less token overhead
- **Check if the rubric matches** what the skill actually teaches

### 5. Skill not activated

**Symptoms:**
- Skills Loaded column shows `⚠️ NOT ACTIVATED`
- `skillActivationIsolated` and/or `skillActivationPlugin` fields in results.json show `activated: false` (or the legacy `skillActivation` alias)
- `detectedSkills` is empty or `skillEventCount` is 0
- The skilled run metrics look similar to baseline (the agent ran normally but without the skill's guidance)

**Cause:** The agent runtime didn't select the skill for this prompt. The skill's frontmatter `description` didn't match.

**Fixes:**
- Update the skill's `description` in SKILL.md frontmatter to better match the scenario prompt
- Make sure the description includes keywords from the scenario
- Check the scenario itself has sufficient information that the agent can reason that it needs the skill. (It should not cheat and suggest the skill.)

### 6. Rubric penalizes valid alternatives

**Symptoms:**
- Pairwise judge picks baseline over skill
- Both outputs are correct but use different approaches
- `pairwiseResult.rubricResults` shows the rubric criterion is too narrow

**Cause:** The rubric item favors one specific approach (e.g., step-by-step UI walkthrough) over an equally valid alternative (e.g., single CLI command).

**Fixes:**
- **Broaden the rubric** to explicitly accept multiple valid approaches
- Example: Instead of `"Shows step-by-step UI configuration"`, use `"Explains how to connect — either as a single CLI command or via the UI configuration"`

### 7. Judge regressions on close calls

**Symptoms:**
- `overallJudgmentImprovement` is -0.4 even though quality scores are similar
- Pairwise judge is inconsistent between position-swapped runs

**Cause:** When outputs are nearly equal, the judge's position bias can dominate. The position-swap mitigation defaults to "tie" on inconsistency, but the weighted scoring still penalizes.

**Fixes:**
- This is usually noise — re-run the eval to see if it persists
- If it consistently happens, improve the skill to produce clearly differentiated output

## When multiple patterns apply

Most failing scenarios match 2–3 patterns simultaneously (e.g., timeout + token overhead + high variance). Fix them in this priority order:

1. **Timeouts (#1)** — if the model can't finish, nothing else matters. Increase timeout first.
2. **Skill not activated (#5)** — if the skill never loaded, fix the description before tuning anything else.
3. **Baseline already bad (#2)** — if the baseline scores ≤2.0/5, the scenario may need simplification regardless of the skill.
4. **High variance (#3)** — if `perRunScores` are unstable, a single eval run is unreliable. Re-run before concluding the skill is broken.
5. **Rubric/judgment issues (#6, #7)** — once the runs are stable, tune the rubric.
6. **Token overhead (#4)** — only optimize if quality is already good but the weighted score is marginally negative.

## Analyzing results with an AI agent

The `results.json` file is designed to be machine-readable. An AI agent can:

1. **Parse the JSON** and extract metrics for each scenario
2. **Compare baseline vs skilled** metrics to identify regressions
3. **Read `agentOutput`** to see what the model actually produced
4. **Check `assertionResults`** to see which assertions failed
5. **Read `pairwiseResult.rubricResults`** for the judge's per-criterion reasoning
6. **Examine `perRunScores`** to assess variance
7. **Look at `toolCallBreakdown`** to understand what the model spent time on
8. **Cross-reference `isolatedBreakdown`** to see which metrics drove the score

### Example analysis script

```python
import json

def analyze(path):
    with open(path) as f:
        data = json.load(f)
    for verdict in data['verdicts']:
        for scenario in verdict['scenarios']:
            name = scenario['scenarioName']
            bl = scenario['baseline']['metrics']
            sk = scenario['skilledIsolated']['metrics']
            print(f"--- {name} ---")
            print(f"  Baseline: timedOut={bl['timedOut']}, output={len(bl.get('agentOutput',''))} chars")
            print(f"  Skilled:  timedOut={sk['timedOut']}, output={len(sk.get('agentOutput',''))} chars")
            print(f"  Improvement: {scenario.get('isolatedImprovementScore', 0):.1%}")
            for a in bl.get('assertionResults', []):
                status = 'PASS' if a['passed'] else 'FAIL'
                print(f"  Baseline assertion [{status}]: {a['message']}")

analyze('results.json')
```

## See also

- [skill-validator README](README.md) — CLI usage, eval file format, scoring weights
- [Overfitting detection](OverfittingDetection.md) — how overfitting scores are computed
- [CONTRIBUTING.md](../../CONTRIBUTING.md) — writing eval files and running tests locally
