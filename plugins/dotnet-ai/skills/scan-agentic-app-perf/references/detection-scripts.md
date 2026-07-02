# Detection scripts

The `scripts/` directory ships three deterministic detectors that automate the
mechanical extraction behind the `topology.*`, `otel.*`, and `model.*` checks.
They exist to cut token cost and run-to-run variance: the skill spends its
budget on judgment (severity, the `why`/`next` narrative, role-vs-model calls)
instead of re-reading every source file.

## Contract

- **Detection only.** A script never assigns severity, never writes a report,
  and never edits the scanned project. It emits facts + best-effort `flags`.
- **Invocation.** `& "<skill-directory>/scripts/<Name>.ps1" -Path <app-root> -Json`.
  Omit `-Json` for a short human summary. The skill loader supplies
  `<skill-directory>`. Requires PowerShell 7+ (`pwsh`).
- **Source-text parsing.** No build, no Roslyn — regex over `.cs` (and
  `appsettings*.json` for OTel). This is why they also work on a file-based
  AppHost (a bare `.cs` with no `.csproj`/`.sln`).
- **Always exit 0.** On success `ok = true`. The skill must still guard against
  a non-zero exit, empty output, or malformed JSON and **fall back to reading
  the reference doc and scanning by hand** (graceful degradation).
- **Evidence gate still applies.** Every `file`/`line` a script emits must be
  re-opened and confirmed before it becomes a report finding (see SKILL.md
  step 3). A `flag` set by a script is a *candidate*, not a verdict.

## `Detect-Topology.ps1`

Feeds `references/topology-checks.md` (`topology.cycle`, `topology.deep-single-leaf`).

JSON shape:

```
{ ok, scanned,
  metrics: { agentCount, edgeCount, orphanCount, maxFanout },
  nodes:  [ { name, kind, file, line } ],       # kind: project | agent
  edges:  [ { source, target, file, line } ],
  orphans:[ "<name>" ],
  notes }
```

Edge `source` is inferred from the defining file/project (best-effort); confirm
direction against the cited file before asserting a cycle or a deep chain. The
script does not decide severity — a warn-level orphan/fan-out signal only
becomes critical if you confirm a cycle.

## `Detect-OtelCoverage.ps1`

Feeds `references/otel-coverage-checks.md`.

JSON shape:

```
{ ok, scanned,
  present:  { serviceDefaults, addOpenTelemetry, otlpExporter, withTracing,
              withMetrics, genAiTokens, aspireDashboard, sensitiveData },  # booleans
  evidence: { <signal>: { file, line, text } | null },
  flags:    { otel.missing-sdk, otel.no-aspire-dashboard, otel.no-token-cost }, # booleans
  notes }
```

`otel.missing-sdk` fires only when **neither** `AddServiceDefaults` **nor**
`AddOpenTelemetry` is present. `gen_ai.*` tags are emitted automatically by
`Microsoft.Extensions.AI`, so confirm exporter wiring before asserting
`otel.no-token-cost`.

## `Detect-ModelLiterals.ps1`

Feeds `references/model-assignment-checks.md`.

JSON shape:

```
{ ok, scanned,
  distinctIds: [ "<model-id>" ],
  hits: [ { id, form, appHost, file, line, text } ],   # form: literal | enum | deployment-call
  flags: { model.same-default, model.hardcoded },       # booleans (candidates)
  notes }
```

An AppHost `AddDeployment(...)` is the canonical model-id location and does
**not** trip `model.hardcoded`; only a literal in a non-AppHost service file
does. The role-driven checks (`model.reasoning-on-deterministic`,
`model.cheap-on-planner`) are **not** decided by the script — they require
reading each agent's prompt/tools, which the skill does directly.
