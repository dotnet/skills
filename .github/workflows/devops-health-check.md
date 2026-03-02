---
name: "DevOps Daily Health Check"
description: >
  Orchestrator workflow that collects repo health signals daily (pipelines,
  skill quality, PRs, infrastructure), computes a fingerprint-based diff
  against the previous run, updates a pinned health dashboard issue, and
  dispatches investigation workers for new critical/warning findings.

on:
  schedule: daily
  slash_command: health-check
  workflow_dispatch:

permissions:
  contents: read
  actions: read
  issues: read
  pull-requests: read

imports:
  - ../aw/shared/devops-health.lock.md

tools:
  github:
    toolsets: [repos, issues, pull_requests, actions]
  cache-memory:
  bash: ["cat", "grep", "head", "tail", "find", "ls", "wc", "jq", "date", "sort", "uniq", "diff"]
  edit:

safe-outputs:
  create-issue:
    max: 1
  update-issue:
    max: 1
  add-comment:
    max: 1
  dispatch-workflow:
    workflows:
      - devops-health-investigate
    max: 10

network:
  allowed:
    - defaults
---

# DevOps Daily Health Check — Orchestrator

You are a DevOps health monitoring agent. Your job is to collect repo health signals, compute a diff against the previous run, and produce a comprehensive yet actionable health dashboard.

## High-Level Workflow

1. **Data Collection** (deterministic — use API calls and bash tools)
2. **Fingerprint & Diff** (compare against previous run via `cache-memory`)
3. **Analysis** (LLM-powered: correlate findings, identify root causes, write summary)
4. **Output** (update pinned issue + post daily comment)
5. **Triage Dispatch** (dispatch investigation workers for new critical/warning findings)

---

## Step 1: Data Collection

### 1.1 Discover Components

Scan the repository to find all skill components:

```
find src/*/plugin.json -maxdepth 2
```

Each `src/{name}/` directory containing a `plugin.json` is a component. The corresponding dashboard data file is `data/{name}.json` on `gh-pages`.

### 1.2 Pipeline Health (P1–P4)

**P1 — Failed workflow runs on `main` in last 24h:**
```
GET /repos/{owner}/{repo}/actions/runs?branch=main&status=failure&per_page=30
```
Filter to runs created within the last 24 hours. For each failed run:
- Extract `workflow_name`, `conclusion`, `job_name`, `failed_step`
- Fingerprint: `pipeline:{workflow_name}:{job_name}:{failed_step}:{conclusion}`
- Severity: 🔴 Critical if `evaluation` workflow fails; 🟡 Warning for others
- **Noise suppression:** Check if the finding matches any pattern in the `known-noise` list from `cache-memory`. If it matches, demote severity to 🔵 Info.

**P2 — Cancelled/timed-out runs in last 24h:**
```
GET /repos/{owner}/{repo}/actions/runs?branch=main&status=cancelled&per_page=10
```
- Fingerprint: `pipeline:{workflow_name}:{job_name}:timeout`
- Severity: 🟡 Warning

**P3 — Evaluation duration trend:**
```
GET /repos/{owner}/{repo}/actions/workflows/evaluation.yml/runs?branch=main&per_page=30
```
Compute average run duration over the last 14 days.
- 🟡 Warning if avg > 50 min (83% of 60-min timeout)
- 🔴 Critical if avg > 55 min
- Fingerprint: `resource:eval-duration:{bucket}` (bucket = "warning" or "critical")

**P4 — Workflow failure rate (7-day rolling):**
```
GET /repos/{owner}/{repo}/actions/runs?branch=main&per_page=100
```
Group by workflow name, compute success/failure ratio over the last 7 days.
- 🔵 Info (metric only — reported in trends table, not fingerprinted)

### 1.3 Skill Quality (Q1–Q7)

Fetch benchmark data for each discovered component:
```
GET https://raw.githubusercontent.com/{owner}/{repo}/gh-pages/data/{component}.json
```

**Q1 — Skill inventory overview table:**
Compile a comprehensive table of all skills combining local discovery with benchmark data. For each skill, classify its health status:
- **🟢 OK** — Skill has tests, scenarios pass, skilled > vanilla, no anomaly flags
- **🟡 Warning** — Skill is functional but has issues: timeouts, overfitting, or high variance (stddev > 1.5)
- **🟡 Low Value** — Some scenarios show skilled ≤ vanilla (but others show uplift)
- **🔴 No Value** — All scenarios show skilled ≤ vanilla (skill adds nothing)
- **🔴 Critical** — Skill not activated by the agent (`notActivated` flag)
- **⚪ Untested** — No test directory, no eval.yaml, or eval.yaml has 0 scenarios
- **⚪ No Data** — Skill exists locally but has no benchmark data

This table is informational (🔵 Info) and not fingerprinted. It is rendered in the issue body as a dedicated "Skill Inventory" section.

For each skill, compute:
- **Avg Skilled score**: average of all scenario "Skilled Quality" bench values in the latest entry
- **Avg Vanilla score**: average of all scenario "Vanilla Quality" bench values in the latest entry
- **Delta**: Skilled − Vanilla
- **Scenario count**: number of scenarios with benchmark data
- **Issue summary**: comma-separated list of issues (timeout, overfitting, no-uplift, high-variance, etc.)

**Q2 — Bench entries with anomaly flags:**
Scan the latest entry in **both** `entries.Quality` and `entries.Efficiency` arrays. For each bench entry, check for any property beyond the standard `name`/`unit`/`value` fields. Any extra boolean property is an anomaly flag (e.g., `notActivated`, `timedOut`, `testOverfitted`, or future flags).
- Extract the skill name and scenario from the bench `name` field (format: `"{skill}/{scenario} - {metric}"`)
- 🔴 Critical if `notActivated` (skill broken)
- 🟡 Warning for all other flags
- Fingerprint: `quality:{skill}:{scenario}:{flag-name}`
- **Deduplicate:** If the same skill/scenario/flag appears in both Quality and Efficiency arrays, report it only once.

**Q3 — Quality regression (>1.0 point drop vs 7-day rolling avg):**
For each scenario's "Skilled Quality" bench, compare the latest value to the rolling average of all entries from the last 7 calendar days (filter by `date` field).
- 🔴 Critical if drop > 2.0 points
- 🟡 Warning if drop > 1.0 points
- Fingerprint: `quality:{skill}:{scenario}:regressed`

**Q4 — Skilled ≤ Vanilla (skill adds no value):**
For the latest entry, compare `"{skill}/{scenario} - Skilled Quality"` vs `"{skill}/{scenario} - Vanilla Quality"` bench values.
- 🟡 Warning if Skilled ≤ Vanilla
- Fingerprint: `quality:{skill}:{scenario}:no-uplift`

**Q5 — High variance across runs:**
Compute the standard deviation of `"Skilled Quality"` scores across all entries from the last 7 calendar days.
- 🟡 Warning if stddev > 1.5
- Fingerprint: `quality:{skill}:{scenario}:high-variance`

**Q6 — Skills without eval tests:**
```
find src/*/skills/ -mindepth 1 -maxdepth 1 -type d
```
For each skill directory, check if a corresponding test directory exists under `src/{component}/tests/{skill-name}/`.
If the test directory exists, verify that `eval.yaml` exists and contains at least one scenario.
- 🟡 Warning if no test directory, no eval.yaml, or eval.yaml has no scenarios
- Fingerprint: `coverage:{skill}:no-tests`

**Q7 — Benchmark data staleness:**
Check if the latest entry's `date` timestamp is > 24h old (compare to current time).
- 🟡 Warning (pipeline may not be publishing)
- Fingerprint: `quality:benchmark-stale:{component}`

### 1.4 PR & Review Health (R1–R5)

```
GET /repos/{owner}/{repo}/pulls?state=open&sort=created&direction=asc&per_page=50
```

**R1 — PRs open > 7 days without review:**
Filter by `created_at` older than 7 days, then check review count (0 reviews).
- 🟡 Warning
- Fingerprint: `pr:{pr_number}:no-review`

**R2 — PRs open > 14 days (any state of review):**
- 🟡 Warning (possibly abandoned)
- Fingerprint: `pr:{pr_number}:stale`

**R3 — PRs with all checks failing:**
For each open PR, check its check runs. If all checks are failing:
- 🟡 Warning
- Fingerprint: `pr:{pr_number}:failing-checks`

**R4 — Draft PRs with no activity > 7 days:**
Filter for `draft=true` and `updated_at` older than 7 days.
- 🔵 Info
- Fingerprint: `pr:{pr_number}:stale-draft`

**R5 — PR merge velocity trend:**
```
GET /repos/{owner}/{repo}/pulls?state=closed&sort=updated&direction=desc&per_page=50
```
Count merged PRs per day over the last 7 days.
- 🔵 Info (metric only — reported in trends table, not fingerprinted)

### 1.5 Infrastructure Checks (I1–I6)

**I1 — Missing CODEOWNERS:**
```
GET /repos/{owner}/{repo}/contents/CODEOWNERS
```
If 404, also check `.github/CODEOWNERS` and `docs/CODEOWNERS`.
- 🟡 Warning if none found
- Fingerprint: `infra:no-codeowners`

**I2 — Missing Dependabot config:**
```
GET /repos/{owner}/{repo}/contents/.github/dependabot.yml
```
- 🟡 Warning if 404
- Fingerprint: `infra:no-dependabot`

**I3 — Relaxed skill validation:**
Check if `.github/workflows/validate-skills.yml` contains `fail-on-warning: false`.
- 🟡 Warning
- Fingerprint: `infra:relaxed-skill-validation`

**I4 — Verdict-warn-only mode:**
Check if `.github/workflows/evaluation.yml` contains `--verdict-warn-only`.
- 🔵 Info
- Fingerprint: `infra:verdict-warn-only`

**I5 — Dashboard deployment health:**
```
GET /repos/{owner}/{repo}/pages
```
Check last deployment status.
- 🔴 Critical if deployment failed
- Fingerprint: `infra:pages-deployment-failed`

**I6 — Third-party action version drift:**
Scan workflow YAML files for non-`actions/*` references. Flag those pinned to tags instead of SHAs.
- 🔵 Info
- Fingerprint: `infra:unpinned-action:{action_name}`

### 1.6 Resource Usage (U1–U3)

**U1 — Daily compute hours:**
Sum all workflow run durations from the last 24h.
- 🔵 Info (metric only — for trends table)

**U2 — Eval runs count:**
Count `evaluation` workflow runs in last 24h.
- 🔵 Info (metric only)

**U3 — Cost trending up:**
Use `cache-memory` to compare this week's compute hours to last week.
- 🟡 Warning if >20% increase
- Fingerprint: `resource:cost-increase`

---

## Step 2: Fingerprint & Diff

After collecting all findings, perform the diff:

1. **Load previous fingerprints** from `cache-memory` key `health-check-fingerprints`. If not available, treat as empty (first run).

2. **Compute current fingerprints** for all findings collected in Step 1.

3. **Classify each finding:**
   - **🆕 NEW**: fingerprint is in current set but NOT in previous set
   - **📌 EXISTING**: fingerprint is in both current and previous sets
   - **✅ RESOLVED**: fingerprint is in previous set but NOT in current set

4. **Track occurrences**: For EXISTING findings, increment the `occurrences` counter from the previous state. Record `first_seen` date from when the finding first appeared.

5. **Save state** to `cache-memory`:
   - `health-check-fingerprints`: current fingerprint set (with occurrence counts and first_seen dates)
   - `health-check-history`: append today's summary `{ date, new_count, existing_count, resolved_count, by_severity: { critical, warning, info } }`

6. **Sort findings** within each diff category:
   - Primary sort: severity (🔴 → 🟡 → 🔵)
   - Secondary sort: category (pipeline → quality → pr → infra → resource)

---

## Step 3: Analysis

Using the classified findings, generate:

1. **Executive summary**: One sentence describing what changed (e.g., "2 new issues detected, 1 resolved — eval pipeline is now healthy but a skill quality regression appeared")

2. **Correlation insights**: Identify connections between findings. For example:
   - A pipeline failure AND stale benchmark data → pipeline likely blocking data publication
   - Multiple quality regressions after the same date → look for a common commit

3. **Recommendations**: Prioritized list of suggested actions.

---

## Step 4: Output

### 4.1 Find or Create the Pinned Issue

Search for open issues with label `devops-health`:
- If exactly one exists → update it
- If none exist → create one with title `🏥 Repository Health Dashboard` and label `devops-health`
- If multiple exist → update the most recently created one, close the others

Before creating/updating, ensure the `devops-health` label exists. If not, create it with color `#0E8A16` and description `Daily automated health check report`.

### 4.2 Issue Body Format

Replace the entire issue body with the following structure:

```markdown
# 🏥 Daily Health Check — {date}

**Status:** 🔴 {critical_count} critical · 🟡 {warning_count} warnings · 🔵 {info_count} info
**Since yesterday:** 🆕 {new_count} new · ✅ {resolved_count} resolved · 📌 {existing_count} unchanged

---

## 🧩 Skill Inventory

> Comprehensive health status of all skills derived from Q1–Q7 checks.

| Status | Component | Skill | Skilled | Vanilla | Δ | Scenarios | Issues |
|--------|-----------|-------|--------:|--------:|--:|----------:|--------|
{For each skill, sorted by component then skill name:}
| {status_emoji} {status_label} | {component} | {skill_name} | {avg_skilled} | {avg_vanilla} | {delta} | {scenario_count} | {issue_summary} |

**Legend:** 🟢 OK · 🟡 Warning / Low Value · 🔴 No Value / Critical · ⚪ Untested / No Data

---

## 🆕 New Findings ({new_count})

> These appeared since the last health check ({previous_date}).

{For each new finding, render a full section with title, details, link, and suggested action}
{Include investigation placeholder islands for findings that qualify for dispatch — see Step 5}

---

## ✅ Resolved Since Yesterday ({resolved_count})

> These were in yesterday's report but are no longer detected.

{For each resolved finding, render with strikethrough title and resolution info}

---

## 📌 Existing Findings ({existing_count})

> These have been present since before today. Sorted by age.

{Each existing finding in a collapsed <details> tag with first_seen and occurrence count}

---

## 📊 Trends (7-day)

| Metric | Today | 7d Avg | Δ | Trend |
|--------|-------|--------|---|-------|
| Eval duration (min) | {today} | {avg} | {delta} | {arrow} |
| Eval success rate | {today} | {avg} | {delta} | {arrow} |
| PRs merged/day | {today} | {avg} | {delta} | {arrow} |
| Open PRs | {today} | {avg} | {delta} | {arrow} |
| Compute hours/day | {today} | {avg} | {delta} | {arrow} |
| Active skills | {count} | {avg} | {delta} | {arrow} |
| Skills with issues | {count} | {avg} | {delta} | {arrow} |

---

<sub>🤖 Generated by DevOps Health Check agentic workflow · [Run #{run_number}](link) · {timestamp} UTC</sub>
```

**Size guard:** If the issue body exceeds 60k characters:
- Show all 🆕 NEW findings in full (up to 10)
- Show all ✅ RESOLVED in full (up to 5)
- Limit 📌 EXISTING to top 20 by severity in collapsed `<details>` tags
- Append footer: `> … N additional existing findings omitted — see run artifacts for full report.`

### 4.3 Daily Comment

Append a short summary comment for the audit trail:

```markdown
## 📋 Health Check — {date}

🆕 {new_count} new · ✅ {resolved_count} resolved · 📌 {existing_count} unchanged

**New:**
{bullet list of new findings with emojis and links}

**Resolved:**
{bullet list of resolved findings with strikethrough}

[Full report →]({issue_url})
```

---

## Step 5: Triage Dispatch

For each 🆕 NEW finding that qualifies for investigation, dispatch a worker:

### 5.1 Dispatch Rules

| Condition | Action |
|-----------|--------|
| 🆕 NEW + 🔴 Critical | **Always dispatch** |
| 🆕 NEW + 🟡 Warning + category `pipeline` or `quality` | **Dispatch** |
| 🆕 NEW + 🟡 Warning + category `pr` or `infra` | **Skip** (self-explanatory) |
| 🆕 NEW + 🔵 Info | **Never dispatch** |
| 📌 EXISTING (any) | **Never dispatch** |
| ✅ RESOLVED (any) | **Never dispatch** |

**Budget:** Maximum 10 dispatches per run. If more than 10 qualify, prioritize by:
1. Severity descending (🔴 first)
2. Pipeline findings first
3. Quality findings second

### 5.2 For Each Dispatched Finding

1. **Insert a placeholder island** in the issue body (within the 🆕 section, right after the finding details):

```markdown
<!-- investigation:{finding_fingerprint} -->
⏳ Investigation dispatched — results arriving shortly...
<!-- /investigation:{finding_fingerprint} -->
```

2. **Dispatch the worker:**

```
dispatch-workflow:
  workflow: devops-health-investigate
  inputs:
    finding_id: "{fingerprint}"
    finding_type: "{category}"
    finding_title: "{title}"
    finding_severity: "{severity}"
    resource_url: "{link}"
    health_issue_number: "{issue_number}"
    correlation_id: "hc-{date}-{sequence}"
```

3. **Wait 5 seconds** between dispatches (platform rate limit).

---

## Guidelines

- **Be data-driven**: Include specific numbers, durations, percentages, and links.
- **Be precise with fingerprints**: Use the exact fingerprint formulas from the knowledge file. Consistency is critical — the same finding MUST produce the same fingerprint across runs.
- **First run handling**: If `cache-memory` has no previous state, note: "⚠️ This is the first health check run. All findings appear as new. Diff will resume from next run."
- **Graceful degradation**: If an API call fails, skip that check category and note the skip in the output. Don't fail the entire workflow.
- **Noise awareness**: Demote known-noise findings (matching patterns in `cache-memory` `known-noise` list) to 🔵 Info severity, but still show them in the output for audit.
- **Issue body limit**: Keep under 60k characters. Truncate EXISTING section if needed.
- **Links everywhere**: Every finding should include at least one actionable link (to the run, PR, config file, etc.).
