# Overfitting Detection — Implementation Plan

A LLM-based overfitting judge that gives skill authors actionable feedback when their eval tests memorization instead of genuine improvement.

## 1. Problem Statement

The eval system judges whether a skill meaningfully improves agent performance by comparing "with-skill" runs against "baseline" runs. But the eval definitions themselves can be overfitted — testing whether the agent parrots the skill's specific phrasing or tests for knowledge the LLM already has, rather than testing whether the skill teaches something genuinely new that produces better outcomes.

| Pattern | Severity | Example |
|---------|----------|---------|
| Assertion gates on shell-escaping that LLMs already know | High | `output_matches "(-bl:\\{\\{\\}\\}|/bl:\\{\\{\\}\\})"` — PowerShell `{{}}` escaping |
| Rubric items testing skill-specific vocabulary labels | Moderate | "Measured three build scenarios: cold, warm, no-op" |
| Repeated rubric items inflating niche patterns | Moderate | "params overload without 1-arg fast path" across 2 scenarios |
| Diagnostic-method testing vs. conclusion testing | Low | "Mentioned building twice to verify incrementality" |

---

## 2. Architecture Overview

```
┌──────────────────────────────────────────────────────────┐
│                    ValidateCommand                        │
│                          │                               │
│          ┌───────────────┼───────────────┐               │
│          ▼               ▼               │               │
│  OverfittingJudge    AgentRunner         │               │
│  (NEW, parallel)     (scenarios)         │               │
│  1 LLM call/skill        │               │               │
│          │               ▼               │               │
│          └────────→ Comparator ──→ SkillVerdict          │
│                                  + OverfittingResult     │
│                                          │               │
│                  ┌───────────────────────┤               │
│                  ▼               ▼       ▼               │
│              Reporter        Reporter  Dashboard         │
│              (console)       (md/json) (bench data)      │
│              + overfit col   + overfit + overfit flag     │
└──────────────────────────────────────────────────────────┘
```

**Simple design:** One new service (`OverfittingJudge`) makes a single LLM call per skill, passing both the SKILL.md content and eval.yaml definition inline in the prompt. The LLM classifies each rubric item and assertion, returns a structured JSON response, and the service computes a score. The score is attached to the verdict and flows through to all reporters and the dashboard. The overfitting check runs **in parallel** with scenario execution (forked from `ValidateCommand`, same level as `AgentRunner`) to minimize wall-clock impact. It is **on by default** and can be disabled with `--no-overfitting-check`.

**Verdict impact:** The overfitting score is **informational only** in v1 — it does not affect `SkillVerdict.Passed`. Overfitting is a new signal with uncalibrated thresholds; failing CI runs on it before calibration (Section 8) is risky. It is surfaced prominently (console, markdown table, dashboard) as a warning. A future `--fail-on-overfitting` flag can be added once calibration confirms reliable thresholds.

---

## 3. Overfitting Judge — Prompt Design

This is the core of the feature. The prompt must:

1. Give the LLM a clear conceptual framework for what "overfitting" means in this context
2. Frame the key questions from both the domain-expert perspective AND the LLM-knowledge perspective
3. Provide concrete examples spanning high/moderate/low overfitting (few-shot)
4. Ask for per-element classification with structured JSON output

### 3.1 System Prompt

```
You are an expert evaluator assessing whether an AI skill's evaluation
definition (eval) is overfitted.

A skill teaches an LLM new knowledge or techniques. The eval tests whether
loading the skill produces better outcomes. An eval is OVERFITTED when it
rewards the agent for repeating the skill's specific phrasing, syntax, or
methodology — rather than for producing a genuinely better result that the
agent could NOT have produced without the skill.

TWO KEY QUESTIONS to ask for every rubric item and assertion:

1. DOMAIN EXPERT TEST: "Would a knowledgeable developer who gives a correct,
   high-quality answer — but has NOT read this specific skill document — fail
   this rubric item or assertion?" If yes → overfitted.

2. LLM KNOWLEDGE TEST: "Does this eval item test for knowledge the LLM likely
   already has from its training data, rather than something genuinely new
   that the skill teaches?" If it tests pre-existing LLM knowledge
   (common APIs, standard syntax, general best practices, language escaping
   rules) → overfitted. The skill's value should be in teaching something
   the LLM wouldn't reliably do on its own.

IMPORTANT DISTINCTION: Some skills teach a GENUINELY NOVEL technique that has
no practical alternative — testing for that technique is NOT overfitting.
The test is: are there other valid approaches that would solve the problem
equally well? If not, the item tests a real outcome, not memorization.

Both the skill document and the eval definition will be provided inline
in the user prompt, clearly delimited.

YOUR TASK: Classify each rubric item and each assertion.

### Rubric item classifications

- "outcome" — Tests whether the agent reached a correct, useful result.
  The criterion describes WHAT should be achieved, not HOW.
  A different valid approach would also score well.

- "technique" — Tests whether the agent used a specific method or diagnostic
  procedure taught in the skill. A correct answer using a different valid
  method would score poorly.

- "vocabulary" — Tests whether the agent used specific terminology, labels,
  or syntax from the skill. The phrasing rewards the skill's exact wording
  rather than equivalent correct alternatives.

### Assertion classifications

- "broad" — Tests for a generally-correct behavior. Multiple valid approaches
  would pass.

- "narrow" — Tests for a skill-specific pattern. Correct alternative approaches
  would fail this assertion.

### Scoring guidance

- Does the rubric item contain quoted syntax or commands copied from the skill? → likely "vocabulary"
- Does the rubric item describe a diagnostic STEP rather than a FINDING? → likely "technique"
- Does the assertion regex match only one specific syntax when alternatives exist? → likely "narrow"
- Does the item test knowledge any good LLM already has (language escaping, common APIs, general best practices)? → overfitted
- Does the skill teach something genuinely novel with no practical alternative? → testing for it is NOT overfitting
- Is the item testing a CONCLUSION or a PATH to the conclusion?

### Few-shot examples

#### Example 1: HIGH overfitting — testing shell escaping the LLM already knows

SKILL teaches: Use -bl:{} for unique binlog names in MSBuild. In PowerShell,
escape as -bl:{{}}.

OVERFITTED assertion (narrow) — HIGH:
  output_matches: "(-bl:\{\{\}\}|/bl:\{\{\}\})"
  → Tests PowerShell escaping of curly braces. This is general shell knowledge
  the LLM already has in training data. Testing for it measures pre-existing
  LLM capability, not skill value.

WELL-DESIGNED assertion (broad):
  file_exists: "*.binlog"
  → Tests the actual outcome regardless of the syntax used.

#### Example 2: MODERATE overfitting — testing skill vocabulary labels

SKILL teaches: Measure builds in three tiers — "cold build", "warm build",
"no-op build".

OVERFITTED rubric (vocabulary) — MODERATE:
  "Measured three build scenarios: cold build, warm/incremental build, and no-op build"
  → Tests the skill's specific labeling system. A developer who measures
  "clean build, cached build, null build" is doing the same thing with
  different vocabulary.

WELL-DESIGNED rubric (outcome):
  "Established a reproducible baseline by measuring builds under different cache states"
  → Tests the concept without requiring exact labels.

#### Example 3: LOW overfitting — testing a diagnostic step

SKILL teaches: Build twice and compare binlogs to verify incrementality.

BORDERLINE rubric (technique) — LOW:
  "Mentioned building twice to verify incrementality"
  → This is a technique from the skill, but an agent that directly analyzes
  the binlog for missing Inputs/Outputs attributes could give an equally
  valid answer without mentioning this step.

WELL-DESIGNED rubric (outcome):
  "Identified the root cause of incremental build failure"
  → Tests the finding, not the diagnostic path.

#### Example 4: NOT overfitted — testing a genuinely novel technique

SKILL teaches: Use -bl:{} for unique binlog filenames (MSBuild 17.8+).
The {} placeholder makes MSBuild auto-generate a unique name per invocation,
preventing overwrites when running multiple builds in sequence.

NOT OVERFITTED rubric (outcome):
  "Used /bl:{} or equivalent to ensure each build produces a unique binlog
  that does not overwrite previous binlogs"
  → The {} placeholder is genuinely the only reliable way to prevent binlog
  collision across sequential MSBuild invocations in the same directory.
  There is no practical alternative — this IS the outcome, not just vocabulary.

#### Example 5: NOT overfitted — testing correctness, not style

SKILL teaches: Use LibraryImport for .NET 7+, DllImport for .NET Framework.

NOT OVERFITTED rubric (outcome):
  "Uses LibraryImport instead of DllImport since the target is .NET 8"
  → Tests a genuine correctness decision. LibraryImport is the right API
  for .NET 7+ — this is not a stylistic preference.

#### Example 6: MODERATE overfitting — artificial example

SKILL teaches: Use `dotnet trace collect` with `--clreventlevel` flag for
targeted perf tracing.

OVERFITTED rubric (vocabulary) — MODERATE:
  "Used --clreventlevel flag with dotnet trace collect"
  → Tests for the specific CLI flag name from the skill. An agent that
  correctly uses the EventPipe API directly or another profiling tool to
  achieve the same targeted trace would score poorly.

WELL-DESIGNED rubric (outcome):
  "Collected a performance trace scoped to the relevant events"
  → Tests the result regardless of tool selection.

#### Example 7: HIGH overfitting — artificial example

SKILL teaches: Run `dotnet-counters monitor` and look for "GC Heap Size"
exceeding 500MB as a memory leak indicator.

OVERFITTED rubric (vocabulary) — HIGH:
  "Checked if GC Heap Size counter exceeds 500MB threshold"
  → Tests for the skill's specific counter name and threshold. A developer
  who checks Gen2 GC frequency, allocations/sec, or uses a memory profiler
  to find the leak is solving the same problem differently.

WELL-DESIGNED rubric (outcome):
  "Identified evidence of a memory leak and proposed a diagnosis path"
  → Tests whether the agent found the issue, not which metric it checked.

Respond ONLY with JSON. No markdown, no commentary outside the JSON.
```

### 3.2 User Prompt

The skill content and eval definition must be cleanly separated from the prompt structure to avoid markdown/header confusion. We use delimited blocks with unique boundary markers.

The eval definition is included as **raw YAML** — the native authoring format that the LLM understands natively. The `<<<...>>>` delimiters isolate it from the prompt structure. Scenario prompts are included because the LLM needs them to judge whether rubric items test the prompt's stated goal vs. the skill's vocabulary.

**Skill content truncation:** If `SkillMdContent` exceeds 12,000 tokens (~48,000 characters), the inline content is truncated and the full file path is provided for reference:

```
[TRUNCATED — skill document was {n} tokens, showing first 12,000.
 Full document at: {skill.SkillMdPath}]
```

In practice, current SKILL.md files are well within this limit.

```
Assess the following skill and its eval definition for overfitting.

=== SKILL DOCUMENT (from: {skill.SkillMdPath}) ===
<<<SKILL_CONTENT_START>>>
{skill.SkillMdContent — or truncated with path to full document}
<<<SKILL_CONTENT_END>>>

=== EVAL DEFINITION (raw YAML) ===
<<<EVAL_CONTENT_START>>>
{raw eval.yaml content}
<<<EVAL_CONTENT_END>>>

=== CLASSIFICATION REQUEST ===

Classify every rubric item and every assertion across all scenarios.
Then provide an overall overfitting score from 0.0 to 1.0.

Respond in this exact JSON schema:

{
  "rubric_assessments": [
    {
      "scenario": "<scenario name>",
      "criterion": "<exact rubric text>",
      "classification": "outcome" | "technique" | "vocabulary",
      "confidence": <0.0-1.0>,
      "reasoning": "<1-2 sentence explanation>"
    }
  ],
  "assertion_assessments": [
    {
      "scenario": "<scenario name>",
      "assertion_summary": "<type: pattern_or_value>",
      "classification": "broad" | "narrow",
      "confidence": <0.0-1.0>,
      "reasoning": "<1-2 sentence explanation>"
    }
  ],
  "cross_scenario_issues": [
    "<description of any cross-scenario overfitting patterns>"
  ],
  "overall_overfitting_score": <0.0-1.0>,
  "overall_reasoning": "<2-3 sentence summary>"
}
```

### 3.3 Prompt Design Rationale

| Design choice | Rationale |
|--------------|-----------|
| **Two key questions** | The "domain expert" test catches rubric items that penalize valid alternative approaches. The "LLM knowledge" test catches items that test pre-existing LLM capabilities (like shell escaping) rather than genuinely new skill knowledge. Together they cover both dimensions of overfitting. |
| **"No practical alternative" escape hatch** | Prevents false positives on genuinely novel techniques. If `-bl:{}` is the only way to solve binlog collision, testing for it is testing an outcome. The LLM must reason about whether alternatives exist. |
| **Three rubric categories** (outcome/technique/vocabulary) | Maps to severity tiers. "Outcome" = not overfitted, "technique" = moderate, "vocabulary" = high. |
| **Two assertion categories** (broad/narrow) | Assertions are simpler — they either match alternative correct answers or they don't. |
| **Delimited skill content** | `<<<SKILL_CONTENT_START>>>` / `<<<SKILL_CONTENT_END>>>` markers prevent the skill's markdown headers and formatting from being confused with the user prompt's own structure. The file path is also shown for context. |
| **Raw YAML for eval** | The eval definition is included as raw YAML (the authoring format). The LLM parses YAML natively, and the `<<<...>>>` delimiters isolate it from the prompt. |
| **Skill content truncation** | If the skill exceeds 12K tokens, the content is truncated with a note and the full file path is included so the information loss is explicit. In practice, current skills are well within limits. |
| **Seven few-shot examples** | Span the full spectrum: 2 high, 2 moderate, 1 low, 2 "NOT overfitted". Includes both real and artificial examples. The "NOT overfitted" examples are critical to prevent over-flagging. |
| **No `suggested_rewrite` in response** | Rewrites are handled separately by `--overfitting-fix` (Section 5.4). Keeping the judge response focused on classification improves reliability. |
| **Per-element classification** | More reliable than asking for a single overall judgment. The overall score is computed from per-element results. |
| **Confidence field** | Allows scoring to weight high-confidence classifications more heavily. |

### 3.4 Score Computation from LLM Results

```csharp
double ComputeOverfittingScore(OverfittingJudgeResponse response)
{
    // Rubric scoring: weight by classification and confidence
    double rubricScore = 0;
    int rubricCount = 0;
    foreach (var item in response.RubricAssessments)
    {
        double weight = item.Classification switch
        {
            "outcome" => 0.0,
            "technique" => 0.5,
            "vocabulary" => 1.0,
        };
        rubricScore += weight * item.Confidence;
        rubricCount++;
    }

    // Assertion scoring
    double assertionScore = 0;
    int assertionCount = 0;
    foreach (var item in response.AssertionAssessments)
    {
        double weight = item.Classification switch
        {
            "broad" => 0.0,
            "narrow" => 1.0,
        };
        assertionScore += weight * item.Confidence;
        assertionCount++;
    }

    // Weighted combination (rubric matters more — assertions are secondary gates)
    double rubricAvg = rubricCount > 0 ? rubricScore / rubricCount : 0;
    double assertionAvg = assertionCount > 0 ? assertionScore / assertionCount : 0;

    return Math.Clamp(0.7 * rubricAvg + 0.3 * assertionAvg, 0.0, 1.0);
}
```

The LLM also returns an `overall_overfitting_score` directly. The final score is a blend:

```
final_score = 0.6 * computed_score + 0.4 * llm_overall_score
```

This blend trusts the per-element computation more (it's systematic) while still giving the LLM's holistic judgment some weight.

**Severity mapping:**

| Score | Severity | Icon |
|-------|----------|------|
| 0.00 – 0.20 | Low | ✅ |
| 0.20 – 0.50 | Moderate | 🟡 |
| 0.50 – 1.00 | High | 🔴 |

---

## 4. New Models

Add to `Models/Models.cs`:

```csharp
using System.Text.Json.Serialization;

// --- Overfitting assessment ---

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverfittingSeverity
{
    Low,
    Moderate,
    High,
}

public sealed record RubricOverfitAssessment(
    string Scenario,
    string Criterion,
    string Classification,      // "outcome" | "technique" | "vocabulary"
    double Confidence,
    string Reasoning);

public sealed record AssertionOverfitAssessment(
    string Scenario,
    string AssertionSummary,
    string Classification,      // "broad" | "narrow"
    double Confidence,
    string Reasoning);

public sealed record OverfittingResult(
    double Score,               // [0, 1]
    OverfittingSeverity Severity,
    IReadOnlyList<RubricOverfitAssessment> RubricAssessments,
    IReadOnlyList<AssertionOverfitAssessment> AssertionAssessments,
    IReadOnlyList<string> CrossScenarioIssues,
    string OverallReasoning);
```

```csharp
public sealed record OverfittingJudgeOptions(
    string Model,
    bool Verbose,
    int Timeout,
    string WorkDir);
```

Add to existing types:

```csharp
// In SkillVerdict:
public OverfittingResult? OverfittingResult { get; set; }

// In ValidatorConfig:
public bool OverfittingCheck { get; init; } = true;  // on by default
public bool OverfittingFix { get; init; }
```

**Note:** `OverfittingSeverity` uses `[JsonConverter(typeof(JsonStringEnumConverter))]` so it serializes as `"High"` / `"Moderate"` / `"Low"` in JSON output, which the dashboard's PowerShell script consumes via string comparison.

---

## 5. Pipeline Integration

### 5.1 CLI Changes

Add to `ValidateCommand`:

```
--no-overfitting-check   Disable LLM-based overfitting analysis (on by default)
--overfitting-fix        Generate a fixed eval.yaml with improved rubric items/assertions
```

The overfitting check is **on by default** — it adds one LLM call per skill and provides valuable feedback. Authors who want to skip it (e.g., for faster iteration cycles) can opt out with `--no-overfitting-check`.

The `--overfitting-fix` flag triggers a second LLM call that proposes high-level rewrites for flagged items and writes a `eval.fixed.yaml` alongside the original. This gives authors a starting point — the suggestions are intentionally high-level rather than exact drop-in replacements, since over-precise auto-fixes could themselves become a form of overfitting.

### 5.2 Execution Flow

The overfitting check runs **in parallel with scenario execution** to minimize wall-clock impact. It only needs the eval definition and SKILL.md content (both from `SkillInfo`), not run results:

```csharp
// In ValidateCommand.EvaluateSkill — launch overfitting check in parallel with scenarios:
Task<OverfittingResult?> overfittingTask = Task.FromResult<OverfittingResult?>(null);
if (config.OverfittingCheck && skill.EvalConfig is not null)
{
    log("🔍 Running overfitting check (parallel)...");
    overfittingTask = OverfittingJudge.Analyze(skill, new OverfittingJudgeOptions(
        config.JudgeModel, config.Verbose, config.JudgeTimeout, workDir));
}

// Existing scenario execution (runs concurrently):
var scenarioTasks = skill.EvalConfig.Scenarios.Select(scenario => ...);
var comparisons = (await Task.WhenAll(scenarioTasks)).ToList();

// Await overfitting result (non-fatal — never blocks an otherwise-successful evaluation):
OverfittingResult? overfittingResult = null;
try
{
    overfittingResult = await overfittingTask;
    if (overfittingResult is not null)
        log($"🔍 Overfitting: {overfittingResult.Score:F2} ({overfittingResult.Severity})");
}
catch (Exception ex)
{
    log($"⚠️ Overfitting check failed: {ex.Message}");
}

var verdict = Comparator.ComputeVerdict(...);
verdict.OverfittingResult = overfittingResult;

// Optional: generate fixed eval.yaml
if (config.OverfittingFix && overfittingResult is { Severity: not OverfittingSeverity.Low })
{
    await OverfittingJudge.GenerateFix(skill, overfittingResult, options);
    log("📝 Generated eval.fixed.yaml with suggested improvements");
}
```

### 5.3 `OverfittingJudge` Service

New file: `Services/OverfittingJudge.cs`

Structure mirrors the existing `Judge` service:
- `Analyze(SkillInfo, OverfittingJudgeOptions) → OverfittingResult` — classification call
- `GenerateFix(SkillInfo, OverfittingResult, options) → void` — writes `eval.fixed.yaml`
- Uses `AgentRunner.GetSharedClient` for the LLM session
- **No tool/file permissions needed** — all content is passed inline in the prompt (skill content from `SkillInfo.SkillMdContent`, eval from `SkillInfo.EvalConfig`). The session is created with no tools and `InfiniteSessions = false`.
- Handles skill content truncation: if `SkillMdContent` exceeds ~48,000 characters (~12K tokens), truncates and appends the full file path for reference
- Retries up to 2 times on failure (same pattern as `Judge`)
- Parses response JSON using existing `LlmJson.ExtractJson` / `LlmJson.ParseLlmJson`
- Computes score from per-element classifications (Section 3.4)

### 5.4 `--overfitting-fix` — Fix Generation

When `--overfitting-fix` is passed and overfitting is Moderate or High, a second LLM call generates a fixed eval definition. The fix prompt:

- Receives the original eval.yaml + the classification results from the judge
- Is asked to produce a revised eval.yaml that replaces flagged rubric items and assertions with outcome-focused alternatives
- Output is written to `eval.fixed.yaml` next to the original `eval.yaml`

The output is a **valid, parseable YAML file** that can replace the original `eval.yaml`. Modified rubric items and assertions are each preceded by a `# CHANGED: <reason>` YAML comment explaining the change. This lets authors run `diff eval.yaml eval.fixed.yaml` to see exact changes with rationale.

The replacement wording is **directional** — it proposes better phrasing but is not guaranteed to be perfectly calibrated. The goal is to give skill authors a starting point they then refine with domain knowledge. Overly prescriptive auto-fixes could themselves become a form of overfitting ("overfitting for the overfitting check").

---

## 6. Reporting Changes

### 6.1 Skill Validation Results Table (Markdown)

Add an **Overfit** column to the existing table. Currently:

```
| Skill | Scenario | Baseline | With Skill | Δ | Skills Loaded | Verdict |
```

Becomes:

```
| Skill | Scenario | Baseline | With Skill | Δ | Skills Loaded | Overfit | Verdict |
```

The Overfit column shows the icon + score: `✅ 0.12`, `🟡 0.38`, or `🔴 0.71`. When overfitting check was not run, shows `—`.

Since the overfitting check produces **one score per skill** (not per scenario), the Overfit column value is **repeated on every scenario row** for that skill. This matches the existing pattern where `Skills Loaded` repeats per scenario, keeping each row self-contained and readable without scanning upward.

### 6.2 Console Output

Add overfitting line to the per-skill summary output:

```
✓ dotnet-pinvoke  +22.3%  [+15.1%, +29.5%] significant
  Improvement score 22.3% meets threshold of 10.0%
  🔍 Overfitting: 0.08 (low) ✅

✗ build-perf-baseline  +18.4%  [+12.0%, +24.8%] significant
  Improvement score 18.4% meets threshold of 10.0%
  ⚠️ Overfitting: 0.55 (high) 🔴
    • [vocabulary] "Measured three build scenarios: cold build, warm/incremental build, and no-op build"
      — tests skill labeling system; equivalent concepts with different names would fail
    • [narrow] output_matches "(baseline|cold.build|warm.build|no.op)" — skill-specific vocabulary
    • [technique] "Built twice to check incremental correctness" — tests diagnostic step, not finding
```

For moderate/high overfitting, the top signals (sorted by confidence descending) are printed. Low overfitting shows just the summary line.

### 6.3 JSON Report

The `OverfittingResult` is serialized directly into each verdict in `results.json`:

```json
{
  "skillName": "binlog-generation",
  "overfittingResult": {
    "score": 0.72,
    "severity": "High",
    "rubricAssessments": [...],
    "assertionAssessments": [...],
    "crossScenarioIssues": [...],
    "overallReasoning": "..."
  }
}
```

### 6.4 Dashboard Integration

#### `generate-benchmark-data.ps1` Changes

Mirror the existing `timedOut` / `notActivated` flag pattern. For each bench entry, add an `overfitting` field when the score is moderate or high:

```powershell
# After existing timedOut/notActivated checks:
if ($verdict.overfittingResult -and $verdict.overfittingResult.severity -in @("Moderate", "High")) {
    $benchEntry.overfitting = $verdict.overfittingResult.severity.ToLower()
    $benchEntry.overfittingScore = $verdict.overfittingResult.score
}
```

This adds the flag to every bench entry (quality and efficiency) for that skill, just like `timedOut` adds to every bench entry for timed-out scenarios.

#### `dashboard.js` Changes

1. **Summary card** — Count overfitting entries in recent runs and show a card (same pattern as "Not Activated" and "Timed Out" cards):

```javascript
// After existing timedOutCount block:
let overfittingCount = 0;
recentEntries.forEach(entry => {
  if (entry.benches.some(b => b.overfitting)) overfittingCount++;
});
if (overfittingCount > 0) {
  summaryDiv.innerHTML += `
    <div class="card">
      <div class="card-label">Overfitting</div>
      <div class="card-value" style="color: var(--warning)">${overfittingCount}</div>
      <div class="card-delta">evals with moderate/high overfitting</div>
    </div>
  `;
}
```

2. **Chart point markers** — Use a distinct marker (star/diamond) and color for overfitting points, same pattern as the triangle markers for not-activated:

```javascript
// In quality chart per-point styling:
const OVERFITTING_COLOR = '#d29922';  // same warning yellow
// point shape: 'star' for overfitting
```

3. **Tooltip** — Add overfitting warning to tooltip callbacks:

```javascript
const hasOverfitting = entry && entry.benches &&
  entry.benches.some(b => b.overfitting);
if (hasOverfitting) {
  parts.push('🔴 EVAL OVERFITTING DETECTED');
}
```

4. **Chart legend note** — Add legend note below charts with overfitting points:

```javascript
if (hasAnyOverfitting) {
  const note = document.createElement('div');
  note.className = 'not-activated-legend';
  note.innerHTML = `🔴 <span style="color:${OVERFITTING_COLOR}">★</span> = Eval overfitting detected`;
  div.appendChild(note);
}
```

---

## 7. Implementation Plan (Files & Order)

| # | File | Change | Size |
|---|------|--------|------|
| 1 | `Models/Models.cs` | Add `OverfittingSeverity` (with `[JsonConverter(typeof(JsonStringEnumConverter))]`), `RubricOverfitAssessment`, `AssertionOverfitAssessment`, `OverfittingResult`, `OverfittingJudgeOptions`. Add `OverfittingResult?` to `SkillVerdict`. Add `OverfittingCheck` (default true), `OverfittingFix` to `ValidatorConfig`. | S |
| 2 | `Services/OverfittingJudge.cs` | **New file.** System prompt, user prompt builder (with delimiter-separated inline content, skill truncation at 12K tokens), LLM call (no tools/permissions), JSON response parsing, score computation. `Analyze()` for classification, `GenerateFix()` for eval.fixed.yaml generation. Follow existing `Judge.cs` patterns. | M |
| 3 | `Commands/ValidateCommand.cs` | Add `--no-overfitting-check` / `--overfitting-fix` CLI options. Launch `OverfittingJudge.Analyze` in parallel with scenario execution. Non-fatal error handling (try/catch around await). Attach result to verdict. Call `GenerateFix` when flagged. | S |
| 4 | `Services/Reporter.cs` | Add Overfit column to markdown table (repeated per scenario row). Add overfitting section to console output. `OverfittingResult` serialized in JSON via existing `JsonStringEnumConverter`. | M |
| 5 | `eng/dashboard/generate-benchmark-data.ps1` | Add `overfitting` / `overfittingScore` fields to bench entries when severity is Moderate or High. Relies on string enum serialization. | S |
| 6 | `eng/dashboard/dashboard.js` | Add overfitting summary card, chart markers, tooltips, and legend notes. | S |
| 7 | `tests/OverfittingJudgeTests.cs` | Test JSON response parsing, score computation, severity mapping, truncation logic. Use mock LLM responses based on real eval.yaml samples. | M |

Build order: 1 → 2 → 3 → 4 → 5+6 → 7

---

## 8. Calibration

Run the overfitting check against the existing eval corpus and compare with manual analysis:

| Skill | Manual severity | Expected score |
|-------|----------------|---------------|
| binlog-generation (PS escaping assertion) | High | 0.40 – 0.65 |
| build-perf-baseline | High | 0.50 – 0.75 |
| analyzing-dotnet-performance (sc 8-9) | Moderate | 0.25 – 0.45 |
| incremental-build | Low | 0.05 – 0.20 |
| dotnet-pinvoke | Low | 0.00 – 0.15 |
| msbuild-antipatterns | Low | 0.00 – 0.15 |
| csharp-scripts | Low | 0.00 – 0.15 |

If the LLM scores don't align, adjust:
1. Few-shot examples in the system prompt (add/refine calibration examples)
2. The 0.7/0.3 rubric/assertion blend weights
3. The 0.6/0.4 computed/LLM-overall blend weights
4. The severity thresholds (0.20 / 0.50 boundaries)
