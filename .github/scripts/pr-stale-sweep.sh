#!/usr/bin/env bash
# pr-stale-sweep.sh
#
# Deterministic stale-PR sweep. Replaces the former agentic
# `close-stale-prs.agent.md`: no model calls, no tokens — just the GitHub API.
#
# Policy (unchanged from the agentic version):
#   * Consider every OPEN pull request, including drafts. The open-PR set is
#     fetched with full pagination (no upper bound).
#   * "Last activity" is the most recent NON-bot issue comment, review, or
#     inline review comment; if there is none, it falls back to the PR's
#     created_at. We deliberately ignore `updated_at` and all `[bot]` activity
#     so the bot's own stale-warning comment never resets the inactivity timer.
#   * created <= 30 days ago                          -> skip (too new)
#   * 30 days < inactivity <= 37 days                 -> post a stale WARNING
#                                                        (once; marker-guarded)
#   * inactivity > 37 days                            -> CLOSE the PR
#   * label `no-stale`                                -> exempt (skip)
#   * author dotnet-maestro[bot] / dotnet-maestro     -> exempt (skip)
#
# Safety:
#   * Fail-safe reads: if any activity/comment source cannot be read for a PR,
#     that PR is SKIPPED (never decided on partial data), so a transient API
#     failure can never cause a wrongful close.
#   * STALE_MAX is applied only AFTER sorting eligible PRs by inactivity
#     (stalest first), so the cap can never starve an old closure behind a
#     newer warning.
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

# Validate numeric inputs up front. They arrive as strings from env/dispatch
# inputs but are used in arithmetic and comparisons, so a non-integer (e.g. a
# typo on a manual dispatch) would otherwise blow up with an opaque bash
# arithmetic error mid-sweep. Fail fast with a clear message instead.
for _var in STALE_MAX WARN_DAYS CLOSE_DAYS; do
  if ! [[ "${!_var}" =~ ^[0-9]+$ ]]; then
    echo "::error::$_var must be a non-negative integer (got '${!_var}')" >&2
    exit 2
  fi
done
if [ "$CLOSE_DAYS" -le "$WARN_DAYS" ]; then
  echo "::error::CLOSE_DAYS ($CLOSE_DAYS) must be greater than WARN_DAYS ($WARN_DAYS)" >&2
  exit 2
fi

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
#
# Returns non-zero if ANY activity source cannot be read. Callers MUST treat a
# non-zero return as "unknown activity" and skip the PR — silently omitting a
# source could drop the only recent human touch and wrongly close a live PR.
last_non_bot_activity_epoch() {
  local pr="$1" created_epoch="$2"
  local issue_comments reviews review_comments newest_ts

  # Three human-activity sources. Each --paginate runs --jq per page, so we
  # aggregate timestamps in the shell. Do NOT swallow gh failures: if a read
  # fails, propagate it so the caller skips the PR rather than deciding on
  # partial data.
  #   1. issue comments        (repos/{}/issues/{pr}/comments)      -> created_at
  #   2. reviews               (repos/{}/pulls/{pr}/reviews)        -> submitted_at
  #   3. inline review comments(repos/{}/pulls/{pr}/comments)       -> created_at
  if ! issue_comments=$(gh api --paginate "repos/$REPO/issues/$pr/comments" \
      --jq '.[] | select(.user != null) | select((.user.login | endswith("[bot]")) | not) | .created_at'); then
    return 1
  fi
  if ! reviews=$(gh api --paginate "repos/$REPO/pulls/$pr/reviews" \
      --jq '.[] | select(.user != null) | select((.user.login | endswith("[bot]")) | not) | .submitted_at'); then
    return 1
  fi
  if ! review_comments=$(gh api --paginate "repos/$REPO/pulls/$pr/comments" \
      --jq '.[] | select(.user != null) | select((.user.login | endswith("[bot]")) | not) | .created_at'); then
    return 1
  fi

  # `|| true` here only guards grep returning 1 on no matches (a pure text
  # pipeline); the API reads above already succeeded, so no error is hidden.
  newest_ts=$(printf '%s\n%s\n%s\n' "$issue_comments" "$reviews" "$review_comments" \
    | grep -v -e '^null$' -e '^$' | sort | tail -n 1 || true)

  if [ -z "$newest_ts" ]; then
    echo "$created_epoch"
    return 0
  fi
  to_epoch "$newest_ts"
}

# 0 = already warned, 1 = not warned, 2 = read error (caller should skip).
already_warned() {
  local pr="$1" hit
  if ! hit=$(gh api --paginate "repos/$REPO/issues/$pr/comments" \
      --jq ".[] | select(.body != null) | select(.body | contains(\"$WARN_MARKER\")) | .id"); then
    return 2
  fi
  [ -n "$(printf '%s' "$hit" | head -n 1)" ]
}

log "repo=$REPO dry_run=$DRY_RUN warn>${WARN_DAYS}d close>${CLOSE_DAYS}d max=$STALE_MAX"

# ----------------------------------------------------------------------
# Phase 1 — enumerate every open PR and compute a decision for each.
# ----------------------------------------------------------------------
# Fully paginated (no upper bound) so the sweep really considers EVERY open PR,
# drafts included. --paginate walks all pages; @tsv emits one line per PR.
if ! PR_ROWS=$(gh api --paginate "repos/$REPO/pulls?state=open&per_page=100" \
    --jq '.[] | [.number, .created_at, .draft, (.user.login // ""), ([.labels[].name] | join(","))] | @tsv'); then
  echo "::error::failed to enumerate open PRs — aborting sweep" >&2
  exit 1
fi

# Candidate records for writes, one per line:
#   INACTIVE_SECS|PR|AUTHOR|CREATED_DATE|INACTIVE_DAYS|DECISION
CANDIDATES=""
FETCHED=0
SKIPPED_READ_ERR=0

while IFS=$'\t' read -r PR CREATED_AT IS_DRAFT AUTHOR LABELS; do
  [ -z "${PR:-}" ] && continue
  FETCHED=$(( FETCHED + 1 ))

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

  # Fail safe: if any activity source can't be read, skip this PR rather than
  # risk closing a PR that actually had recent human activity.
  if ! LAST_EPOCH=$(last_non_bot_activity_epoch "$PR" "$CREATED_EPOCH"); then
    log "::warning::PR #$PR: activity read failed — skipping (won't decide on partial data)"
    SKIPPED_READ_ERR=$(( SKIPPED_READ_ERR + 1 ))
    continue
  fi
  INACTIVE_SECS=$(( NOW_SECS - LAST_EPOCH ))
  INACTIVE_DAYS=$(( INACTIVE_SECS / 86400 ))

  DECISION="skip(active)"
  if [ "$INACTIVE_SECS" -gt "$CLOSE_CUTOFF_SECS" ]; then
    DECISION="close"
  elif [ "$INACTIVE_SECS" -gt "$WARN_CUTOFF_SECS" ]; then
    if already_warned "$PR"; then
      DECISION="skip(already-warned)"
    else
      rc=$?
      if [ "$rc" = "2" ]; then
        log "::warning::PR #$PR: comment read failed — skipping"
        SKIPPED_READ_ERR=$(( SKIPPED_READ_ERR + 1 ))
        continue
      fi
      DECISION="warn"
    fi
  fi

  if [ "$DECISION" = "skip(active)" ] || [ "$DECISION" = "skip(already-warned)" ]; then
    log "PR #$PR: inactivity=${INACTIVE_DAYS}d -> $DECISION"
    continue
  fi

  CANDIDATES+="${INACTIVE_SECS}|${PR}|${AUTHOR}|${CREATED_AT%%T*}|${INACTIVE_DAYS}|${DECISION}"$'\n'
done <<<"$PR_ROWS"

log "open PRs fetched: $FETCHED (read-error skips: $SKIPPED_READ_ERR)"

# ----------------------------------------------------------------------
# Phase 2 — sort eligible PRs by inactivity (oldest activity first) and apply
# STALE_MAX only after sorting, so the stalest closures are never starved by
# newer warnings when the cap is hit.
# ----------------------------------------------------------------------
summary "## Stale PR sweep"
summary ""
summary "Repo \`$REPO\` · dry_run=\`$DRY_RUN\` · warn>\`${WARN_DAYS}d\` · close>\`${CLOSE_DAYS}d\` · max=\`$STALE_MAX\`"
summary ""
summary "| PR | author | created | inactivity(d) | decision |"
summary "|---:|---|---|---:|---|"

ACTIONS=0
if [ -n "$CANDIDATES" ]; then
  # -rn on the leading INACTIVE_SECS field: largest inactivity (closures) first.
  SORTED=$(printf '%s' "$CANDIDATES" | grep -v '^$' | sort -t'|' -k1,1 -rn)
  while IFS='|' read -r _SECS PR AUTHOR CREATED_DATE INACTIVE_DAYS DECISION; do
    [ -z "${PR:-}" ] && continue

    if [ "$ACTIONS" -ge "$STALE_MAX" ]; then
      log "PR #$PR: reached STALE_MAX=$STALE_MAX — skipping remaining writes"
      summary "| #$PR | $AUTHOR | $CREATED_DATE | $INACTIVE_DAYS | ${DECISION} (capped) |"
      continue
    fi

    summary "| #$PR | $AUTHOR | $CREATED_DATE | $INACTIVE_DAYS | $DECISION |"

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
  done <<<"$SORTED"
fi

summary ""
summary "**Actions taken (warn+close): $ACTIONS** (dry_run=\`$DRY_RUN\`)"
[ "$SKIPPED_READ_ERR" -gt 0 ] && summary "_Skipped $SKIPPED_READ_ERR PR(s) due to activity read errors._"
log "done — actions=$ACTIONS"
