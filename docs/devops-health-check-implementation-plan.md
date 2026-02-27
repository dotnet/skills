# DevOps Health Check — Implementation Plan

> **Date:** 2026-02-27
> **Based on:** [devops-agentic-workflow-research.md](devops-agentic-workflow-research.md)
> **Goal:** A daily `gh aw` agentic workflow that collects repo health signals and presents them in a **diff-oriented** format — making it instantly clear what is **🆕 new**, what is **📌 preexisting**, and what has been **✅ resolved** since the last run.

---

## Table of Contents

1. [Design Philosophy: The Diff Approach](#1-design-philosophy-the-diff-approach)
2. [Architecture Overview](#2-architecture-overview)
3. [File Layout](#3-file-layout)
4. [Workflow Definition: `devops-health-check.md` + `devops-health-investigate.md`](#4-workflow-definition)
5. [Fingerprinting & Diff Engine](#5-fingerprinting--diff-engine)
6. [Health Check Catalog](#6-health-check-catalog)
7. [Output Format](#7-output-format)
8. [Pinned Issue Management](#8-pinned-issue-management)
8A. [Automated Triage & Investigation Architecture](#8a-automated-triage--investigation-architecture)
9. [Implementation Phases](#9-implementation-phases)
9A. [Local Development & Testing](#9a-local-development--testing)
10. [Risk & Mitigation](#10-risk--mitigation)
11. [Appendix: Example Output](#appendix-example-output)

---

## 1. Design Philosophy: The Diff Approach

The core insight from the research is that a flat list of health findings is **noisy** — the same 25 "Copilot code review" failures show up every day and drown out the one new eval failure that actually matters. The solution:

### 1.1 Every Finding Gets a Fingerprint

Each health finding is hashed into a stable **fingerprint** — a deterministic ID derived from its category, key attributes, and identity (but _not_ timestamps or run numbers). For example:

| Finding | Fingerprint Inputs | Example Fingerprint |
|---------|--------------------|---------------------|
| Workflow run failed | `workflow_name + conclusion + job_name + failure_step` | `wf:evaluation/job:evaluate-dotnet/step:run-skill-validator` |
| PR stale | `pr_number` | `pr:142` |
| Skill not activated | `skill_name + scenario_name` | `skill:dump-collect/scenario:basic-dump` |
| Quality regression | `skill_name + scenario_name + direction` | `quality:binlog-failure-analysis/diag-errors/regressed` |
| Infra missing | `config_type` | `infra:no-codeowners` |

### 1.2 State Transitions Drive the Diff

The workflow uses `cache-memory` to persist the set of fingerprints from the previous run. On each new run:

| Previous Run | Current Run | Classification | Display |
|-------------|-------------|----------------|---------|
| ❌ absent | ✅ present | **🆕 NEW** | Red highlight, top of section |
| ✅ present | ✅ present | **📌 EXISTING** | Collapsed/dimmed, grouped at bottom |
| ✅ present | ❌ absent | **✅ RESOLVED** | Green highlight, shown in "Resolved" section |

This is the fundamental mechanism: **if a developer only reads the 🆕 and ✅ sections, they see exactly what changed overnight.**

### 1.3 Severity Still Matters

Within each diff category, findings are still ordered by severity (🔴 → 🟡 → 🔵). A new 🔵-info item appears _above_ an existing 🔴-critical item, because **novelty is the primary axis** and severity is secondary.

---

## 2. Architecture Overview

Following the research's **Option C (Hybrid)** recommendation:

```
┌─────────────────────────────────────┐
│  GitHub Actions cron (daily 00:00)  │
│  or  /health-check slash command    │
│  or  workflow_dispatch (manual)     │
└──────────────┬──────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────────────────────────┐
│  ORCHESTRATOR: devops-health-check.md  (gh aw runtime)         │
│                                                                 │
│  1. DATA COLLECTION (deterministic)                             │
│     ├─ GitHub MCP: Actions API                                  │
│     ├─ GitHub MCP: PRs API                                      │
│     ├─ GitHub MCP: Issues API                                   │
│     ├─ GitHub MCP: Repos API                                    │
│     ├─ bash: parse benchmark JSON                               │
│     └─ bash: file system checks                                 │
│                                                                 │
│  2. FINGERPRINT & DIFF                                          │
│     ├─ Hash each finding                                        │
│     ├─ Load previous fingerprints from cache-memory             │
│     ├─ Classify: NEW / EXISTING / RESOLVED                      │
│     └─ Save current fingerprints                                │
│                                                                 │
│  3. ANALYSIS (LLM-powered)                                      │
│     ├─ Correlate findings                                       │
│     ├─ Identify root causes                                     │
│     ├─ Generate recommendations                                 │
│     └─ Write natural-language summary of what changed           │
│                                                                 │
│  4. OUTPUT                                                      │
│     ├─ Update pinned issue body (with investigation placeholders)│
│     ├─ Post comment with day's diff                             │
│     └─ (future) Update dashboard                                │
│                                                                 │
│  5. TRIAGE DISPATCH (for 🆕 findings ≥ 🟡 severity)             │
│     └─ dispatch-workflow → devops-health-investigate             │
│        (one dispatch per finding, up to 10)                     │
└──────────────┬──────────────────────────────────────────────────┘
               │  dispatch-workflow (with inputs:
               │  finding_id, finding_type, resource_url,
               │  health_issue_number, correlation_id)
               │
               ▼  ×N (up to 10 concurrent workers)
┌─────────────────────────────────────────────────────────────────┐
│  WORKER: devops-health-investigate.md  (separate gh aw runtime) │
│                                                                 │
│  Each worker has FRESH CONTEXT and investigates ONE finding:    │
│                                                                 │
│  1. DEEP INVESTIGATION                                          │
│     ├─ Read failing workflow run logs                            │
│     ├─ Analyze error messages & stack traces                     │
│     ├─ Check recent commits for possible causes                 │
│     ├─ Compare with previous successful runs                    │
│     └─ Correlate with related PRs / issues                      │
│                                                                 │
│  2. ROOT CAUSE ANALYSIS (LLM-powered)                           │
│     ├─ Determine most likely cause                              │
│     ├─ Assess blast radius                                      │
│     └─ Generate remediation steps                               │
│                                                                 │
│  3. REPORT BACK                                                 │
│     └─ update-issue: replace-island on the pinned health issue  │
│        (populates the investigation placeholder for this finding)│
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. File Layout

**Recommended:** `eng/agentic-workflows/` (cross-cutting infrastructure)

```
eng/agentic-workflows/
├── devops-health-check.md              # ← NEW: Orchestrator — daily health check workflow
├── devops-health-investigate.md        # ← NEW: Worker — deep investigation of individual findings
└── shared/
    └── compiled/
        ├── devops-health.lock.md       # ← NEW: Compiled knowledge (health check catalog,
        │                               #    fingerprinting rules, output templates)
        └── devops-investigate.lock.md  # ← NEW: Compiled knowledge (investigation playbooks,
                                        #    root-cause patterns, remediation templates)

src/dotnet-msbuild/agentic-workflows/   # (existing — unchanged)
├── shared/
│   ├── compiled/
│   │   ├── build-errors.lock.md        # (existing)
│   │   ├── performance.lock.md         # (existing)
│   │   └── style-and-modernization.lock.md  # (existing)
│   └── binlog-mcp.md                   # (existing)
├── build-failure-analysis.md           # (existing)
├── build-perf-audit.md                 # (existing)
├── msbuild-pr-review.md               # (existing)
└── README.md                           # ← UPDATE: Add new workflows to table
```

> **Recommended location: `eng/agentic-workflows/`** — This workflow monitors the _entire repo_ (all components, all pipelines, all PRs), not just the `dotnet-msbuild` component. Placing it under `eng/` reflects its cross-cutting infrastructure role. The compiled knowledge files live co-located at `eng/agentic-workflows/shared/compiled/`.
>
> The existing `src/dotnet-msbuild/agentic-workflows/shared/` infrastructure (binlog-mcp, compiled knowledge) remains where it is — those are MSBuild-specific. The health check imports only its own knowledge files.
>
> **Alternative (if team prefers co-location):** Keep it at `src/dotnet-msbuild/agentic-workflows/` alongside the other workflows. This is simpler if the team wants all agentic workflows in one place regardless of scope.

---

## 4. Workflow Definition

### 4.1 Frontmatter

```yaml
---
name: "DevOps Daily Health Check"
description: >
  Orchestrator workflow that collects repo health signals daily (pipelines,
  skill quality, PRs, infrastructure), computes a fingerprint-based diff
  against the previous run, updates a pinned health dashboard issue, and
  dispatches investigation workers for new critical/warning findings.

on:
  schedule: "0 0 * * *"    # Run daily at midnight UTC
  slash_command: health-check   # On-demand via /health-check comment (native gh-aw feature, no extra infra needed)
  workflow_dispatch:        # Manual trigger from Actions UI or `gh aw run`

permissions:
  contents: read
  actions: read
  issues: write            # Create/update the pinned health issue
  pull-requests: read

imports:
  - shared/compiled/devops-health.lock.md

tools:
  github:
    toolsets: [repos, issues, pull_requests, actions]
  cache-memory:            # Persist fingerprints across runs
  bash: ["cat", "grep", "head", "tail", "find", "ls", "wc", "jq", "date", "sort", "uniq", "diff"]
  edit:

safe-outputs:
  create-issue:
    max: 1
  update-issue:
    max: 1
  add-comment:
    max: 1
  dispatch-workflow:        # ← NEW: Fan out to investigation workers
    workflows:
      - devops-health-investigate
    max: 10                 # Up to 10 parallel investigations per run

network:
  allowed:
    - defaults
---
```

### 4.2 Investigation Worker Frontmatter

```yaml
---
name: "DevOps Health — Deep Investigation"
description: >
  Worker agent that performs deep root-cause analysis on a single
  health check finding. Dispatched by the health check orchestrator.

on:
  workflow_dispatch:
    inputs:
      finding_id:
        description: "Fingerprint ID of the finding to investigate"
        required: true
      finding_type:
        description: "Category: pipeline | quality | pr | infra | resource"
        required: true
      finding_title:
        description: "Human-readable title of the finding"
        required: true
      finding_severity:
        description: "Severity: critical | warning | info"
        required: true
      resource_url:
        description: "URL to the primary resource (run, PR, etc.)"
        required: true
      health_issue_number:
        description: "Issue number of the pinned health dashboard"
        required: true
      correlation_id:
        description: "Unique ID linking this investigation to the health check run"
        required: true

permissions:
  contents: read
  actions: read
  issues: write            # Update the pinned health issue with findings
  pull-requests: read

imports:
  - shared/compiled/devops-investigate.lock.md

tools:
  github:
    toolsets: [repos, issues, pull_requests, actions]
  bash: ["cat", "grep", "head", "tail", "find", "ls", "wc", "jq", "date", "sort", "diff"]

safe-outputs:
  update-issue:
    max: 1                  # Replace investigation island on the health issue
  add-comment:
    max: 1                  # Optional: add detailed comment

network:
  allowed:
    - defaults
---
```

### 4.3 Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Trigger** | Daily at midnight UTC (`0 0 * * *`) + `/health-check` slash command + `workflow_dispatch` (manual) | Routine monitoring + on-demand flexibility from UI or CLI (`gh aw run devops-health-check`) |
| **Slash command** | `slash_command: health-check` — native gh-aw feature, no extra webhook/app infra needed | The GitHub App installed for `gh aw` already listens for issue/PR comments matching the configured pattern. No additional setup required. |
| **Detection latency** | ~24h worst-case for scheduled runs (midnight UTC) | Failures occurring after 00:00 UTC won't be caught until the next day. Mitigated by: (1) `/health-check` slash command for on-demand checks, (2) `workflow_dispatch` from Actions UI or CLI. If latency matters, a second scheduled run at 12:00 UTC can be added (`schedule: ["0 0 * * *", "0 12 * * *"]`). |
| **MCP tools** | `github` (actions, issues, PRs, repos) | Covers all four data domains from the research |
| **gh-pages data access** | `gh api "https://raw.githubusercontent.com/.../gh-pages/data/{component}.json"` via GitHub MCP | Fetches raw file content from the gh-pages branch without needing `curl` in the bash tool list. Same pattern used in testing data probes (§9A.3). |
| **Persistence** | `cache-memory` | Stores fingerprint sets + diff summary history (see §5.3). **Trends table** metrics are **re-computed each run** from live API/dashboard data — not persisted. |
| **Output** | Single pinned issue + daily comment | Single well-known URL; comment history provides audit trail |
| **Investigation** | `dispatch-workflow` → worker agents | Each finding investigated by a fresh agent with clean context (see §8A) |
| **Worker limit** | max: 10 dispatches per run | Balances thoroughness with compute cost; only 🆕 findings ≥ 🟡 severity trigger investigation |
| **Noise suppression** | Configurable `known-noise` list in `cache-memory`, editable via `/health-check suppress <fingerprint-pattern>` | Initial list: `copilot-code-review` (known org-level noise). New patterns can be added at runtime without recompiling the workflow. Suppressed findings are demoted to 🔵 Info, not hidden — still visible in the Existing section for audit. |
| **No binlog-mcp** | Omitted from orchestrator | This workflow reads _metadata_ about builds, not binlogs themselves |
| **No dotnet runtime** | Omitted | Pure data collection + LLM analysis, no compilation needed |

---

## 5. Fingerprinting & Diff Engine

This is the core innovation. The compiled knowledge file (`devops-health.lock.md`) must encode the fingerprinting rules so the LLM applies them consistently.

### 5.1 Fingerprint Schema

Each finding produces a structured record:

```json
{
  "id": "wf-eval-dotnet-run-skill-validator",
  "category": "pipeline",
  "severity": "critical",
  "title": "Evaluation failed on main: evaluate (dotnet) → Run skill-validator",
  "details": "Run #221 failed at 2026-02-25T14:30:00Z. Error: process exited with code 1.",
  "link": "https://github.com/dotnet/skills/actions/runs/12345",
  "fingerprint": "pipeline:evaluation:evaluate-dotnet:run-skill-validator:failure",
  "first_seen": "2026-02-25",
  "occurrences": 2
}
```

### 5.2 Fingerprint Rules by Category

#### Pipeline Health
```
fingerprint = "pipeline:{workflow_name}:{job_name}:{failed_step}:{conclusion}"
```
- Same workflow + job + step + conclusion = same finding (even across different run IDs)
- A workflow that fails in a _different_ step is a _different_ finding

#### Skill Quality
```
fingerprint = "quality:{skill_name}:{scenario_name}:{signal}"
  where signal ∈ { "{flag-name}", "regressed", "no-uplift", "high-variance" }
  {flag-name} = any non-standard property found on a bench entry
    (e.g., "notActivated", "timedOut", "testOverfitted", or any future flag)
```
- Anomaly flags are dynamically discovered: any boolean property beyond `name`/`unit`/`value` on a bench entry
- Regression is detected by comparing the latest benchmark data point to the rolling average

#### PR Health
```
fingerprint = "pr:{pr_number}:{signal}"
  where signal ∈ { "stale", "no-review", "failing-checks" }
```
- A PR that was "stale" last run and is still "stale" this run → EXISTING
- A PR that was "stale" but got merged/closed → RESOLVED

#### Infrastructure
```
fingerprint = "infra:{config_key}"
  where config_key ∈ { "no-codeowners", "no-dependabot", "relaxed-skill-validation", ... }
```
- These are mostly static; they'll show as NEW once and EXISTING thereafter until fixed

#### Resource Usage
```
fingerprint = "resource:{metric}:{threshold_breach}"
```
- Only fingerprinted when a threshold is breached (e.g., "eval duration > 50min")

### 5.3 Diff Algorithm (Pseudo-code)

```
previous_fps = cache_memory_load("health-check-fingerprints") ?? {}
current_fps  = {}

for each finding in collect_all_findings():
    fp = compute_fingerprint(finding)
    current_fps[fp] = finding

new_findings      = { fp: f for fp, f in current_fps  if fp NOT IN previous_fps }
existing_findings = { fp: f for fp, f in current_fps  if fp IN previous_fps }
resolved_findings = { fp: f for fp, f in previous_fps if fp NOT IN current_fps }

cache_memory_save("health-check-fingerprints", current_fps)
cache_memory_save("health-check-history", append(load("health-check-history"), {
    date: today,
    new_count: len(new_findings),
    existing_count: len(existing_findings),
    resolved_count: len(resolved_findings),
    by_severity: { critical: N, warning: N, info: N }
}))
```

> **What is persisted vs re-computed:** Only the fingerprint set and the diff summary history (above) are stored in `cache-memory`. The **trends table** metrics (eval duration, PR velocity, compute hours, etc.) are **re-computed each run** from live API queries and dashboard JSON data. This avoids stale-data risks and keeps the cache small. The 7-day averages in the trends table are computed from the raw API data for that window, not from cached values.

### 5.4 Occurrence Tracking

For findings that recur, the `occurrences` counter (from cache-memory) is incremented. This enables messaging like:
> "📌 **Copilot code review failing** — 14th consecutive day (first seen: 2026-02-13)"

---

## 6. Health Check Catalog

These are the specific checks the agent performs, prioritized per the research. Each check maps to a data source, a fingerprint pattern, and a severity rule.

### 6.1 Pipeline Health Checks

| ID | Check | Data Source | Severity Rule | Fingerprint |
|----|-------|-------------|---------------|-------------|
| P1 | Failed workflow runs on `main` in last 24h | `gh api /repos/{owner}/{repo}/actions/runs?branch=main&status=failure&created=>={yesterday}` | 🔴 if `evaluation` fails; 🟡 for others; findings matching a `known-noise` pattern in `cache-memory` are demoted to 🔵 (see design decisions — Noise suppression) | `pipeline:{wf}:{job}:{step}:{conclusion}` |
| P2 | Cancelled / timed-out runs in last 24h | Actions API `status=cancelled` | 🟡 Warning | `pipeline:{wf}:{job}:timeout` |
| P3 | Evaluation duration trend | Actions API: run durations for `evaluation` workflow on main, last 14 days | 🟡 if avg > 50 min (83% of 60-min timeout); 🔴 if avg > 55 min | `resource:eval-duration:{bucket}` |
| P4 | Workflow failure rate (7-day rolling) | Actions API: success/failure counts per workflow | 🔵 Info (always reported for trending) | — (metric only, not fingerprinted) |

### 6.2 Skill Quality Checks

> **Data source:** Checks Q1–Q4 and Q6 read the **same `data/{component}.json` files on `gh-pages`** that power the [benchmark dashboard](../eng/dashboard/dashboard.html). These files are generated by [`generate-benchmark-data.ps1`](../eng/dashboard/generate-benchmark-data.ps1) from `skill-validator` results and contain the full eval run history: quality scores (0-10), efficiency metrics, anomaly flags, commit info, and timestamps — per skill, per scenario, per run. Bench entries have a standard schema of `{ name, unit, value }` — any additional boolean properties (e.g., `notActivated`, `timedOut`, `testOverfitted`, or future flags added to `generate-benchmark-data.ps1`) are **anomaly flags** indicating non-standard outcomes. The health check dynamically detects all such flags rather than hardcoding specific ones, so new flag types are picked up automatically. Using the dashboard's own data source means the health check sees exactly what the dashboard sees, and any eval failures or regressions from the last 24h are immediately visible by comparing the latest entries against the historical time series.
>
> **Component discovery:** The orchestrator discovers components by scanning `src/*/plugin.json` on the repo file system (using `find` / `ls` from the bash toolset). Each `src/{name}/` directory containing a `plugin.json` is treated as a component. The corresponding dashboard data file is `data/{name}.json` on `gh-pages`.
>
> **Data access pattern:** The agent fetches gh-pages data via `gh api "https://raw.githubusercontent.com/{owner}/{repo}/gh-pages/data/{component}.json"` through the GitHub MCP toolset. This works for raw file content without requiring `curl` (which is intentionally omitted from the bash tool list). The same pattern works in the testing data probes (§9A.3).

| ID | Check | Data Source | Severity Rule | Fingerprint |
|----|-------|-------------|---------------|-------------|
| Q1 | Bench entries with anomaly flags | Dashboard data: `gh-pages/data/{component}.json` → latest entry in **both** `entries.Quality` and `entries.Efficiency` arrays. Scan each bench for any property beyond the standard `name`/`unit`/`value` fields (e.g., `notActivated`, `timedOut`, `testOverfitted`, or any future flag). Both arrays carry the same flags — see [`generate-benchmark-data.ps1`](../eng/dashboard/generate-benchmark-data.ps1). Dynamically discovers all non-standard boolean properties. | 🔴 Critical if `notActivated` (skill broken); 🟡 Warning for all other flags | `quality:{skill}:{scenario}:{flag-name}` |
| Q2 | Quality regression (>1.0 point drop vs 7-day rolling avg) | Dashboard data: compare latest entry's quality scores to rolling avg of all entries from the last 7 calendar days (filter by `date` field, not entry count — resilient to eval frequency changes) | 🔴 if drop > 2.0; 🟡 if drop > 1.0 | `quality:{skill}:{scenario}:regressed` |
| Q3 | Skilled ≤ Vanilla (skill adds no value) | Dashboard data: latest entry, compare `Skilled Quality` vs `Vanilla Quality` benches for same scenario | 🟡 Warning (may indicate skill not helping) | `quality:{skill}:{scenario}:no-uplift` |
| Q4 | High variance across runs | Dashboard data: all entries from the last 7 calendar days (filter by `date` field), compute stddev of `Skilled Quality` scores per scenario | 🟡 Warning (unreliable results) | `quality:{skill}:{scenario}:high-variance` |
| Q5 | Skills without eval tests | File system: `find src/*/skills/*/` with no matching `tests/` entry | 🟡 Warning | `coverage:{skill}:no-tests` |
| Q6 | Benchmark data staleness | Dashboard data: check if latest entry's `date` timestamp is > 24h old | 🟡 Warning (pipeline may not be publishing) | `quality:benchmark-stale:{component}` |

### 6.3 PR & Review Health Checks

| ID | Check | Data Source | Severity Rule | Fingerprint |
|----|-------|-------------|---------------|-------------|
| R1 | PRs open > 7 days without review | PRs API: `state=open`, filter by `created_at` and review count | 🟡 Warning | `pr:{number}:no-review` |
| R2 | PRs open > 14 days (any state of review) | PRs API | 🟡 Warning (possibly abandoned) | `pr:{number}:stale` |
| R3 | PRs with all checks failing | PRs API + check runs | 🟡 Warning | `pr:{number}:failing-checks` |
| R4 | Draft PRs with no activity > 7 days | PRs API: `draft=true` | 🔵 Info | `pr:{number}:stale-draft` |
| R5 | PR merge velocity trend | PRs API: merged PRs per day, 7-day rolling | 🔵 Info (metric only) | — |

### 6.4 Infrastructure Checks

| ID | Check | Data Source | Severity Rule | Fingerprint |
|----|-------|-------------|---------------|-------------|
| I1 | Missing CODEOWNERS | `gh api /repos/{owner}/{repo}/contents/CODEOWNERS` → 404 | 🟡 Warning | `infra:no-codeowners` |
| I2 | Missing Dependabot config | `gh api /repos/{owner}/{repo}/contents/.github/dependabot.yml` → 404 | 🟡 Warning | `infra:no-dependabot` |
| I3 | Relaxed skill validation | Parse `validate-skills.yml`: check for `fail-on-warning: false` | 🟡 Warning | `infra:relaxed-skill-validation` |
| I4 | Verdict-warn-only mode | Parse `evaluation.yml`: check for `--verdict-warn-only` | 🔵 Info (intentional, but worth tracking) | `infra:verdict-warn-only` |
| I5 | Dashboard deployment health | Pages API: check last deployment status | 🔴 if failed | `infra:pages-deployment-failed` |
| I6 | Third-party action version drift | Parse workflow YAMLs for non-`actions/*` references; check if pinned to SHA vs tag | 🔵 Info | `infra:unpinned-action:{action_name}` |

### 6.5 Resource Usage Checks

| ID | Check | Data Source | Severity Rule | Fingerprint |
|----|-------|-------------|---------------|-------------|
| U1 | Daily compute hours | Actions API: sum of all run durations in last 24h | 🔵 Info | — (metric only) |
| U2 | Eval runs count | Actions API: count of `evaluation` runs in last 24h | 🔵 Info | — (metric only) |
| U3 | Cost trending up | cache-memory: compare this week's compute to last week | 🟡 if >20% increase | `resource:cost-increase` |

---

## 7. Output Format

### 7.1 Issue Body (Replaced Daily)

The pinned issue body is **fully replaced** each run. It always shows the current state. The structure:

```markdown
# 🏥 Daily Health Check — 2026-02-27

**Status:** 🔴 2 critical · 🟡 5 warnings · 🔵 3 info
**Since yesterday:** 🆕 3 new · ✅ 1 resolved · 📌 6 unchanged

---

## 🆕 New Findings (3)

> These appeared since the last health check (2026-02-26).

### 🔴 Evaluation failed on main: evaluate (dotnet)
- **Run:** [#14523](link) — failed at step "Run skill-validator"
- **When:** 2026-02-27 04:00 UTC (scheduled run)
- **Impact:** Benchmark data not published for `dotnet` component
- **Suggested action:** Check [run logs](link). Recent similar failure: #221 (2026-02-24).

### 🟡 PR #185 has been open 8 days without review
- **Author:** @danmoseley
- **Title:** "Add nullable annotations skill"
- **Suggested action:** Assign a reviewer or add to triage.

### 🔵 Eval compute trending up: 9.2h/day (+12% vs last week)
- Previous week average: 8.2h/day
- Driven by new `dotnet-pinvoke` test suite added in #178

---

## ✅ Resolved Since Yesterday (1)

> These were in yesterday's report but are no longer detected.

### ~~🟡 Dashboard data stale for `dotnet` component~~
- **Was stale for:** 2 days (since 2026-02-24)
- **Resolved by:** Successful eval run #14520 published fresh data

---

## 📌 Existing Findings (6)

> These have been present since before today. Sorted by age.

<details><summary>🔴 <code>dump-collect/basic-dump</code> — skill not activating (12 days)</summary>

- **First seen:** 2026-02-15
- **Tracked in:** [#137](link)
- **Occurrences:** 12 consecutive days
- **Impact:** Benchmark data polluted with vanilla-only results

</details>

<details><summary>🟡 Copilot code review consistently failing (14 days)</summary>

- **First seen:** 2026-02-13
- **Failure rate:** 23/25 runs in last 7 days (92%)
- **Note:** Org-level workflow, not repo-controlled
- **Suggested action:** Contact org admins or suppress in CI status checks

</details>

<!-- ... more existing findings in collapsed sections ... -->

---

## 📊 Trends (7-day)

| Metric | Today | 7d Avg | Δ | Trend |
|--------|-------|--------|---|-------|
| Eval duration (min) | 44.1 | 43.3 | +0.8 | ↗️ |
| Eval success rate | 100% | 96% | +4% | ✅ |
| PRs merged/day | 4 | 3.2 | +0.8 | ↗️ |
| Open PRs | 28 | 30 | -2 | ✅ |
| Compute hours/day | 9.2 | 8.6 | +0.6 | ↗️ |
| Active skills | 16 | 16 | 0 | ➡️ |
| Skills with issues | 2 | 2 | 0 | ➡️ |

---

<sub>🤖 Generated by DevOps Health Check agentic workflow · [Run #14525](link) · 2026-02-27 06:15 UTC</sub>
```

### 7.2 Issue Comment (Appended Daily)

Each run also appends a short comment for audit trail:

```markdown
## 📋 Health Check — 2026-02-27

🆕 3 new · ✅ 1 resolved · 📌 6 unchanged

**New:**
- 🔴 Evaluation failed on main: evaluate (dotnet) [#14523](link)
- 🟡 PR #185 open 8 days without review
- 🔵 Compute trending up (+12%)

**Resolved:**
- ~~🟡 Dashboard data stale for `dotnet`~~

[Full report →](link to issue body)
```

### 7.3 Design Rationale for Output Format

| Design Choice | Rationale |
|---------------|-----------|
| 🆕 section at the top | **The diff** — the most important information is what changed |
| ✅ Resolved section second | Positive feedback loop — people see things getting fixed |
| 📌 Existing in `<details>` | Reduces noise; expand only if investigating |
| Trends table at the bottom | Context for "is the repo improving overall?" |
| Severity emojis (🔴🟡🔵) | Matches existing repo conventions (build-perf-audit, PR review) |
| `first_seen` + `occurrences` | Creates urgency for long-standing issues |
| Links to runs/PRs/issues | Actionable — one click to investigate |

---

## 8. Pinned Issue Management

### 8.1 Issue Lifecycle

1. **First run:** Create issue titled `🏥 Repository Health Dashboard` with label `devops-health`.
2. **Subsequent runs:** Find the open issue by label, replace its body, add a comment.
3. **Pin the issue** manually once (or via API if available).
4. **Never close** the issue — it's a living dashboard.

### 8.2 Finding the Issue

```
Search for open issues with label "devops-health" in this repo.
If exactly one exists, update it.
If none exist, create one and add the "devops-health" label.
If multiple exist, update the most recently created one and close the others.
```

### 8.3 Label Setup

The workflow should verify the label exists:
```
Check if label "devops-health" exists. If not, create it with color #0E8A16 and
description "Daily automated health check report".
```

---

## 8A. Automated Triage & Investigation Architecture

### 8A.1 Problem Statement

The daily health check identifies _what_ is broken, but not _why_. A human reading "🔴 Evaluation failed at step Run skill-validator" still needs to:

1. Open the failed run
2. Read through logs
3. Check recent commits for possible regressions
4. Compare with previous successful runs
5. Formulate a hypothesis and remediation plan

This is time-consuming, context-heavy work — exactly what a dedicated agent with fresh context should do automatically.

### 8A.2 gh-aw Constraints & Design

**Key constraint:** A single gh-aw workflow = a single agent. There is no way to spawn multiple isolated agents within one workflow run.

**Solution:** gh-aw's **Orchestrator/Worker pattern** via the `dispatch-workflow` safe output. The health check orchestrator dispatches a separate worker workflow for each finding that needs deep investigation. Each worker runs as its own gh-aw agent with:
- **Fresh context** — no accumulated state pollution from the overview analysis
- **Focused scope** — investigates exactly one finding
- **Dedicated tools** — can be given different permissions or MCP servers than the orchestrator
- **Independent failure** — a worker crash doesn't affect the overview or other investigations

### 8A.3 Investigation Flow

```
┌─── ORCHESTRATOR: devops-health-check.md ───────────────────────┐
│                                                                 │
│  After computing the diff, for each 🆕 finding with            │
│  severity ≥ 🟡 (warning):                                       │
│                                                                 │
│  1. Insert a placeholder island in the issue body:              │
│     <!-- investigation:{finding_id} -->                         │
│     ⏳ Investigation dispatched — pending results...            │
│     <!-- /investigation:{finding_id} -->                        │
│                                                                 │
│  2. Dispatch the worker:                                        │
│     dispatch-workflow:                                           │
│       workflow: devops-health-investigate                        │
│       inputs:                                                    │
│         finding_id: "pipeline:evaluation:evaluate-dotnet:..."   │
│         finding_type: "pipeline"                                │
│         finding_title: "Evaluation failed on main"              │
│         finding_severity: "critical"                            │
│         resource_url: "https://github.com/.../runs/14523"       │
│         health_issue_number: "42"                               │
│         correlation_id: "hc-2026-02-27-001"                     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
         │
         │  5-second delay between dispatches (platform rate limit)
         │
         ▼  ×N workers (up to 10)
┌─── WORKER: devops-health-investigate.md ───────────────────────┐
│                                                                 │
│  Receives inputs, performs category-specific deep dive:         │
│                                                                 │
│  Pipeline findings → read run logs, identify error, check       │
│    recent commits, compare with last successful run             │
│                                                                 │
│  Quality findings → analyze benchmark time series, check        │
│    skill definition changes, compare with vanilla baseline      │
│                                                                 │
│  PR findings → check PR activity, review status, identify       │
│    potential reviewers, assess merge readiness                  │
│                                                                 │
│  Infra findings → audit config files, check if intentional,    │
│    compare with repo best practices                             │
│                                                                 │
│  Then reports back via update-issue with replace-island:        │
│                                                                 │
│  update-issue:                                                  │
│    issue: {health_issue_number}                                 │
│    operation: replace-island                                    │
│    island: "investigation:{finding_id}"                         │
│    body: |                                                      │
│      🔍 **Investigation Complete** — [Run #{worker_run}](link) │
│      **Root cause:** SDK version mismatch in global.json ...    │
│      **Confidence:** High                                       │
│      **Suggested fix:** Update global.json to pin SDK 9.0.200   │
│      **Related:** Commit abc1234 by @user (2h ago)              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 8A.4 The `replace-island` Mechanism

The gh-aw `update-issue` safe output supports an `operation: replace-island` mode. This replaces content between HTML comment markers in the issue body:

```markdown
<!-- investigation:pipeline:evaluation:evaluate-dotnet:failure -->
⏳ Investigation dispatched — pending results...
<!-- /investigation:pipeline:evaluation:evaluate-dotnet:failure -->
```

When the worker completes, the content between the markers is replaced:

```markdown
<!-- investigation:pipeline:evaluation:evaluate-dotnet:failure -->
🔍 **Investigation Complete** — [Worker Run #14530](link)

**Root cause:** The `SkillValidator` binary was rebuilt against .NET SDK 9.0.201
but the `global.json` still pins 9.0.200. The SDK mismatch causes a runtime
loader failure at step "Run skill-validator".

**Confidence:** High (error message explicitly states version mismatch)

**Blast radius:** All `dotnet` component evaluations are blocked. The `dotnet-msbuild`
component is unaffected (uses a different validator path).

**Suggested fix:**
1. Update `global.json` to pin SDK 9.0.201
2. Or revert `eng/skill-validator/SkillValidator.csproj` to target 9.0.200

**Related commits:**
- `abc1234` by @viktorhofer — "Update SDK version" (2h ago)

**Related issues:** None found
<!-- /investigation:pipeline:evaluation:evaluate-dotnet:failure -->
```

This means the pinned health issue becomes a **self-updating document**: the orchestrator creates the overview with placeholders, and workers asynchronously fill in the investigation details over the following minutes.

### 8A.5 Dispatch Rules: What Gets Investigated

Not every finding warrants a dedicated agent investigation. The orchestrator applies these rules:

| Condition | Action |
|-----------|--------|
| 🆕 NEW + 🔴 Critical | **Always dispatch** investigation |
| 🆕 NEW + 🟡 Warning | **Dispatch** if finding is in `pipeline` or `quality` category |
| 🆕 NEW + 🟡 Warning (PR/Infra) | **Skip** — PR staleness and config issues are self-explanatory |
| 🆕 NEW + 🔵 Info | **Never dispatch** — informational items don't need investigation |
| 📌 EXISTING (any severity) | **Never dispatch** — was already investigated when it was NEW |
| ✅ RESOLVED (any severity) | **Never dispatch** — issue is gone |

**Budget guard:** Even with the above filters, the orchestrator caps at 10 dispatches per run. If more than 10 findings qualify, it prioritizes by: (1) severity descending, (2) pipeline findings first, (3) quality findings second.

### 8A.6 Investigation Playbooks (by Category)

The worker's compiled knowledge file (`devops-investigate.lock.md`) contains category-specific investigation playbooks:

#### Pipeline Investigation Playbook
```
When finding_type == "pipeline":
1. Fetch the failed run using the resource_url
2. Identify the exact failed step and read its logs (last 200 lines)
3. Extract error messages, exception types, and exit codes
4. Fetch the last 5 successful runs of the same workflow
5. Compare: what changed between the last success and this failure?
6. Check commits to the repo between the last success and this failure
7. Check if the failure is in repo code or in a GitHub Action version update
8. Determine root cause with confidence level (High/Medium/Low)
9. Generate 1-3 specific remediation steps
10. Check if there's already an open issue tracking this
```

#### Quality Investigation Playbook
```
When finding_type == "quality":
1. Fetch benchmark data for the affected skill (last 14 data points)
2. Plot the trend: is this a sudden drop or gradual degradation?
3. For anomaly flags (notActivated, timedOut, testOverfitted, etc.):
   a. Identify which flag(s) are set on the bench entry
   b. For notActivated: check the skill definition file for syntax/trigger issues
   c. For timedOut: check recent complexity changes and timeout thresholds
   d. For other/unknown flags: describe the flag and check generate-benchmark-data.ps1
      for context on when this flag is set
4. For regression: identify the exact eval run where quality dropped
5. Check commits between the last good data point and the regression
6. For high-variance: analyze the spread across recent runs
7. Check if the skill's test definition changed recently
8. Determine root cause with confidence level
9. Generate remediation steps specific to skill quality
```

#### PR Investigation Playbook
```
When finding_type == "pr":
1. Fetch PR details including timeline, reviews, and check status
2. For no-review: identify potential reviewers from CODEOWNERS or git blame
3. For stale: check if there's activity on related issues
4. For failing-checks: identify which checks fail and cross-reference with
   known pipeline issues from the same health check
5. Provide a summary of what's blocking the PR
```

### 8A.7 Worker Failure Handling

Workers are independent and may fail (API errors, timeout, etc.). The design handles this gracefully:

| Scenario | Result |
|----------|--------|
| Worker succeeds | Island is replaced with investigation results |
| Worker times out | Island retains placeholder: "⏳ Investigation dispatched — pending results..." |
| Worker fails | Island retains placeholder; next day's health check can re-dispatch if finding persists |
| Worker produces poor analysis | The placeholder remains until manually edited or next investigation |

The orchestrator does **not** wait for workers to complete (they are dispatched fire-and-forget via `dispatch-workflow`). The health issue may briefly show "⏳ pending" placeholders that fill in over the next 5-15 minutes as workers finish.

### 8A.8 Example: End-to-End Triage Experience

**06:00 UTC** — Health check orchestrator runs.

**06:08 UTC** — Orchestrator finishes. Pinned issue is updated:

```markdown
## 🆕 New Findings (2)

### 🔴 Evaluation failed on main: evaluate (dotnet) → Run skill-validator
- **Run:** [#14523](link) — failed at 2026-02-27 04:00 UTC
- **Impact:** Benchmark data not published for `dotnet` component

<!-- investigation:pipeline:evaluation:evaluate-dotnet:run-skill-validator:failure -->
⏳ Investigation dispatched — results arriving shortly...
<!-- /investigation:pipeline:evaluation:evaluate-dotnet:run-skill-validator:failure -->

### 🟡 `csharp-scripts/basic-script` quality regressed by 2.3 points
- **Previous 7-day avg:** 7.8 → **Latest:** 5.5
- **Component:** dotnet

<!-- investigation:quality:csharp-scripts:basic-script:regressed -->
⏳ Investigation dispatched — results arriving shortly...
<!-- /investigation:quality:csharp-scripts:basic-script:regressed -->
```

**06:12 UTC** — First worker (pipeline investigation) completes:

```markdown
<!-- investigation:pipeline:evaluation:evaluate-dotnet:run-skill-validator:failure -->
🔍 **Investigation Complete** — [Worker Run #14530](link)

**Root cause:** The `SkillValidator` binary targets .NET SDK 9.0.201 (updated in
commit `abc1234` by @viktorhofer, 8h ago) but `global.json` still pins 9.0.200.
The SDK resolution fails silently, causing the validator to crash with exit code 1.

**Confidence:** High — the error log explicitly shows:
> `A compatible .NET SDK version for global.json version [9.0.200] was not found`

**Blast radius:** All `dotnet` component evaluations are blocked. `dotnet-msbuild`
evaluations are unaffected.

**Suggested fix:**
1. Update `global.json` → `"version": "9.0.201"` (recommended)
2. Or revert commit `abc1234` in `eng/skill-validator/SkillValidator.csproj`

**Related:** Commit abc1234 by @viktorhofer — "Bump SDK to 9.0.201" (8h ago)
<!-- /investigation:pipeline:evaluation:evaluate-dotnet:run-skill-validator:failure -->
```

**06:15 UTC** — Second worker (quality investigation) completes:

```markdown
<!-- investigation:quality:csharp-scripts:basic-script:regressed -->
🔍 **Investigation Complete** — [Worker Run #14531](link)

**Root cause:** PR #187 (merged 2026-02-26) refactored the `csharp-scripts` skill
description to use a more concise format. The new phrasing removes context that
the LLM was using to generate better script scaffolding.

**Confidence:** Medium — the timing correlates perfectly, but the causal mechanism
is inferential (LLM behavior change from prompt wording).

**Suggested fix:**
1. Review the skill description diff in PR #187
2. Consider restoring the detailed examples section that was removed
3. Run a targeted eval (`/eval csharp-scripts`) to confirm

**Data trend:** 7.9, 7.8, 7.7, 7.8, 7.9, 7.8, **5.5** ← sharp drop, not gradual
<!-- /investigation:quality:csharp-scripts:basic-script:regressed -->
```

**Developer arrives at 09:00 UTC**, opens the pinned issue:
- Sees the overview (what's new, what's resolved)
- Sees inline investigation results with root causes and fixes
- Can act immediately — no manual log diving needed

### 8A.9 Alternative Approaches Considered

Three mechanisms for automated triage were evaluated during research:

| Approach | Mechanism | Pros | Cons | Verdict |
|----------|-----------|------|------|---------|
| **A. Orchestrator/Worker** (`dispatch-workflow`) | Health check dispatches investigation workflows | Fresh context per finding; compile-time validated; full tool access; findings link back to overview | Same-repo only; 5s delay between dispatches; workers can't coordinate with each other | **✅ Recommended** |
| **B. Issue → Copilot Agent** (`assignees: copilot`) | Health check creates a triage issue assigned to Copilot | Leverages Copilot coding agent's full capabilities; can create PRs with fixes | Copilot agent may not have the right tools/context for DevOps investigation; requires PAT; investigation results live in a separate issue (not inline) | Consider for Phase 3 |
| **C. Agent Sessions** (`create-agent-session`) | Health check spawns new Copilot agent sessions | Max 10 sessions; can provide custom instructions | Less structured than dispatch-workflow; harder to link results back; newer/less documented feature | Monitor for future use |

**Decision:** Approach A (`dispatch-workflow`) is the primary mechanism because:
1. Worker workflows are **version-controlled** and **compile-time validated** — the system verifies `devops-health-investigate.md` exists
2. Workers have **explicit frontmatter** with controlled permissions and tools
3. The `replace-island` output mechanism creates a seamless **inline experience** on the health issue
4. Workers can be tested independently via `workflow_dispatch` from the Actions UI

**Future evolution:** Approach B (Copilot agent assignment) could be added as a Phase 3 enhancement for findings where the agent should not just _investigate_ but also _fix_ — e.g., creating a PR to update `global.json`.

---

## 9. Implementation Phases

### Phase 1: Core Infrastructure (Week 1)

**Goal:** A working daily health check with pipeline + PR checks and diff output.

| Task | Deliverable | Est. Effort |
|------|-------------|-------------|
| 1.1 Write `devops-health-check.md` workflow definition | Agentic workflow file with frontmatter + instructions | 1 day |
| 1.2 Write `devops-health.lock.md` knowledge file | Health check catalog, fingerprinting rules, output templates | 1 day |
| 1.3 Implement checks P1–P3 (pipeline health) | Working pipeline failure detection | Included in workflow |
| 1.4 Implement checks R1–R4 (PR health) | Working PR staleness detection | Included in workflow |
| 1.5 Implement fingerprinting + diff via `cache-memory` | NEW / EXISTING / RESOLVED classification | Included in workflow |
| 1.6 Implement pinned issue output | Issue creation/update with diff-formatted body + comment | Included in workflow |
| 1.7 Update `agentic-workflows/README.md` | Add new workflow to table | 10 min |
| 1.8 Manual testing via `workflow_dispatch` | Verify end-to-end flow | 0.5 day |

**Acceptance criteria:**
- [ ] `/health-check` slash command produces a well-formatted issue
- [ ] Second run correctly identifies NEW vs EXISTING findings
- [ ] Pipeline failures on `main` are detected and fingerprinted
- [ ] Stale PRs are detected

### Phase 2: Automated Triage & Quality Checks (Week 2)

**Goal:** Add investigation worker + skill quality monitoring from benchmark data.

| Task | Deliverable | Est. Effort |
|------|-------------|-------------|
| 2.1 Write `devops-health-investigate.md` worker workflow definition | Worker agentic workflow with frontmatter + investigation playbooks | 1 day |
| 2.2 Write `devops-investigate.lock.md` knowledge file | Investigation playbooks for pipeline, quality, PR, infra categories | 1 day |
| 2.3 Add `dispatch-workflow` logic to orchestrator | Dispatch rules, island placeholder insertion, correlation IDs | 0.5 day |
| 2.4 Add `replace-island` output to worker | Worker updates pinned issue with investigation results | 0.5 day |
| 2.5 Implement checks Q1–Q4 (quality from benchmark data) | Anomaly flag detection + regression detection | 1 day |
| 2.6 Implement check Q5 (test coverage scan) | Missing test detection | 0.5 day |
| 2.7 Implement check Q6 (benchmark staleness) | Data freshness monitoring | 0.5 day |
| 2.8 Add trends table to output | 7-day rolling metrics | 0.5 day |
| 2.9 End-to-end triage testing | Trigger health check, verify worker dispatches and island replacement | 0.5 day |

**Acceptance criteria:**
- [ ] Orchestrator dispatches investigation workers for 🆕 critical/warning pipeline+quality findings
- [ ] Workers complete investigation and replace islands on the pinned issue
- [ ] Failed workers leave placeholder intact (graceful degradation)
- [ ] Bench entries with anomaly flags (any non-standard property) are detected and reported with correct severity
- [ ] New flag types added to `generate-benchmark-data.ps1` are automatically picked up without workflow changes
- [ ] Quality regression > 1.0 point triggers a finding + investigation
- [ ] Trends table shows 7-day averages for key metrics

### Phase 3: Infrastructure, Resources & Triage Polish (Week 3)

**Goal:** Complete check coverage + polish investigation quality.

| Task | Deliverable | Est. Effort |
|------|-------------|-------------|
| 3.1 Implement checks I1–I6 (infrastructure) | Config health monitoring | 0.5 day |
| 3.2 Implement checks U1–U3 (resource usage) | Compute/cost tracking | 0.5 day |
| 3.3 Tune fingerprinting rules based on real data | Reduce false positives / missed diffs | 1 day |
| 3.4 Add Copilot code review noise suppression | Known-noise handling (demote to 🔵) | 0.5 day |
| 3.5 Tune investigation playbooks based on real worker output | Improve root-cause accuracy, reduce hallucination | 0.5 day |
| 3.6 Add investigation budget controls | Prevent runaway compute from too many dispatches | 0.5 day |
| 3.7 Documentation: add to CONTRIBUTING.md | Developer docs for both workflows | 0.5 day |

**Acceptance criteria:**
- [ ] All checks from the catalog are operational
- [ ] Known-noise patterns (e.g., `copilot-code-review`) are demoted to � Info via `cache-memory` config
- [ ] `/health-check suppress <pattern>` command works to add new noise patterns at runtime
- [ ] Resource trends are tracked across runs
- [ ] Investigation playbooks produce actionable root-cause analysis for pipeline and quality findings
- [ ] Investigation budget cap (10 dispatches) is enforced

### Phase 4: Dashboard Integration & Auto-Remediation (Week 4+, Optional)

**Goal:** Visual health trends + Copilot agent for automated fixes.

| Task | Deliverable | Est. Effort |
|------|-------------|-------------|
| 4.1 Add "Health" tab to `eng/dashboard/dashboard.html` | New dashboard page | 1 day |
| 4.2 Generate `data/health.json` from health check results | Pipeline health time-series data | 1 day |
| 4.3 Add health data to `generate-benchmark-data.ps1` or new script | Automated data pipeline | 0.5 day |
| 4.4 Explore `assignees: copilot` for auto-fix PRs | For findings where the fix is mechanical (e.g., version bump), create an issue assigned to Copilot coding agent to propose a fix PR | 1 day |
| 4.5 Add `create-agent-session` as alternative triage path | For complex multi-file investigations that benefit from Copilot's code editing capabilities | 0.5 day |

---

## 9A. Local Development & Testing

All workflow development can be validated and tested locally before merging to `main`. The `gh aw` CLI provides a full ladder from instant syntax checks to sandboxed execution.

### 9A.1 Prerequisites

```powershell
# Install the gh-aw CLI extension
gh extension install github/gh-aw

# Verify installation
gh aw version

# Bootstrap secrets (interactive — prompts for missing API keys)
gh aw secrets bootstrap
```

### 9A.2 Development Loop

| Phase | Command | What It Tests | Cost |
|-------|---------|---------------|------|
| **1. Edit** | `gh aw compile --watch` | Syntax, frontmatter, imports — auto-recompiles on save | Free |
| **2. Validate** | `gh aw validate devops-health-check --strict` | Schema, permissions, safe-outputs, security best practices | Free |
| **3. Probe data sources** | `gh api ...` / `jq` (see §9A.3) | API queries return expected data | Free |
| **4. Dry-run** | `gh aw trial ./workflow.md --dry-run` | Preview execution plan without running | Free |
| **5. Sandbox run** | `gh aw trial ./workflow.md --clone-repo dotnet/skills` | Full execution in isolated trial repo | LLM tokens |
| **6. Branch run** | `gh aw run devops-health-check --ref feature/... --wait` | Real execution against real repo from branch | LLM tokens |
| **7. Analyze** | `gh aw logs` / `gh aw audit <run-id>` | Debug failures, check token usage, review tool calls | Free |

### 9A.3 Probing Data Sources Locally

Before writing the agentic workflow, verify that the GitHub API queries and data files return the expected shape. These commands can be run directly from a local terminal:

```powershell
# P1 — Pipeline failures on main in last 24h
gh api "/repos/dotnet/skills/actions/runs?branch=main&status=failure&per_page=10" |
  jq '.workflow_runs[] | {name: .name, conclusion, created_at, html_url}'

# P3 — Evaluation run durations (last 14 days)
gh api "/repos/dotnet/skills/actions/workflows/evaluation.yml/runs?branch=main&per_page=30" |
  jq '[.workflow_runs[] | {created: .created_at, duration_min: ((.updated_at | fromdateiso8601) - (.created_at | fromdateiso8601)) / 60}] | sort_by(.created)'

# Q1 — Benchmark data for a component (same data the dashboard uses)
# This is the primary data source for all quality checks (Q1–Q4, Q6)
gh api "https://raw.githubusercontent.com/dotnet/skills/gh-pages/data/dotnet-msbuild.json" |
  jq '.entries.Quality[-1].benches[:3]'

# Q1 — Find bench entries with ANY anomaly flags (non-standard properties)
# Standard fields are: name, unit, value — anything else is an anomaly flag
# Scan BOTH Quality and Efficiency arrays (both carry the same flags)
gh api "https://raw.githubusercontent.com/dotnet/skills/gh-pages/data/dotnet-msbuild.json" |
  jq '{ quality: [.entries.Quality[-1].benches[] | to_entries | map(select(.key | IN("name","unit","value") | not)) | select(length > 0) | from_entries], efficiency: [.entries.Efficiency[-1].benches[] | to_entries | map(select(.key | IN("name","unit","value") | not)) | select(length > 0) | from_entries] }'

# Q6 — Check data freshness (latest entry timestamp vs now)
gh api "https://raw.githubusercontent.com/dotnet/skills/gh-pages/data/dotnet-msbuild.json" |
  jq '.entries.Quality[-1].date / 1000 | strftime("%Y-%m-%dT%H:%M:%SZ")'

# R1 — PRs open > 7 days without review
gh api "/repos/dotnet/skills/pulls?state=open&sort=created&direction=asc&per_page=20" |
  jq '[.[] | select((now - (.created_at | fromdateiso8601)) > 604800)] | length'

# I1 — CODEOWNERS existence
gh api "/repos/dotnet/skills/contents/CODEOWNERS" --silent && echo "exists" || echo "missing"
```

These are the exact queries the agent will call via the `github` MCP toolset. If a query doesn't return useful data here, it won't work in the workflow either.

### 9A.4 Compile-Time Validation

The compiler catches most structural issues before any execution:

```powershell
# Validate a single workflow (strict mode enforces safe-output-only writes)
gh aw validate devops-health-check --strict

# Validate both orchestrator and worker together
gh aw validate devops-health-check devops-health-investigate --strict

# Continuous validation during editing
gh aw compile --watch
```

What compile-time validation catches:
- Frontmatter syntax errors (YAML, indentation)
- Missing or invalid `safe-outputs` declarations
- `dispatch-workflow` targets that don't exist (e.g., typo in `devops-health-investigate`)
- Invalid permission combinations
- Missing imports (e.g., `shared/compiled/devops-health.lock.md`)
- Security issues in `--strict` mode (write permissions without safe-outputs, wildcard domains, etc.)

### 9A.5 Trial Mode — Sandboxed Execution

`gh aw trial` is the primary testing tool. It creates a temporary private repo, installs the workflow, runs it, and captures outputs — without touching `dotnet/skills`.

```powershell
# Preview what trial would do (no execution)
gh aw trial ./devops-health-check.md --dry-run

# Full sandboxed run — clones real repo content so the agent can read
# global.json, benchmark data, workflow YAMLs, etc.
gh aw trial ./devops-health-check.md --clone-repo dotnet/skills

# Override the prompt to test a subset of checks
gh aw trial ./devops-health-check.md --clone-repo dotnet/skills \
  --append "Only run pipeline health checks P1-P3. Skip all other categories."

# Repeat for consistency testing
gh aw trial ./devops-health-check.md --clone-repo dotnet/skills --repeat 3

# Clean up the trial repo after the run
gh aw trial ./devops-health-check.md --clone-repo dotnet/skills --delete-host-repo-after
```

Trial results are saved locally in `trials/*.json` with metadata about safe outputs (issues created, comments posted), token usage, and execution duration.

#### Testing the Orchestrator + Worker Together

The `dispatch-workflow` safe output requires both workflows to exist in the same repo. For integrated testing:

```powershell
# Option A: Trial both workflows together
gh aw trial ./devops-health-check.md ./devops-health-investigate.md \
  --clone-repo dotnet/skills

# Option B: Use a persistent test repo for iterative testing
gh aw trial ./devops-health-check.md ./devops-health-investigate.md \
  --host-repo health-check-sandbox \
  --clone-repo dotnet/skills
# The trial repo persists — reuse across test sessions

# Option C: Run directly in a dedicated test repository
gh aw trial ./devops-health-check.md \
  --repo your-org/health-check-test \
  --clone-repo dotnet/skills
```

> **Note:** In trial mode, safe outputs (issues, comments) are created in the trial repo, not in `dotnet/skills`. Check the trial repo's Issues tab to see the generated health dashboard.

### 9A.6 Branch Testing — Real Execution

Once trial runs look good, test against the real repo from a feature branch:

```powershell
# Compile, push, and trigger in one step
gh aw run devops-health-check --push --ref feature/health-check

# Or step by step:
gh aw compile devops-health-check
git add .github/workflows/
git push origin feature/health-check
gh aw run devops-health-check --ref feature/health-check --wait --verbose
```

The `--wait` flag monitors execution in real-time and exits with success/failure code. The `--verbose` flag shows additional debugging details.

> **Caution:** Branch runs create real issues/comments in `dotnet/skills`. Use a descriptive issue title prefix (e.g., `[TEST] 🏥 Repository Health Dashboard`) during development, or temporarily reduce `safe-outputs` limits.

### 9A.7 Post-Run Analysis

```powershell
# Download and view logs for the workflow
gh aw logs devops-health-check

# Detailed analysis of a specific run (tool usage, network, errors)
gh aw audit <run-id> --parse

# Health metrics over time (success rate, duration, cost)
gh aw health devops-health-check --days 30

# Debug logging for compile issues
$env:DEBUG="*"; gh aw compile devops-health-check
```

### 9A.8 Testing Checklist

Use this checklist during development to verify each component:

- [ ] `gh aw validate --strict` passes for both workflows
- [ ] Data probe queries (§9A.3) return expected data shapes
- [ ] Trial run of orchestrator alone produces a well-formatted health issue
- [ ] Fingerprinting produces stable IDs (run twice — second run shows EXISTING, not NEW)
- [ ] Trial run of orchestrator + worker: worker dispatches fire, islands get replaced
- [ ] Worker failure is graceful (island retains "⏳ pending" placeholder)
- [ ] Branch run on `feature/` branch produces correct output against real data
- [ ] `gh aw logs` shows reasonable token usage (orchestrator < 15k, worker < 8k)
- [ ] Issue body stays under 65k characters with maximum findings

---

## 10. Risk & Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| **LLM inconsistency in fingerprinting** | Same finding gets different fingerprints across runs → false NEW/RESOLVED | Medium | Encode fingerprint rules extremely precisely in `devops-health.lock.md`; include examples; use `bash` tools to compute fingerprints deterministically where possible |
| **cache-memory data loss** | Previous fingerprints lost → everything shows as NEW | Low | Accept gracefully: "⚠️ Previous state unavailable — all findings shown as new. Diff will resume from next run." |
| **GitHub API rate limiting** | Can't collect all data in a single run | Low | Use targeted API queries (date-filtered); batch requests; the `github` MCP toolset handles pagination |
| **LLM token cost per run** | Daily runs add up | Low | Most work is data collection (API calls, bash commands). LLM reasoning is limited to analysis section. Estimated: ~10k tokens/run for orchestrator + ~5k per worker |
| **Noisy output in early runs** | First few runs show all findings as NEW | Expected | Documented in workflow: "First run establishes baseline — all findings will appear as 🆕 NEW" |
| **Benchmark data format changes** | Quality checks break if JSON schema changes | Low | Benchmark data is generated by a known script (`generate-benchmark-data.ps1`); pin to expected schema in knowledge file |
| **Issue body size limit** | GitHub issues have a ~65k character limit | Low | **Truncation strategy:** Show all 🆕 NEW findings in full (up to 10); show all ✅ RESOLVED in full (up to 5). All 📌 EXISTING findings go in collapsed `<details>` tags, limited to top 20 by severity. If body exceeds 60k chars after rendering, truncate remaining EXISTING findings and append a footer: "> … N additional existing findings omitted — see [run artifacts](link) for full report." The daily comment always includes the complete summary counts. |
| **Worker hallucination in root-cause analysis** | Investigation agent attributes failure to wrong cause | Medium | Require confidence levels (High/Medium/Low); include source evidence (log excerpts, commit SHAs); playbooks demand factual grounding; human review still expected |
| **Worker compute cost scaling** | 10 workers × daily = significant Actions minutes | Medium | Budget cap at 10 dispatches; skip investigation for low-severity/non-pipeline findings; Phase 3 tuning to reduce unnecessary dispatches |
| **`replace-island` race condition** | Two workers updating the same issue simultaneously | Low | Each worker targets a different island (unique `finding_id`); `replace-island` is atomic per island |
| **Worker timeout before completing** | Investigation placeholder remains "⏳ pending" | Low | Workers focus on a single finding — should complete in 2-5 min. Stale placeholders are overwritten on next health check run |

---

## Appendix: Example Output

### First Run (Baseline)

```markdown
# 🏥 Daily Health Check — 2026-02-28

**Status:** 🔴 2 critical · 🟡 8 warnings · 🔵 4 info
**Since yesterday:** 🆕 14 new (first run — establishing baseline)

---

## 🆕 New Findings (14)

> ⚠️ This is the first health check run. All findings appear as new.
> Starting from the next run, only changes will be highlighted.

### 🔴 `dump-collect/basic-dump` — anomaly flag: `notActivated`
- **Component:** dotnet
- **Latest benchmark:** 2026-02-28 — flags: `notActivated: true`
- **Tracked in:** [#137](link)
- **Suggested action:** Review skill description and trigger keywords.

> **Note:** Anomaly flags are dynamically discovered from bench entry properties.
> Any non-standard property (beyond `name`/`unit`/`value`) is reported — e.g., `timedOut`, `testOverfitted`, or future flags.

### 🔴 Evaluation failed on main: evaluate (dotnet)
- **Runs:** [#14523](link), [#14510](link)
- **Impact:** 2 missed benchmark data points in last 7 days

### 🟡 Copilot code review failing (92% failure rate)
- **Last 7 days:** 23/25 runs failed
- **Note:** Org-level workflow — classify as known noise after confirmation

...
```

### Second Run (Normal Diff)

```markdown
# 🏥 Daily Health Check — 2026-03-01

**Status:** 🔴 1 critical · 🟡 7 warnings · 🔵 4 info
**Since yesterday:** 🆕 1 new · ✅ 2 resolved · 📌 9 unchanged

---

## 🆕 New Findings (1)

### 🟡 PR #192 has been open 8 days without review
- **Author:** @AaronRobinsonMSFT
- **Title:** "Add interop pinvoke skill"

---

## ✅ Resolved Since Yesterday (2)

### ~~🔴 Evaluation failed on main: evaluate (dotnet)~~
- **Resolved:** All 12 scheduled runs in last 24h succeeded ✅

### ~~🟡 Dashboard data stale for `dotnet`~~
- **Was stale for:** 3 days
- **Resolved by:** Run #14530 published fresh data

---

## 📌 Existing Findings (9)

<details><summary>🔴 <code>dump-collect/basic-dump</code> — skill not activating (13 days)</summary>
...
</details>

<details><summary>🟡 Copilot code review failing — 15th consecutive day</summary>
...
</details>

...
```

---

## Summary

This implementation plan delivers a **daily agentic health check with automated triage** — a two-tier system where an orchestrator identifies _what_ changed and worker agents investigate _why_.

The approach:
1. **Follows existing patterns** — `gh aw` frontmatter, shared knowledge, MCP tools, issue output
2. **Hybrid architecture** — deterministic data collection + LLM analysis
3. **Fingerprint-based diffing** — stable IDs for each finding enable NEW/EXISTING/RESOLVED tracking
4. **Orchestrator/Worker triage** — new critical findings are automatically dispatched to dedicated investigation agents with fresh context, using `dispatch-workflow` + `replace-island` for inline results
5. **Progressive rollout** — pipeline + PRs first (Phase 1), triage + quality (Phase 2), infra + polish (Phase 3)
6. **Single pinned issue** — one well-known URL with the overview _and_ drill-down investigations, updated in-place as workers complete
7. **Graceful degradation** — worker failures leave placeholders; budget caps prevent runaway compute; confidence levels flag uncertain analysis
