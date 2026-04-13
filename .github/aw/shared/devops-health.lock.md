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
- For aggregate failure rate (P5): `pipeline:evaluation:failure-rate:{bucket}` where bucket = "critical" or "warning"
- For scheduled cancellation rate (P6): `pipeline:evaluation:schedule-cancellation:{bucket}` where bucket = "critical" or "warning"

**Examples:**
| Finding | Fingerprint |
|---------|-------------|
| Evaluation workflow, evaluate job, "Run skill-validator" step failed | `pipeline:evaluation:evaluate:run-skill-validator:failure` |
| Evaluation workflow, evaluate job, "Build validator" step failed | `pipeline:evaluation:evaluate:build-validator:failure` |
| validate-skills workflow, validate job timed out | `pipeline:validate-skills:validate:timeout` |
| Evaluation failure rate > 30% across all branches | `pipeline:evaluation:failure-rate:critical` |
| Evaluation failure rate > 15% across all branches | `pipeline:evaluation:failure-rate:warning` |
| Evaluation scheduled cancellation rate > 60% | `pipeline:evaluation:schedule-cancellation:critical` |
| Evaluation scheduled cancellation rate > 30% | `pipeline:evaluation:schedule-cancellation:warning` |

### 1.2 Infrastructure Fingerprints

```
fingerprint = "infra:{config_key}"
  where config_key ∈ { "no-codeowners", "no-dependabot", "relaxed-skill-validation",
                        "verdict-warn-only", "pages-deployment-failed",
                        "unpinned-action:{action_name}",
                        "orphan-skill:{component}:{skill_name}",
                        "orphan-plugin:{directory_basename}" }
```

### 1.3 Resource Fingerprints

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
2. **Secondary**: Category — pipeline → infra → resource
3. **Tertiary**: Alphabetical by title

---

## 3. Severity Rules Reference

### Pipeline

| Check | Condition | Severity |
|-------|-----------|----------|
| P1 | `evaluation` workflow failed on `main` | 🔴 Critical |
| P1 | Other workflow failed on `main` | 🟡 Warning |
| P1 | Matches `known-noise` pattern | 🔵 Info (demoted) |
| P2 | Any cancelled/timed-out run on `main` | 🟡 Warning |
| P3 | Eval avg duration > 55 min | 🔴 Critical |
| P3 | Eval avg duration > 50 min | 🟡 Warning |
| P5 | Eval failure rate > 30% (all branches, 24h) | 🔴 Critical |
| P5 | Eval failure rate > 15% (all branches, 24h) | 🟡 Warning |
| P6 | Eval scheduled cancellation rate > 60% (24h) | 🔴 Critical |
| P6 | Eval scheduled cancellation rate > 30% (24h) | 🟡 Warning |

### Infrastructure

| Check | Condition | Severity |
|-------|-----------|----------|
| I1 | No CODEOWNERS file | 🟡 Warning |
| I2 | No Dependabot config | 🟡 Warning |
| I3 | `fail-on-warning: false` in validate-skills | 🟡 Warning |
| I4 | `--verdict-warn-only` in evaluation | 🔵 Info |
| I5 | Pages deployment failed | 🔴 Critical |
| I6 | Unpinned third-party action | 🔵 Info |
| I7 | Orphan skill (not registered in any plugin) | 🟡 Warning |
| I8 | Orphan plugin (not listed in marketplace.json) | 🟡 Warning |

### Resource

| Check | Condition | Severity |
|-------|-----------|----------|
| U3 | Weekly compute up >20% | 🟡 Warning |

---

## 4. Known Noise Patterns

The `cache-memory` key `known-noise` stores a list of fingerprint prefixes or patterns that should be demoted to 🔵 Info severity. Example patterns:

- `pipeline:copilot-code-review` — org-level workflow with known chronic failures
- `infra:verdict-warn-only` — intentional configuration, always Info

When a finding's fingerprint matches any known-noise pattern (prefix match), demote its severity to 🔵 Info. The finding is still reported in the output (in the EXISTING section if recurring) — it is NOT hidden.

New patterns can be added by manually editing the `known-noise` list in `cache-memory`.

---

## 5. Investigation Dispatch Rules

Only 🆕 NEW findings that meet these criteria qualify for investigation dispatch:

| Condition | Action |
|-----------|--------|
| 🆕 + 🔴 Critical | **Always dispatch** |
| 🆕 + 🟡 Warning + `pipeline` category | **Dispatch** |
| 🆕 + 🟡 Warning + `infra` or `resource` category | **Skip** |
| 🆕 + 🔵 Info | **Never dispatch** |
| 📌 EXISTING or ✅ RESOLVED | **Never dispatch** |

**Budget cap:** Maximum 2 dispatches per run.
**Priority order when cap is hit:**
1. 🔴 Critical findings first
2. Pipeline findings before infrastructure
3. Other categories last

## 6. Output Templates

### 6.1 Issue Title

```
🏥 Repository Health Dashboard
```

### 6.2 Issue Label

```
devops-health
```
- Color: `#0E8A16`
- Description: `Daily automated health check report`

### 6.3 First Run Notice

If no previous fingerprints exist in `cache-memory`:

```markdown
> ⚠️ This is the first health check run. All findings appear as new.
> Starting from the next run, only changes will be highlighted.
```

### 6.4 Trends Arrow Legend

| Condition | Arrow | Meaning |
|-----------|-------|---------|
| Δ positive and good (e.g., success rate up) | ✅ | Improving |
| Δ positive and bad (e.g., compute hours up) | ↗️ | Increasing (watch) |
| Δ negative and good (e.g., open PRs down) | ✅ | Improving |
| Δ negative and bad (e.g., success rate down) | ⚠️ | Degrading |
| Δ ≈ 0 | ➡️ | Stable |

### 6.5 Investigation Island Template

```markdown
<!-- investigation:{fingerprint} -->
⏳ Investigation dispatched — results arriving shortly...
<!-- /investigation:{fingerprint} -->
```

---

## 7. Operational Guardrails

### 7.1 API Rate Limits
- Use targeted, date-filtered queries to minimize API calls
- The `github` MCP toolset handles pagination automatically
- Space dispatches 5 seconds apart

### 7.2 Issue Body Size
- GitHub issues have a ~65,535 character limit
- If body exceeds 60k: truncate EXISTING section (keep top 20 by severity)
- Footer: `> … N additional existing findings omitted`
- The daily comment always includes complete summary counts

### 7.3 Cache Memory Keys

| Key | Contents | Updated |
|-----|----------|---------|
| `health-check-fingerprints` | Map of fingerprint → finding (with occurrences, first_seen) | Every run |
| `health-check-history` | Array of daily summaries (date, counts by diff type and severity) | Appended each run |
| `known-noise` | Array of fingerprint patterns to demote to Info | Manual edit |

### 7.4 Graceful Degradation

If any data source is unavailable:
- Skip that check category entirely
- Note the skip in the output: `> ⚠️ Skipped {category} checks: {reason}`
- Do NOT fail the entire workflow
- Continue with available data

### 7.5 Cache Memory Loss

If `cache-memory` returns no previous state:
- Treat all findings as 🆕 NEW
- Display the first-run notice (§6.3)
- The diff will resume automatically on the next run
