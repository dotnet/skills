# Investigating Evaluation Results

> **⚠️ Skill evaluations run on Vally; custom-agent evaluations use this runner
> as an execution lane.** The CI pipeline adapts native agent results through
> `eng/vally-adapter/adapt-agent-results.mjs` before publishing them, so use
> `eng/vally-adapter/InvestigatingResults.md` for the final schema. This
> document describes the raw `skill-validator evaluate` output retained under
> `_agent-evaluation/` for custom-agent diagnosis and for historical results.
>
> The current Vally workflow makes one targeted recovery attempt for executor
> `session.idle` timeouts before adaptation. See
> `executor-retry-summary.json` in the result artifact and the current guide for
> the bounded retry and fail-closed rules.
> Native-agent `RunMetrics.errorCount` is diagnostic and may include recovered
> tool-call failures. Adaptation invalidates a scenario only for terminal
> evidence: an explicit execution error, a missing or timed-out required arm, a
> failed run, or a missing pairwise result.

> The workflow token preflight tries another pool candidate for HTTP 429 or 402
> quota exhaustion and for the paired heading and token-environment lines in
> the Copilot CLI's no-authentication setup block. Unrelated service and
> configuration failures remain terminal.

> Current Vally PR evaluations default to `claude-sonnet-5` and `gpt-5.6-luna`,
> with judges `gpt-5.6-terra` and `claude-haiku-4.5`, respectively.
> Explicit profiles and the scheduled cadence can select other models.
> These defaults do not change the model fields in historical results or the
> legacy schema below. Health and issue-triage workflow models are separate.

> Current Vally runs use a version-checked SDK 1.0.11/1.0.13 startup guard. It waits for
> filesystem-provider setup before sessions start and shares concurrent startup
> calls. Session-provider errors are harness failures, not skill-quality verdicts.
> This does not change the historical result schema documented below.

> **Vally schema:** Vally adapter results use an independently owned and
> versioned schema. Consult the current Vally investigation guide for its
> schema version and fields. `state` is authoritative:
> `VALID_PASS`, `VALID_REGRESSION`, `VALID_NO_CHANGE`, or
> `INVALID_INCONCLUSIVE`. Use `stateReason` and `errors[]` for machine-readable
> causes. `preferenceRegressed` is report-only LLM preference evidence and is
> not an objective completion regression. `adapter-summary.json` reconciles the
> exact expected-eval manifest with observed and written results.
> `practicalSignificance` adds the 20% net-win floor. Objective completion is a
> separately defined tri-state over explicitly selected deterministic graders;
> aggregate Vally pass booleans remain report-only. These fields do not exist
> in the legacy schema documented below. Do not pass Vally results to
> `skill-validator consolidate`; it accepts only the legacy skill-validator
> schema. Malformed or unsupported inputs make consolidation return a nonzero
> exit code, even when it can still write a partial diagnostic summary.

This guide is intended primarily for AI agents investigating skill evaluation failures, though humans will find it useful too. It documents the `results.json` schema, common failure patterns, and recommended fixes.

## Using this guide with an AI agent

This document is designed to be read by AI coding agents. When a skill evaluation has failures, the PR comment includes a ready-to-use prompt — just copy and paste it to your AI agent. The agent will download the artifacts, read this guide, analyze the results, and suggest fixes.

If you need to run the investigation manually, follow the [Quick start](#quick-start) below.

## Quick start

1. **Download the results artifact:** `gh run download <run-id> --repo dotnet/skills --pattern "skill-validator-results-*" --dir <path>`
2. **Read `summary.md` first** for a quick overview of which scenarios passed/failed
3. **Read `results.json`** for the full metrics, agent output, assertions, and judge reasoning
4. **Identify the failure pattern** using the categories below — most failures match multiple patterns; fix them in priority order (timeouts first, then activation, then quality/rubric issues)
5. **Apply the fix** and re-run with `/evaluate`

## Finding the artifacts

### Via CLI (recommended for AI agents)

Extract the workflow run ID from the **Full results** link in the PR eval comment (e.g., `https://github.com/dotnet/skills/actions/runs/23520818616` → `23520818616`), then:

```bash
gh run download <run-id> --repo dotnet/skills --pattern "skill-validator-results-*" --dir ./eval-results
```

This downloads all result artifacts into subdirectories, each containing `results.json` and `summary.md`.

> **Note:** The `--pattern` flag is important — without it, `gh` will attempt to download all workflow artifacts including non-zip files (e.g., `.tar.gz`), which causes an extraction error and a non-zero exit code even though the eval results download successfully.

### Via browser

From the PR comment, click the **Full results** link to open the GitHub Actions workflow run. Then:

1. Click on any job (e.g., `evaluate (technology-selection)`)
2. Expand the **Upload results** step
3. Find the `Artifact download URL` in the log output
4. Download and extract

Alternatively, scroll to the bottom of the workflow run summary page and download from the **Artifacts** section.

## Understanding `results.json`

Each file contains a top-level object with:

| Field | Description |
|-------|-------------|
| `schemaOwner` | `skill-validator`. This distinguishes the retired evaluator output from Vally adapter results |
| `schemaVersion` | Legacy skill-validator results schema version. The first explicit version is `1`; older unversioned files remain readable |
| `model` | Model used for agent runs |
| `judgeModel` | Model used for judging |
| `timestamp` | When the results were written (UTC) |
| `verdicts[]` | Array of per-skill results |

### Verdict structure

Each verdict contains:

| Field | Description |
|-------|-------------|
| `schemaOwner` / `schemaVersion` | The same legacy schema identity, repeated so standalone `verdict.json` files are self-describing |
| `skillKind` | `skill` or `agent`; native custom-agent runs set `agent` before CI adaptation |
| `skillName` | Compatibility field containing the skill or custom-agent name |
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
| `perRunScores` | Per-run improvement scores used by the statistical gate. Agent evaluations always use isolated-vs-baseline scores because the plugin arm is diagnostic. Skill evaluations use `min(isolated, plugin)` when a plugin run is present and the isolated score otherwise |

> **Note:** Scenarios do not have a `passed` field. To determine pass/fail for an individual scenario, check whether `improvementScore >= 0`. For skills, this effective score is the minimum of isolated and plugin scores when both arms exist. For agents, it is always the isolated score; `pluginImprovementScore` and `pluginBreakdown` remain diagnostic production-surface telemetry. The `passed` field exists only at the verdict level.

> **Agent activation:** Expected-active native custom-agent scenarios select the
> target agent as the primary persona in both isolated and plugin arms. The
> selected-agent event satisfies the target activation contract; later specialist
> delegation remains visible as diagnostic telemetry. Expected-dormant scenarios
> register the target agent but do not preselect it, so both arms exercise normal
> routing and report organic selection. Dormant scenarios are excluded from
> preference scoring, and unexpected target selection in the isolated arm fails
> the activation contract. Missing target activation in an expected-active
> isolated arm is also a verdict gate, while plugin-arm activation remains
> diagnostic.

> **Skill activation:** Expected-active scenarios require target activation in
> both isolated and plugin arms. Expected-dormant scenarios must keep the target
> inactive in the isolated arm; unexpected isolated activation fails with
> `unexpected_activation`, while plugin-arm activity remains diagnostic. Inline
> and cross-directory rejudge reapply the same contract from persisted
> `expect_activation` metadata. Databases created before schema version 4 retain
> this field as unknown when migrated. Rejudge then recovers the expectation from
> the current target's matching `eval.yaml` scenario when possible, using the
> stored checkout path or the current repository and requiring persisted prompt
> text to still match when available. If the eval, scenario, or matching prompt is
> unavailable, it uses the legacy expected-active behavior instead of inventing
> historical dormancy.
> Schema-version-4 databases keep their explicit values while migration removes
> the old non-null/default constraint so schema 5 has one consistent shape.

> **Plugin skill staging:** Plugin runs load staged copies of manifest-declared
> skills rather than exposing the source directories directly. Skill directories
> and `SKILL.md` files must remain inside the plugin without symlink/reparse-point
> components, and linked descendants are omitted while copying the skill tree.
> Runtime file and shell permissions include the staged copies but exclude the
> original plugin source tree, so evaluation changes cannot mutate the checkout.
> The evaluator captures `GH_TOKEN` or `GITHUB_TOKEN` for its SDK client, then
> removes both aliases from the process and every setup-command or command-grader
> child environment.
> The session filesystem provider stores `session-state/*` under the private
> config directory, resolves relative file-tool paths from the scenario
> workspace, limits absolute paths to the private evaluator root, and rejects
> any reparse-point or symbolic-link component that escapes the selected root.
> Evaluator clients use a process-private directory under the system temp
> directory as their SDK filesystem root because the shared client is created
> before per-scenario `sv-*` workspaces. Fixtures and staged skills are created
> beneath that private root, which is created with owner-only permissions on
> Unix and a protected owner-only ACL on Windows. Per-session pre-tool and
> permission hooks further restrict file access
> to the current fixture workspace and its explicitly staged skill/plugin
> directories. Judge, overfitting, and rejudge sessions also receive tracked
> private work directories beneath that root; they never use the shared system
> temp directory as their working or absolute-access root. The filesystem
> provider receives only the current workspace and explicitly staged roots,
> and multi-path file operations validate every source and destination. File
> reads, metadata queries, writes, appends, and directory creation walk from
> an opened allowed root with OS no-follow semantics, so a path component
> replaced after validation cannot redirect the operation through a symbolic
> link or reparse point.
> Permission requests fail closed: read/write paths use the same containment
> checks, URL access is denied, shell requests without path or URL metadata
> are limited to a small exact local-command allowlist, and MCP access is
> limited to registered, sanitized servers and their explicitly declared
> tools; an omitted tool list permits none, while an explicit `*` permits all.
> The native evaluator currently accepts only the repository's shipped
> `dotnet dnx Microsoft.AITools.BinlogMcp --yes --prerelease` stdio launch
> shape as input, then rewrites it to package version 3.0.2 with a
> validator-owned NuGet configuration, trusted source, and private package and
> HTTP caches. Plugin-supplied environment variables, arbitrary runtimes,
> scripts, projects, and package substitutions are rejected before the server
> starts.

> **Command graders:** A Vally `run-command` grader with an explicit `args`
> array executes `command` directly with those argument boundaries preserved.
> When `args` is absent, the command remains a shell string so existing quoting,
> redirection, and compound-command behavior stays compatible.

> **Reused baselines:** When the run was invoked with `--baseline-from`, the `baseline` arm is not executed — its `metrics` and `judgeResult` come from the shared baseline file produced earlier with `--baseline-out` (computed once, honoring `--runs`). Such scenarios are reported with the `baseline-reused` session phase and a `reused` baseline status. The baseline file is keyed on `--model` and `--judge-model` plus, per scenario, a SHA-256 of the prompt and a composite SHA-256 over its setup inputs (copied test files, explicit setup files, and setup commands) and its evaluation criteria (rubric, assertions, expect/reject tools, and turn/token/timeout limits); reuse fails fast if the agent model, judge model, or any prompt-plus-setup-plus-criteria identity is missing, so the baseline you compare against is always identity-matched and a shared prompt across cases with different fixtures or rubrics cannot cross-contaminate. Because the baseline output is identical across every skill/agent that consumes the same file, this acts as a shared control group and removes baseline run-to-run variance from cross-skill comparisons.

> **Decoupled runs and judging:** `evaluate --no-judge` runs the agent arms and persists `sessions.db` but performs no judging and needs no baseline file, so baseline and treatment arms can run in one parallel pool. Each persisted session row carries a `baseline_key` column — the same prompt-SHA-plus-target-SHA identity used for baseline reuse. Scenario execution failures are persisted with terminal `failed` status and make `--no-judge` return nonzero; rejudge rejects any baseline or treatment database containing those failed sessions instead of silently dropping them. A later `rejudge <treatment-dir> --baseline-dir <baseline-dir>` pairs each treatment run with its baseline run by that key (preferring the matching run index), runs the same judges and gates an inline `evaluate` would, and writes baseline judge/pairwise results back to the baseline `sessions.db` and treatment judge results to the treatment `sessions.db`. Cross-directory rejudge also requires complete accounting: any unmatched baseline or treatment run is listed by skill, scenario, run, role, session ID, and baseline key, and stops rejudge before judging or publishing a partial verdict. Duplicate isolated or plugin role records for the same skill, scenario, and run are reported with every conflicting role and session ID and also stop rejudge before pairing. Inline rejudge similarly stops when a completed run group lacks its required baseline or isolated arm. Inline rejudge accepts normal and reused baselines plus both skill and agent isolated/plugin roles. It persists each new scenario's activation expectation, reconstructs target activation from saved events, and reapplies the skill or agent activation-contract gate. When an older database has no expectation, rejudge first reads the current matching eval scenario and otherwise preserves the legacy expected-active behavior. Baseline and treatment must share `--model`; the judge model resolves to `--judge-model`, else the treatment DB's persisted judge model, else the baseline DB's, and a mismatch between the two persisted judge models (without an explicit override) is rejected.

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
| `tokenEstimate` | Total tokens estimated for this run. When usage events are present, computed as `inputTokens + outputTokens`; if `inputTokens` is zero but cache activity was reported, uses `cacheReadTokens + outputTokens` instead. Falls back to chars÷4 when no usage events are available. |
| `inputTokens` | Input tokens sent to the model |
| `outputTokens` | Output tokens generated by the model |
| `cacheReadTokens` | Prompt tokens served from cache |
| `cacheWriteTokens` | Prompt tokens written to cache |
| `judgeInputTokens` | Input tokens consumed by the LLM judge for this run |
| `judgeOutputTokens` | Output tokens generated by the LLM judge for this run |
| `judgeCacheReadTokens` | Judge prompt tokens served from cache |
| `judgeCacheWriteTokens` | Judge prompt tokens written to cache |
| `turnCount` | Number of agent turns |
| `toolCallCount` | Number of tool calls |
| `toolCallBreakdown` | Tool call counts by tool name |
| `errorCount` | Number of errors during the run |
| `assertionResults[]` | Per-assertion pass/fail with messages |
| `agentOutput` | The agent's final text output |

> **Tip:** To get the total input/output token cost for an eval scenario, sum the token fields across baseline and skilled runs. For example, a scenario's total input tokens are `baseline.metrics.inputTokens + skilledIsolated.metrics.inputTokens` (plus `skilledPlugin.metrics.inputTokens` if a plugin run is present). The `judge*` fields track the judging overhead separately.

> **Note:** The quality scores shown in the summary table (e.g., "4.0/5") come from `baseline.judgeResult.overallScore`, `skilledIsolated.judgeResult.overallScore`, etc. — they are on the run result object, not inside `metrics`. When parsing `results.json`, look for `judgeResult.overallScore` alongside `metrics` on each run.

### eval.yaml scenario options

Several scenario-level options in `eval.yaml` are relevant when diagnosing failures:

| Option | Description |
|--------|-------------|
| `timeout` | Maximum wall-clock time per run in seconds. Default is 120 seconds if omitted. Increase when skilled runs time out. |
| `reject_tools` | Array of tool names that will cause the run to fail if they are used (e.g., `["bash", "edit"]`). This is enforced as a post-run assertion in the validator (it does not sandbox or block the tool calls), and is useful to force the agent to explain rather than explore/build, leveling the playing field between baseline and skilled runs. |
| `setup.files` | Array of files to create before the run. Gives the agent concrete code to work with, reducing variance from different scaffolding strategies. |

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

**In CI:** a required arm that times out makes the whole eval
measurement-invalid, even when every other scenario produced clean evidence. The
evaluation workflow therefore runs `eng/vally-adapter/retry-agent-timeouts.mjs`
before the adapter. It re-runs only the timed-out scenario, using
`skill-validator evaluate --scenario "<name>"`, writes that retry into its own
`--results-dir`, and replaces only that one scenario record in the native
results file. Because the retry never shares a results directory, its sessions
never merge with the first attempt's: every role/session record stays unique and
the `rejudge` pairing rules that reject duplicate completed roles still apply
unchanged. The retry judges the arms it re-runs, so no separate `rejudge` pass
is needed.

The retry is deliberately narrow. It fires only when a wall-clock timeout is the
scenario's sole defect; an `executionError`, `failedRunCount > 0`, a missing
arm, or a scenario the agent simply lost is never retried. A second timeout,
more than two timed-out scenarios, or any unexpected retry shape leaves the
original measurement in place and keeps the eval invalid. Check
`agent-timeout-retry-summary.json` in the leg artifact for
`recoveredScenarioCount`, `unresolvedScenarioCount`, `clearedAggregates`, and a
per-scenario reason.

Because a timed-out arm reports no completed task and no activation, the first
attempt's verdict-level aggregates may claim a completion regression or an
activation failure that the recovered scenario contradicts. After a swap the
retry re-derives those aggregates from the surviving scenarios and clears
`failureKind=completion_regression`, `failureKind=skill_not_activated`, and
`skillNotActivated` only when no scenario still supports them — a genuine
regression or activation failure in any scenario keeps failing. The bootstrapped
confidence interval is dropped rather than approximated because it covered the
timed-out run; `overfittingResult` is kept, since it analyses agent and eval
text rather than run outcomes.

The retry directory lives under the leg's results directory so its sessions
stay available for audit, but its own `results.json` files are renamed to
`results.retry.json` so no recursive collector counts the narrower retry copy as
a second native result.

`--scenario` is repeatable, matches scenario names case-insensitively, and exits
`1` when a name matches nothing, so a typo can never quietly evaluate an empty
set and report a clean run.

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
- Skills Loaded column shows `⚠️ NOT ACTIVATED` in the legacy `skill-validator` report (the current Vally PR comment shows this as `⚠️ N/total`, e.g. `⚠️ 1/2`, when fewer scenarios activated the skill than expected)
- `skillActivationIsolated` and/or `skillActivationPlugin` fields in results.json show `activated: false` (or the legacy `skillActivation` alias)
- `detectedSkills` is empty or `skillEventCount` is 0
- The skilled run metrics look similar to baseline (the agent ran normally but without the skill's guidance)

**Cause:** The agent runtime didn't select the skill for this prompt. The skill's frontmatter `description` didn't match.

**Fixes:**
- Update the skill's `description` in SKILL.md frontmatter to better match the scenario prompt
- Make sure the description includes keywords from the scenario
- Check the scenario itself has sufficient information that the agent can reason that it needs the skill. (It should not cheat and suggest the skill.)

> **Plugin-arm-only non-activation (skill-menu budget overflow).** If a skill
> activates reliably in the **isolated** arm but consistently fails to activate
> in the **plugin** arm (`skillActivationIsolated.activated: true` but
> `skillActivationPlugin.activated: false`, with empty `detectedSkills`), the
> cause is usually *not* the description text — it may never be shown. The
> Copilot CLI renders the model-facing `<available_skills>` menu under a hard
> **15,000-character budget** (the agent SDK's `SKILL_CHAR_BUDGET`, default
> `15e3`). Skills are listed **alphabetically by name** and emitted with their
> full `<description>` only until the budget is exhausted; every skill past the
> cut-off collapses to a **bare name with no description** and can no longer be
> reliably model-activated. In a large plugin, an alphabetically-late skill
> (e.g. `run-tests`, `test-*`) silently loses its description in the plugin
> menu even though it is fine in isolation.
>
> Fixes for this case (description tuning will *not* help — the text is not in
> the menu):
> - Mark reference / agent-orchestrated skills that are never meant to be
>   model-invoked from a user prompt with `disable-model-invocation: true`.
>   The CLI drops them from the menu entirely, freeing budget for the skills
>   that should be discoverable. (They remain invocable by explicit name.)
> - Reduce the plugin's aggregate skill-menu footprint so its model-invocable
>   skills fit under the budget. The `check` command enforces this via
>   `SkillProfiler.MaxRenderedSkillMenuLength` (15,000), summing each
>   model-invocable skill's **rendered `<skill>` block** (name + description +
>   location + markup, via `SkillProfiler.RenderedSkillMenuCost`) — not just the
>   raw description — and counting only skills *without*
>   `disable-model-invocation: true`. Counting the rendered block makes passing
>   `check` a faithful proxy for "fits in the real CLI menu budget".
> - As a last resort, consolidate overlapping skills so the plugin exposes
>   fewer model-invocable entries.

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

### 8. Baseline already good (no headroom)

**Symptoms:**
- Baseline scores are high (4.5–5.0/5)
- Skilled scores are similar or slightly lower
- `perRunScores` are consistently negative (e.g., `[-0.42, -0.73, -0.47]`)
- Breakdown shows negative `tokenReduction` and `toolCallReduction` (skill overhead) but no quality gain

**Cause:** The model already knows this topic well from training data. The skill can't improve on an already-excellent answer, and the overhead of loading the skill (extra tokens, tool calls) causes a net regression.

**Fixes:**
- **Add a `reject_tools` constraint** (e.g., `["bash", "edit"]`) so the eval fails if either baseline or skilled agent uses those tools — this keeps the comparison focused on answer quality instead of tool-induced overhead
- **Make the scenario harder** so the baseline struggles — add complexity, edge cases, or constraints that require the skill's specific knowledge
- **Rewrite the prompt** to be purely diagnostic (e.g., "Don't modify any files — just explain the root cause") to prevent the agent from spending time on tool calls
- **Remove the scenario** if the model consistently scores 5.0/5 without the skill — it isn't testing the skill's value

## When multiple patterns apply

Most failing scenarios match 2–3 patterns simultaneously (e.g., timeout + token overhead + high variance). Fix them in this priority order:

1. **Timeouts (#1)** — if the model can't finish, nothing else matters. Increase timeout first.
2. **Skill not activated (#5)** — if the skill never loaded, fix the description before tuning anything else.
3. **Baseline already bad (#2)** — if the baseline scores ≤2.0/5, the scenario may need simplification regardless of the skill.
4. **Baseline already good (#8)** — if the baseline scores ≥4.5/5, consider adding `reject_tools`, making the scenario harder, or removing it.
5. **High variance (#3)** — if `perRunScores` are unstable, a single eval run is unreliable. Re-run before concluding the skill is broken.
6. **Rubric/judgment issues (#6, #7)** — once the runs are stable, tune the rubric.
7. **Token overhead (#4)** — only optimize if quality is already good but the weighted score is marginally negative.

## Improving the skill vs. gaming the eval

When investigating failures, the goal is to **make the skill more useful to users** — not simply to make the eval score go up. Score improvement should be *evidence* of a better skill, not an end in itself.

### Legitimate fixes (improve the skill)

- Better skill content, structure, or examples
- Better frontmatter `description` so the skill activates on relevant prompts
- Removing a scenario where the baseline already scores perfectly (the skill genuinely adds no value)
- Adding `setup.files` so the scenario tests what was intended rather than scaffolding ability

### Illegitimate fixes (game the eval)

- Relaxing rubric criteria so both runs score higher for less
- Rewriting rubric items to match what the skill *happens* to produce rather than what a good answer *should* contain
- Softening prompt expectations to avoid exposing a real skill weakness
- Adding `reject_tools` to hide behavioral divergences between baseline and skilled runs (e.g., baseline explains while skilled run edits files — constraining tools makes the scores converge but doesn't fix the underlying issue)

### Gray area (use judgment)

- **Tightening a prompt** to reduce ambiguity is legitimate if the prompt is genuinely unclear, but illegitimate if done to steer toward the skill's strengths
- **Broadening a rubric** to accept multiple valid approaches (#6 above) is legitimate; broadening it to accept *wrong* approaches is not
- **Removing a scenario** because the baseline already aces it is an honest admission; removing it because the skill makes things worse is hiding a problem

When in doubt, ask: *"Would this change make the skill more useful to a real user, or does it just make the number go up?"*

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
9. **Review `overfittingResult`** if present — check `rubricAssessments` for items classified as `"technique"` (the rubric enforces a specific approach rather than testing an outcome). These are candidates for broadening. Also check `crossScenarioIssues` for systemic concerns about the eval design.

### Example analysis script

> **Note:** Save this as a `.py` file rather than running via `python -c "..."` — the nested quotes in f-string dictionary access are difficult to escape on the command line.

```python
import json

def analyze(path):
    with open(path) as f:
        data = json.load(f)
    for verdict in data['verdicts']:
        print(f"=== {verdict['skillName']} (passed={verdict['passed']}) ===")
        for scenario in verdict['scenarios']:
            name = scenario['scenarioName']
            bl_metrics = scenario['baseline']['metrics']
            sk_metrics = scenario['skilledIsolated']['metrics']
            bl_quality = scenario['baseline'].get('judgeResult', {}).get('overallScore', '?')
            sk_quality = scenario['skilledIsolated'].get('judgeResult', {}).get('overallScore', '?')
            improvement = scenario.get('improvementScore', 0)
            print(f"\n--- {name} ---")
            print(f"  Quality: baseline={bl_quality}/5, skilled={sk_quality}/5")
            print(f"  Baseline: timedOut={bl_metrics['timedOut']}, tokens={bl_metrics.get('tokenEstimate', 0)}")
            print(f"    input={bl_metrics.get('inputTokens', 0)}, output={bl_metrics.get('outputTokens', 0)}")
            print(f"  Skilled:  timedOut={sk_metrics['timedOut']}, tokens={sk_metrics.get('tokenEstimate', 0)}")
            print(f"    input={sk_metrics.get('inputTokens', 0)}, output={sk_metrics.get('outputTokens', 0)}")
            print(f"  Improvement: {improvement:.1%}")

            # perRunScores is a flat list of numbers (one per run)
            per_run = scenario.get('perRunScores', [])
            if per_run:
                formatted = ', '.join(f'{s:.2f}' for s in per_run)
                print(f"  Per-run scores: [{formatted}]")

            for a in sk_metrics.get('assertionResults', []):
                status = 'PASS' if a['passed'] else 'FAIL'
                print(f"  Assertion [{status}]: {a['message']}")

analyze('results.json')
```

## See also

- [skill-validator README](../README.md) — CLI usage, eval file format, scoring weights
- [Overfitting detection](OverfittingDetection.md) — how overfitting scores are computed
