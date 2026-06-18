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

See `references/project-template.md` for the file tree, csproj, and
`GlobalUsings.cs` template. The project is **MSTest** by default
(`<App>.Evals.Tests`); a console-runner shape is available behind an
explicit `--shape console` flag.

Always emit: `Reporting/{ReportingConfig.cs, Tier.cs, AievalReport.cs,
WordCountEvaluator.cs, MetricsGlossary.cs}`, `Wire/{AgentChatClientFactory.cs,
StubChatClient.cs}`, `Quality/{QualityTests.cs, rubric.md, golden.json}`,
`Telemetry/{TelemetryTests.cs, inputs.json, prices.json}`,
`Compare/{CompareTests.cs, matrix.json}`, `quality.thresholds.json`,
`GlobalUsings.cs`, `dotnet-tools.json`. Emit `Safety/SafetyTests.cs` and
`.github/workflows/evals.yml` only if the user opted in (steps 7 and 9).

After writing files:

```pwsh
dotnet sln <SolutionFile> add <App>.Evals.Tests/<App>.Evals.Tests.csproj
dotnet tool restore                  # installs aieval
# .gitignore additions
echo ".copilot/perf-reports/evals/`n<App>.Evals.Tests/_store/" >> .gitignore
```

### 4. Wire telemetry mode

See `references/telemetry-capture.md`. Default ON. Captures latency,
input/output tokens, and price-table-driven cost via a delegating
`IChatClient`. Writes `telemetry.{md,json,junit.xml}` next to
`report.html`. **Not** the MEAI HTML report — that's quality mode's job.

### 5. Wire quality mode

See `references/quality-modes.md`. Default ON. The **only** runner that
produces `report.html`. Uses `DiskBasedReportingConfiguration` +
`ScenarioRun.EvaluateAsync` + `[AssemblyCleanup]` invokes
`dotnet tool run aieval report`. Stub tier registers the 4 NLP
evaluators; judge tier (`EVAL_USE_REAL_JUDGE=1`) adds the LLM-as-judge
evaluators from `references/evaluators-catalog.md`.

### 6. Wire compare mode

See `references/compare-mode.md`. Default ON. Reads `matrix.json`,
each entry runs through the **same** `ReportingConfiguration` with a
distinct `executionName`, so `aieval report` aggregates the comparison
columns into a single HTML view. Also writes a `compare.md` delta
table.

### 7. Wire safety mode (opt-in)

See `references/safety-mode.md`. **Default OFF.** When enabled, adds
`Microsoft.Extensions.AI.Evaluation.Safety` and emits `SafetyTests.cs`
with `ContentHarmEvaluator` (single-shot 4-metric bundle), plus
ProtectedMaterial / IndirectAttack / CodeVulnerability /
UngroundedAttributes. Skipped at runtime via `Assert.Inconclusive`
when `EVAL_USE_FOUNDRY_SAFETY` is unset — never fails the build for
missing creds.

### 8. Optional Aspire panel (apply mode)

See `references/aspire-dashboard-panel.md`. Default OFF; modifies
the AppHost project. To enable, user must say "wire the panel" /
equivalent. Always show a unified diff + ask for confirmation before
writing AppHost edits.

### 9. Optional CI workflow (opt-in)

See `references/ci-workflow.md`. Default OFF. Emits
`.github/workflows/evals.yml` that runs `dotnet test` on every PR,
auto-detects tier from repo secrets (`AZURE_OPENAI_ENDPOINT` →
judge; `AZURE_AI_FOUNDRY_ENDPOINT` → safety), and uploads
`report.html` as a workflow artifact.

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
2. **Paths.** Project path, HTML report path, **glossary path**
   (`metrics-glossary.md` co-located with `report.html`), persistent
   `_store/` path.
3. **CLI invocations.** `dotnet test`, `dotnet tool run aieval report`,
   and the IChatClient detection result so the user knows what was
   auto-wired.
4. **Promoting to the judge tier.** If the app's `IChatClient` reads from
   a connection string (Aspire pattern), include the exact two commands:

   ```pwsh
   dotnet user-secrets init --project <App>.Evals.Tests
   dotnet user-secrets set "ConnectionStrings:<alias>" `
     "Endpoint=https://<host>.services.ai.azure.com/models;DeploymentId=<alias>" `
     --project <App>.Evals.Tests
   # then: $env:EVAL_USE_REAL_AGENT="1"; $env:EVAL_USE_REAL_JUDGE="1"; dotnet test
   ```

   Note the **three** endpoint gotchas: (a) hostname strips dashes on
   some resources, (b) key auth is often disabled → drop the `Key=`
   segment and rely on `DefaultAzureCredential`, (c) **the judge
   deployment must be a non-reasoning model** (gpt-4o / gpt-4o-mini /
   gpt-4-turbo). Reasoning models (gpt-5*, o-series) reject `max_tokens`
   with HTTP 400 and MEAI silently records that as a per-metric error
   row. If the production model is a reasoning one, set
   `EVAL_JUDGE_DEPLOYMENT_NAME=<non-reasoning-alias>` so the judge
   points at a different deployment than the agent. Full details in
   `references/ichatclient-detection.md`.
5. **Follow-up recommendation.** "Re-run after applying a
   `select-agent-models` recommendation to confirm no quality
   regression."

Also link `references/evaluators-catalog.md` and
`references/metrics-glossary.md` so the user can see what each metric
means.

## Common pitfalls

See `references/common-pitfalls.md`.

## References

- `references/project-template.md` — file tree + `.csproj` layout.
- `references/ichatclient-detection.md` — registration scan + factory emission.
- `references/evaluators-catalog.md` — NLP + Quality + Safety catalog with required `EvaluationContext` types.
- `references/metrics-glossary.md` — per-run glossary content + `MetricsGlossary.cs` template.
- `references/telemetry-capture.md` — per-call hook + cost report format.
- `references/quality-modes.md` — `DiskBasedReportingConfiguration` wiring + `aieval report` invocation.
- `references/compare-mode.md` — `matrix.json` + per-entry `executionName`.
- `references/safety-mode.md` — opt-in safety scaffold + `ContentHarmEvaluator` default.
- `references/ci-workflow.md` — `.github/workflows/evals.yml` template.
- `references/aspire-dashboard-panel.md` — optional static-file panel.
- `references/common-pitfalls.md` — known footguns to avoid when scaffolding.
- [MEAI.Evaluation libraries](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries) | [Tutorial: evaluate with reporting](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/evaluate-with-reporting) | [dotnet/ai-samples](https://github.com/dotnet/ai-samples/blob/main/src/microsoft-extensions-ai-evaluation/api/)
