# PR Triage Workflows

Three GitHub Actions workflows keep open PRs moving without manual nudging:

- **`pr-triage-batch.yml`** — hourly orchestrator (cron `17 * * * *`). Enumerates
  open non-draft PRs, computes a deterministic state for each, and dispatches the
  per-PR worker (or the malicious-code scanner). No comments, no labels, no model
  calls.
- **`pr-triage.yml`** — per-PR worker (`workflow_dispatch`). Re-validates the
  PR's state, reconciles a single `pr-state/*` label, and performs at most one
  of: trigger evaluation (via the `evaluate-now` label), ping the author, or
  ping maintainers. Cool-down (default 4 days) is enforced via marker comments.
- **`pr-malicious-scan.agent.md`** — per-PR malicious-code scanner (gh-aw).
  Static diff review for untrusted contributors. Reports findings as
  code-scanning alerts and an optional comment; never executes PR head code.

## Architecture

```mermaid
flowchart TD
    Cron["cron: every hour"] --> Batch["pr-triage-batch.yml<br/>(orchestrator)"]
    Batch -->|workflow_dispatch| Worker["pr-triage.yml<br/>(per-PR worker)"]
    Batch -->|workflow_dispatch| Scan["pr-malicious-scan.agent.lock.yml<br/>(per-PR scanner)"]
    Worker -->|adds 'evaluate-now' label| Eval["evaluation.yml<br/>(existing)"]
    Worker -->|adds pr-state/* label| PR[("PR")]
    Worker -->|posts ping comment| PR
    Scan -->|code-scanning alert + comment| PR
    PR -.->|labeled: evaluate-now| Eval
```

## Entry points into `evaluation.yml`

The existing `/evaluate` slash command continues to work. In addition, applying
the **`evaluate-now`** label fires evaluation via `pull_request_target [labeled]`.
Both paths share a per-PR concurrency group so a race collapses to a single run.
The label is consumed (removed) by the `gate` job so reapplying re-fires.

## State machine (worker)

Order of evaluation; first match wins:

| Order | Condition | State | Label | Action |
|---|---|---|---|---|
| 1 | draft, or `mergeable_state == unknown` | `skip` | — | none |
| 2 | non-bot && non-trusted && no malicious-scan marker on head | `needs-malicious-scan` | — | dispatch scanner |
| 3 | `CHANGES_REQUESTED` \|\| unresolved threads > 0 \|\| `mergeable_state == dirty` | `needs-author-attention` | `waiting-on-author` | author-ping |
| 4 | eval == success && `APPROVED` | `ready-for-merge` | `ready-to-merge` | maintainer-ping/C |
| 5 | eval == success && `REVIEW_REQUIRED`/none | `ready-for-review` | `waiting-on-review` | maintainer-ping/A |
| 6 | eval == success && other decision | `in-review` | `pr-state/in-review` | reconcile only |
| 7 | otherwise | `ready-for-eval` | `pr-state/ready-for-eval` | eval-trigger |

Trusted = `OWNER` / `MEMBER` / `COLLABORATOR`. Bots are short-circuited as trusted.

## Cool-down and idempotency

Each ping variant writes a hidden HTML marker into its comment. The worker
fetches prior bot comments and:

- If a marker for the same variant exists within `COOLDOWN_DAYS` (default 4),
  the new comment is suppressed.
- A first-ping age gate (default 30 min after PR creation) prevents pings on
  freshly opened PRs; it is bypassed once any prior ping marker exists.

Marker shapes:

- `<!-- pr-triage:fingerprint=author-ping:{sha7}:{yyyy-mm-dd} -->`
- `<!-- pr-triage:fingerprint=maintainer-ping/{A,B,C}:{sha7}:{yyyy-mm-dd} -->`
- `<!-- pr-malicious-scan:fingerprint={sha7}:{yyyy-mm-dd} -->`

## Labels owned by these workflows

State labels (exactly one is reconciled at a time). Where the existing label
taxonomy already covered a state, the workflow reuses it rather than introducing
a duplicate `pr-state/*` name:

- `pr-state/ready-for-eval` *(new)*
- `waiting-on-review` *(existing — reused for `ready-for-review`)*
- `ready-to-merge` *(existing — reused for `ready-for-merge`)*
- `waiting-on-author` *(existing — reused for `needs-author-attention`)*
- `pr-state/in-review` *(new)*

Triggers and opt-outs:

- `evaluate-now` — applied to fire evaluation; removed by the gate after consumption.
- `no-stale` — opt-out of stale-PR closure (consumed by `close-stale-prs`).
