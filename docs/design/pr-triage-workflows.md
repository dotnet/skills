# PR Triage Workflows — Implementation Plan

Status: **Draft / proposal** · Scope: `dotnet/skills` repository

## 1. Goals

Continuously assess every open pull request and move it to the right next step
without a human having to babysit the queue:

1. **Classify** each open PR into a deterministic state (ready-for-eval, ready-for-review,
   needs-author-attention).
2. **Drive the next action** for that state — trigger evaluation, ping maintainers,
   ping the author — while respecting a per-PR / per-actor cool-down.
3. **Scan PRs from non-Microsoft contributors** for suspicious / malicious changes and
   alert maintainers before any evaluation or review is requested.
4. Run cheaply and predictably (a few times per day) and stay well within the gh-aw
   `safe-outputs` model already used in the repo.

## 2. Non-goals

- Re-implementing the evaluation pipeline. Evaluation continues to be driven by
  [evaluation.yml](../../.github/workflows/evaluation.yml). The triage workflow
  drives it via a small new `pull_request_target: [labeled]` entry point
  (§6.2) and leaves the existing `/evaluate` `issue_comment` path intact for
  human use.
- Replacing the human-driven review process. The triage workflow only signals state and
  pings; it never approves, merges, or closes PRs (closing stale PRs already lives in
  [close-stale-prs.agent.md](../../.github/workflows/close-stale-prs.agent.md)).
- Sandboxing PR code execution. The malicious-code scan is **static-only**; it never
  checks out and runs untrusted code.

## 3. Existing building blocks we reuse

| Building block | Where | What we reuse |
|---|---|---|
| gh-aw + `safe-outputs` (`add-comment`, `add-labels`, `create-code-scanning-alert`) | `.github/workflows/*.agent.md` | Same compile / lock-yml / PAT-pool pattern that `close-stale-prs` and `issue-triage` already use. |
| `select-copilot-pat` action | [.github/actions/select-copilot-pat](../../.github/actions/select-copilot-pat) | Used **only** by the gh-aw scanner (Workflow C) for the agentic Copilot calls. The PATs in the pool are scoped to `Copilot Requests (Read)` only — they cannot post comments or labels. All GitHub side effects use the workflow's `GITHUB_TOKEN`. |
| Evaluation pipeline + `evaluation-status` commit status | [evaluation.yml](../../.github/workflows/evaluation.yml) | This is the contract the triage workflow drives. We extend it with one new entry point: `pull_request_target: [labeled, unlabeled]` gated on a dedicated `evaluate-now` label (§6.2). The existing `/evaluate` `issue_comment` path is preserved for humans. |
| Devops-health "fingerprint" pattern | `.github/aw/shared/devops-health.lock.md` | Reused as the cool-down / dedup mechanism via hidden HTML marker comments. |
| `issue-triage-batch.yml` standard-Actions dispatcher | [issue-triage-batch.yml](../../.github/workflows/issue-triage-batch.yml) | Reused as the deterministic outer loop pattern: a plain GHA workflow enumerates open PRs and dispatches a per-PR worker. |

## 4. Architecture

Three workflows, mirroring the orchestrator / worker / scanner split that already
works for issue-triage and devops-health.

```
                    ┌────────────────────────────────────────────────┐
                    │ pr-triage-batch (cron: every hour)             │
                    │ standard GHA, deterministic                    │
                    │   • list open PRs                              │
                    │   • compute state for each PR (no AI)          │
                    │   • dispatch worker per PR that needs an action│
                    └─────────┬───────────────────────────┬──────────┘
                              │ workflow_dispatch         │ workflow_dispatch
                              ▼                           ▼
              ┌──────────────────────────────┐  ┌──────────────────────────────┐
              │ pr-triage (plain GHA, per PR)│  │ pr-malicious-scan (per PR)   │
              │   • re-validate state        │  │   • static diff scan (gh-aw) │
              │   • toggle `evaluate-now`    │  │   • create code-scanning     │
              │     label to trigger eval    │  │     alerts on findings       │
              │   • ping author/maintainers  │  │   • ping maintainers if any  │
              │   • respect cool-down        │  │                              │
              └──────────────────────────────┘  └──────────────────────────────┘
```

Why split orchestrator / worker:

- The hourly enumerator must be **deterministic and cheap** (no model calls per PR
  that has nothing to do).
- A per-PR worker is the unit that owns one cool-down fingerprint, so its
  concurrency group is per-PR, mirroring `issue-triage`. Even though v1's worker is
  not agentic (see §7.1), keeping it as a separate dispatchable workflow preserves
  the option to add personalised wording later without redesigning the orchestrator.

Why a separate malicious-scan worker:

- It needs different permissions (`security-events: write` for code-scanning alerts) and
  a different trigger surface (`pull_request_target` for the open-on-fork case so we can
  run the scan immediately on first open, before the hourly cron fires).
- It must run from the **base** branch's workflow definition, never from the PR head.
- Keeping it separate makes its threat-model story isolated and reviewable.

## 5. State machine (deterministic)

The orchestrator computes the state of each open PR using only GitHub API data — no
model calls. The worker re-validates before acting (state can change between
enumeration and dispatch).

### 5.1 Inputs

For each open PR, gather:

| Signal | Source |
|---|---|
| `is_draft` | `pr.draft` |
| `is_fork` | `pr.head.repo.full_name != pr.base.repo.full_name` |
| `author_association` | `gh api repos/{repo}/pulls/{n} --jq .author_association` (`MEMBER`, `OWNER`, `COLLABORATOR` count as "trusted"). **Note**: `gh pr view --json` does **not** expose this field — must use the REST endpoint. Verified empirically against PR #713. |
| `mergeable` / `mergeable_state` | `gh api repos/{repo}/pulls/{n}` returns `mergeable` (boolean) and `mergeable_state` (`clean`, `dirty`, `blocked`, `behind`, `unstable`, `unknown`). Equivalent GraphQL via `gh pr view --json mergeable,mergeStateStatus` (uppercased: `MERGEABLE`/`CONFLICTING`/`UNKNOWN` and `CLEAN`/`DIRTY`/`BLOCKED`/`BEHIND`/`UNSTABLE`/`UNKNOWN`). |
| `unresolved_review_threads` | GraphQL `pullRequest.reviewThreads(first:100){ isResolved }` |
| `requested_changes` | latest `PULL_REQUEST_REVIEW` per reviewer; any `CHANGES_REQUESTED` not superseded by `APPROVED`/`COMMENTED` from same reviewer on a later commit |
| `evaluation_status_state` | `GET /repos/{repo}/statuses/{head_sha}` filter `context == "evaluation-status"`, take latest. **Caveat**: this status is posted asynchronously by the `pr-status` job; on a freshly opened PR the status array can be **empty** for a few seconds before the job posts. Treat "no status yet" as `pending`. Verified empirically on PR #713. |
| `last_eval_run_for_head` | `GET /repos/{repo}/actions/workflows/evaluation.yml/runs?head_sha={head_sha}` whose `event=issue_comment` |
| `labels` | `pr.labels` |
| `last_triage_marker` | most recent comment whose body **contains** `<!-- pr-triage:fingerprint=... -->` (do **not** require it to be the first line — see §10 / §5.4 below). |
| `malicious_scan_marker` | most recent comment whose body contains `<!-- pr-malicious-scan:fingerprint=... -->` |

Caveat captured during research: `pr.mergeable_state` is computed asynchronously by
GitHub. The first read after a push can return `null`/`unknown`. The orchestrator
treats `unknown` as "skip this PR this cycle" rather than guessing — the next hourly
run will see a settled value.

Also: the `blocked` value of `mergeable_state` (`BLOCKED` in GraphQL) does **not**
mean a merge conflict. It means a required check is missing or failing, or a review
is required. PR #713, which only edits `evaluation.yml`, sits at
`mergeable=MERGEABLE, mergeStateStatus=BLOCKED, reviewDecision=REVIEW_REQUIRED`
until evaluation succeeds and a review is recorded. The state machine in §5.2 must
therefore distinguish:

- `blocked` + no `pr-state/ready-for-eval` precondition met → still progresses
  through the normal states (often → `ready-for-eval` because evaluation hasn't
  run yet).
- `dirty` → `needs-author-attention` (real conflict).
- `behind` → not flagged as a conflict (caller's responsibility).
- `unknown` → skip cycle.

### 5.2 States

Before applying the table, the orchestrator short-circuits two cases:

- **Bot-authored PR** (`pr.user.type == "Bot"`, e.g. dependabot, copilot[bot]):
  state is `ready-for-eval` regardless of `author_association`. Bot identities are
  configured by repo admins; trust follows from the App install, not from the
  contributor signal. Skip `needs-malicious-scan` entirely.
- **Stale-close in progress**: if the PR carries the same "stale" label that
  [close-stale-prs.agent.md](../../.github/workflows/close-stale-prs.agent.md)
  applies, state is `skip`. Don't compete with the close countdown.

The state machine then uses GitHub's pre-computed `reviewDecision`
(`APPROVED` / `CHANGES_REQUESTED` / `REVIEW_REQUIRED`) plus `latestReviews` rather
than re-implementing CODEOWNERS expansion. CODEOWNERS is consulted **only** to
build the maintainer-ping mention list (§10); deciding *whether* a CODEOWNER has
approved is delegated to GitHub.

| State | Definition | Action on entry |
|---|---|---|
| `skip` | Draft, `no-stale` label exempts it from pings, stale-close label present, or `mergeable_state == unknown` | Do nothing. |
| `needs-malicious-scan` | Not a bot author **and** untrusted contributor (`author_association` ∉ trusted set) **and** no `pr-malicious-scan` marker for the current `head_sha` | Dispatch `pr-malicious-scan` worker. |
| `needs-author-attention` | `reviewDecision == CHANGES_REQUESTED` OR `unresolved_review_threads > 0` OR `mergeable_state == dirty` (actual merge conflict; `behind` is *not* a conflict and is ignored) | Apply label `pr-state/needs-author`. Ping author if cool-down and first-ping age gate allow. |
| `ready-for-eval` | None of the above AND `evaluation_status_state ∈ {pending, error, failure}` AND no successful eval run for current `head_sha` | Apply label `pr-state/ready-for-eval`. Toggle the `evaluate-now` label (§6.2) if cool-down allows. |
| `ready-for-review` | None of the above AND `evaluation_status_state == success` for current `head_sha` AND `reviewDecision == REVIEW_REQUIRED` (or `null` for repos with no required-review rule, in which case we treat "no review at all yet" as the trigger) | Apply label `pr-state/ready-for-review`. Ping CODEOWNERS for the changed paths (§10). If no CODEOWNERS rule matches the changed paths (or matches resolve to an empty set), ping `@dotnet/skills-merge-approvers` instead. Cool-down per §5.3. |
| `ready-for-merge` | None of the above AND `evaluation_status_state == success` AND `reviewDecision == APPROVED` | Apply label `pr-state/ready-for-merge`. Ping `@dotnet/skills-merge-approvers` if cool-down allows. |
| `in-review` | A review exists for the current `head_sha` but `reviewDecision` is neither `APPROVED` nor `CHANGES_REQUESTED` (e.g. `COMMENTED`-only) | Apply label `pr-state/in-review`. No ping. |

Labels are managed exclusively by this workflow (mutually exclusive `pr-state/*`).
Existing labels like `Triaged`, `area-*`, `no-stale` are untouched.

### 5.3 Cool-down (per-actor, per-PR)

Cool-down is **4 days by default**, configurable per action via workflow input. Two
tracking mechanisms are used — each picks whichever is the natural "already-done"
signal for that action.

Additionally, **first-ping age gate**: `author-ping` and `maintainer-ping` are
suppressed while `now - pr.updated_at < 30 minutes` (configurable). This prevents
piling on a PR the author is still actively iterating on. The gate does not apply
to `eval-trigger` (a machine action) or `malicious-scan` (security-sensitive,
should fire as soon as content is available).

| Action | Cool-down marker | Why |
|---|---|---|
| `eval-trigger` | The `evaluate-now` label being present, **or** the existence of any `evaluation.yml` workflow run for the current `head_sha` | The label *is* the trigger; the run record is the side-effect log. No comment marker needed. |
| `author-ping` | Hidden HTML marker comment | A pure-message action with no other side effect to query. |
| `maintainer-ping` | Hidden HTML marker comment | Same. |
| `malicious-scan` | Hidden HTML marker comment + (optionally) the per-`head_sha` code-scanning alert state | Re-scan on every new `head_sha`; suppress duplicate pings within cool-down for unchanged head. |

Marker comment format for the message-only actions:

```
<!-- pr-triage:fingerprint={action}:{head_sha_short}:{day} -->
```

Where `{action}` ∈ `author-ping | maintainer-ping | malicious-scan`. The worker:

1. Lists comments authored by the bot whose body contains `<!-- pr-triage:fingerprint={action}:`.
2. Picks the newest by `created_at`.
3. Skips the action if `now - created_at < cooldown_days`. (For pings, any push
   during the cool-down should not re-ping; the next cycle after the cool-down will.)

For `eval-trigger`, the worker instead:

1. Queries `gh api repos/{repo}/actions/workflows/evaluation.yml/runs?head_sha={sha}`.
2. If any run exists for the current `head_sha`, skips. (A new push removes the
   `evaluate-now` label automatically when GitHub recomputes labels on synchronize
   — actually, labels persist across pushes, so we explicitly remove `evaluate-now`
   in `evaluation.yml`'s first step; see §6.2.)
3. Otherwise, applies the `evaluate-now` label.

This means a force-push naturally resets the eval-trigger cool-down (new sha → no
run yet → trigger fires) but does not reset ping cool-downs.

## 6. Workflow A — `pr-triage-batch.yml` (orchestrator, deterministic)

Standard GHA workflow, no agentic engine. Mirrors `issue-triage-batch.yml`.

- **Trigger**: `schedule: cron: "17 * * * *"` (hourly, off-the-hour to avoid the
  evaluation cron at 00:00 UTC), plus `workflow_dispatch` for manual reruns.
- **`if`**: skip on forks (`!github.event.repository.fork`).
- **Permissions**: `pull-requests: read`, `actions: write`, `statuses: read`,
  `contents: read`. No `write` to comments — that lives in the worker.
- **Steps**:
  1. `gh api graphql` (or `gh pr list --json`) for cheap bulk metadata, then
     **per PR** call `gh api repos/{repo}/pulls/{n}` to get `author_association`
     and `mergeable_state` (these are *not* in `pr list --json`). For ~30 open PRs
     this is ~60 API calls per hourly run — well under the 5000/hr quota.
  2. For each PR, evaluate state per §5 using **only** the JSON above plus a per-PR
     status check via `gh api repos/{repo}/statuses/{sha}` (cheap; one call per PR).  3. Compute the action list. Emit a workflow summary table for observability.
  4. For each PR with a non-`skip`/non-`in-review` state:
     - `gh workflow run pr-triage.lock.yml -f pr_number=<n> -f intended_state=<state>`
     - Or `gh workflow run pr-malicious-scan.lock.yml -f pr_number=<n>` for
       `needs-malicious-scan`.
  5. Hard cap: dispatch ≤ 30 PRs/hour (matrix-style throttle) to bound cost.

### 6.1 Why deterministic (no AI) for the orchestrator

The user specifically asked for the state determination to be deterministic. gh-aw
supports "deterministic sections" in two ways: `safe-outputs` only (no model needed)
or a non-agentic standard-actions step in the same workflow. The simplest and most
auditable choice is to make the orchestrator a plain `.yml` GHA workflow (like
`issue-triage-batch.yml`) and reserve gh-aw for the per-PR worker that has to compose
a contextual ping comment.

### 6.2 New entry point in `evaluation.yml`: the `evaluate-now` label

**Why not `/evaluate` from the worker.** The PATs in the `COPILOT_PAT_*` pool
are scoped to `Copilot Requests (Read)` only (see
[select-copilot-pat README](../../.github/actions/select-copilot-pat/README.md)),
so they cannot post comments. They are also explicitly documented as for-Copilot-only:
“All outputs from the workflow use the `github-actions[bot]` account token. Issues,
PRs, comments, and all other content generated by the workflow will be attributed to
`github-actions[bot]`.” The `/evaluate` flow only fires because comments authored
by a *human* PAT trigger `issue_comment`; the `github-actions[bot]` token does not.
Provisioning a new long-lived PAT just for this would be all downside (extra secret,
rotation, blast radius) for no UX win over the label approach.

**The label entry point.** Extend `evaluation.yml` with one new trigger and one
small job. Concretely:

```yaml
on:
  # … existing triggers …
  pull_request_target:
    types: [opened, synchronize, labeled, unlabeled, reopened]
```

A new `label-trigger` job runs when:

```yaml
label-trigger:
  if: >-
    github.event_name == 'pull_request_target' &&
    github.event.action == 'labeled' &&
    github.event.label.name == 'evaluate-now'
  runs-on: ubuntu-latest
  permissions:
    contents: read
    pull-requests: write   # to remove the label after consuming it
    statuses: write        # to set the pending status
    actions: write         # not needed if we keep evaluation logic in this same file
  steps:
    - name: Verify label was applied by a trusted actor
      env: { GH_TOKEN: ${{ github.token }} }
      run: |
        SENDER='${{ github.event.sender.login }}'
        # Trusted = (a) workflow's own bot identity, OR (b) a user with write+
        if [[ "$SENDER" == 'github-actions[bot]' ]]; then exit 0; fi
        PERM=$(gh api repos/${{ github.repository }}/collaborators/$SENDER/permission --jq .permission)
        case "$PERM" in admin|write|maintain) ;; *) echo "::error::untrusted actor"; exit 1;; esac

    - name: Remove the label so re-applying re-fires
      env: { GH_TOKEN: ${{ github.token }} }
      run: gh pr edit ${{ github.event.pull_request.number }} --remove-label evaluate-now

    # Then jump into the same `discover`/build/eval pipeline the
    # `/evaluate` path uses today — the existing `gate` job already factored out
    # head_sha/base_sha/pr_number/is_fork outputs; this job emits the same shape
    # so `needs.gate.outputs.*` consumers are reused unchanged.
```

The `label-trigger` job replaces the `gate` job for label-driven invocations and
emits the same outputs (`head_sha`, `base_sha`, `pr_number`, `is_fork`). Downstream
jobs (`discover`, `build-validator`, `evaluate`, …) are unchanged.

**Why `pull_request_target` and not `pull_request`.** `pull_request_target` runs
from the **base ref**'s workflow definition with read-only secrets exposed; this is
the canonical safe trigger for fork PRs and matches the existing `fork-pr-status`
job in the same file. The triage worker's `evaluate-now` label is honored on fork
PRs through this path with no additional plumbing.

**Recursion safety.** A common worry is that the label-removal step will itself
generate an `unlabeled` event and re-run. Two reasons it does not:

1. The `if:` filter requires `github.event.action == 'labeled'`, not `unlabeled`.
2. Removal is performed via the workflow's own `GITHUB_TOKEN`. Per GitHub's
   workflow-recursion rules, events emitted by `GITHUB_TOKEN` do not trigger new
   workflow runs — which is the very property that broke the `/evaluate` path and
   we now exploit deliberately.

**Race between `/evaluate` and `evaluate-now`.** A maintainer typing `/evaluate`
at the same instant the triage worker applies the label could spawn two runs for
the same `head_sha`. We dedupe at the `evaluation.yml` top level with:

```yaml
concurrency:
  group: evaluation-${{ github.event.pull_request.number || github.event.issue.number }}-${{ github.event.pull_request.head.sha || github.event.issue.pull_request.url }}
  cancel-in-progress: true
```

Both entry points share the group. The second arrival cancels the first; net
effect is one run per sha regardless of trigger source. (Implementing this is a
prerequisite of the rollout's Phase 4 \u2014 see \u00a713.)

**The triage worker's role** is now just:

```bash
gh pr edit "$PR_NUMBER" --add-label evaluate-now
```

Using the standard workflow `GITHUB_TOKEN` with `pull-requests: write`. No PAT, no
comment, no recursion concern. The label is removed by `evaluation.yml` itself when
the label-trigger job fires, so the next time the PR returns to `ready-for-eval`
(typically after a new push that resets the eval status) the triage workflow can
re-apply the label and the cycle re-fires.

### 6.3 Manual override paths preserved

- A maintainer can still post `/evaluate` to trigger evaluation — the existing
  `issue_comment` `gate` job is untouched.
- A maintainer can also manually apply the `evaluate-now` label, taking the same
  path as the triage worker.
- `workflow_dispatch` continues to work for one-off plugin re-runs.

Validation evidence collected on PR #713 (2026-06-03) confirmed the
`/evaluate`-on-line-1 ordering and the existing `gate` permission check both
behave as documented; that path remains the human-facing fallback.

## 7. Workflow B — `pr-triage.yml` (per-PR worker, plain GHA — no gh-aw)

Per-PR worker. Triggered by the orchestrator via `workflow_dispatch`. Re-validates
state at runtime — does not trust the orchestrator's snapshot.

### 7.1 Why this is *not* a gh-aw workflow

The initial draft of this plan made the worker agentic. On review that's the wrong
shape:

- All ping comments are templated (author handle, sha, count of unresolved threads,
  CODEOWNERS team). There is no value a model adds beyond filling slots.
- All side effects are GitHub API calls expressible with `gh` and the workflow's
  default `GITHUB_TOKEN` (after the move to label-driven eval triggering in §6.2).
  There is nothing for an agent to *decide* once state is computed.
- gh-aw repo conventions inject the PAT-pool selection as a `pre-activation` job;
  introducing an agent here would also pull in `select-copilot-pat`, the
  Copilot-only PAT pool, and a markdown prompt that does nothing useful.

v1 worker is therefore a plain GHA workflow that uses `gh` directly with the
standard `GITHUB_TOKEN`. This matches `issue-triage-batch.yml` in spirit. If, later,
we want personalised ping wording, we can add a small gh-aw "scribe" workflow that
returns a sentence and is consumed by this worker — but that's a follow-up, not v1.

### 7.2 Skeleton

```yaml
name: pr-triage
on:
  workflow_dispatch:
    inputs:
      pr_number:     { required: true,  type: string }
      cooldown_days: { required: false, type: string, default: "4" }

concurrency:
  group: pr-triage-${{ inputs.pr_number }}
  cancel-in-progress: false   # do not cancel in-flight workers; let them complete

permissions:
  contents: read
  pull-requests: write   # add/remove labels, post ping comments
  issues: write          # PR comments use the issues comments API
  statuses: read
  actions: read

jobs:
  triage:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6
        with:
          persist-credentials: false
          sparse-checkout: .github/scripts
          sparse-checkout-cone-mode: true
          fetch-depth: 1
      - name: Recompute state and act
        env:
          GH_TOKEN:      ${{ github.token }}
          PR_NUMBER:     ${{ inputs.pr_number }}
          COOLDOWN_DAYS: ${{ inputs.cooldown_days }}
        run: ./.github/scripts/pr-triage-act.sh
```

### 7.3 What the script does (deterministic, in one place)

1. **Re-fetch** PR JSON + comments + reviews + threads + statuses; recompute state
   per §5.2. (Don't trust the orchestrator's input.)
2. **Reconcile labels**: remove any other `pr-state/*` labels the bot previously
   applied; ensure the one matching the current state is present.
3. **Cool-down decision** per §5.3:
   - For `eval-trigger`: query workflow runs for the current `head_sha`; if any
     run already exists, skip.
   - For ping actions: look up the bot's most recent `<!-- pr-triage:fingerprint={action}:… -->`
     marker comment, filter to comments authored by `github-actions[bot]`, compare
     `created_at` to `cooldown_days`.
4. **Apply the action**:
   - `eval-trigger` → `gh pr edit $PR_NUMBER --add-label evaluate-now`. No
     comment, no PAT, no `/evaluate`. `evaluation.yml`'s `label-trigger` job (§6.2)
     fires immediately and removes the label as its first step.
   - `author-ping` / `maintainer-ping` → `gh pr comment $PR_NUMBER --body "…"`
     where the body's first lines are the templated message followed by the
     hidden `<!-- pr-triage:fingerprint=… -->` marker.
5. **Idempotency**: each action is gated on its respective "already done" check
   (label/run lookup for eval; marker comment for pings), so a duplicate dispatch
   within the same minute produces at most one extra action.
## 8. Workflow C — `pr-malicious-scan.agent.md` (per-PR scanner, gh-aw)

Adapted from
[githubnext/agentics `malicious-code-scan.md`](https://github.com/githubnext/agentics/blob/main/workflows/malicious-code-scan.md),
narrowed from "last 3 days of commits" to "diff of one PR".

### 8.1 Trigger surface

Two triggers, both required:

1. `pull_request_target: [opened, synchronize, reopened]` — fires on fork PRs from
   the *base branch's* workflow definition, with read-only secrets. We **never**
   check out the PR head with write tokens; we fetch the diff via API.
2. `workflow_dispatch` (input `pr_number`) — for orchestrator dispatch and reruns.

`pull_request_target` is the canonical safe trigger for fork-PR analysis (see
[GitHub docs](https://docs.github.com/actions/security-guides/automatic-token-authentication)
and the existing `fork-pr-status` job in `evaluation.yml` which uses the same
pattern). It runs from the base ref's workflow YAML, so a malicious PR can't modify
the scanner.

### 8.2 Scope — when to scan

- Scan if and only if `author_association ∉ {OWNER, MEMBER, COLLABORATOR}`.
  External contributors only.
- Skip if a `pr-malicious-scan` marker exists for the current `head_sha` (idempotent
  per push).
- Re-scan on every `synchronize` event so a malicious update mid-PR is still caught.

### 8.3 Inputs to the scan

The scan operates on the **textual diff**, not on a checkout-and-build:

```bash
gh api repos/{repo}/pulls/{pr_number}/files --paginate \
  --jq '.[] | {filename, status, additions, deletions, patch}'
```

Plus, for binary or oversized patches, fetch the raw blob from the head ref via
`GET /repos/{repo}/contents/{path}?ref={head_sha}` (read-only).

### 8.4 Detections (taken from the agentics reference, reduced to PR-relevant)

| Category | Pattern |
|---|---|
| `secret-exfiltration` | New code that combines secret-shaped tokens (`secret`, `token`, `api_key`, `password`, `BEGIN RSA PRIVATE KEY`) with an outbound network primitive (`curl`, `wget`, `fetch(`, `requests.post`, `HttpClient`, `WebClient`) added in the same hunk. |
| `obfuscation` | Long base64/hex literals (≥ 100 chars), `eval(atob(`, `Invoke-Expression` of decoded strings, `FromBase64String` followed by `Assembly.Load`. |
| `out-of-context` | Newly added executables / DLLs / scripts under directories that previously held only markdown (`plugins/*/skills/*/**`, `tests/*`). |
| `supply-chain` | New entries (not edits to existing entries) in `*.csproj` `<PackageReference>`, `global.json` SDK or MSBuild SDKs, `package.json` `dependencies`/`devDependencies`, `Dockerfile` `FROM`/`RUN curl|wget`, or any addition under `.github/actions/**`. Plain version bumps of pre-existing dependencies do not flag. |
| `workflow-tamper` | Any `.github/workflows/**` change from an external contributor — automatic high-severity, regardless of content. |
| `system-access` | New `Process.Start`, `os.system`, shell injection sinks tied to user-controlled input. |

The patterns are encoded as a deterministic bash pre-pass (cheap regex over the
diff) plus an agent pass that reads only flagged hunks for context-aware
classification — this keeps the agent budget low and the scan auditable.

### 8.5 Outputs (gh-aw `safe-outputs`)

```yaml
safe-outputs:
  create-code-scanning-alert:
    driver: "PR Malicious Code Scanner"
  add-comment:    { max: 1 }   # maintainer ping if any finding
  add-labels:     { max: 2 }
  noop:           { report-as-issue: false }
```

- Findings are surfaced as code-scanning alerts (visible in the Security tab) with
  the file/line metadata gh-aw expects.
- If at least one alert is created **OR** any `workflow-tamper` / `supply-chain`
  match is found, the worker also posts a single comment pinging the CODEOWNERS team
  and applies a `pr-needs-security-review` label — one comment per `head_sha`,
  marker-tracked.
- If clean: `noop` with the file count, no PR comment.

### 8.6 Permissions

```yaml
permissions:
  contents: read
  pull-requests: write
  security-events: write   # for create-code-scanning-alert
  issues: write
```

`security-events: write` is the strict minimum for code-scanning alerts; nothing
else escalates beyond what `pull_request_target` already grants.

## 9. Labels (single source of truth)

| Label | Meaning | Applied by |
|---|---|---|
| `pr-state/ready-for-eval` | Mergeable, no unresolved review threads, awaiting `/evaluate` | pr-triage |
| `pr-state/ready-for-review` | Eval succeeded for current head, awaiting CODEOWNER (or `@dotnet/skills-merge-approvers` if no CODEOWNERS match) review | pr-triage |
| `pr-state/ready-for-merge` | Eval succeeded and `reviewDecision == APPROVED`; awaiting `@dotnet/skills-merge-approvers` for merge | pr-triage |
| `pr-state/needs-author` | Open review threads, requested changes, or merge conflicts | pr-triage |
| `pr-state/in-review` | Maintainer review in progress, no action needed | pr-triage |
| `pr-needs-security-review` | Malicious-code scan flagged something on some `head_sha`. Auto-cleared by the scanner when a later `head_sha` is scanned clean **and** no open code-scanning alerts remain for the PR. May also be removed manually. | pr-malicious-scan |
| `evaluate-now` | Trigger / re-trigger `evaluation.yml` for the current `head_sha`. Applied by the worker, by maintainers, or by `workflow_dispatch`. Removed automatically by `evaluation.yml`'s `label-trigger` job as its first step \u2014 always transient. | pr-triage / maintainer / `evaluation.yml` |
| `no-stale` (existing) | Exempts PR from any pings | manual |

A small bootstrap step (run once via `workflow_dispatch` of `pr-triage-batch`) creates
the labels with `gh label create --force` if missing. Same idempotent pattern as
`issue-triage`.

## 10. Comment templates (initial drafts)

The `eval-trigger` action posts **no comment** — it toggles the `evaluate-now`
label (§6.2). The remaining bot comments embed an HTML marker comment for
cool-down lookup.

### `author-ping`

```
<!-- pr-triage:fingerprint=author-ping:{sha7}:{yyyy-mm-dd} -->
👋 @{author} — this PR has {N} unresolved review thread(s) / merge conflicts. When you're ready, please address them and push an update; the triage bot will pick up the next state automatically. (Add the `no-stale` label to silence further pings.)
```

### `maintainer-ping`

The worker resolves [`.github/CODEOWNERS`](../../.github/CODEOWNERS) from the
base ref, matches the PR's changed paths (`gh pr diff --name-only` or
`GET /repos/{repo}/pulls/{n}/files`), and unions the resulting `@user` /
`@dotnet/...` handles. CODEOWNERS resolution reuses the lookup logic from
`issue-triage.md` step 4.

Variant A — PR is in `ready-for-review` and CODEOWNERS resolved to a non-empty set:

```
<!-- pr-triage:fingerprint=maintainer-ping:{sha7}:{yyyy-mm-dd} -->
✅ Evaluation passed for {sha7}. cc {codeowner-handles} — please review.
```

Variant B — PR is in `ready-for-review` but no CODEOWNERS rule matches the changed
paths (or all matching rules resolve to an empty set):

```
<!-- pr-triage:fingerprint=maintainer-ping:{sha7}:{yyyy-mm-dd} -->
✅ Evaluation passed for {sha7}. No CODEOWNERS entry matched the changed paths; cc @dotnet/skills-merge-approvers — please review.
```

Variant C — PR is in `ready-for-merge` (a CODEOWNER has already approved):

Variant C — PR is in `ready-for-merge` (`reviewDecision == APPROVED`). The
worker derives `{approving-handles}` from `pullRequest.latestReviews` filtered to
`state == APPROVED`:

```
<!-- pr-triage:fingerprint=maintainer-ping/C:{sha7}:{yyyy-mm-dd} -->
✅ Approved by {approving-handles}. cc @dotnet/skills-merge-approvers — ready to merge.
```

> **Note on team mentions.** GitHub may not deliver `@dotnet/...` mentions from
> `github-actions[bot]` reliably for private teams. The `pr-state/ready-for-merge`
> and `pr-state/ready-for-review` labels are the belt-and-braces signal:
> maintainers can subscribe via a saved search even if the mention itself is
> filtered. Validate empirically in Phase 3 before relying on the mention.

The single `maintainer-ping` fingerprint is shared across the three variants so
the cool-down treats them as one logical action: the worker re-pings only when
the cool-down has elapsed *and* the variant for the current state has not been
posted for the current `head_sha`. (Concretely: include the variant tag in the
marker, e.g. `maintainer-ping/A`, `maintainer-ping/B`, `maintainer-ping/C`, so a
state transition from B to C posts a fresh ping immediately while a same-variant
repeat respects the 4-day window.)

### `malicious-scan`

```
<!-- pr-malicious-scan:fingerprint={sha7}:{yyyy-mm-dd} -->
🚨 The PR malicious-code scanner flagged {N} finding(s) on {sha7}. Details are in the [Security tab]({alerts_url}). cc @{security-team} — please review before requesting evaluation.
```

## 11. Security model

| Concern | Mitigation |
|---|---|
| Untrusted PR code execution | Static-only scan; **no checkout** of PR head with write tokens; diff fetched via API. `pr-malicious-scan` runs from base-ref workflow definition via `pull_request_target`. |
| Untrusted contributor applying `evaluate-now` to trigger evaluation | The new `label-trigger` job in `evaluation.yml` (§6.2) verifies `github.event.sender.login` is either `github-actions[bot]` (the triage worker) or a user with `admin…write…maintain` permission. Anyone else applying the label is rejected. |
| Privileged comment from untrusted contributor triggering `/evaluate` re-runs | Unchanged: `evaluation.yml`'s existing `gate` job already requires `write+`. |
| Triage worker token scope | The triage worker uses only the workflow's `GITHUB_TOKEN` with `pull-requests: write` + `issues: write` + `actions: write` (for dispatching the gh-aw scanner). No long-lived PAT is introduced or required. |
| `COPILOT_PAT_*` pool exposure | Used **only** by the gh-aw scanner (Workflow C) for Copilot calls; never used to drive GitHub side effects. The pool's PATs lack the scopes to do so anyway (`Copilot Requests (Read)` only). |
| Marker forgery (someone posts a `<!-- pr-triage:fingerprint=... -->` comment to suppress pings) | Marker lookup MUST filter `comment.user.login == 'github-actions[bot]'`. Only the triage workflow's `GITHUB_TOKEN` can post comments under that identity, so authenticity follows. |
| `pull_request_target` confused-deputy | Standard hardening: `persist-credentials: false`, no `actions/checkout` of `head_ref`, all PR data via API. Mirror `evaluation.yml`'s `fork-pr-status` job. |
| Concurrency races | `concurrency.group: pr-triage-{pr_number}` ensures only one worker runs per PR at a time, even if the orchestrator dispatches twice. |

## 12. Failure modes and edge cases

| Edge case | Behavior |
|---|---|
| PR opened, then immediately force-pushed | `mergeable_state` is briefly `unknown`. Orchestrator skips this cycle; next hourly run picks it up. |
| Force-push during cool-down | Eval-trigger naturally re-fires (no run exists yet for the new `head_sha`). Ping cool-downs do **not** reset — the marker lookup compares `created_at` to wall-clock time, not to `head_sha`. |
| Author resolves all review threads but doesn't push | State flips from `needs-author` to `ready-for-eval` on the next cycle. The worker reconciles the `pr-state/*` labels atomically. |
| Maintainer posts `/evaluate` manually inside the cool-down | The `/evaluate` `issue_comment` path is independent of the bot's cool-down. Manual triggers always work. |
| Maintainer applies `evaluate-now` manually | Allowed by the label-trigger job's permission gate. Same code path as the worker; label is removed and evaluation runs. |
| Evaluation fails | `evaluation-status` becomes `failure`. The eval-trigger cool-down is keyed on "any run exists for this `head_sha`", so we do **not** auto-retry on the same sha (avoids retry storms on broken eval infra). The next push gives a new sha and the trigger re-fires. |
| `evaluate-now` label applied while a run is already in progress for the same sha | Eval-trigger cool-down skips re-application. The label is removed by the in-progress run's `label-trigger` job's first step regardless. |
| External contributor's PR has a malicious-scan finding then is force-pushed clean | Re-scan triggered by `synchronize`. New marker for new `head_sha`. Old finding remains in code-scanning history (this is correct — alerts have their own lifecycle). |
| Two PRs from the same author, both untrusted | Independent fingerprints (per PR number); independent cool-downs. Correct. |
| Maintainer pinged via `pr-state/ready-for-review` but doesn't review | Cool-down expires after 4 days; we ping again. Maintainer can `no-stale` to silence. |

## 13. Rollout

Phase 1 (read-only, dry-run):
- Land orchestrator and worker with a `DRY_RUN=true` env var that short-circuits all
  `add-comment` and `add-label` calls; the worker still writes a workflow summary
  line per PR describing what it *would* have done.
- For the gh-aw scanner (Workflow C), keep `add-comment` and
  `create-code-scanning-alert` disabled and set `noop: report-as-issue: true` until
  Phase 4.
- Run the orchestrator for one week. Inspect generated summaries.

Phase 2 (state labels only):
- Enable `pr-state/*` label management. Confirm labels match expectations on real PRs.

Phase 3 (pings):
- Enable `author-ping` and `maintainer-ping` comments. Confirm cool-down works on a
  synthetic PR by triggering the worker twice within an hour.

Phase 4 (eval triggering):
- Land the `concurrency` group on `evaluation.yml` (§6.2) so `/evaluate` and
  `evaluate-now` cannot race.
- Land the `label-trigger` job in `evaluation.yml` and enable the `evaluate-now`
  label action in the worker. Manually apply the label on a sandbox PR before
  letting the worker do it; verify the run starts and the label is removed.

Phase 5 (malicious scan):
- Enable the malicious-scan workflow on `pull_request_target`. Manually walk a known-
  benign external contribution and a synthetic suspicious one through the scanner
  before letting it post comments.

## 14. Open questions / decisions for the maintainer

1. **`@dotnet/skills-merge-approvers` membership and access** — the design pings
   this team as the fallback reviewer (no CODEOWNERS match) and as the merge
   gate (after CODEOWNER approval). Confirm the team exists, has at least
   `Triage` access on `dotnet/skills`, and the membership is current. Creating
   or modifying the team requires a `dotnet` org owner; the triage workflow
   itself only references it by handle.
2. **Stale-close label name** — §5.2 short-circuits to `skip` when the PR carries
   the same label that `close-stale-prs.agent.md` applies. Confirm the exact
   label name (currently expected to be `stale`) so the worker can match it
   without drift.

## 15. Validation / test plan

Before enabling on the main branch:

| Test | How |
|---|---|
| `evaluate-now` label triggers `evaluation.yml` end-to-end | Land the `label-trigger` job in `evaluation.yml`. Manually apply the label on a sandbox PR with the workflow's own `GITHUB_TOKEN` (via `gh workflow run`) and confirm a new evaluation run starts on `pull_request_target` with `event.action=labeled`. |
| Label is removed by `label-trigger` itself | Same test, confirm `evaluate-now` is gone after the run starts. |
| Removing the label does not re-fire the workflow | Confirm no new run is created with `event.action=unlabeled` (the `if:` filter excludes it). |
| Untrusted actor cannot abuse the label | Apply `evaluate-now` from a fork-PR author account (no write); confirm the `label-trigger` job's permission step exits non-zero and no evaluation runs. |
| `/evaluate` from a user with `write+` still works (manual fallback) | **DONE** — verified on PR #713 (2026-06-03): `JanKrivanek` (write) posted `/evaluate`, run `26890795146` started, `gate` succeeded. |
| Cool-down for pings | Run worker twice within 4 days against the same PR, confirm second run produces no new comment. |
| Force-push resets eval-trigger but not pings | Force-push a sandbox PR; confirm `evaluate-now` is re-applied (because no run exists for the new sha) but `author-ping` is not re-posted (within window). |
| `pull_request_target` malicious-scan does not check out PR head with write token | Audit lock-yml output; confirm no `actions/checkout` step references `github.event.pull_request.head.sha`. |
| Label atomicity | Move a sandbox PR through state transitions; confirm exactly one `pr-state/*` label at any time. |
| External contributor with no commits triggers nothing | Open empty PR; confirm orchestrator marks it `needs-malicious-scan`, scanner does nothing, `pr-needs-security-review` is not applied. |
| `pr.mergeable_state` reaches `unknown` on a fresh push | Force-push and confirm orchestrator skips that cycle without crashing. |

## 16. Concrete file layout (deliverables)

```
.github/
├── workflows/
│   ├── pr-triage-batch.yml             # NEW — orchestrator (deterministic GHA)
│   ├── pr-triage.yml                   # NEW — per-PR worker (deterministic GHA)
│   ├── pr-malicious-scan.agent.md      # NEW — scanner (gh-aw)
│   ├── pr-malicious-scan.agent.lock.yml # NEW — generated by `gh aw compile`
│   └── evaluation.yml                  # MODIFIED — adds `pull_request_target: [labeled]` trigger and `label-trigger` job (§6.2)
├── scripts/
│   └── pr-triage-act.sh                # NEW — worker logic (state recompute, label/marker, post)
└── (no other changes to existing files)

docs/
└── design/
    └── pr-triage-workflows.md          # this document
```

The change to `evaluation.yml` is purely additive: a new event filter and a new
job. All existing trigger paths (`pull_request`, `pull_request_target` on open/sync,
`/evaluate` `issue_comment`, scheduled cron, `workflow_dispatch`) continue to work
unchanged. The triage workflow drives the existing pipeline through a single new
documented contract \u2014 the `evaluate-now` label \u2014 with `/evaluate` preserved as
the human-facing fallback.
