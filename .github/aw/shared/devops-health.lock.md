<!-- AUTO-GENERATED — DO NOT EDIT -->
<!-- Source: devops-health-check.md knowledge compilation -->

# DevOps Health Check — Compiled Knowledge

This document contains the health check catalog, fingerprinting rules, output templates, and operational guidance for the DevOps Daily Health Check agentic workflow.

---

## 1. Fingerprinting Rules

Every health finding MUST be assigned a deterministic **fingerprint** — a string ID derived from the finding's category and key attributes (but NOT timestamps, run IDs, or other ephemeral data). The same real-world issue MUST produce the same fingerprint on every run.

### 1.1 Pipeline Fingerprints

```
fingerprint = "pipeline:{workflow_name}:{job_name}:{failed_step}:{conclusion}"
```

- Normalize `workflow_name` by lowercasing and replacing spaces with hyphens
- Normalize `job_name` and `failed_step` the same way
- Same workflow + job + step + conclusion = same finding (even across different run IDs)
- A workflow that fails in a _different_ step is a _different_ finding
- For timeouts/cancellations: `pipeline:{workflow_name}:{job_name}:timeout`

**Examples:**
| Finding | Fingerprint |
|---------|-------------|
| Evaluation workflow, evaluate job, "Run skill-validator" step failed | `pipeline:evaluation:evaluate:run-skill-validator:failure` |
| Evaluation workflow, evaluate job, "Build validator" step failed | `pipeline:evaluation:evaluate:build-validator:failure` |
| validate-skills workflow, validate job timed out | `pipeline:validate-skills:validate:timeout` |

### 1.2 Quality Fingerprints

```
fingerprint = "quality:{skill_name}:{scenario_name}:{signal}"
  where signal ∈ { "{flag_name}", "regressed", "no-uplift", "high-variance" }
```

- Extract `skill_name` and `scenario_name` from bench entry `name` field
  - Format: `"{skill}/{scenario} - {metric}"` → parse text before ` - ` and split on `/`
- `{flag_name}` = any non-standard boolean property found on a bench entry (e.g., `notActivated`, `timedOut`, `testOverfitted`, or any future flag added to `generate-benchmark-data.ps1`)
- Anomaly flags are **dynamically discovered**: any property beyond `name`/`unit`/`value` on a bench entry is treated as an anomaly flag

**Examples:**
| Finding | Fingerprint |
|---------|-------------|
| dump-collect/basic-dump has notActivated flag | `quality:dump-collect:basic-dump:notActivated` |
| csharp-scripts/basic-script quality dropped 2.3 points | `quality:csharp-scripts:basic-script:regressed` |
| dotnet-pinvoke/marshal-array skilled ≤ vanilla | `quality:dotnet-pinvoke:marshal-array:no-uplift` |
| analyzing-dotnet-performance/memory-leak high stddev | `quality:analyzing-dotnet-performance:memory-leak:high-variance` |

### 1.3 Coverage Fingerprints

```
fingerprint = "coverage:{skill_name}:no-tests"
```

### 1.4 Benchmark Staleness Fingerprints

```
fingerprint = "quality:benchmark-stale:{component_name}"
```

### 1.5 PR Fingerprints

```
fingerprint = "pr:{pr_number}:{signal}"
  where signal ∈ { "stale", "no-review", "failing-checks", "stale-draft" }
```

- `pr_number` is the integer PR number (not the node ID)
- A PR that was "stale" last run and is still stale → EXISTING
- A PR that was "stale" but got merged/closed → RESOLVED

### 1.6 Infrastructure Fingerprints

```
fingerprint = "infra:{config_key}"
  where config_key ∈ { "no-codeowners", "no-dependabot", "relaxed-skill-validation",
                        "verdict-warn-only", "pages-deployment-failed",
                        "unpinned-action:{action_name}" }
```

### 1.7 Resource Fingerprints

```
fingerprint = "resource:{metric}:{threshold_breach}"
```

- `resource:eval-duration:warning` — eval avg > 50 min
- `resource:eval-duration:critical` — eval avg > 55 min
- `resource:cost-increase` — weekly compute hours up >20%

---

## 2. Diff Algorithm

```
previous_fps = cache_memory_load("health-check-fingerprints") ?? {}
current_fps  = {}

for each finding in all_collected_findings:
    fp = compute_fingerprint(finding)
    current_fps[fp] = finding

new_findings      = { fp: f for fp, f in current_fps  if fp NOT IN previous_fps }
existing_findings = { fp: f for fp, f in current_fps  if fp IN previous_fps }
resolved_findings = { fp: f for fp, f in previous_fps if fp NOT IN current_fps }

# Update occurrence tracking
for fp in existing_findings:
    existing_findings[fp].occurrences = previous_fps[fp].occurrences + 1
    existing_findings[fp].first_seen = previous_fps[fp].first_seen

for fp in new_findings:
    new_findings[fp].occurrences = 1
    new_findings[fp].first_seen = today

cache_memory_save("health-check-fingerprints", current_fps)
cache_memory_save("health-check-history", append(
    load("health-check-history"),
    { date: today, new_count, existing_count, resolved_count, by_severity }
))
```

### 2.1 Sorting Within Diff Categories

Within each category (NEW, EXISTING, RESOLVED):
1. **Primary**: Severity descending — 🔴 Critical → 🟡 Warning → 🔵 Info
2. **Secondary**: Category — pipeline → quality → pr → infra → resource
3. **Tertiary**: Alphabetical by title

---

## 3. Severity Rules Reference

### Pipeline

| Check | Condition | Severity |
|-------|-----------|----------|
| P1 | `evaluation` workflow failed | 🔴 Critical |
| P1 | Other workflow failed | 🟡 Warning |
| P1 | Matches `known-noise` pattern | 🔵 Info (demoted) |
| P2 | Any cancelled/timed-out run | 🟡 Warning |
| P3 | Eval avg duration > 55 min | 🔴 Critical |
| P3 | Eval avg duration > 50 min | 🟡 Warning |

### Quality

| Check | Condition | Severity |
|-------|-----------|----------|
| Q1 | `notActivated` flag on bench entry | 🔴 Critical |
| Q1 | Any other anomaly flag | 🟡 Warning |
| Q2 | Quality drop > 2.0 points vs 7-day avg | 🔴 Critical |
| Q2 | Quality drop > 1.0 points vs 7-day avg | 🟡 Warning |
| Q3 | Skilled ≤ Vanilla quality | 🟡 Warning |
| Q4 | Quality stddev > 1.5 over 7 days | 🟡 Warning |
| Q5 | Skill has no eval tests | 🟡 Warning |
| Q6 | Benchmark data > 24h old | 🟡 Warning |

### PR

| Check | Condition | Severity |
|-------|-----------|----------|
| R1 | Open > 7 days, no review | 🟡 Warning |
| R2 | Open > 14 days (any review) | 🟡 Warning |
| R3 | All checks failing | 🟡 Warning |
| R4 | Draft, no activity > 7 days | 🔵 Info |

### Infrastructure

| Check | Condition | Severity |
|-------|-----------|----------|
| I1 | No CODEOWNERS file | 🟡 Warning |
| I2 | No Dependabot config | 🟡 Warning |
| I3 | `fail-on-warning: false` in validate-skills | 🟡 Warning |
| I4 | `--verdict-warn-only` in evaluation | 🔵 Info |
| I5 | Pages deployment failed | 🔴 Critical |
| I6 | Unpinned third-party action | 🔵 Info |

### Resource

| Check | Condition | Severity |
|-------|-----------|----------|
| U3 | Weekly compute up >20% | 🟡 Warning |

---

## 4. Benchmark Data Format

Dashboard data files on `gh-pages` at `data/{component}.json` have this structure:

```json
{
  "lastUpdate": 1740000000000,
  "repoUrl": "",
  "entries": {
    "Quality": [
      {
        "commit": { "id": "abc1234", "message": "...", "timestamp": "..." },
        "date": 1740000000000,
        "tool": "customBiggerIsBetter",
        "model": "claude-opus-4.6",
        "benches": [
          {
            "name": "skillName/scenarioName - Skilled Quality",
            "unit": "Score (0-10)",
            "value": 7.8
          },
          {
            "name": "skillName/scenarioName - Vanilla Quality",
            "unit": "Score (0-10)",
            "value": 5.2
          },
          {
            "name": "skillName/scenarioName - Skilled Quality",
            "unit": "Score (0-10)",
            "value": 0.0,
            "notActivated": true
          }
        ]
      }
    ],
    "Efficiency": [
      {
        "commit": { ... },
        "date": 1740000000000,
        "tool": "customSmallerIsBetter",
        "model": "claude-opus-4.6",
        "benches": [
          {
            "name": "skillName/scenarioName - Skilled Time",
            "unit": "seconds",
            "value": 45.2,
            "timedOut": true
          }
        ]
      }
    ]
  }
}
```

### Key Points for Parsing:
- `date` is Unix epoch in **milliseconds**
- Bench entry `name` format: `"{skill}/{scenario} - {metric}"`
- Standard fields: `name`, `unit`, `value` — anything else is an anomaly flag
- Both `Quality` and `Efficiency` arrays carry the same anomaly flags
- Quality scores range 0-10 (mapped from 0-5 judge scale)
- The latest entry is the last element in the array (`entries.Quality[-1]`)
- For 7-day rolling averages, filter entries by `date` field (not array index)

### Parsing Skill/Scenario from Bench Name:

```
bench.name = "dump-collect/basic-dump - Skilled Quality"
parts = bench.name.split(" - ")
# parts[0] = "dump-collect/basic-dump"
# parts[1] = "Skilled Quality"

skill_scenario = parts[0].split("/")
# skill_scenario[0] = "dump-collect"    (skill name)
# skill_scenario[1] = "basic-dump"      (scenario name)
```

### Detecting Anomaly Flags:

For each bench entry, check all properties. Any property key that is NOT `name`, `unit`, or `value` is an anomaly flag:

```
standard_fields = {"name", "unit", "value"}
flags = { key: value for key, value in bench_entry if key not in standard_fields and value == true }
```

This approach automatically discovers new flag types added in the future.

---

## 5. Component Discovery

Components are discovered by scanning the file system:

```bash
find src/*/plugin.json -maxdepth 2
```

Each `src/{name}/` directory containing a `plugin.json` is a component. The dashboard data file is at `data/{name}.json` on the `gh-pages` branch.

To fetch benchmark data via the GitHub API (without `curl`):
```
GET https://raw.githubusercontent.com/{owner}/{repo}/gh-pages/data/{component}.json
```

---

## 6. Known Noise Patterns

The `cache-memory` key `known-noise` stores a list of fingerprint prefixes or patterns that should be demoted to 🔵 Info severity. Example patterns:

- `pipeline:copilot-code-review` — org-level workflow with known chronic failures
- `infra:verdict-warn-only` — intentional configuration, always Info

When a finding's fingerprint matches any known-noise pattern (prefix match), demote its severity to 🔵 Info. The finding is still reported in the output (in the EXISTING section if recurring) — it is NOT hidden.

New patterns can be added via the `/health-check suppress <pattern>` slash command, which appends to the `known-noise` list in `cache-memory`.

---

## 7. Investigation Dispatch Rules

Only 🆕 NEW findings that meet these criteria qualify for investigation dispatch:

| Condition | Action |
|-----------|--------|
| 🆕 + 🔴 Critical | **Always dispatch** |
| 🆕 + 🟡 Warning + `pipeline` or `quality` category | **Dispatch** |
| 🆕 + 🟡 Warning + `pr` or `infra` category | **Skip** |
| 🆕 + 🔵 Info | **Never dispatch** |
| 📌 EXISTING or ✅ RESOLVED | **Never dispatch** |

**Budget cap:** Maximum 10 dispatches per run.
**Priority order when cap is hit:**
1. 🔴 Critical findings first
2. Pipeline findings before quality
3. Other categories last

---

## 8. Output Templates

### 8.1 Issue Title

```
🏥 Repository Health Dashboard
```

### 8.2 Issue Label

```
devops-health
```
- Color: `#0E8A16`
- Description: `Daily automated health check report`

### 8.3 First Run Notice

If no previous fingerprints exist in `cache-memory`:

```markdown
> ⚠️ This is the first health check run. All findings appear as new.
> Starting from the next run, only changes will be highlighted.
```

### 8.4 Trends Arrow Legend

| Condition | Arrow | Meaning |
|-----------|-------|---------|
| Δ positive and good (e.g., success rate up) | ✅ | Improving |
| Δ positive and bad (e.g., compute hours up) | ↗️ | Increasing (watch) |
| Δ negative and good (e.g., open PRs down) | ✅ | Improving |
| Δ negative and bad (e.g., success rate down) | ⚠️ | Degrading |
| Δ ≈ 0 | ➡️ | Stable |

### 8.5 Investigation Island Template

```markdown
<!-- investigation:{fingerprint} -->
⏳ Investigation dispatched — results arriving shortly...
<!-- /investigation:{fingerprint} -->
```

---

## 9. Operational Guardrails

### 9.1 API Rate Limits
- Use targeted, date-filtered queries to minimize API calls
- The `github` MCP toolset handles pagination automatically
- Space dispatches 5 seconds apart

### 9.2 Issue Body Size
- GitHub issues have a ~65,535 character limit
- If body exceeds 60k: truncate EXISTING section (keep top 20 by severity)
- Footer: `> … N additional existing findings omitted`
- The daily comment always includes complete summary counts

### 9.3 Cache Memory Keys

| Key | Contents | Updated |
|-----|----------|---------|
| `health-check-fingerprints` | Map of fingerprint → finding (with occurrences, first_seen) | Every run |
| `health-check-history` | Array of daily summaries (date, counts by diff type and severity) | Appended each run |
| `known-noise` | Array of fingerprint patterns to demote to Info | Via `/health-check suppress` |

### 9.4 Graceful Degradation

If any data source is unavailable:
- Skip that check category entirely
- Note the skip in the output: `> ⚠️ Skipped {category} checks: {reason}`
- Do NOT fail the entire workflow
- Continue with available data

### 9.5 Cache Memory Loss

If `cache-memory` returns no previous state:
- Treat all findings as 🆕 NEW
- Display the first-run notice (§8.3)
- The diff will resume automatically on the next run
