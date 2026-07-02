---
name: setup-maf-evals
description: |
  Scaffold an `<App>.Evals.Tests` MSTest project alongside a .NET agentic app (MAF; Aspire/Foundry optional), wired to the GA Microsoft.Extensions.AI.Evaluation (MEAI) reporting pipeline. Categories: **NLP** (BLEU/GLEU/F1, no key), **Quality** (LLM-as-judge), **Safety** (content-harm via Foundry). Auto-installs `aieval`, detects `IChatClient`, generates a factory (`EVAL_USE_REAL_AGENT=1`), emits an HTML report; optional PR workflow. Topologies: Aspire/console/ASP.NET/worker. WHEN: "set up evals", "add evaluation harness/coverage", "validate quality after a model change", "compare gpt-4o vs gpt-4o-mini", "add safety evaluators"; or troubleshooting — "why are my Quality columns erroring/empty", "reasoning model breaks evals / max_tokens vs max_completion_tokens", "which evaluators fail my stylistic/summarizer agent". NOT-WHEN: one-shot audit (scan-agentic-app-perf), install rules (configure-agentic-perf-rules), reasoning-model questions unrelated to evals, or running an existing suite (dotnet test).
---

# setup-maf-evals

Scaffold an `<App>.Evals.Tests` MSTest project that measures latency,
token usage, cost, quality, and safety on every change to a .NET
agentic app — and produces the proper Microsoft.Extensions.AI.Evaluation
HTML report by default.

## When to Use

- The user asks to "set up evals", "add an evaluation harness", "wire
  up MEAI evaluation", or "measure my agent's quality".
- The user wants to **validate quality** after a model change ("does
  swapping gpt-4o → gpt-4o-mini regress responses?") and needs a
  reproducible baseline.
- The user wants to **compare** model assignments side-by-side
  ("compare gpt-4o vs gpt-4o-mini across my agents").
- The user wants to **add safety evaluators** (Hate/Violence/SelfHarm/
  Sexual via Azure AI Foundry) to an existing agent.
- The user wants a recurring eval run wired into CI (the optional
  GitHub Actions workflow).
- The user is **troubleshooting an eval setup** — "why are my Quality
  columns erroring or empty?", "my reasoning model rejects `max_tokens`
  in evals", "which evaluators will systematically fail my stylistic /
  summarizer agent?". The skill owns these answers; see
  `references/common-pitfalls.md`.

## When Not to Use

- The user wants a **one-shot audit** of existing code without scaffolding
  a new test project — use `scan-agentic-app-perf` instead.
- The user wants **always-on perf guard-rails** in the project's
  agent-instructions file — use `configure-agentic-perf-rules`.
- The project is not a .NET agentic app, or the user is not using
  `Microsoft.Extensions.AI` / `Microsoft.Agents.AI`.
- The user explicitly does not want an MSTest dependency and is not
  willing to use the opt-in `--shape console` runner.
- The user asks a **general model-behavior question unrelated to
  evaluation** (e.g. how reasoning models work in production), or just
  wants to **run an existing eval suite** (`dotnet test`) — no
  scaffolding or eval-specific guidance is needed.

## Supported topologies

The skill targets any .NET project using Microsoft Agent Framework
(`Microsoft.Agents.AI`), regardless of how it's hosted:

- **Aspire AppHost** (`*.AppHost.csproj` orchestrating agent services) — the
  most common shape; the generated `AgentChatClientFactory` mirrors the
  AppHost's connection-string-driven `IChatClient` registration.
- **Plain console / worker service** — `Microsoft.Agents.AI` registered
  directly against an OpenAI or Foundry client without an Aspire AppHost.
  The factory mirrors the app's direct registration (e.g. `AddOpenAIClient`
  or `AddAzureOpenAIChatClient` at the host level).
- **ASP.NET Core minimal API** — agents registered via the same DI patterns.

`IChatClient` detection (see `references/ichatclient-detection.md`) works
the same way across all topologies; only the AppHost-vs-Program.cs search
locations differ.

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
| **infra**        | `*.Evals.Tests.csproj`, `dotnet-tools.json`, `Reporting/*`, `Wire/*`, test class skeletons | update to current template; merge package refs; create if missing |
| **user data**    | `Quality/rubric.md`, `Quality/golden.json`, `Compare/matrix.json`, `Telemetry/inputs.json`, `Telemetry/prices.json`, `quality.thresholds.json`, `.github/workflows/evals.yml` | never overwrite; create if missing |
| **generated**    | `.copilot/perf-reports/evals/`                                         | regenerate freely          |

If an existing infra file differs from the current template, **update it to the
current template** — that is the update the user asked for. Merge in any user-added
package references, report what changed in the chat output, and never touch the
**user data** files above. Only user data is sacrosanct; infra is skill-owned
scaffolding, and Git preserves history if the user wants to review or revert.

`golden.json` has a `schema_version` field. If the detected version is
older than the current template, offer to migrate (additive only —
preserves existing rows, adds the new fields as nullable).

### 2. Resolve scope (proceed with defaults — do not block)

This is an **autonomous scaffold**. Do **not** stop and wait for the user
to answer before scaffolding. Print a one-line detection summary, apply
the defaults below, scaffold immediately, then state any assumptions you
made so the user can adjust. Honor any modes/categories the user *did*
specify; otherwise use the defaults. The only action that requires explicit
confirmation **first** is the genuinely destructive/irreversible one called out
elsewhere — the Aspire dashboard panel's AppHost edit. Updating infra files in
update mode (step 1a) is the expected, non-destructive action — just do it
(user data is preserved and Git keeps history). Defaults:

1. **Project shape** (default: MSTest). Alternative: console runner
   (legacy v1 shape) — only emit if user explicitly asks for it.
2. **Evaluator categories to enable.** Defaults shown; user can override.

   | Category | Evaluators | Cost | Needs | Default |
   |------|-----------|------|-------|---------|
   | **Quality** *(headline)* | Relevance, Coherence, Fluency, Completeness, Equivalence, Groundedness; agent: IntentResolution, TaskAdherence, ToolCallAccuracy | per-call judge tokens | real `IChatClient` + `EVAL_USE_REAL_JUDGE=1` | **ON** (stubbed until judge wired) |
   | NLP (zero-config sanity) | BLEU, GLEU, F1, Words | free | reference responses in golden.json | ON |
   | Safety | `ContentHarmEvaluator` (Hate+SelfHarm+Violence+Sexual single-shot), ProtectedMaterial, IndirectAttack, CodeVulnerability, UngroundedAttributes, GroundednessPro | Foundry evaluation service charges | Azure AI Foundry endpoint + `EVAL_USE_FOUNDRY_SAFETY=1` | **OFF** by default — scaffold only when the user explicitly asks for safety evaluators (do not block to ask) |

   Frame Quality as the headline evaluation; NLP is the zero-config
   first-run experience that emits a real `report.html` before any
   creds exist; Safety is the opt-in for production-bound apps.

3. **IChatClient detection result.** Show what was detected (e.g., "Found
   `AddAzureOpenAIChatClient` in `AppHost.cs:41` with deployment alias `chat`").
   State the detected registration and proceed; if detection failed, generate
   a stub factory the user will fill in (do not block to confirm).
4. **Run modes to scaffold.** Telemetry (default ON) and Quality (default
   ON). **Compare mode is opt-in and OFF by default** — scaffold it only
   when the user explicitly asks for a side-by-side matrix.json run. Compare
   adds the largest scaffold (extra runner + matrix.json + delta-table
   generator) and is the least commonly used surface. Do not block to ask.
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
`quality.thresholds.json`, `GlobalUsings.cs`, `dotnet-tools.json`.
Emit `Compare/{CompareTests.cs, matrix.json}` only if the user opted into
compare mode (step 2 #4). Emit `Safety/SafetyTests.cs` and
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

### 6. Wire compare mode (opt-in)

See `references/compare-mode.md`. **Default OFF.** Only scaffold when
the user opted in at step 2 (#4). When enabled, reads `matrix.json`;
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

Lead with **Quality** as the headline evaluation; frame NLP as the
zero-config sanity check and Safety/Compare as additions.

1. **Quality (headline).** State whether the judge is wired:
   - *Stubbed* — "Quality scaffolded; judge will run once you wire
     `EVAL_USE_REAL_JUDGE=1` + a chat endpoint. The next block tells
     you how."
   - *Live* — "Quality judge active against `<deployment>`; report
     shows Relevance / Coherence / Fluency / Completeness / Equivalence."
   - Caveat the user **must** know up-front: *"The built-in Quality
     rubrics are generic. Agents with deliberate stylistic constraints
     (brevity, persona, format adherence) will score low on
     Completeness / Equivalence even when working as designed. See
     `references/common-pitfalls.md#tuning-quality-for-stylistic-agents`
     for the per-app override pattern."*
2. **Promoting Quality to the judge tier.** If the app's `IChatClient`
   reads from a connection string (Aspire pattern), include the exact
   two commands:

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
   gpt-4-turbo) — reasoning models (gpt-5*, o-series) reject `max_tokens`
   and MEAI silently records every Quality metric as an error row. If the
   production model is a reasoning one, set
   `EVAL_JUDGE_DEPLOYMENT_NAME=<non-reasoning-alias>` so the judge points
   at a different deployment than the agent. Endpoint specifics in
   `references/ichatclient-detection.md`; the full reasoning-model
   footgun — including `max_completion_tokens` and the durable SDK-migration
   fix — is the canonical writeup in `references/common-pitfalls.md`.
3. **NLP (zero-config sanity).** "`report.html` already populates with
   Words / BLEU / GLEU / F1 from `golden.json` without any creds — run
   `dotnet test` now to see it."
4. **Additional categories you can wire.** List Safety
   (`EVAL_USE_FOUNDRY_SAFETY=1`, opt-in scaffold) and Compare (re-run
   the skill with `compare: true` if it wasn't opted in originally)
   as one-line bullets — not in the main flow.
5. **Paths.** Project path, `report.html` path, **glossary path**
   (`metrics-glossary.md` co-located with `report.html`), persistent
   `_store/` path.
6. **CLI invocations.** `dotnet test`, `dotnet tool run aieval report`,
   and the IChatClient detection result so the user knows what was
   auto-wired.
7. **Follow-up recommendation.** "Re-run after swapping a model per
   rule #3 of `configure-agentic-perf-rules` to confirm no quality
   regression."
8. **Cache payoff.** "First `dotnet test` populates `_store/cache/` and
   takes the full ~60s. Every subsequent run against unchanged inputs
   reuses cached agent + judge responses — typically ~5s with zero LLM
   cost. The Diagnostic Data section of `report.html` shows per-call
   Hit/Miss. To force a fresh run, delete `_store/cache/` or change the
   rubric / golden inputs."

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
