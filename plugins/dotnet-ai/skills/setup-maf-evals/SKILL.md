---
name: setup-maf-evals
description: |
  Scaffold a Microsoft.Extensions.AI.Evaluation project alongside an existing .NET agentic application (MAF + Aspire + Foundry) so the team can measure latency, token usage, cost, and answer quality on every change. Creates an <App>.Evals project with three modes: telemetry (per-call latency / input-tokens / output-tokens / cost), quality (LLM-as-judge against a rubric and golden conversations), and compare (run two model assignments side by side and produce a delta). Outputs Markdown + JSON + JUnit-XML reports under .copilot/perf-reports/evals/<timestamp>/. Optionally wires an Aspire-dashboard panel showing per-agent token/latency live during dev. WHEN: user asks "how do I measure my agent perf", "set up evals", "add evaluation harness", "I changed models and need to validate quality", "compare gpt-4o vs gpt-4o-mini for my planner". NOT-WHEN: user wants a one-shot audit (scan-agentic-app-perf), install rules (configure-agentic-perf-rules), or pick models (select-agent-models).
---

# setup-maf-evals

Scaffold a `<App>.Evals` project that measures latency, token usage,
cost, and quality on every change to a .NET agentic app.

## Workflow

### 1. Discover the target app

Detect:

- Solution file (`*.sln` / `*.slnx`)
- AppHost project name (`*.AppHost.csproj`)
- Agent service projects
- Existing test / eval projects (avoid clobbering)

If no agentic app is detected, abort. If a `*.Evals` project already
exists, switch to update mode (see step 1a).

### 1a. Update mode (when `*.Evals` project already exists)

File classes:

| Class            | Files                                                                  | Behavior on update         |
|------------------|------------------------------------------------------------------------|----------------------------|
| **infra**        | `*.Evals.csproj`, `Program.cs`, runner classes, `Abstractions.cs`      | merge package refs; create file if missing; do **not** overwrite |
| **user data**    | `Quality/rubric.md`, `Quality/golden.json`, `Compare/matrix.json`, `Telemetry/inputs.json`, `Telemetry/prices.json`, `quality.thresholds.json` | never overwrite; create if missing |
| **generated**    | `Reports/`, `.copilot/perf-reports/evals/`                             | regenerate freely          |

If an existing infra file differs from the current template, surface
the diff in the chat output but do not overwrite. Recommend the user
review and merge manually.

### 2. Confirm scope with the user

Ask which modes to wire (default: all three):

- **telemetry** — capture latency, input-tokens, output-tokens, cost
  per agent call across a fixed input set.
- **quality** — LLM-as-judge against a rubric and golden conversations.
- **compare** — run mode A vs mode B and emit a side-by-side delta.

Optional:

- **aspire-panel** — add a static-file-based dashboard panel showing
  per-agent token/latency live during `dotnet run`.

### 3. Scaffold the project

Use `references/project-template.md`. Creates:

```
<App>.Evals/
  <App>.Evals.csproj           # Microsoft.Extensions.AI.Evaluation refs
  Telemetry/
    TelemetryEvalRunner.cs
    inputs.json                # 5 starter inputs the user customizes
  Quality/
    QualityEvalRunner.cs
    rubric.md                  # the LLM-judge rubric
    golden.json                # golden conversations
  Compare/
    CompareEvalRunner.cs
    matrix.json                # model assignments to compare
  Reports/                     # generated, gitignored
  Program.cs                   # CLI: dotnet run -- <mode>
  Directory.Build.props        # version pins
```

Add the project to the solution. Add `Reports/` and
`.copilot/perf-reports/evals/` to the repo `.gitignore`.

### 4. Wire telemetry mode

See `references/telemetry-capture.md`.

- Hooks into the existing `IChatClient` via a delegating wrapper that
  records `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`,
  per-call latency, and a price-table-driven cost estimate.
- Emits a Markdown report at
  `.copilot/perf-reports/evals/<timestamp>/telemetry.md`,
  a machine-readable `telemetry.json`, and a `telemetry.junit.xml`.

### 5. Wire quality mode

See `references/quality-modes.md`.

- LLM-judge configurable model id (default: `gpt-4o`).
- Rubric is a Markdown file the user edits.
- Golden conversations: array of `{ input, expected_traits[] }`.
- Emits per-input score, aggregate pass rate, top failures with
  judge rationale.

### 6. Wire compare mode

See `references/compare-mode.md`.

- Reads `matrix.json`: a list of `{ name, model_assignments }` entries.
- Runs telemetry + quality for each.
- Produces `compare.md` with side-by-side latency / token / cost /
  quality columns and a recommendation row.

### 7. Optional Aspire panel (apply mode)

See `references/aspire-dashboard-panel.md`.

This step is **off by default** and modifies the AppHost project. To
enable it, the user must say "wire the panel" / "add the dashboard
panel" / equivalent.

When enabled:

1. Show a unified diff of the AppHost edit (`app.UseStaticFiles()` and
   the panel files under `wwwroot/eval-panel/`).
2. Ask for confirmation.
3. Only on `yes` / explicit confirmation, write the changes.
4. Run `dotnet build` and report pass/fail.

If declined, scaffold the static files into `<App>.Evals/Panel/` so the
user can move them into the AppHost manually.

### 8. Validation

- `dotnet build <App>.Evals.csproj` exits 0.
- `dotnet run --project <App>.Evals -- telemetry` runs against a
  smoke input and writes a Reports/ file (uses a stub client if no
  API key is configured; emits `(stub)` in the report).
- All three runner classes have unit-level smoke tests under
  `<App>.Evals.Tests/`.

### 9. Surface in chat

- The path to the new project.
- The CLI invocations: `dotnet run -- telemetry`, `-- quality`,
  `-- compare`.
- The reports folder.
- Recommend a follow-up: "Re-run after applying a `select-agent-models`
  recommendation to confirm no quality regression."

## Common pitfalls

- **Calling real models from the smoke test.** The default smoke run
  uses a stub `IChatClient`; the report is clearly marked `(stub)`.
  Real-model runs are opt-in (env var `EVAL_USE_REAL_MODELS=1`).
- **Hard-coding a price table.** The price table lives in
  `Telemetry/prices.json` and is user-editable.
- **Conflating telemetry and quality.** Telemetry never reads the
  conversation content; quality never reads token counts. Keep them
  in separate runners and reports.
- **Auto-failing the build on quality regressions.** Quality mode is
  informational by default. The user explicitly opts into a hard-fail
  threshold by editing `quality.thresholds.json`.
- **Forgetting the `.gitignore` entry.** Reports must not pollute
  source history.

## References

- `references/project-template.md` — exact files and `.csproj` layout.
- `references/telemetry-capture.md` — per-call hook + report format.
- `references/quality-modes.md` — LLM-judge rubric + golden conv format.
- `references/compare-mode.md` — matrix.json layout + delta report.
- `references/aspire-dashboard-panel.md` — optional static-file panel.
