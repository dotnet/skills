#!/usr/bin/env bash
# pr-stale-sweep.sh
#
# Deterministic stale-PR sweep. Replaces the former agentic
# `close-stale-prs.agent.md`: no model calls, no tokens — just the GitHub API.
#
# Policy (unchanged from the agentic version):
#   * Consider every OPEN pull request, including drafts.
#   * "Last activity" is the most recent NON-bot comment or review; if there is
#     none, it falls back to the PR's created_at. We deliberately ignore
#     `updated_at` and all `[bot]` activity so the bot's own stale-warning
#     comment never resets the inactivity timer.
#   * created <= 30 days ago                          -> skip (too new)
#   * 30 days < inactivity <= 37 days                 -> post a stale WARNING
#                                                        (once; marker-guarded)
#   * inactivity > 37 days                            -> CLOSE the PR
#   * label `no-stale`                                -> exempt (skip)
#   * author dotnet-maestro[bot] / dotnet-maestro     -> exempt (skip)
#
# Required env:
#   GH_TOKEN            — token with pull-requests:write, issues:write
#   GITHUB_REPOSITORY   — owner/repo (set by Actions)
#
# Optional env:
#   DRY_RUN     — "true" to log intended actions without any writes
#   STALE_MAX   — hard cap on warn+close writes per run (default 25)
#   WARN_DAYS   — inactivity threshold for a warning (default 30)
#   CLOSE_DAYS  — inactivity threshold for a close   (default 37)
#
# Exits 0 on success (including no-op). Non-zero only on hard failures.

set -euo pipefail

: "${GH_TOKEN:?GH_TOKEN is required}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"

DRY_RUN="${DRY_RUN:-false}"
STALE_MAX="${STALE_MAX:-25}"
WARN_DAYS="${WARN_DAYS:-30}"
CLOSE_DAYS="${CLOSE_DAYS:-37}"

REPO="$GITHUB_REPOSITORY"
OWNER="${REPO%/*}"
NAME="${REPO#*/}"

# Idempotency marker embedded in every warning comment. Its presence on a PR
# means "already warned" regardless of how the visible text may change.
WARN_MARKER="<!-- pr-triage:stale-warning -->"

NOW_SECS=$(date -u +%s)
WARN_CUTOFF_SECS=$(( WARN_DAYS * 86400 ))
CLOSE_CUTOFF_SECS=$(( CLOSE_DAYS * 86400 ))

log() { printf '[stale-sweep] %s\n' "$*" >&2; }
summary() {
  if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    printf '%s\n' "$*" >> "$GITHUB_STEP_SUMMARY"
  fi
}

# Parse an ISO-8601 timestamp to epoch seconds (GNU date on the runner; BSD fallback).
to_epoch() {
  local ts="$1"
  date -u -d "$ts" +%s 2>/dev/null || date -u -j -f "%Y-%m-%dT%H:%M:%SZ" "$ts" +%s
}

WARNING_BODY() {
  cat <<EOF
$WARN_MARKER
This PR has been automatically marked as stale because it has no activity for ${WARN_DAYS} days. It will be closed if no further activity occurs within another $(( CLOSE_DAYS - WARN_DAYS )) days of this comment. If it is closed, you may reopen it anytime when you're ready again.
EOF
}

CLOSING_BODY() {
  cat <<EOF
This pull request has been automatically closed because it has been open for more than ${WARN_DAYS} days with no recent activity.

If you believe this work is still relevant, please feel free to reopen or create a new pull request. Thank you for your contribution!
EOF
}

# Most recent NON-bot activity epoch for a PR. Considers issue comments and
# reviews; ignores any author whose login ends in "[bot]". Falls back to the
# supplied created_at epoch when there is no human activity.
last_non_bot_activity_epoch() {
  local pr="$1" created_epoch="$2"
  local newest_ts

  # Emit one timestamp per non-bot comment/review, then pick the max. --paginate
  # runs --jq per page, so we must aggregate in the shell, not inside jq.
  newest_ts=$(
    {
      gh api --paginate "repos/$REPO/issues/$pr/comments" \
        --jq '.[] | select((.user.login | endswith("[bot]")) | not) | .created_at' 2>/dev/null || true
      gh api --paginate "repos/$REPO/pulls/$pr/reviews" \
        --jq '.[] | select(.user != null) | select((.user.login | endswith("[bot]")) | not) | .submitted_at' 2>/dev/null || true
    } | grep -v '^null$' | sort | tail -n 1
  )

  if [ -z "$newest_ts" ]; then
    echo "$created_epoch"
    return
  fi
  to_epoch "$newest_ts"
}

already_warned() {
  local pr="$1" hit
  hit=$(gh api --paginate "repos/$REPO/issues/$pr/comments" \
    --jq ".[] | select(.body | contains(\"$WARN_MARKER\")) | .id" 2>/dev/null | head -n 1)
  [ -n "$hit" ]
}

log "repo=$REPO dry_run=$DRY_RUN warn>${WARN_DAYS}d close>${CLOSE_DAYS}d max=$STALE_MAX"

summary "## Stale PR sweep"
summary ""
summary "Repo \`$REPO\` · dry_run=\`$DRY_RUN\` · warn>\`${WARN_DAYS}d\` · close>\`${CLOSE_DAYS}d\` · max=\`$STALE_MAX\`"
summary ""
summary "| PR | author | created | inactivity(d) | decision |"
summary "|---:|---|---|---:|---|"

# Enumerate all open PRs (drafts included). --limit caps the working set; PRs
# newer than WARN_DAYS are filtered out per-PR below.
PRS_JSON=$(gh pr list --repo "$REPO" --state open --limit 500 \
  --json number,createdAt,isDraft,author,labels)

COUNT=$(jq 'length' <<<"$PRS_JSON")
log "open PRs fetched: $COUNT"

ACTIONS=0
processed=0
while IFS= read -r row; do
  PR=$(jq -r '.number' <<<"$row")
  CREATED_AT=$(jq -r '.createdAt' <<<"$row")
  AUTHOR=$(jq -r '.author.login // ""' <<<"$row")
  LABELS=$(jq -r '[.labels[].name] | join(",")' <<<"$row")

  # Exemptions ------------------------------------------------------------
  if [[ ",$LABELS," == *",no-stale,"* ]]; then
    log "PR #$PR: no-stale label — exempt"
    continue
  fi
  case "$AUTHOR" in
    "dotnet-maestro[bot]"|"dotnet-maestro")
      log "PR #$PR: maestro-authored — exempt"
      continue ;;
  esac

  CREATED_EPOCH=$(to_epoch "$CREATED_AT")
  AGE_SECS=$(( NOW_SECS - CREATED_EPOCH ))
  # Too new: opened within WARN_DAYS.
  if [ "$AGE_SECS" -le "$WARN_CUTOFF_SECS" ]; then
    continue
  fi

  LAST_EPOCH=$(last_non_bot_activity_epoch "$PR" "$CREATED_EPOCH")
  INACTIVE_SECS=$(( NOW_SECS - LAST_EPOCH ))
  INACTIVE_DAYS=$(( INACTIVE_SECS / 86400 ))

  DECISION="skip(active)"
  if [ "$INACTIVE_SECS" -gt "$CLOSE_CUTOFF_SECS" ]; then
    DECISION="close"
  elif [ "$INACTIVE_SECS" -gt "$WARN_CUTOFF_SECS" ]; then
    if already_warned "$PR"; then
      DECISION="skip(already-warned)"
    else
      DECISION="warn"
    fi
  fi

  if [ "$DECISION" = "skip(active)" ] || [ "$DECISION" = "skip(already-warned)" ]; then
    log "PR #$PR: inactivity=${INACTIVE_DAYS}d -> $DECISION"
    continue
  fi

  summary "| #$PR | $AUTHOR | ${CREATED_AT%%T*} | $INACTIVE_DAYS | $DECISION |"

  if [ "$ACTIONS" -ge "$STALE_MAX" ]; then
    log "PR #$PR: reached STALE_MAX=$STALE_MAX — skipping remaining writes"
    continue
  fi

  case "$DECISION" in
    close)
      if [ "$DRY_RUN" = "true" ]; then
        log "PR #$PR: [DRY_RUN] would close (inactivity=${INACTIVE_DAYS}d)"
      else
        gh pr comment "$PR" --repo "$REPO" --body "$(CLOSING_BODY)" >/dev/null
        gh pr close "$PR" --repo "$REPO" >/dev/null
        log "PR #$PR: closed (inactivity=${INACTIVE_DAYS}d)"
      fi
      ACTIONS=$(( ACTIONS + 1 ))
      ;;
    warn)
      if [ "$DRY_RUN" = "true" ]; then
        log "PR #$PR: [DRY_RUN] would post stale warning (inactivity=${INACTIVE_DAYS}d)"
      else
        gh pr comment "$PR" --repo "$REPO" --body "$(WARNING_BODY)" >/dev/null
        log "PR #$PR: warned (inactivity=${INACTIVE_DAYS}d)"
      fi
      ACTIONS=$(( ACTIONS + 1 ))
      ;;
  esac
  processed=$(( processed + 1 ))
done < <(jq -c '.[]' <<<"$PRS_JSON")

summary ""
summary "**Actions taken (warn+close): $ACTIONS** (dry_run=\`$DRY_RUN\`)"
log "done — actions=$ACTIONS"
