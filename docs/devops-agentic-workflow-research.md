# DevOps Agentic Workflow — Research Document

> **Date:** 2026-02-27  
> **Purpose:** Comprehensive research for planning a DevOps agentic workflow that collects and presents all possible pipeline issues for the `dotnet/skills` repo in a single, well-known overview.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Current Repository Architecture](#2-current-repository-architecture)
3. [Current CI/CD Pipelines — Deep Dive](#3-current-cicd-pipelines--deep-dive)
4. [Current Dashboard & Benchmarking System](#4-current-dashboard--benchmarking-system)
5. [Existing Agentic Workflows in This Repo](#5-existing-agentic-workflows-in-this-repo)
6. [Observed Issues & Suspicious Patterns](#6-observed-issues--suspicious-patterns)
7. [Industry Research: GitHub Agentic Workflows](#7-industry-research-github-agentic-workflows)
8. [What the Daily Overview Should Contain](#8-what-the-daily-overview-should-contain)
9. [Implementation Approaches](#9-implementation-approaches)
10. [Appendix: Raw Data](#10-appendix-raw-data)

---

## 1. Executive Summary

The `dotnet/skills` repository (private, `github.com/dotnet/skills`) is a **3.5-week-old, rapidly growing** repository containing AI coding agent skills for .NET and MSBuild. It has:

- **4 custom GitHub Actions workflows** + 3 dynamic/org-level workflows
- **Scheduled evaluations every 2 hours** using LLM-based skill validation (claude-opus-4.6)
- **A GitHub Pages dashboard** for benchmark trends
- **30 open PRs**, **13 open issues**, **69 merged PRs** — very high velocity
- **Multiple failure categories** that nobody is systematically tracking

The proposed DevOps agentic workflow would act as a **daily health monitor** — collecting pipeline failures, quality regressions, infrastructure anomalies, and PR bottlenecks into a single actionable overview, either as a GitHub Issue or a dashboard page.

---

## 2. Current Repository Architecture

### 2.1 Components

| Component | Path | Skills | Agents | MCP Servers | Tests |
|-----------|------|--------|--------|-------------|-------|
| **dotnet-msbuild** | `src/dotnet-msbuild/` | 12 | 3 | 1 (binlog-mcp) | 12 eval suites |
| **dotnet** | `src/dotnet/` | 4 | 1 | 0 | 4 eval suites |

### 2.2 Key Infrastructure

| Component | Path | Purpose |
|-----------|------|---------|
| Skill Validator | `eng/skill-validator/` | C# tool — runs LLM evaluations, comparative judging, statistical analysis |
| Dashboard | `eng/dashboard/` | HTML+JS+Chart.js — benchmark visualizations on GitHub Pages |
| Benchmark Data Gen | `eng/dashboard/generate-benchmark-data.ps1` | Converts eval results → dashboard JSON |
| Agentic Workflow Templates | `src/dotnet-msbuild/agentic-workflows/` | 3 existing `gh aw` workflow templates |

### 2.3 Repository Metadata

- **Visibility:** Private
- **Default branch:** `main`
- **No CODEOWNERS file** — no automated review assignment
- **No Dependabot** — no automated dependency updates
- **No branch protection rules observed** (though this is hard to confirm from outside)
- **Contributors:** 13+ (ViktorHofer, JanKrivanek, danmoseley, AaronRobinsonMSFT, etc.)

---

## 3. Current CI/CD Pipelines — Deep Dive

### 3.1 Workflow Inventory

| Workflow | File | Triggers | Avg Duration |
|----------|------|----------|-------------|
| **evaluation** | `evaluation.yml` | PR (src/\*\*), schedule (every 2h), manual | **~43 min** (39-46 min range) |
| **skill-validator** | `skill-validator.yml` | push/PR on `eng/skill-validator/**` | < 1 min |
| **custom-agent-validation** | `validate-agents.yml` | push/PR on `src/**/agents/**` | < 1 min |
| **agent-skill-validation** | `validate-skills.yml` | push/PR on `src/**/skills/**` | < 1 min |
| **Copilot code review** | dynamic | PR events | ~2-6 min |
| **Copilot coding agent** | dynamic | issue assignment | varies |
| **pages-build-deployment** | dynamic | gh-pages push | < 2 min |
| **Dependabot Updates** | dynamic | (inactive — no config) | N/A |

### 3.2 Evaluation Pipeline — Architecture

This is the most complex and expensive pipeline:

```
discover → build-validator → evaluate (matrix) → comment-on-pr
                                                → publish-benchmark (main only)
```

Key design decisions:
- **8 Copilot token secrets** randomly rotated per eval job (load balancing / rate limiting)
- **Matrix strategy** over discovered skills/components (fail-fast: false)
- **5 runs per test on main**, 3 on PRs (statistical significance)
- **60-minute timeout** per matrix job
- **`--verdict-warn-only`**: Quality failures don't fail the pipeline (exit 0) — only execution errors and missing evals fail
- **Results artifacts**: 30-day retention, includes JSON + Markdown + Console output
- **PR comments**: Auto-posted/updated with consolidated evaluation summary
- **Benchmark publishing**: On main merges → generates dashboard data → deploys to gh-pages

### 3.3 Validation Pipelines

**`validate-skills.yml`**:
- Uses `Flash-Brew-Digital/validate-skill@v1` (third-party action)
- `fail-on-warning: false` — **⚠️ temporarily disabled, see note in YAML**
- `ignore-rules: reference-exists` — **⚠️ reference validation skipped**
- Matrix over changed SKILL.md files only

**`validate-agents.yml`**:
- Uses `timheuer/vscode-agent-validation@v0.1.0` (third-party action)
- `fail-on-warning: true` — stricter than skill validation
- `validate-references: true`

### 3.4 Historical Failure Analysis (Last 100 Runs)

**Total failures observed: ~60+** (out of ~100 recent runs with `--status failure`)

| Failure Category | Count | Severity | Pattern |
|-----------------|-------|----------|---------|
| **Copilot code review** failures | ~25+ | Low (org-level, external) | Consistently failing on almost every PR |
| **Evaluation pipeline** failures (early days) | ~15 | Medium | Mostly from repo's first 2 weeks — infra being built out |
| **Agent validation** failures | ~5 | Low | Early PRs before validation action stabilized |
| **Evaluation pipeline** failures (recent) | 2 (#221, #218) | **High** | `dotnet` component's skill-validator failing — `evaluate (dotnet)` job failed at "Run skill-validator" step |

**Current status (last 10 main runs):** All 10 most recent evaluation runs on `main` are ✅ success.

---

## 4. Current Dashboard & Benchmarking System

### 4.1 Dashboard URL

`https://refactored-sniffle-qm9o678.pages.github.io/`

(Behind GitHub Pages auth — private repo, requires org membership)

### 4.2 Dashboard Architecture

- **Frontend:** Single-page HTML + Chart.js v4, dark theme
- **Data source:** `data/components.json` (manifest) + `data/{component}.json` (benchmark history)
- **Deployment:** GitHub Actions → gh-pages branch
- **Metrics tracked per scenario:**
  - **Quality:** Skilled Quality vs Vanilla Quality (0-10 scale, derived from rubric scoring)
  - **Efficiency:** Time (seconds) and Tokens In (thousands)
  - **Flags:** `notActivated` (skill wasn't triggered), `timedOut` (execution exceeded timeout)
- **Visual indicators:** Summary cards, line charts with commit tooltips, special markers for anomalies

### 4.3 What the Dashboard Does NOT Currently Show

- ❌ Pipeline health / failure rates
- ❌ PR velocity / review bottlenecks
- ❌ Evaluation run duration trends
- ❌ Token usage / Copilot quota consumption trends
- ❌ Skill activation rates (only flagged per-point, no aggregate)
- ❌ Cross-run flakiness / variance metrics
- ❌ Alerts or anomaly detection
- ❌ Skill coverage (which skills have no tests, or tests that never activate)

---

## 5. Existing Agentic Workflows in This Repo

The repo already has **3 agentic workflow templates** under `src/dotnet-msbuild/agentic-workflows/`. These follow the **GitHub Agentic Workflows (`gh aw`)** pattern and serve as distribution templates for the MSBuild skill domain.

### 5.1 Existing Workflows

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `build-failure-analysis.md` | `/analyze-build-failure` slash command | Builds repo, analyzes binlog via MCP, classifies errors, posts PR comment |
| `build-perf-audit.md` | Weekly schedule or manual | Builds with binlog, analyzes bottlenecks, tracks trends, creates issue if regression |
| `msbuild-pr-review.md` | `/review-msbuild` slash command | Reviews PR diff for MSBuild anti-patterns, posts comment |

### 5.2 Shared Infrastructure

- `shared/mcp-servers.md` — Shared MCP server config (binlog-mcp container)
- `shared/compiled/*.lock.md` — Pre-compiled knowledge bundles

### 5.3 Key Design Patterns Used

1. **Slash-command triggers** for on-demand analysis
2. **Scheduled triggers** for periodic health checks
3. **MCP tool integration** for domain-specific analysis (binlog-mcp)
4. **Issue creation** for reporting findings
5. **PR comment posting** for review feedback
6. **Trend tracking** via cache-memory tool
7. **Severity classification** (🔴 🟡 🟢) for actionable output
8. **Knowledge compilation** — skills compiled into lock files for workflow consumption

---

## 6. Observed Issues & Suspicious Patterns

### 6.1 🔴 Critical Issues

#### 6.1.1 Copilot Code Review Consistently Failing
- **Every single PR** triggers a "Copilot code review" run, and **most of them fail**
- This is an org-level dynamic workflow, not repo-controlled
- ~25+ failures in the last 100 runs
- **Impact:** Noise — developers likely ignore all workflow status checks because this one always fails
- **What the agentic workflow should detect:** "Copilot code review has failed X/Y times in the last 7 days — investigate or disable"

#### 6.1.2 Skill Not Activating (Issue #137)
- `dump-collect` eval tests don't invoke the SKILL
- Dashboard shows test runs without skill triggering
- **Impact:** False "vanilla = skilled" results pollute benchmark data
- **What to detect:** "N scenarios across M skills show `notActivated` flag — skill description/trigger may need tuning"

#### 6.1.3 Two Recent Evaluation Failures on Main (#218, #221)
- Both failed on the `dotnet` component's "Run skill-validator" step
- `dotnet-msbuild` component was cancelled as a result
- These were scheduled runs on `main` — not PR-related
- **Impact:** Missed benchmark data points, dashboard gaps
- **What to detect:** "Evaluation failed on main at [time] — [component] failed, investigate run [link]"

### 6.2 🟡 Warning-Level Issues

#### 6.2.1 No CODEOWNERS File
- 30 open PRs, no automated review assignment
- **Impact:** PRs may stagnate waiting for reviews
- **What to detect:** "N PRs have been open > 7 days without review"

#### 6.2.2 No Dependabot Configuration
- Dependabot Updates workflow exists (org-level) but no `dependabot.yml` config
- **Impact:** No automated dependency updates
- **What to detect:** "Dependabot is not configured — dependencies may be outdated"

#### 6.2.3 Skill Validation Leniency
- `validate-skills.yml` has `fail-on-warning: false` and `ignore-rules: reference-exists`
- Comment in YAML says "temporary"
- **Impact:** Skills with broken references or warnings pass CI
- **What to detect:** "Skill validation rules are relaxed — N skills have warnings that would fail under strict mode"

#### 6.2.4 Verdict-Warn-Only Mode
- Evaluation pipeline uses `--verdict-warn-only` — quality failures don't block merges
- **Impact:** Regression in skill quality can be merged without anyone noticing
- **What to detect:** "N evaluation runs in the last 7 days had verdict warnings — quality may be regressing"

#### 6.2.5 Evaluation Duration Creep
- Current runs take 39-46 min against a 60-min timeout
- As more skills are added, this will grow
- **Impact:** Eventually hits timeout → failures
- **What to detect:** "Average eval duration is X min (Y% of timeout) — trending up by Z min/week"

#### 6.2.6 High PR Volume / Stale PRs
- 30 open PRs, many from 9-14 days ago
- Several appear to be stale (e.g., #12 WIP from 14 days ago, #29 "Ideation" from 10 days ago)
- **What to detect:** "N PRs are > 7 days old, M PRs have no activity in 5 days"

### 6.3 🔵 Information / Opportunity Items

#### 6.3.1 Token Rotation Pattern
- 8 Copilot tokens randomly selected per eval job
- No visibility into which tokens are being used, or if any are expired/rate-limited
- **What to detect:** "Token [N] has been used X times today, consider load balancing"

#### 6.3.2 Evaluation Runs Every 2 Hours on Main
- 12 runs/day × ~43 min each = ~8.6 hours of compute per day
- 5 runs per test per evaluation = significant LLM token consumption
- **What to detect:** "Daily eval cost: ~X compute-hours, ~Y tokens consumed"

#### 6.3.3 No Issue for Plugin Validation (Issue #81)
- Requested: E2E plugin validation workflow
- Requested: Auto-bump plugin version (Issue #83)
- Neither implemented yet

#### 6.3.4 Dashboard Is Auth-Gated
- Private repo → GitHub Pages requires org membership
- No public status page for external visibility
- **Opportunity:** A daily summary issue would be accessible to all repo collaborators

---

## 7. Industry Research: GitHub Agentic Workflows

### 7.1 What Are GitHub Agentic Workflows?

GitHub Agentic Workflows are a pattern for **AI-powered automation** that runs within GitHub Actions. The key distinction from traditional CI/CD:

| Aspect | Traditional CI/CD | Agentic Workflow |
|--------|------------------|-----------------|
| Logic | Deterministic scripts | LLM-driven reasoning |
| Scope | Pre-defined steps | Open-ended analysis |
| Output | Pass/fail | Nuanced reports, issue creation |
| Adaptation | Static rules | Contextual understanding |
| Trigger | Code events | Schedules, slash commands, events |

### 7.2 GitHub's Agentic AI Ecosystem (as of 2026-02)

1. **Copilot Agent Mode (VS Code)** — Interactive agentic coding in the editor
2. **Copilot Coding Agent** — Autonomous PR creation from issues (formerly "Project Padawan")  
3. **GitHub MCP Server** — Model Context Protocol server connecting AI tools to GitHub APIs (27.3k stars)
4. **GitHub Actions** — The runtime for agentic workflows
5. **Custom Agentic Workflows (`gh aw`)** — The pattern this repo already uses

### 7.3 GitHub MCP Server — Key Capabilities for DevOps

The [github/github-mcp-server](https://github.com/github/github-mcp-server) provides tools that a DevOps agentic workflow could leverage:

| Toolset | Relevant Tools |
|---------|---------------|
| **actions** | Monitor workflow runs, analyze build failures, manage releases |
| **issues** | Create/update/manage issues, triage bugs |
| **pull_requests** | Review PRs, check status, manage reviews |
| **repos** | Browse code, search files, analyze commits |
| **code_security** | Code scanning, security findings |
| **dependabot** | Dependabot alerts and management |

### 7.4 Best Practices from Industry

Based on research of GitHub's engineering blog, the MCP server ecosystem, and the existing agentic workflow templates in this repo:

#### Pattern 1: Scheduled Health Digest
- Run daily/weekly on a cron schedule
- Collect data from multiple GitHub APIs
- Produce a summary issue with sections, severity levels, and links
- Use `cache-memory` tool for trend tracking across runs
- Example: The existing `build-perf-audit.md` follows this pattern

#### Pattern 2: Event-Driven Triage
- Trigger on specific events (workflow failure, issue creation, PR staleness)
- Analyze the event context with LLM reasoning
- Take action (label, assign, comment, create follow-up issue)
- Example: GitHub MCP Server's issue triage prompts

#### Pattern 3: Comparative Analysis
- Compare current state against baseline/history
- Detect regressions, anomalies, or trends
- Report with statistical confidence
- Example: The existing evaluation pipeline's pairwise judging and bootstrap CI

#### Pattern 4: Multi-Source Aggregation
- Pull data from Actions API, Issues API, PRs API, Pages deployment, benchmark data
- Correlate across sources (e.g., "PR #X merged → eval score dropped → dashboard shows regression")
- Present unified view

### 7.5 Key Design Principles

1. **Single source of truth** — One place to check for repo health
2. **Signal, not noise** — Categorize by severity, suppress known/accepted issues
3. **Actionable** — Every finding should have a suggested next step or link
4. **Trend-aware** — Show whether things are getting better or worse
5. **Low maintenance** — Should work as new skills/components are added without reconfiguration
6. **Idempotent** — Running it twice shouldn't create duplicate issues

---

## 8. What the Daily Overview Should Contain

Based on the research above, here's a prioritized list of what the DevOps agentic workflow should collect and present:

### 8.1 Pipeline Health

| Check | Data Source | Severity |
|-------|------------|----------|
| Failed workflow runs in last 24h | Actions API | 🔴 Critical if eval fails on main |
| Cancelled/timed-out runs | Actions API | 🟡 Warning |
| Evaluation duration trend | Actions API (run timestamps) | 🟡 if approaching timeout |
| Workflow failure rate by type | Actions API (aggregate) | 🔵 Info |
| Token/secret rotation health | (would need custom tracking) | 🔵 Info |

### 8.2 Skill Quality

| Check | Data Source | Severity |
|-------|------------|----------|
| Skills with `notActivated` flag | Benchmark data JSON or eval results | 🔴 Critical |
| Skills with quality regression (>10% drop) | Benchmark data (trend analysis) | 🔴 Critical |
| Skills with `timedOut` flag | Benchmark data JSON | 🟡 Warning |
| Skills with high variance across runs | Eval results JSON | 🟡 Warning |
| Skills without eval tests | File system scan | 🟡 Warning |
| Skilled vs Vanilla delta decreasing | Benchmark trend | 🟡 Warning |

### 8.3 PR & Review Health

| Check | Data Source | Severity |
|-------|------------|----------|
| PRs open > 7 days without review | PRs API | 🟡 Warning |
| PRs with failing checks | PRs API + Actions API | 🟡 Warning |
| WIP/draft PRs that are stale | PRs API | 🔵 Info |
| PR merge velocity (PRs merged/day) | PRs API | 🔵 Info |

### 8.4 Infrastructure Health

| Check | Data Source | Severity |
|-------|------------|----------|
| Missing CODEOWNERS | Repo contents API | 🟡 Warning (one-time) |
| Missing Dependabot config | Repo contents API | 🟡 Warning (one-time) |
| Relaxed validation rules | Workflow YAML content | 🟡 Warning |
| Dashboard deployment status | Pages API | 🔴 if broken |
| gh-pages data freshness | Data file timestamps | 🟡 if stale |

### 8.5 Resource Usage

| Check | Data Source | Severity |
|-------|------------|----------|
| Daily compute hours | Actions API (run durations) | 🔵 Info |
| Eval runs per day | Actions API | 🔵 Info |
| Estimated token consumption | Eval results (token counts) | 🔵 Info |
| Cost trend | Derived | 🟡 if trending up significantly |

---

## 9. Implementation Approaches

### 9.1 Option A: GitHub Agentic Workflow (LLM-Powered)

Follow the existing `gh aw` pattern from `src/dotnet-msbuild/agentic-workflows/`.

**Pros:**
- Consistent with existing repo patterns
- LLM can provide nuanced analysis and natural language summaries
- Can adapt to new situations without code changes
- Can correlate across data sources intelligently

**Cons:**
- LLM cost per run (tokens)
- Non-deterministic output
- Harder to test/validate
- Requires Copilot token

**Implementation:**
```
agentic-workflows/
  devops-health-check.md    # The workflow definition
  shared/
    compiled/devops.lock.md # Compiled knowledge about what to check
```

**Trigger:** `schedule: daily` + `workflow_dispatch` + `/health-check` slash command

### 9.2 Option B: Pure GitHub Actions Workflow (Deterministic)

A standard GitHub Actions workflow with PowerShell/bash scripts.

**Pros:**
- Deterministic, testable
- No LLM cost
- Faster execution
- Can still produce rich Markdown output

**Cons:**
- More rigid — needs code changes for new checks
- Can't provide nuanced analysis
- More maintenance burden

**Implementation:**
```
.github/workflows/devops-health-check.yml
eng/devops-health/
  collect-pipeline-health.ps1
  collect-skill-quality.ps1
  collect-pr-health.ps1
  generate-report.ps1
```

### 9.3 Option C: Hybrid (Recommended)

Deterministic data collection + LLM-powered analysis and reporting.

**Pros:**
- Best of both worlds
- Reliable data collection
- Intelligent analysis and recommendations
- Can fall back to script-only mode if LLM is unavailable

**Implementation:**
```
.github/workflows/devops-health-check.yml   # Orchestration
eng/devops-health/
  collect-data.ps1                           # Deterministic data gathering
  analyze-and-report.md                      # Agentic workflow for analysis
```

### 9.4 Output Options

| Output | Pros | Cons |
|--------|------|------|
| **GitHub Issue** (daily, update same issue) | Single well-known URL, history in comments | Can get cluttered |
| **GitHub Issue** (weekly, new issue) | Clean, searchable | Multiple issues to track |
| **Dashboard page** (new tab on existing dashboard) | Visual, always accessible | Requires gh-pages deployment |
| **PR comment** (on-demand) | Context-specific | Not a standing overview |
| **GITHUB_STEP_SUMMARY** | Built-in, visible in Actions | Ephemeral, not a standing overview |
| **Wiki page** | Persistent, easy to find | Wiki not enabled on this repo |

**Recommended:** A **pinned GitHub Issue** that gets updated daily (body replaced, history in comments) + an optional **dashboard tab** for visual trends.

---

## 10. Appendix: Raw Data

### 10.1 Current Workflow Run Statistics (2026-02-27)

- **Evaluation runs on main (last 10):** All ✅ success
- **Average eval duration:** 43.3 min (range: 39.5–46.2 min)
- **Evaluation schedule:** Every 2 hours (12 runs/day)
- **Total failed runs (last 100):** ~60, dominated by Copilot code review failures

### 10.2 Open Issues Summary

| # | Title | Category |
|---|-------|----------|
| #137 | dump-collect eval tests don't invoke SKILL | 🔴 Quality |
| #125 | add skill for .NET 9→10 migration | Feature |
| #83 | Add plugin version auto-bump | Infrastructure |
| #81 | Add plugin validation workflow | Infrastructure |
| #78 | skill: msbuild baseline eval | Quality |
| #38 | skill: nullable annotations | Feature |
| #35 | Use fewer, larger skills | Architecture |
| #31 | Skill: .NET Agent Framework | Feature |
| #10 | API Design Guideline Checker | Feature |
| #9 | JIT-Aware Hot Path Optimization | Feature |
| #8 | Trimming & AOT Readiness | Feature |
| #7 | Source Generator / Analyzer Authoring | Feature |
| #5 | .NET interop skill | Feature |

### 10.3 Failure Breakdown

```
Copilot code review (org-level, dynamic):     ~25 failures
Evaluation pipeline (early infra building):   ~15 failures  
Evaluation pipeline (recent, on main):         2 failures (#218, #221)
Agent validation (early):                      5 failures
Skill validation:                              0 recent failures
```

### 10.4 Evaluation Workflow Resource Estimates

| Metric | Value | Notes |
|--------|-------|-------|
| Runs per day (main) | 12 | Every 2 hours |
| Duration per run | ~43 min | 2 matrix jobs (dotnet, dotnet-msbuild) |
| Runs per test (main) | 5 | Statistical significance |
| Runs per test (PR) | 3 | Reduced quota pressure |
| Total daily compute | ~8.6 hours | 12 × 43 min |
| Token secrets | 8 | Random rotation per job |
| Artifact retention | 30 days | Per matrix entry |
| Timeout | 60 min | Per matrix job |

### 10.5 Dashboard Data Components

Currently tracked:
- `dotnet-msbuild.json` — Benchmark history
- `dotnet.json` — Benchmark history
- `components.json` — Manifest (`["dotnet-msbuild", "dotnet"]`)

### 10.6 Third-Party Actions in Use

| Action | Version | Used In | Risk |
|--------|---------|---------|------|
| `actions/checkout` | v4 | All workflows | Low (official) |
| `actions/upload-artifact` | v4 | evaluation | Low (official) |
| `actions/download-artifact` | v4 | evaluation | Low (official) |
| `actions/setup-dotnet` | v4 | evaluation, skill-validator | Low (official) |
| `Flash-Brew-Digital/validate-skill` | v1 | validate-skills | **Medium** (third-party) |
| `timheuer/vscode-agent-validation` | v0.1.0 | validate-agents | **Medium** (third-party, v0.x) |

---

## Summary of Key Recommendations

1. **Start with Option C (Hybrid)** — Deterministic collection + LLM analysis
2. **Output to a pinned GitHub Issue** — single well-known place, daily updates
3. **Priority checks for v1:**
   - Pipeline failures on `main` (🔴)
   - Skill activation failures / `notActivated` flags (🔴)
   - Quality regressions in benchmark data (🔴)
   - Stale PRs and review bottlenecks (🟡)
   - Eval duration creep toward timeout (🟡)
4. **Follow existing patterns** — Match the `gh aw` template structure from `agentic-workflows/`
5. **Add a dashboard tab** later for visual pipeline health trends
6. **Address the Copilot code review noise** — either fix the org-level workflow or suppress it in the report
