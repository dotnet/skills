# DevOps Agentic Workflows

Cross-cutting [GitHub Agentic Workflow](https://github.com/github/gh-aw) workflows for repository-wide DevOps automation.

The workflow source files live in `.github/workflows/` and are compiled with `gh aw compile` to generate `.lock.yml` files (standard GitHub Actions YAML with security hardening). These workflows monitor the _entire repo_ (all components, all pipelines, all PRs).

## Available Workflows

| Workflow | Description | Trigger |
|----------|-------------|---------|
| [devops-health-check](../.github/workflows/devops-health-check.md) | Daily orchestrator that collects repo infrastructure health signals (pipelines, CI/CD infrastructure, resource usage), computes a fingerprint-based diff against the previous run, and updates a pinned health dashboard issue | `cron: 0 3 * * *` (03:00 UTC daily), `workflow_dispatch` |
| [devops-health-investigate](../.github/workflows/devops-health-investigate.md) | Worker agent dispatched by the health check orchestrator to perform deep root-cause analysis on individual findings | `workflow_dispatch` (dispatched by orchestrator via `dispatch-workflow`) |
| [devops-health-groom](../.github/workflows/devops-health-groom.md) | Runs ~3h after the health check to link investigation results into the issue body, hide stale comments (>7 days), and clean up resolved investigations | `cron: 0 6 * * *` (06:00 UTC daily), `workflow_dispatch` |
| [issue-triage](../.github/workflows/issue-triage.md) | Triages individual issues: assigns an `area-*` label, identifies owners from CODEOWNERS, adds the `Triaged` label, and posts a brief actionable summary | `issues: [opened, reopened]`, `workflow_dispatch` |
| [issue-triage-batch](../.github/workflows/issue-triage-batch.yml) | Deterministic workflow that dispatches the issue-triage agent for each untriaged issue in an optional date range | `workflow_dispatch` (with optional `date_from`/`date_to`) |
| [issue-investigate](../.github/workflows/issue-investigate.md) | Deep investigation agent that analyzes an issue against the codebase, suggests next steps, and creates a draft PR if the fix is clear | `issues: [labeled]` (when `auto-investigate` label is added) |

## Architecture

```
devops-health-check (Orchestrator) ─── runs daily
  ├─ Collects health signals from 3 categories:
  │   Pipeline · Infrastructure · Resources
  ├─ Fingerprints each finding for stable diff tracking
  ├─ Classifies: 🆕 NEW · 📌 EXISTING · ✅ RESOLVED
  ├─ Updates pinned health dashboard issue
  └─ Dispatches investigation workers (up to 10)
       │
       ▼
devops-health-investigate (Worker × N) ─── dispatched
  ├─ Investigates ONE finding with fresh context
  ├─ Follows category-specific playbook
  ├─ Determines root cause + remediation
  └─ Posts investigation results as a comment on the health issue
       │
       ▼  (~3 hours later)
devops-health-groom (Groomer) ─── runs daily
  ├─ Links investigation comments into the issue body
  │   (updates 🔄 Dispatched → ✅ Done with summary + link)
  ├─ Marks resolved investigations as ✅ Resolved
  ├─ Hides (collapses) daily overview comments older than 7 days
  └─ Hides (collapses) investigation comments for resolved findings
```

## Setup

1. Install the validated `gh aw` CLI extension: `gh extension install github/gh-aw --pin v0.89.22`
2. Compile: `gh aw compile` (from the repo root — this compiles all `.md` files in `.github/workflows/`)
3. Commit both the `.md` and generated `.lock.yml` files
4. The health check runs daily, or on-demand via `workflow_dispatch`

## Local Development

```powershell
# Compile workflows with the repository's deterministic action pins and
# schedule seed (generates .lock.yml from .md frontmatter)
gh aw compile --strict --validate --schedule-seed dotnet/skills `
  --action-mode action `
  --action-tag 2fbab69bfca02bebd76cd0fc43f2d12acfed994f

# Dry-run (validates without triggering on GitHub Actions)
gh aw run devops-health-check --dry-run

# Run the same active-workflow and package validation used by CI
python eng/agentic-workflows/validate_agentic_workflows.py --normalize

# Exercise the shipping proxy's custom-tool request/result contract (requires Docker)
python eng/agentic-workflows/test_copilot_proxy.py

# Exercise the packaged build-failure operational-value evaluator fixtures
python eng/agentic-workflows/test_build_failure_analysis_operational_value.py

# Run on GitHub Actions (from a pushed branch)
gh aw run devops-health-check --push --ref <branch>
```

## Runtime Compatibility and HTTP 400 Failures

The validated runtime is gh-aw **v0.89.22**, paired with the matching
`gh-aw-actions` commit and AWF **v0.28.25**. Keep the CI and Copilot setup
installers, validation constants, runtime-upgrade guard, package `min-version`
fields, and compiled workflows aligned when
upgrading. Commit regenerated locks and maintenance workflow together with
`.github/aw/actions-lock.json`.

When upgrading, run compilation with `--force-refresh-container-pins`, then
repeat the standard compilation command above to finalize cache-derived action
annotations before validation. This keeps Windows and Linux outputs identical.

AWF v0.28.16 corrupts Copilot Responses requests containing a custom tool such
as `apply_patch`: its provider body transform returns an object instead of a
buffer, producing an invalid upstream request (`Content-Length: undefined`).
The CLI reports only `400 Bad Request`, before any model tokens or workflow
work are produced. [The upstream fix](https://github.com/github/gh-aw-firewall/pull/8720)
is included in AWF v0.28.21 and later. AWF versions before v0.28.25 also
fail when replaying a custom-tool result: the translated function call retains
a `ctc_` ID, while Copilot requires an `fc_` ID.
[The replay fix](https://github.com/github/gh-aw-firewall/pull/8944) is included
in the pinned runtime too. Changing the model, rotating a working PAT, or
retrying the same request does not fix these runtime incompatibilities.

For this failure, use `gh aw audit <run-id> --repo dotnet/skills` and inspect
`agent-stdio.log`, the Copilot process log, and the pinned proxy image. Verify
the fix with the actual CLI through the pinned proxy, including a custom-tool
call and its result; compilation alone cannot detect request corruption.

## File Structure

```
.github/
├── workflows/
│   ├── devops-health-check.md              # Orchestrator workflow
│   ├── devops-health-check.lock.yml        # Compiled workflow (generated by gh aw compile)
│   ├── devops-health-investigate.md        # Worker workflow
│   ├── devops-health-investigate.lock.yml  # Compiled workflow (generated by gh aw compile)
│   ├── devops-health-groom.md              # Grooming workflow
│   ├── devops-health-groom.lock.yml        # Compiled workflow (generated by gh aw compile)
│   ├── issue-triage.md                     # Issue triage agent
│   ├── issue-triage.lock.yml               # Compiled workflow (generated by gh aw compile)
│   ├── issue-triage-batch.yml              # Batch triage dispatcher (standard GHA)
│   ├── issue-investigate.md                # Deep issue investigation agent
│   └── issue-investigate.lock.yml          # Compiled workflow (generated by gh aw compile)
└── aw/
    └── shared/
        ├── devops-health.lock.md       # Health check catalog & fingerprinting rules
        └── devops-investigate.lock.md  # Investigation playbooks & remediation templates
