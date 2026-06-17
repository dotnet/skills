---
name: setup-maf-evals
description: |
  Scaffold an `<App>.Evals.Tests` MSTest project alongside a .NET agentic app (MAF + Aspire + Foundry) wired to the GA `Microsoft.Extensions.AI.Evaluation.Reporting` pipeline. Three evaluator categories: **NLP** (deterministic BLEU/GLEU/F1, no API key), **Quality** (LLM-as-judge Relevance/Coherence/Fluency, etc.), **Safety** (Hate/Violence/SelfHarm/Sexual via Azure AI Foundry). Auto-installs the `aieval` dotnet tool, detects the app's `IChatClient` registration and generates a factory so `EVAL_USE_REAL_AGENT=1` works without manual wiring, and emits an HTML report at `.copilot/perf-reports/evals/<ts>/report.html`. Optional GitHub Actions workflow runs the evals on every PR. WHEN: user asks "set up evals", "add evaluation harness", "measure my agent perf", "validate quality after a model change", "compare gpt-4o vs gpt-4o-mini", "add safety evaluators", "generate eval report". NOT-WHEN: one-shot audit (use scan-agentic-app-perf), install rules (configure-agentic-perf-rules), pick models (select-agent-models).
---

# setup-maf-evals

Scaffold an `<App>.Evals.Tests` MSTest project that measures latency,
token usage, cost, quality, and safety on every change to a .NET
agentic app — and produces the proper Microsoft.Extensions.AI.Evaluation
HTML report by default.

## Workflow

### 1. Discover the target app

Detect:

- Solution file (`*.sln` / `*.slnx`)
- AppHost project name (`*.AppHost.csproj`)
- Agent service projects
- Existing test / eval projects (avoid clobbering)
- **`IChatClient` registration** (scan AppHost + agent projects for
  `AddChatClient` / `AddAzureOpenAIChatClient` / `AddOllamaChatClient`
  / `AddOpenAIChatClient` and any explicit `services.AddSingleton<IChatClient>` or
  Foundry deployment alias references). See `references/ichatclient-detection.md`.

If no agentic app is detected, abort. If a `*.Evals.Tests` project already
exists, switch to update mode (see step 1a).

### 1a. Update mode (when `*.Evals.Tests` project already exists)

File classes:

| Class            | Files                                                                  | Behavior on update         |
|------------------|------------------------------------------------------------------------|----------------------------|
| **infra**        | `*.Evals.Tests.csproj`, `dotnet-tools.json`, `Reporting/*`, `Wire/*`, test class skeletons | merge package refs; create file if missing; do **not** overwrite |
| **user data**    | `Quality/rubric.md`, `Quality/golden.json`, `Compare/matrix.json`, `Telemetry/inputs.json`, `Telemetry/prices.json`, `quality.thresholds.json`, `.github/workflows/evals.yml` | never overwrite; create if missing |
| **generated**    | `.copilot/perf-reports/evals/`                                         | regenerate freely          |

If an existing infra file differs from the current template, surface
the diff in the chat output but do not overwrite. Recommend the user
review and merge manually.

`golden.json` has a `schema_version` field. If the detected version is
older than the current template, offer to migrate (additive only —
preserves existing rows, adds the new fields as nullable).

### 2. Confirm scope with the user

Present the detection summary, then confirm:

1. **Project shape** (default: MSTest). Alternative: console runner
   (legacy v1 shape) — only emit if user explicitly asks for it.
2. **Evaluator tiers to enable.** Defaults shown; user can override.

   | Tier | Evaluators | Cost | Needs |
   |------|-----------|------|-------|
   | 1 — NLP (default ON) | BLEU, GLEU, F1, Words | free | reference responses in golden.json |
   | 2 — Quality (default ON, but stubbed) | Relevance, Coherence, Fluency, Completeness, Equivalence, Groundedness; agent: IntentResolution, TaskAdherence, ToolCallAccuracy | per-call judge tokens | real `IChatClient` + `EVAL_USE_REAL_JUDGE=1` |
   | 3 — Safety (default OFF) | `ContentHarmEvaluator` (Hate+SelfHarm+Violence+Sexual single-shot), ProtectedMaterial, IndirectAttack, CodeVulnerability, UngroundedAttributes, GroundednessPro | Foundry evaluation service charges | Azure AI Foundry endpoint + `EVAL_USE_FOUNDRY_SAFETY=1` |

3. **IChatClient detection result.** Show what was detected (e.g., "Found
   `AddAzureOpenAIChatClient` in `AppHost.cs:41` with deployment alias `chat`").
   Ask the user to confirm or override. If detection failed, generate a
   stub factory the user will fill in.
4. **Run modes to scaffold** (telemetry / quality / compare). Default: all three.
5. **Optional add-ons:** Aspire dashboard panel, GitHub Actions workflow.

### 3. Scaffold the project

Use `references/project-template.md`. Creates:

```
<App>.Evals.Tests/
  <App>.Evals.Tests.csproj         # MSTest + MEAI.Evaluation refs (Reporting, NLP, Quality, optional Safety)
  Reporting/
    ReportingConfig.cs             # DiskBasedReportingConfiguration factory; tier-aware evaluator list
    Tier.cs                        # EVAL_USE_REAL_AGENT / EVAL_USE_REAL_JUDGE / EVAL_USE_FOUNDRY_SAFETY enum
  Wire/
    AgentChatClientFactory.cs      # auto-generated from IChatClient detection (step 1)
    StubChatClient.cs              # used when EVAL_USE_REAL_AGENT is unset
  Telemetry/
    TelemetryTests.cs              # [TestMethod] per input
    inputs.json
    prices.json
  Quality/
    QualityTests.cs                # [TestMethod] per golden scenario; NLP always, Quality when judge on
    rubric.md
    golden.json                    # schema_version, user_message, reference_response, expected_traits, optional context, optional expected_tool_calls
  Compare/
    CompareTests.cs                # [DataRow] per matrix entry; distinct executionName per entry
    matrix.json
  Safety/                          # only emitted if user opted in
    SafetyTests.cs                 # ContentHarmEvaluator + ProtectedMaterial + IndirectAttack
  quality.thresholds.json          # per-metric (Relevance / Coherence / BLEU / ...) -> minimum EvaluationRating
  GlobalUsings.cs
  dotnet-tools.json                # NEW — pins aieval (Microsoft.Extensions.AI.Evaluation.Console, GA)
  .github/workflows/evals.yml      # OPTIONAL — only if user opted in
```

After writing files:

1. `dotnet sln <SolutionFile> add <App>.Evals.Tests/<App>.Evals.Tests.csproj`
2. Update `.gitignore`: append `.copilot/perf-reports/evals/` and `<App>.Evals.Tests/_store/` if missing.
3. `dotnet tool restore` (installs `aieval`).

### 4. Wire telemetry mode

See `references/telemetry-capture.md`.

- `TelemetryTests` hooks into the resolved `IChatClient` via a
  delegating wrapper that records `gen_ai.usage.input_tokens`,
  `gen_ai.usage.output_tokens`, per-call latency, and a
  price-table-driven cost estimate.
- Emits a Markdown report at
  `.copilot/perf-reports/evals/<timestamp>/telemetry.md`,
  a machine-readable `telemetry.json`, and a `telemetry.junit.xml`.
- **Note:** Telemetry mode is *not* an MEAI eval report. It's a
  cost/latency capture. The HTML report comes from quality mode.

### 5. Wire quality mode

See `references/quality-modes.md`.

- `QualityTests` uses `DiskBasedReportingConfiguration` +
  `ScenarioRun.EvaluateAsync` — the actual MEAI reporting pipeline.
- Stub tier registers `WordCountEvaluator`, `BLEUEvaluator`,
  `GLEUEvaluator`, `F1Evaluator`. Judge tier adds the LLM-judge
  evaluators listed in step 2.
- Each evaluator gets its required `EvaluationContext` from
  `golden.json` (`BLEUEvaluatorContext(references)`,
  `F1EvaluatorContext(groundTruth)`, etc.).
- After all `[TestMethod]`s run, an `[AssemblyCleanup]` invokes
  `dotnet tool run aieval report --path _store --output .copilot/perf-reports/evals/<timestamp>/report.html`.

### 6. Wire compare mode

See `references/compare-mode.md`.

- `CompareTests` uses `[DynamicData]` to feed each `matrix.json` entry
  to a single test method.
- Each entry gets its own `executionName` so `aieval report` aggregates
  the comparison view automatically.
- Produces `compare.md` (side-by-side latency / token / cost / quality
  per matrix entry) **in addition to** the unified HTML report.

### 7. Wire safety mode (opt-in)

See `references/safety-mode.md`.

Off by default. When enabled in step 2:

- Adds `Microsoft.Extensions.AI.Evaluation.Safety` package.
- Generates `SafetyTests` using `ContentHarmEvaluator` (covers Hate +
  SelfHarm + Violence + Sexual in one Foundry call), plus
  `ProtectedMaterialEvaluator`, `IndirectAttackEvaluator`,
  `CodeVulnerabilityEvaluator`, `UngroundedAttributesEvaluator`, and
  optionally `GroundednessProEvaluator`.
- Skipped at runtime via `Assert.Inconclusive` if
  `EVAL_USE_FOUNDRY_SAFETY` is unset — never fails the build for
  missing creds.

### 8. Optional Aspire panel (apply mode)

See `references/aspire-dashboard-panel.md`. Off by default; modifies
the AppHost project. To enable, user must say "wire the panel" /
equivalent.

When enabled:

1. Show a unified diff of the AppHost edit (`app.UseStaticFiles()` and
   the panel files under `wwwroot/eval-panel/`).
2. Ask for confirmation.
3. Only on `yes` / explicit confirmation, write the changes.
4. Run `dotnet build` and report pass/fail.

If declined, scaffold the static files into `<App>.Evals.Tests/Panel/`
so the user can move them into the AppHost manually.

### 9. Optional CI workflow (opt-in)

See `references/ci-workflow.md`. Off by default.

When enabled, emits `.github/workflows/evals.yml`:

- Runs `dotnet test` on every PR.
- Checks for repo secrets `AZURE_OPENAI_ENDPOINT` and `AZURE_TENANT_ID`;
  if present, sets `EVAL_USE_REAL_JUDGE=1`. Otherwise stub tier.
- Runs `dotnet tool run aieval report` and uploads `report.html` as a
  build artifact (PR comment optional).

### 10. Validation

- `dotnet build <App>.Evals.Tests.csproj` exits 0.
- `dotnet test <App>.Evals.Tests.csproj` exits 0 in stub tier (no creds needed).
  - Stub tier must emit a `report.html` with **≥ 4 distinct metric columns**
    (Words, BLEU, GLEU, F1) across all scenarios in golden.json.
  - All scenarios must produce non-null metric values (no "—" placeholders).
- If `EVAL_USE_REAL_JUDGE=1` and an `IChatClient` is wired,
  `dotnet test` must additionally produce ≥ 3 Quality metrics (Relevance,
  Coherence, Fluency).

### 11. Surface in chat

Print a 3-block summary:

1. **Tier banner.** Which tier is active (Stub / Judge / Foundry-Safety)
   and the exact env-var commands to upgrade.
2. **Paths.** Project path, HTML report path, persistent `_store/` path.
3. **CLI invocations.** `dotnet test`, `dotnet tool run aieval report`,
   and the IChatClient detection result so the user knows what was
   auto-wired.
4. **Follow-up recommendation.** "Re-run after applying a
   `select-agent-models` recommendation to confirm no quality
   regression."

Also link `references/evaluators-catalog.md` so the user can see what
each metric means.

## Common pitfalls

- **Hand-rolling reports instead of using the Reporting pipeline.**
  The whole point of GA 10.7.0 is `DiskBasedReportingConfiguration` +
  `aieval`. Never write a hand-rolled markdown report and call it the
  "quality report" — that's an MEAI report (HTML) vs a cost/latency
  capture (markdown).
- **Calling real models from the default test run.** Stub tier uses
  `StubChatClient`; report is clearly marked `(stub IChatClient)`.
  Real-model runs are opt-in via the three env vars.
- **Conflating agent and judge clients.** They're two different
  `IChatClient` roles. The skill exposes them as two independent env
  vars (`EVAL_USE_REAL_AGENT`, `EVAL_USE_REAL_JUDGE`) — one can be
  real while the other is stubbed.
- **Hard-coding a price table.** Lives in `Telemetry/prices.json`,
  user-editable.
- **Wiring 4 separate safety evaluators.** Use `ContentHarmEvaluator`
  for the Hate/SelfHarm/Violence/Sexual bundle — single Foundry call,
  4 metrics back.
- **Auto-failing the build on quality regressions.** Quality mode is
  informational by default. Users opt into a hard-fail by editing
  `quality.thresholds.json` (which maps to real MEAI metric names like
  `Relevance` / `BLEU` / `F1` and `EvaluationRating` levels).
- **Forgetting `.gitignore` entries.** Must include both
  `.copilot/perf-reports/evals/` and `<App>.Evals.Tests/_store/`.
- **Treating telemetry/compare/quality as separate report streams.**
  Compare mode goes through `ReportingConfiguration` with a distinct
  `executionName` per matrix entry, so `aieval report` aggregates them
  into the same HTML.

## References

- `references/project-template.md` — exact files and `.csproj` layout (MSTest shape).
- `references/ichatclient-detection.md` — how to scan AppHost + agent for `IChatClient` registration and emit `AgentChatClientFactory.cs`.
- `references/evaluators-catalog.md` — full catalog of NLP + Quality + Safety evaluators with required `EvaluationContext` types and which tier they belong to.
- `references/telemetry-capture.md` — per-call hook + cost report format. Calls out: this is NOT the MEAI HTML report.
- `references/quality-modes.md` — `DiskBasedReportingConfiguration` wiring, tier-based evaluator registration, `aieval report` invocation.
- `references/compare-mode.md` — `matrix.json` layout, `[DynamicData]` test shape, per-entry `executionName`.
- `references/safety-mode.md` — opt-in safety scaffold, Foundry runtime check, `ContentHarmEvaluator` default.
- `references/ci-workflow.md` — `.github/workflows/evals.yml` template.
- `references/aspire-dashboard-panel.md` — optional static-file panel.
- [Microsoft.Extensions.AI.Evaluation libraries](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries) — upstream catalog of evaluators.
- [Tutorial: evaluate with reporting](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/evaluate-with-reporting) — canonical MSTest pattern.
- [dotnet/ai-samples → microsoft-extensions-ai-evaluation/api](https://github.com/dotnet/ai-samples/blob/main/src/microsoft-extensions-ai-evaluation/api/) — canonical unit-test examples.
